using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>The UIA-independent rule for when the hook thread's dispatcher may be asked to shut down.</summary>
    public class HookDispatcherShutdownPolicyTests
    {
        [Fact]
        public void ThreadThatNeverEnteredThePump_NeverGetsAShutdownRequest()
        {
            // The registration-failure path: InvokeShutdown here would block forever.
            Assert.False(HookDispatcherShutdownPolicy.ShouldRequestShutdown(false, false, false));
        }

        [Fact]
        public void PumpingThread_GetsExactlyOneShutdownRequest()
        {
            Assert.True(HookDispatcherShutdownPolicy.ShouldRequestShutdown(true, false, false));
            Assert.False(HookDispatcherShutdownPolicy.ShouldRequestShutdown(true, true, false));
            Assert.False(HookDispatcherShutdownPolicy.ShouldRequestShutdown(true, false, true));
            Assert.False(HookDispatcherShutdownPolicy.ShouldRequestShutdown(true, true, true));
        }
    }
}
