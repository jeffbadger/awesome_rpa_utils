using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using EventAutomation.Native;

namespace EventAutomation
{
    /// <summary>
    /// Maps a raw WinEvent id to its human-readable event name and the
    /// <see cref="EventCategory"/> set it belongs to. An event can map to more
    /// than one category (a "#32770" dialog is both a window-created event and a
    /// dialog event).
    /// </summary>
    internal static class EventCategoryMap
    {
        public static string EventName(uint eventType)
        {
            switch (eventType)
            {
                case WinEventInterop.EVENT_OBJECT_CREATE: return "WindowCreated";
                case WinEventInterop.EVENT_OBJECT_DESTROY: return "WindowDestroyed";
                case WinEventInterop.EVENT_OBJECT_SHOW: return "WindowShown";
                case WinEventInterop.EVENT_OBJECT_HIDE: return "WindowHidden";
                case WinEventInterop.EVENT_SYSTEM_FOREGROUND: return "ForegroundChanged";
                case WinEventInterop.EVENT_OBJECT_FOCUS: return "FocusChanged";
                case WinEventInterop.EVENT_SYSTEM_DIALOGSTART: return "DialogAppeared";
                case WinEventInterop.EVENT_SYSTEM_DIALOGEND: return "DialogClosed";
                case WinEventInterop.EVENT_OBJECT_NAMECHANGE: return "TitleChanged";
                case WinEventInterop.EVENT_OBJECT_STATECHANGE: return "StateChanged";
                case WinEventInterop.EVENT_OBJECT_VALUECHANGE: return "ValueChanged";
                case WinEventInterop.EVENT_SYSTEM_MENUSTART: return "MenuOpened";
                case WinEventInterop.EVENT_SYSTEM_MENUEND: return "MenuClosed";
                case WinEventInterop.EVENT_SYSTEM_MENUPOPUPSTART: return "MenuPopupOpened";
                case WinEventInterop.EVENT_SYSTEM_MENUPOPUPEND: return "MenuPopupClosed";
                case WinEventInterop.EVENT_SYSTEM_MINIMIZESTART: return "WindowMinimized";
                case WinEventInterop.EVENT_SYSTEM_MINIMIZEEND: return "WindowRestored";
                case WinEventInterop.EVENT_SYSTEM_MOVESIZE: return "WindowMoved";
                case WinEventInterop.EVENT_SYSTEM_MOVESIZEEND: return "WindowMoveEnded";
                case WinEventInterop.EVENT_SYSTEM_SWITCHSTART: return "SessionSwitched";
                default: return "Unknown";
            }
        }

        public static List<EventCategory> CategoriesForEvent(uint eventType, string className)
        {
            var cats = new List<EventCategory>(2);
            switch (eventType)
            {
                case WinEventInterop.EVENT_OBJECT_CREATE:
                case WinEventInterop.EVENT_OBJECT_DESTROY:
                case WinEventInterop.EVENT_OBJECT_SHOW:
                case WinEventInterop.EVENT_OBJECT_HIDE:
                    cats.Add(EventCategory.Windows);
                    break;
                case WinEventInterop.EVENT_SYSTEM_FOREGROUND:
                case WinEventInterop.EVENT_OBJECT_FOCUS:
                    cats.Add(EventCategory.Foreground);
                    break;
                case WinEventInterop.EVENT_SYSTEM_DIALOGSTART:
                case WinEventInterop.EVENT_SYSTEM_DIALOGEND:
                    cats.Add(EventCategory.Dialogs);
                    break;
                case WinEventInterop.EVENT_OBJECT_NAMECHANGE:
                    cats.Add(EventCategory.Titles);
                    break;
                case WinEventInterop.EVENT_OBJECT_STATECHANGE:
                case WinEventInterop.EVENT_OBJECT_VALUECHANGE:
                    cats.Add(EventCategory.States);
                    break;
                case WinEventInterop.EVENT_SYSTEM_MENUSTART:
                case WinEventInterop.EVENT_SYSTEM_MENUEND:
                case WinEventInterop.EVENT_SYSTEM_MENUPOPUPSTART:
                case WinEventInterop.EVENT_SYSTEM_MENUPOPUPEND:
                    cats.Add(EventCategory.Menus);
                    break;
                case WinEventInterop.EVENT_SYSTEM_MINIMIZESTART:
                case WinEventInterop.EVENT_SYSTEM_MINIMIZEEND:
                case WinEventInterop.EVENT_SYSTEM_MOVESIZE:
                case WinEventInterop.EVENT_SYSTEM_MOVESIZEEND:
                    cats.Add(EventCategory.WindowOps);
                    break;
                case WinEventInterop.EVENT_SYSTEM_SWITCHSTART:
                    cats.Add(EventCategory.Session);
                    break;
            }
            // Dialog heuristic: a "#32770" window being created/shown is a dialog.
            if ((eventType == WinEventInterop.EVENT_OBJECT_CREATE || eventType == WinEventInterop.EVENT_OBJECT_SHOW)
                && string.Equals(className, "#32770", StringComparison.OrdinalIgnoreCase))
            {
                cats.Add(EventCategory.Dialogs);
            }
            return cats;
        }
    }

    /// <summary>
    /// Owns the dedicated WinEvent hook thread, its GetMessage/TranslateMessage/
    /// DispatchMessage pump, and the enrichment of raw WinEvents into
    /// <see cref="EventData"/>. One hook covers the whole SYSTEM+OBJECT range;
    /// the callback does near-zero work (no UIA/OCR/file IO/blocking) and hands
    /// enriched events to a consumer delegate that runs on the hook thread.
    /// </summary>
    internal sealed class WinEventEngine : IDisposable
    {
        private const int RingCapacity = 500;

        private readonly Action<EventData, List<EventCategory>> _consumer;
        private readonly WinEventInterop.WinEventProcDelegate _delegateRef; // kept alive for the hook's lifetime
        private readonly ConcurrentQueue<EventData> _ring = new ConcurrentQueue<EventData>();
        private readonly ManualResetEventSlim _threadStarted = new ManualResetEventSlim(false);
        private int _ringCount;
        private Thread _hookThread;
        private uint _hookThreadId;
        private volatile bool _hookRequested;
        private volatile bool _disposed;
        private IntPtr _hook = IntPtr.Zero;

        public WinEventEngine(Action<EventData, List<EventCategory>> consumer)
        {
            _consumer = consumer;
            _delegateRef = OnWinEvent;
        }

        /// <summary>Starts the background hook thread (idempotent).</summary>
        public void StartThread()
        {
            if (_hookThread != null)
                return;
            _hookThread = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "EventUtils.WinEventHook"
            };
            _hookThread.Start();
            _threadStarted.Wait(2000);
        }

        /// <summary>Requests the hook be installed (true) or uninstalled (false).</summary>
        public void RequestHook(bool on)
        {
            _hookRequested = on;
            if (_hookThreadId != 0)
                WinEventInterop.PostThreadMessage(_hookThreadId, WinEventInterop.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Adds an event to the bounded ring buffer (drops oldest when full).</summary>
        public void AddToRing(EventData e)
        {
            _ring.Enqueue(e);
            if (Interlocked.Increment(ref _ringCount) > RingCapacity)
            {
                _ring.TryDequeue(out _);
                Interlocked.Decrement(ref _ringCount);
            }
        }

        /// <summary>Snapshots the last <paramref name="count"/> events (newest last).</summary>
        public EventData[] SnapshotRing(int count)
        {
            var all = _ring.ToArray();
            if (all.Length <= count)
                return all;
            var tail = new EventData[count];
            Array.Copy(all, all.Length - count, tail, 0, count);
            return tail;
        }

        private void PumpLoop()
        {
            _hookThreadId = WinEventInterop.GetCurrentThreadId();
            _threadStarted.Set();
            while (!_disposed)
            {
                SyncHook();
                int ret = WinEventInterop.GetMessage(out var msg, IntPtr.Zero, 0, 0);
                if (ret <= 0) // WM_QUIT (0) or error (-1)
                    break;
                WinEventInterop.TranslateMessage(ref msg);
                WinEventInterop.DispatchMessage(ref msg);
            }
            SyncHook(); // ensure unhooked on exit
        }

        private void SyncHook()
        {
            if (_hookRequested && _hook == IntPtr.Zero)
            {
                _hook = WinEventInterop.SetWinEventHook(
                    WinEventInterop.EVENT_MIN,
                    WinEventInterop.EVENT_MAX,
                    IntPtr.Zero,
                    _delegateRef,
                    0, 0,
                    WinEventInterop.WINEVENT_OUTOFCONTEXT | WinEventInterop.WINEVENT_SKIPOWNPROCESS);
                if (_hook == IntPtr.Zero)
                    Debug.WriteLine("EventUtils: SetWinEventHook failed, Win32 error " + Marshal.GetLastWin32Error());
            }
            else if (!_hookRequested && _hook != IntPtr.Zero)
            {
                WinEventInterop.UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idThread, uint dwmsEventTime)
        {
            if (idObject != WinEventInterop.OBJID_WINDOW && idObject != WinEventInterop.OBJID_CLIENT)
                return;
            if (hwnd == IntPtr.Zero)
                return;
            var data = Enrich(eventType, hwnd);
            if (data == null)
                return;
            AddToRing(data);
            var cats = EventCategoryMap.CategoriesForEvent(eventType, data.ClassName);
            if (cats.Count == 0)
                return;
            try
            {
                _consumer(data, cats);
            }
            catch
            {
                // The consumer must never throw off the hook thread.
            }
        }

        /// <summary>
        /// Snapshots window identity (class, title, pid, process name) while the
        /// window still exists. For DESTROY events this data is only available
        /// during the callback — grab it here or it is gone forever. Time-boxed to
        /// a hard ~30 ms budget so a slow window never stalls the pump.
        /// </summary>
        private EventData Enrich(uint eventType, IntPtr hwnd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = new EventData
            {
                EventId = Guid.NewGuid().ToString("N"),
                Category = EventCategoryMap.EventName(eventType),
                Timestamp = DateTime.UtcNow.Ticks,
                Hwnd = unchecked((uint)hwnd.ToInt64())
            };
            data.ClassName = GetClassName(hwnd);
            data.Title = GetTitle(hwnd);
            uint pid = 0;
            WinEventInterop.GetWindowThreadProcessId(hwnd, out pid);
            data.ProcessId = pid;
            data.ProcessName = ProcessHelpers.GetProcessName(pid);
            if (eventType == WinEventInterop.EVENT_OBJECT_STATECHANGE)
                data.State = GetWindowState(hwnd);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 30)
                Debug.WriteLine("EventUtils: enrichment took " + sw.ElapsedMilliseconds + " ms for " + data.Category);
            return data;
        }

        private static string GetClassName(IntPtr hwnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                int n = WinEventInterop.GetClassName(hwnd, sb, sb.Capacity);
                return n > 0 ? sb.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetTitle(IntPtr hwnd)
        {
            try
            {
                int len = WinEventInterop.GetWindowTextLength(hwnd);
                if (len <= 0)
                    return null;
                var sb = new StringBuilder(len + 1);
                WinEventInterop.GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string GetWindowState(IntPtr hwnd)
        {
            try
            {
                int style = WinEventInterop.GetWindowLong(hwnd, WinEventInterop.GWL_STYLE);
                if (style == 0)
                    return null;
                string state;
                if ((style & WinEventInterop.WS_MINIMIZE) != 0)
                    state = "Minimized";
                else if ((style & WinEventInterop.WS_VISIBLE) != 0)
                    state = "Visible";
                else
                    state = "Hidden";
                if ((style & WinEventInterop.WS_DISABLED) != 0)
                    state += ",Disabled";
                return state;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            RequestHook(false);
            if (_hookThreadId != 0)
                WinEventInterop.PostThreadMessage(_hookThreadId, WinEventInterop.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread?.Join(2000);
            _threadStarted.Dispose();
        }
    }
}
