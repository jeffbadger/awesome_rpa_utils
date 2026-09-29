using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>
    /// Real (not faked) UI Automation smoke tests for <see cref="BrowserPopupHookThread"/>,
    /// mirroring <see cref="UiaTests"/>'s role and self-skipping convention (a plain
    /// <c>OperatingSystem.IsWindows()</c> guard - this repo has no custom Skippable attribute).
    /// This is inherently a bit fiddly to exercise meaningfully in-process, so these are two
    /// targeted smoke tests rather than an exhaustive suite: the desktop-wide WindowOpened
    /// notification genuinely firing for a newly-shown top-level window, and WatchWindow/
    /// UnwatchWindow genuinely registering/deregistering a real StructureChanged handler against
    /// a watched window's content. Like <see cref="UiaTests"/>, these cannot execute on this
    /// repo's Linux dev host (no Microsoft.WindowsDesktop.App runtime); they are verified there
    /// by compiling cleanly only.
    /// </summary>
    public class HookThreadUiaTests
    {
        [Fact]
        public void WindowOpened_FiresForANewTopLevelWindow()
        {
            if (!OperatingSystem.IsWindows())
                return;

            RunOnStaThread(() =>
            {
                var hook = new BrowserPopupHookThread();
                try
                {
                    bool started = hook.Start(_ => { }, out string message);
                    Assert.True(started, message);

                    // Recorded by hwnd (not a single flag) and polled after the window is created,
                    // rather than waited on before it: the desktop-wide handler may legitimately
                    // see other windows open too (this machine's own background activity), so the
                    // assertion is "our window's handle eventually showed up", not "the first
                    // notification was ours".
                    var seenHwnds = new ConcurrentDictionary<IntPtr, byte>();
                    hook.WindowOpened += info => seenHwnds[info.Hwnd] = 0;

                    using (var host = new WpfHost())
                    {
                        bool fired = SpinWait.SpinUntil(() => seenHwnds.ContainsKey(host.Hwnd), TimeSpan.FromSeconds(10));
                        Assert.True(fired, "WindowOpened never reported the new top-level window's handle within the timeout.");
                    }
                }
                finally
                {
                    hook.Stop();
                }
            });
        }

        [Fact]
        public void WatchWindow_And_UnwatchWindow_GenuinelyToggleStructureChangedDelivery()
        {
            if (!OperatingSystem.IsWindows())
                return;

            RunOnStaThread(() =>
            {
                var hook = new BrowserPopupHookThread();
                try
                {
                    bool started = hook.Start(_ => { }, out string message);
                    Assert.True(started, message);

                    using (var host = new WpfHost())
                    {
                        var windowRef = new BrowserElementRef(Array.Empty<int>(), host.Hwnd);

                        var raised = new ManualResetEventSlim(false);
                        BrowserElementRef received = default;
                        hook.WindowStructureChanged += r =>
                        {
                            received = r;
                            raised.Set();
                        };

                        hook.WatchWindow(windowRef);
                        host.AddChildButton();

                        bool fired = raised.Wait(TimeSpan.FromSeconds(10));
                        Assert.True(fired, "WindowStructureChanged did not fire for a watched window's real content mutation.");
                        Assert.Equal(windowRef, received);

                        hook.UnwatchWindow(windowRef);
                        raised.Reset();

                        host.AddChildButton();
                        // A generous wait to give an incorrectly-still-registered handler every
                        // chance to fire before asserting it did not.
                        bool firedAfterUnwatch = raised.Wait(TimeSpan.FromSeconds(2));
                        Assert.False(firedAfterUnwatch, "WindowStructureChanged fired again after UnwatchWindow.");
                    }
                }
                finally
                {
                    hook.Stop();
                }
            });
        }

        /// <summary>
        /// Runs <paramref name="action"/> (the hook/automation-client calls) on a dedicated STA
        /// thread. See <c>UiaTests.RunOnStaThread</c> for the identical rationale; duplicated here
        /// rather than shared because the two test classes intentionally have no dependency on
        /// each other.
        /// </summary>
        private static void RunOnStaThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("RunOnStaThread: the STA thread did not complete within 30 seconds (possible COM deadlock or unbounded walk).");
            if (failure != null)
                throw failure;
        }

        /// <summary>
        /// A minimal WPF window hosting one container panel that child buttons can be added to (to
        /// trigger a real UIA <c>StructureChanged</c> event), positioned off-screen so it does not
        /// visibly flash during a test run. Owns its own dedicated STA thread running a real
        /// <see cref="Dispatcher.Run"/> message loop for its whole lifetime - see
        /// <c>UiaTests.WpfButtonHost</c> for the identical rationale.
        /// </summary>
        private sealed class WpfHost : IDisposable
        {
            private readonly Thread _uiThread;
            private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
            private Window _window;
            private StackPanel _panel;
            private Dispatcher _dispatcher;

            public IntPtr Hwnd { get; private set; }

            public WpfHost()
            {
                _uiThread = new Thread(RunUiThread) { IsBackground = true };
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();
                if (!_ready.Wait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("The WPF host window did not initialize in time.");
            }

            private void RunUiThread()
            {
                _panel = new StackPanel();
                _window = new Window
                {
                    Title = "BrowserPopupHookThread smoke test",
                    Width = 200,
                    Height = 100,
                    Content = _panel,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStyle = WindowStyle.ToolWindow,
                    Left = -3000,
                    Top = -3000
                };
                _window.Show();
                Hwnd = new WindowInteropHelper(_window).Handle;
                _dispatcher = _window.Dispatcher;
                _ready.Set();

                Dispatcher.Run();
            }

            /// <summary>Adds one more child button to the panel, on the host's own UI thread - a real, structural UIA tree mutation.</summary>
            public void AddChildButton()
            {
                _dispatcher.Invoke(() => _panel.Children.Add(new Button { Content = "x", Width = 10, Height = 10 }));
            }

            public void Dispose()
            {
                Dispatcher dispatcher = _dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.Invoke(() =>
                    {
                        _window.Close();
                        Dispatcher.CurrentDispatcher.InvokeShutdown();
                    });
                }
                _uiThread.Join(TimeSpan.FromSeconds(5));
                _ready.Dispose();
            }
        }
    }
}
