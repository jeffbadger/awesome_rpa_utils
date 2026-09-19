using System.Collections.Generic;

namespace ClipboardAutomation
{
    internal enum ReadOutcome
    {
        /// <summary>The bytes were read.</summary>
        Ok,
        /// <summary>The clipboard would not give the data (for example, the application that owns it did not answer).</summary>
        Failed,
        /// <summary>The format cannot be copied as bytes.</summary>
        Unsupported,
        /// <summary>The data is bigger than the room that was left.</summary>
        TooLarge
    }

    internal struct ReadResult
    {
        public ReadOutcome Outcome;
        public byte[] Data;
        public string Reason;

        /// <summary>The size of the data, for <see cref="ReadOutcome.TooLarge"/>.</summary>
        public long Size;

        internal static ReadResult Ok(byte[] data) => new ReadResult { Outcome = ReadOutcome.Ok, Data = data };
        internal static ReadResult Failed(string reason) => new ReadResult { Outcome = ReadOutcome.Failed, Reason = reason };
        internal static ReadResult Unsupported(string reason) => new ReadResult { Outcome = ReadOutcome.Unsupported, Reason = reason };
        internal static ReadResult TooLarge(long size) => new ReadResult { Outcome = ReadOutcome.TooLarge, Size = size };
    }

    /// <summary>
    /// The raw clipboard, as the engine sees it. The real implementation is <c>Win32ClipboardApi</c>; the tests
    /// substitute an in-memory clipboard so that no test ever touches the real one.
    /// </summary>
    internal interface IClipboardApi
    {
        /// <summary>Opens the clipboard for this thread, retrying briefly if another process has it open.</summary>
        bool Open(out string error);

        void Close();

        /// <summary>Changes every time the clipboard's contents change. Needs no open clipboard.</summary>
        long SequenceNumber { get; }

        /// <summary>The formats on the clipboard, in the order the owner offered them. Needs the clipboard open.</summary>
        IReadOnlyList<uint> EnumerateFormats();

        /// <summary>The name a format was registered under, or <c>null</c> for a predefined or unknown one.</summary>
        string GetFormatName(uint id);

        /// <summary>The number for a registered format name, registering it if it is new; 0 if it cannot be.</summary>
        uint RegisterFormat(string name);

        /// <summary>Whether the format is on the clipboard. Needs no open clipboard.</summary>
        bool IsFormatAvailable(uint id);

        /// <summary>Reads one format's bytes, refusing anything over <paramref name="maxBytes"/>. Needs the clipboard open.</summary>
        ReadResult ReadFormat(uint id, long maxBytes);

        /// <summary>Empties the clipboard. Needs the clipboard open.</summary>
        bool Empty(out string error);

        /// <summary>Puts one format on the clipboard. Needs the clipboard open (and emptied first).</summary>
        bool WriteFormat(uint id, byte[] data, out string error);
    }

    /// <summary>Sends the keystroke that pastes.</summary>
    internal interface IKeySender
    {
        /// <summary>Presses and releases Ctrl+V.</summary>
        bool SendPasteChord(out string error);
    }
}
