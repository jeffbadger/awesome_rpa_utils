using System;
using System.Collections.Generic;
using System.Linq;

namespace InterruptAutomation.Tests
{
    /// <summary>A window in the fake desktop.</summary>
    internal sealed class FakeWindow
    {
        public IntPtr Handle;
        public string Class = "#32770";
        public string Title = "";
        public uint Pid = 4242;
        public string Message = "";
        public bool Alive = true;
        public bool Visible = true;
        public List<PopupButton> Buttons = new List<PopupButton>();

        /// <summary>Handles of buttons whose click closes the window.</summary>
        public HashSet<IntPtr> ClosingButtons = new HashSet<IntPtr>();

        /// <summary>If set, clicks are swallowed and the window stays open.</summary>
        public bool IgnoreClicks;

        /// <summary>If set, a close request is swallowed.</summary>
        public bool IgnoreClose;

        public int Clicks;
        public int Closes;
        public string LastClickedText;
    }

    /// <summary>A desktop made of <see cref="FakeWindow"/>s, so the engine's decisions can be checked without a real one.</summary>
    internal sealed class FakeProbe : IPopupProbe
    {
        private readonly object _lock = new object();
        private readonly Dictionary<IntPtr, FakeWindow> _windows = new Dictionary<IntPtr, FakeWindow>();
        private readonly Dictionary<uint, string> _processNames = new Dictionary<uint, string>();
        private int _nextHandle = 0x1000;

        public uint CurrentProcessId { get; set; } = 1;

        public FakeProbe() => _processNames[4242] = "targetapp";

        public void SetProcessName(uint pid, string name)
        {
            lock (_lock) _processNames[pid] = name;
        }

        /// <summary>Adds a message box with the given buttons; the buttons close it unless <paramref name="clickCloses"/> is false.</summary>
        public FakeWindow AddMessageBox(string title, string message, string[] buttonTexts, bool clickCloses = true, uint pid = 4242)
        {
            lock (_lock)
            {
                var w = new FakeWindow { Handle = new IntPtr(_nextHandle), Title = title, Message = message, Pid = pid };
                _nextHandle += 0x10;
                for (int i = 0; i < buttonTexts.Length; i++)
                {
                    // IDs follow the classic MessageBox layout for the common ones.
                    int id = StandardId(buttonTexts[i]) ?? (100 + i);
                    var b = new PopupButton { Handle = new IntPtr(w.Handle.ToInt64() + 1 + i), Id = id, Text = buttonTexts[i] };
                    w.Buttons.Add(b);
                    if (clickCloses)
                        w.ClosingButtons.Add(b.Handle);
                }
                _windows[w.Handle] = w;
                return w;
            }
        }

        private static int? StandardId(string text)
        {
            switch (text)
            {
                case "OK": return 1;
                case "Cancel": return 2;
                case "Abort": return 3;
                case "Retry": return 4;
                case "Ignore": return 5;
                case "Yes": return 6;
                case "No": return 7;
                default: return null;
            }
        }

        public void AddButton(FakeWindow w, string text, int id, bool closes = true)
        {
            lock (_lock)
            {
                var b = new PopupButton { Handle = new IntPtr(w.Handle.ToInt64() + 1 + w.Buttons.Count), Id = id, Text = text };
                w.Buttons.Add(b);
                if (closes)
                    w.ClosingButtons.Add(b.Handle);
            }
        }

        public void Destroy(FakeWindow w)
        {
            lock (_lock) w.Alive = false;
        }

        public int TotalClicks
        {
            get { lock (_lock) return _windows.Values.Sum(w => w.Clicks); }
        }

        // ---- IPopupProbe ----

        public bool IsWindow(IntPtr hwnd)
        {
            lock (_lock) return _windows.TryGetValue(hwnd, out var w) && w.Alive;
        }

        public PopupWindowInfo Describe(IntPtr hwnd)
        {
            lock (_lock)
            {
                if (!_windows.TryGetValue(hwnd, out var w) || !w.Alive)
                    return null;
                return new PopupWindowInfo { ClassName = w.Class, Title = w.Title, ProcessId = w.Pid };
            }
        }

        public string GetProcessName(uint processId)
        {
            lock (_lock) return _processNames.TryGetValue(processId, out var n) ? n : string.Empty;
        }

        public string GetMessageText(IntPtr hwnd)
        {
            lock (_lock) return _windows.TryGetValue(hwnd, out var w) ? w.Message : string.Empty;
        }

        public IReadOnlyList<PopupButton> GetButtons(IntPtr hwnd)
        {
            lock (_lock) return _windows.TryGetValue(hwnd, out var w) ? w.Buttons.ToList() : new List<PopupButton>();
        }

        public bool ClickButton(PopupButton button, int waitForEnabledMs)
        {
            lock (_lock)
            {
                foreach (var w in _windows.Values)
                {
                    if (!w.Buttons.Any(b => b.Handle == button.Handle))
                        continue;
                    w.Clicks++;
                    w.LastClickedText = button.Text;
                    if (!w.IgnoreClicks && w.ClosingButtons.Contains(button.Handle))
                        w.Alive = false;
                    return true;
                }
                return false;
            }
        }

        public void CloseWindow(IntPtr hwnd)
        {
            lock (_lock)
            {
                if (!_windows.TryGetValue(hwnd, out var w))
                    return;
                w.Closes++;
                if (!w.IgnoreClose)
                    w.Alive = false;
            }
        }

        public IReadOnlyList<IntPtr> EnumerateTopLevelWindows()
        {
            lock (_lock) return _windows.Values.Where(w => w.Alive && w.Visible).Select(w => w.Handle).ToList();
        }
    }

    /// <summary>A stand-in for the hook thread: the test decides when a window "appears".</summary>
    internal sealed class FakeHookSource : IPopupHookSource
    {
        private Action<IntPtr> _onWindow;

        public bool FailToStart;
        public int StartCalls;
        public int StopCalls;

        public bool Start(Action<IntPtr> onWindow, out string message)
        {
            StartCalls++;
            if (FailToStart)
            {
                message = "fake hook failure";
                return false;
            }
            _onWindow = onWindow;
            message = null;
            return true;
        }

        public void Stop()
        {
            StopCalls++;
            _onWindow = null;
        }

        public void Fire(IntPtr hwnd) => _onWindow?.Invoke(hwnd);
    }
}
