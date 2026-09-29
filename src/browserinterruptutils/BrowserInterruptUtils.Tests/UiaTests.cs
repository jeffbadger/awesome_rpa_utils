using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>
    /// Real (not faked) UI Automation smoke tests for <see cref="UiaBrowserPopupProbe"/>, mirroring
    /// <c>InterruptUtils.Tests.Win32Tests</c>'s role: sanity-checking the real implementation
    /// against a trivial, in-process target - a minimal WPF window hosting one named button -
    /// rather than a live browser, which no test host here can provide. These self-skip off
    /// Windows, exactly like <c>Win32Tests</c> (a plain <c>OperatingSystem.IsWindows()</c> guard,
    /// not a custom Skippable attribute - this repo has none). On a genuine Windows desktop they
    /// exercise the real <c>System.Windows.Automation</c>/Win32 code paths end to end: finding the
    /// button via a bounded descendant walk from the window's own handle, and genuinely invoking
    /// it (asserting a click-handler flag flipped, not just that <c>TryInvoke</c> returned
    /// <c>true</c>).
    /// </summary>
    public class UiaTests
    {
        [Fact]
        public void FindOverlayCandidates_FindsANamedButton_InARealWpfWindow()
        {
            if (!OperatingSystem.IsWindows())
                return;

            RunOnStaThread(() =>
            {
                using (var host = new WpfButtonHost())
                {
                    var probe = new UiaBrowserPopupProbe();
                    BrowserElementInfo windowInfo = probe.DescribeWindow(host.Hwnd);
                    Assert.NotNull(windowInfo);

                    var descendants = probe.FindOverlayCandidates(windowInfo.Ref, maxNodes: 50, maxDepth: 10);
                    Assert.Contains(descendants, e => e.Name == WpfButtonHost.ButtonName);
                }
            });
        }

        [Fact]
        public void TryInvoke_GenuinelyClicksTheButton_ViaInvokePattern()
        {
            if (!OperatingSystem.IsWindows())
                return;

            RunOnStaThread(() =>
            {
                using (var host = new WpfButtonHost())
                {
                    var probe = new UiaBrowserPopupProbe();
                    BrowserElementInfo windowInfo = probe.DescribeWindow(host.Hwnd);
                    Assert.NotNull(windowInfo);

                    var descendants = probe.FindOverlayCandidates(windowInfo.Ref, maxNodes: 50, maxDepth: 10);
                    BrowserElementInfo button = null;
                    foreach (var candidate in descendants)
                    {
                        if (candidate.Name == WpfButtonHost.ButtonName)
                            button = candidate;
                    }
                    Assert.NotNull(button);

                    Assert.False(host.Clicked);
                    bool ok = probe.TryInvoke(button.Ref, out string failureReason);
                    Assert.True(ok, failureReason);

                    // InvokePattern.Invoke() runs the click handler on the window's own dispatcher
                    // thread; give it a moment to actually run rather than assuming synchronous
                    // in-proc delivery.
                    Assert.True(SpinWait.SpinUntil(() => host.Clicked, TimeSpan.FromSeconds(5)));
                }
            });
        }

        [Fact]
        public void IsAlive_ReflectsWindowLifetime()
        {
            if (!OperatingSystem.IsWindows())
                return;

            RunOnStaThread(() =>
            {
                var probe = new UiaBrowserPopupProbe();
                BrowserElementRef windowRef;
                using (var host = new WpfButtonHost())
                {
                    BrowserElementInfo windowInfo = probe.DescribeWindow(host.Hwnd);
                    Assert.NotNull(windowInfo);
                    windowRef = windowInfo.Ref;
                    Assert.True(probe.IsAlive(windowRef));
                }
                // The window has since been closed; the same ref must no longer resolve as alive.
                Assert.False(probe.IsAlive(windowRef));
            });
        }

        /// <summary>
        /// Runs <paramref name="action"/> (the probe/automation-client calls) on a dedicated STA
        /// thread, separate from <see cref="WpfButtonHost"/>'s own perpetually-pumping UI thread -
        /// the same client/target thread separation a real popup-interrupt worker has against a
        /// live browser's UI thread. xunit does not run test methods on an STA thread by default,
        /// and UI Automation client calls are conventionally made from one.
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
            // A generous bound for a whole STA-thread-hosted probe call (as opposed to the
            // 5-second bounds elsewhere in this file for a single UI action/dispatcher
            // shutdown): if a probe call ever genuinely hangs (a COM deadlock, or a bug in one
            // of the bounded walks), this must fail fast instead of hanging the test run forever.
            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("RunOnStaThread: the STA thread did not complete within 30 seconds (possible COM deadlock or unbounded walk).");
            if (failure != null)
                throw failure;
        }

        /// <summary>
        /// A minimal WPF window hosting one named, automation-addressable button, positioned
        /// off-screen so it does not visibly flash during a test run. Owns its own dedicated STA
        /// thread that runs a real <see cref="Dispatcher.Run"/> message loop for the window's
        /// entire lifetime - deliberately not a single pump-once helper - so that a UI Automation
        /// client call arriving from a different thread (see <see cref="RunOnStaThread"/>) is
        /// always serviced immediately, exactly as a real, always-responsive browser UI thread
        /// would, rather than depending on precise pump timing from the test itself.
        /// </summary>
        private sealed class WpfButtonHost : IDisposable
        {
            internal const string ButtonName = "UiaTestButton";

            private readonly Thread _uiThread;
            private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
            private Window _window;
            private Dispatcher _dispatcher;
            private volatile bool _clicked;

            public IntPtr Hwnd { get; private set; }
            public bool Clicked => _clicked;

            public WpfButtonHost()
            {
                _uiThread = new Thread(RunUiThread) { IsBackground = true };
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();
                if (!_ready.Wait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("The WPF host window did not initialize in time.");
            }

            private void RunUiThread()
            {
                var button = new Button { Content = "Click me", Width = 80, Height = 24 };
                AutomationProperties.SetName(button, ButtonName);
                AutomationProperties.SetAutomationId(button, "UiaTestButtonId");
                button.Click += (_, __) => _clicked = true;

                _window = new Window
                {
                    Title = "UiaBrowserPopupProbe smoke test",
                    Width = 200,
                    Height = 100,
                    Content = button,
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
