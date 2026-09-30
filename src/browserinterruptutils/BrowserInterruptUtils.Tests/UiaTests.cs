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

                    // The element came out of the cached walk but must keep a live reference:
                    // liveness, pinning and the click below all depend on that.
                    Assert.True(probe.IsAlive(button.Ref));
                    probe.Retain(button.Ref);
                    Assert.True(probe.IsAlive(button.Ref));

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
                BrowserElementRef buttonRef;
                IntPtr hwnd;
                using (var host = new WpfButtonHost())
                {
                    hwnd = host.Hwnd;
                    BrowserElementInfo windowInfo = probe.DescribeWindow(hwnd);
                    Assert.NotNull(windowInfo);
                    windowRef = windowInfo.Ref;
                    Assert.True(probe.IsAlive(windowRef));

                    // A descendant found by the cached walk, pinned like the engine does.
                    BrowserElementInfo button = FindByName(probe.FindOverlayCandidates(windowRef, 50, 10), WpfButtonHost.ButtonName);
                    Assert.NotNull(button);
                    buttonRef = button.Ref;
                    probe.Retain(buttonRef);
                    Assert.True(probe.IsAlive(buttonRef));
                }
                // Dispose returns once the host's dispatcher has shut down, but the OS may finish
                // destroying the HWND a moment later; wait (bounded) for that, so the assertion
                // below tests the probe rather than the teardown timing.
                Assert.True(SpinWait.SpinUntil(() => !NativeMethods.IsWindow(hwnd), TimeSpan.FromSeconds(5)),
                    "the host window was never destroyed");
                // The window has since been closed; the same ref must no longer resolve as alive
                // (IsAlive checks IsWindow first, and evicts the cached element).
                Assert.False(probe.IsAlive(windowRef));
                // The pinned descendant has no window handle of its own: its Dead verdict rests on
                // the LIVE read (never the cached snapshot). A torn-down WPF provider does not throw;
                // it answers with degraded values (ProcessId 0), which the probe treats as dead.
                // Bounded wait, since the provider may take a moment after the window is gone.
                // Only window-close teardown is asserted: a WPF element removed from a still-open
                // window keeps answering (provider peculiarity, not representative of browsers);
                // see the vanished-overlay live check in TESTING.md.
                Assert.True(SpinWait.SpinUntil(() => !probe.IsAlive(buttonRef), TimeSpan.FromSeconds(5)),
                    "a pinned descendant of a closed window never reported dead");
            });
        }

        [Fact]
        public void FindOverlayCandidates_CachedPropertiesEqualLiveValues()
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
                    BrowserElementInfo button = FindByName(descendants, WpfButtonHost.ButtonName);
                    Assert.NotNull(button);

                    Assert.Equal(WpfButtonHost.ButtonName, button.Name);
                    Assert.Equal(WpfButtonHost.ButtonAutomationId, button.AutomationId);
                    Assert.Equal("Button", button.ControlType);
                    Assert.Equal("Button", button.ClassName);
                    Assert.Equal(Environment.ProcessId, button.ProcessId);
                    Assert.False(string.IsNullOrEmpty(button.LocalizedControlType));

                    // Live values, read independently through UIA's uncached Current.
                    AutomationElement liveButton = AutomationElement.FromHandle(host.Hwnd).FindFirst(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, WpfButtonHost.ButtonAutomationId));
                    Assert.NotNull(liveButton);
                    Assert.Equal(liveButton.Current.LocalizedControlType, button.LocalizedControlType);
                    Assert.Equal(liveButton.Current.ClassName, button.ClassName);
                    Assert.Equal(liveButton.Current.ProcessId, button.ProcessId);
                    Assert.Equal(liveButton.GetRuntimeId(), button.Ref.RuntimeId);

                    // The TextBlock is described too (every visited element is returned).
                    BrowserElementInfo text = FindByName(descendants, WpfButtonHost.TextBlockText);
                    Assert.NotNull(text);
                    Assert.Equal("Text", text.ControlType);

                    // The cached path was really used: no Cached read fell back to a Current read.
                    Assert.Equal(0, probe.CachedReadFallbackCount);
                }
            });
        }

        [Fact]
        public void DescribeWindow_ReturnsClassNameAndProcessId_ThroughTheCachedPath()
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

                    Assert.Equal(host.Hwnd, windowInfo.Ref.Hwnd);
                    Assert.Equal(Environment.ProcessId, windowInfo.ProcessId);
                    Assert.Equal(AutomationElement.FromHandle(host.Hwnd).Current.ClassName, windowInfo.ClassName);
                    Assert.False(string.IsNullOrEmpty(windowInfo.ClassName));
                    Assert.Equal("Window", windowInfo.ControlType);
                    Assert.Equal(0, probe.CachedReadFallbackCount);
                }
            });
        }

        [Fact]
        public void TryGetMessageText_ReturnsTheTextBlockText_ThroughTheCachedPath()
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

                    Assert.Equal(WpfButtonHost.TextBlockText, probe.TryGetMessageText(windowInfo.Ref));
                    Assert.Equal(0, probe.CachedReadFallbackCount);
                }
            });
        }

        [Fact]
        public void FindOverlayCandidates_MaxNodesOne_VisitsExactlyOneNode()
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

                    Assert.Single(probe.FindOverlayCandidates(windowInfo.Ref, maxNodes: 1, maxDepth: 10));
                }
            });
        }

        private static BrowserElementInfo FindByName(System.Collections.Generic.IReadOnlyList<BrowserElementInfo> elements, string name)
        {
            foreach (BrowserElementInfo candidate in elements)
            {
                if (candidate.Name == name)
                    return candidate;
            }
            return null;
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
            // Background: if a UIA call hangs past the Join timeout below, the abandoned thread must
            // not keep the test host process alive after the run.
            thread.IsBackground = true;
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
            internal const string ButtonAutomationId = "UiaTestButtonId";
            internal const string TextBlockText = "UiaTestMessageText";

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
                AutomationProperties.SetAutomationId(button, ButtonAutomationId);
                button.Click += (_, __) => _clicked = true;
                var panel = new StackPanel();
                panel.Children.Add(button);
                panel.Children.Add(new TextBlock { Text = TextBlockText });

                _window = new Window
                {
                    Title = "UiaBrowserPopupProbe smoke test",
                    Width = 200,
                    Height = 140,
                    Content = panel,
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
