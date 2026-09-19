using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ClipboardAutomation.Tests
{
    /// <summary>
    /// An in-memory clipboard, so that no test ever touches the real one. It refuses calls that need the clipboard
    /// open when it is not, and counts opens and closes so a test can prove none was left open.
    /// </summary>
    internal sealed class FakeClipboardApi : IClipboardApi
    {
        internal sealed class Item
        {
            public uint Id;
            public byte[] Data;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, uint> _registered = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();
        private uint _nextRegistered = 0xC000;
        private long _sequence = 1000;
        private bool _isOpen;
        private int _openThread;

        public List<Item> Items { get; } = new List<Item>();
        public int OpenCount;
        public int CloseCount;
        public string OpenFailure;
        public string EmptyFailure;
        public string EnumerateFailure;   // makes listing the formats fail, as a failed EnumClipboardFormats does
        public HashSet<uint> UnreadableIds { get; } = new HashSet<uint>();
        public HashSet<uint> UnwritableIds { get; } = new HashSet<uint>();
        public HashSet<uint> ListedButAbsentAfterWrite { get; } = new HashSet<uint>();

        /// <summary>Called as a format is read, outside the fake's lock (a test can block here to imitate a hung owner).</summary>
        public Action<uint> OnRead;
        public Action<uint> OnWrite;

        /// <summary>If set, <see cref="SequenceNumber"/> takes its values from here in turn (the last one repeats).</summary>
        public Queue<long> ScriptedSequence;

        public bool IsOpen => _isOpen;

        // ---- building content in tests ----

        public uint Register(string name)
        {
            lock (_lock)
            {
                if (_registered.TryGetValue(name, out uint id))
                    return id;
                id = _nextRegistered++;
                _registered[name] = id;
                _names[id] = name;
                return id;
            }
        }

        public FakeClipboardApi Put(uint id, byte[] data)
        {
            lock (_lock)
            {
                Items.Add(new Item { Id = id, Data = data });
                _sequence++;
            }
            return this;
        }

        public FakeClipboardApi PutRegistered(string name, byte[] data) => Put(Register(name), data);

        public FakeClipboardApi PutText(string text)
        {
            byte[] unicode = Encoding.Unicode.GetBytes(text);
            var withNull = new byte[unicode.Length + 2];
            Buffer.BlockCopy(unicode, 0, withNull, 0, unicode.Length);
            return Put(ClipboardFormats.CF_UNICODETEXT, withNull);
        }

        public byte[] DataOf(uint id) => Items.FirstOrDefault(i => i.Id == id)?.Data;

        public byte[] DataOf(string registeredName) => DataOf(Register(registeredName));

        public string TextOf()
        {
            byte[] data = DataOf(ClipboardFormats.CF_UNICODETEXT);
            return data == null ? null : Encoding.Unicode.GetString(data, 0, data.Length - 2);
        }

        /// <summary>Another application copies something: the clipboard is emptied and filled with what the callback puts.</summary>
        public void ExternalReplace(Action<FakeClipboardApi> fill)
        {
            lock (_lock)
            {
                Items.Clear();
                _sequence++;
                fill(this);
            }
        }

        public void ExternalChange()
        {
            lock (_lock)
                _sequence++;
        }

        /// <summary>Gives every registered name a different number, as a fresh session would, to prove restoring goes by name.</summary>
        public void RenumberRegisteredFormats()
        {
            lock (_lock)
            {
                foreach (Item item in Items)
                {
                    if (ClipboardFormats.IsRegistered(item.Id))
                        item.Id += 0x100;
                }
                var renamed = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in _registered)
                    renamed[pair.Key] = pair.Value + 0x100;
                _registered.Clear();
                _names.Clear();
                foreach (var pair in renamed)
                {
                    _registered[pair.Key] = pair.Value;
                    _names[pair.Value] = pair.Key;
                }
                _nextRegistered += 0x200;
            }
        }

        // ---- IClipboardApi ----

        public bool Open(out string error)
        {
            lock (_lock)
            {
                OpenCount++;
                if (OpenFailure != null)
                {
                    error = OpenFailure;
                    return false;
                }
                if (_isOpen && _openThread != Environment.CurrentManagedThreadId)
                {
                    error = "the clipboard is held open by another caller (Win32 error 5: Access is denied.)";
                    return false;
                }
                if (_isOpen)
                    throw new InvalidOperationException("The clipboard was opened twice.");
                _isOpen = true;
                _openThread = Environment.CurrentManagedThreadId;
                error = null;
                return true;
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                CloseCount++;
                _isOpen = false;
            }
        }

        public long SequenceNumber
        {
            get
            {
                lock (_lock)
                {
                    if (ScriptedSequence != null && ScriptedSequence.Count > 0)
                    {
                        long value = ScriptedSequence.Count > 1 ? ScriptedSequence.Dequeue() : ScriptedSequence.Peek();
                        return value;
                    }
                    return _sequence;
                }
            }
        }

        public IReadOnlyList<uint> EnumerateFormats()
        {
            lock (_lock)
            {
                RequireOpen();
                if (EnumerateFailure != null)
                    throw new InvalidOperationException(EnumerateFailure);
                return Items.Select(i => i.Id).ToList();
            }
        }

        public string GetFormatName(uint id)
        {
            lock (_lock)
                return _names.TryGetValue(id, out string name) ? name : null;
        }

        public uint RegisterFormat(string name) => Register(name);

        public bool IsFormatAvailable(uint id)
        {
            lock (_lock)
                return Items.Any(i => i.Id == id);
        }

        public ReadResult ReadFormat(uint id, long maxBytes)
        {
            Item item;
            lock (_lock)
            {
                RequireOpen();
                item = Items.FirstOrDefault(i => i.Id == id);
            }
            OnRead?.Invoke(id);
            if (item == null || UnreadableIds.Contains(id))
                return ReadResult.Failed("the fake clipboard would not give it");
            if (item.Data.LongLength > maxBytes)
                return ReadResult.TooLarge(item.Data.LongLength);
            return ReadResult.Ok((byte[])item.Data.Clone());
        }

        public bool Empty(out string error)
        {
            lock (_lock)
            {
                RequireOpen();
                if (EmptyFailure != null)
                {
                    error = EmptyFailure;
                    return false;
                }
                Items.Clear();
                _sequence++;
                error = null;
                return true;
            }
        }

        public bool WriteFormat(uint id, byte[] data, out string error)
        {
            OnWrite?.Invoke(id);   // outside the lock, so a test can make a write hang without blocking everything else
            lock (_lock)
            {
                RequireOpen();
                if (UnwritableIds.Contains(id))
                {
                    error = "the fake clipboard refused it";
                    return false;
                }
                if (!ListedButAbsentAfterWrite.Contains(id))
                    Items.Add(new Item { Id = id, Data = (byte[])data.Clone() });
                _sequence++;
                error = null;
                return true;
            }
        }

        private void RequireOpen()
        {
            if (!_isOpen)
                throw new InvalidOperationException("The clipboard was used without being opened.");
        }
    }

    /// <summary>Stands in for the keystroke, and lets a test look at the clipboard at the instant of the paste.</summary>
    internal sealed class FakeKeySender : IKeySender
    {
        public int Sent;
        public bool Fail;
        public Action OnSend;

        public bool SendPasteChord(out string error)
        {
            Sent++;
            OnSend?.Invoke();
            if (Fail)
            {
                error = "the fake keyboard refused";
                return false;
            }
            error = null;
            return true;
        }
    }

    /// <summary>Everything a test needs: a fake clipboard, keys, and a clock that only moves when the component sleeps.</summary>
    internal sealed class Rig : IDisposable
    {
        public readonly FakeClipboardApi Clipboard = new FakeClipboardApi();
        public readonly FakeKeySender Keys = new FakeKeySender();
        public readonly List<int> Sleeps = new List<int>();
        public Action<int> OnSleep;
        public readonly ClipboardUtils Utils;
        private long _now;

        public Rig(int operationTimeoutMs = 5000, int historySettleMs = 20)
        {
            Utils = new ClipboardUtils(Clipboard, Keys, ms => { Sleeps.Add(ms); OnSleep?.Invoke(ms); _now += ms; }, () => _now, operationTimeoutMs, historySettleMs);
        }

        public void Dispose() => Utils.Dispose();

        /// <summary>A clipboard like a rich copy from an office application: text, formatted text, an image and a custom format.</summary>
        public Rig WithRichContent()
        {
            Clipboard.PutText("hello");
            Clipboard.PutRegistered("HTML Format", Encoding.UTF8.GetBytes("<b>hello</b>"));
            Clipboard.Put(ClipboardFormats.CF_DIB, new byte[] { 40, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 });
            Clipboard.PutRegistered("MyApp Private Data", new byte[] { 9, 8, 7, 6 });
            return this;
        }

        public List<(uint Id, byte[] Data)> Contents() => Clipboard.Items.Select(i => (i.Id, i.Data)).ToList();
    }
}
