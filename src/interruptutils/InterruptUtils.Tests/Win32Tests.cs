using System;
using System.Diagnostics;
using Xunit;

namespace InterruptAutomation.Tests
{
    /// <summary>Tests for the real Win32 helpers; the ones that touch the operating system self-skip off Windows.</summary>
    public class Win32Tests
    {
        [Theory]
        [InlineData("Static", true)]
        [InlineData("static", true)]
        [InlineData("WindowsForms10.STATIC.app.0.141b42a_r14_ad1", true)]
        [InlineData("WindowsForms10.Static.app.0.2360855_r3_ad1", true)]
        [InlineData("Button", false)]
        [InlineData("Edit", false)]
        [InlineData("WindowsForms10.BUTTON.app.0.141b42a_r14_ad1", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsStaticClass_RecognizesNativeAndWinFormsLabels(string className, bool expected)
        {
            Assert.Equal(expected, Win32PopupProbe.IsStaticClass(className));
        }

        [Fact]
        public void GetProcessName_ResolvesTheCurrentProcess_AndIsEmptyForAnUnknownOrZeroId()
        {
            if (!OperatingSystem.IsWindows())
                return;
            var probe = new Win32PopupProbe();

            using (var self = Process.GetCurrentProcess())
                Assert.Equal(self.ProcessName, probe.GetProcessName((uint)self.Id));

            Assert.Equal(string.Empty, probe.GetProcessName(0));
            Assert.Equal(string.Empty, probe.GetProcessName(0x7FFFFFF0));
        }

        [Fact]
        public void GetProcessName_IsNotCachedAcrossCalls()
        {
            if (!OperatingSystem.IsWindows())
                return;
            var probe = new Win32PopupProbe();

            // A name that was resolved once for an id must not outlive the process that had it.
            // (The engine memoizes within one pass; the probe itself holds nothing.)
            using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > nul") { CreateNoWindow = true, UseShellExecute = false });
            uint id = (uint)child.Id;
            Assert.Equal("cmd", probe.GetProcessName(id), ignoreCase: true);

            child.WaitForExit(10000);
            Assert.Equal(string.Empty, probe.GetProcessName(id));
        }

        // The hook thread is exercised for real: it must start, stop and restart cleanly, and end on Stop.
        [Fact]
        public void HookThread_StartsStopsAndRestartsCleanly_WithoutReportingAFault()
        {
            if (!OperatingSystem.IsWindows())
                return;
            var hook = new PopupHookThread();
            string fault = null;

            for (int i = 0; i < 10; i++)
            {
                Assert.True(hook.Start(hwnd => { }, hwnd => { }, f => fault = f, out string message), message);
                Assert.Null(message);
                Assert.False(hook.Start(hwnd => { }, hwnd => { }, f => fault = f, out string again));
                Assert.Contains("already running", again);
                hook.Stop();
            }

            hook.Stop(); // and stopping when not running is fine
            Assert.Null(fault);
        }
    }
}
