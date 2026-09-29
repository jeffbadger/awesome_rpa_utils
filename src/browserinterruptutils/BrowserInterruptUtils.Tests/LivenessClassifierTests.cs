using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>The UIA-independent rules that decide when a popup reference is reported closed.</summary>
    public class LivenessClassifierTests
    {
        [Fact]
        public void DestroyedWindowHandle_IsDead()
        {
            Assert.Equal(Liveness.Dead, LivenessClassifier.ClassifyWindow(hasWindowHandle: true, isWindow: false));
        }

        [Fact]
        public void LiveWindowHandle_AndWindowlessReference_AreNotDeadOnTheWindowTest()
        {
            Assert.Equal(Liveness.Alive, LivenessClassifier.ClassifyWindow(true, true));
            Assert.Equal(Liveness.Alive, LivenessClassifier.ClassifyWindow(false, false)); // no handle: the window test does not apply
        }

        [Fact]
        public void ConfirmedUnavailableElement_IsDead()
        {
            Assert.Equal(Liveness.Dead, LivenessClassifier.ClassifyRead(ReadOutcome.Unavailable));
        }

        [Fact]
        public void OtherReadFailure_IsUnknown_NotDead_AndIsReportedAlive()
        {
            Liveness liveness = LivenessClassifier.ClassifyRead(ReadOutcome.OtherFailure);
            Assert.Equal(Liveness.Unknown, liveness);
            Assert.True(LivenessClassifier.ReportsAlive(liveness));
        }

        [Fact]
        public void SuccessfulRead_IsAlive()
        {
            Liveness liveness = LivenessClassifier.ClassifyRead(ReadOutcome.Ok);
            Assert.Equal(Liveness.Alive, liveness);
            Assert.True(LivenessClassifier.ReportsAlive(liveness));
        }

        [Fact]
        public void OnlyDead_IsReportedNotAlive()
        {
            Assert.False(LivenessClassifier.ReportsAlive(Liveness.Dead));
            Assert.True(LivenessClassifier.ReportsAlive(Liveness.Alive));
            Assert.True(LivenessClassifier.ReportsAlive(Liveness.Unknown));
        }

        [Fact]
        public void CacheEntry_IsEvictedOnlyWhenConfirmedUnavailable()
        {
            Assert.True(LivenessClassifier.ShouldEvictCacheEntry(ReadOutcome.Unavailable));
            Assert.False(LivenessClassifier.ShouldEvictCacheEntry(ReadOutcome.OtherFailure)); // a cache hit with a non-definitive failure is not evicted
            Assert.False(LivenessClassifier.ShouldEvictCacheEntry(ReadOutcome.Ok));
        }
    }
}
