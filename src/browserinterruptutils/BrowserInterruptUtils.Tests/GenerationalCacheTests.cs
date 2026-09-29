using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>The UIA-independent cache logic behind <c>UiaBrowserPopupProbe</c>'s element cache, and the reference type it keys on.</summary>
    public class GenerationalCacheTests
    {
        private static GenerationalCache<int, string> Cache(int cap = 100, long rotateAfterMs = 0, Func<long> clock = null) =>
            new GenerationalCache<int, string>(cap, rotateAfterMs, clock);

        [Fact]
        public void HitAndMiss()
        {
            var cache = Cache();
            Assert.False(cache.TryGet(1, out _));
            cache.Set(1, "a");
            Assert.True(cache.TryGet(1, out string value));
            Assert.Equal("a", value);
            cache.Set(1, "b"); // replaces
            Assert.True(cache.TryGet(1, out value));
            Assert.Equal("b", value);
            Assert.False(cache.TryGet(2, out _));
        }

        [Fact]
        public void APreviousGenerationHit_IsPromoted_AndSurvivesTheNextRotation()
        {
            var cache = Cache();
            cache.Set(1, "a");
            cache.Rotate();
            Assert.Equal(0, cache.CurrentCount);
            Assert.Equal(1, cache.PreviousCount);

            Assert.True(cache.TryGet(1, out string value)); // found in previous, promoted
            Assert.Equal("a", value);
            Assert.Equal(1, cache.CurrentCount);
            Assert.Equal(0, cache.PreviousCount);

            cache.Rotate();
            Assert.True(cache.TryGet(1, out _)); // still there: it was in use
        }

        [Fact]
        public void AnUntouchedEntry_IsEvictedAfterTwoRotations()
        {
            var cache = Cache();
            cache.Set(1, "a");
            cache.Set(2, "b");
            cache.Rotate();
            Assert.True(cache.TryGet(2, out _)); // touched: promoted
            cache.Rotate();

            Assert.False(cache.TryGet(1, out _));
            Assert.True(cache.TryGet(2, out _));
        }

        [Fact]
        public void TheCap_TriggersARotation_SoTheCacheStaysBounded()
        {
            var cache = Cache(cap: 10);
            for (int i = 0; i < 1000; i++)
            {
                cache.Set(i, "v" + i);
                Assert.True(cache.CurrentCount <= 10);
                Assert.True(cache.PreviousCount <= 10);
            }
            Assert.True(cache.TryGet(999, out _)); // the newest is kept
            Assert.False(cache.TryGet(0, out _));  // the oldest is gone
        }

        [Fact]
        public void ReplacingAnExistingKey_DoesNotRotate()
        {
            var cache = Cache(cap: 2);
            cache.Set(1, "a");
            cache.Set(2, "b");
            cache.Set(2, "b2");
            Assert.Equal(2, cache.CurrentCount);
            Assert.Equal(0, cache.PreviousCount);
        }

        [Fact]
        public void TheAgeTrigger_RotatesAnIdleGeneration()
        {
            long now = 0;
            var cache = Cache(rotateAfterMs: 1000, clock: () => Interlocked.Read(ref now));
            cache.Set(1, "a");
            Interlocked.Exchange(ref now, 999);
            Assert.True(cache.TryGet(1, out _)); // not yet old

            Interlocked.Exchange(ref now, 1500);
            cache.Set(2, "b"); // rotates first: 1 is now in previous
            Assert.Equal(1, cache.CurrentCount);
            Assert.Equal(1, cache.PreviousCount);

            Interlocked.Exchange(ref now, 3000);
            Assert.False(cache.TryGet(1, out _)); // two generations without a touch
        }

        [Fact]
        public void Remove_DropsFromBothGenerations()
        {
            var cache = Cache();
            cache.Set(1, "a");
            cache.Rotate();
            cache.Set(1, "a2");
            cache.Remove(1);
            Assert.False(cache.TryGet(1, out _));
            Assert.Equal(0, cache.CurrentCount + cache.PreviousCount);
        }

        [Fact]
        public void StrongReferences_SurviveAGarbageCollection()
        {
            var cache = new GenerationalCache<int, object>(100, 0);
            WeakReference weak = Store(cache);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.True(weak.IsAlive); // held by the cache, unlike the WeakReference the probe used to keep
            Assert.True(cache.TryGet(1, out _));
        }

        private static WeakReference Store(GenerationalCache<int, object> cache)
        {
            var value = new object();
            cache.Set(1, value);
            return new WeakReference(value);
        }

        [Fact]
        public void ConcurrentUse_KeepsTheCacheConsistentAndBounded()
        {
            var cache = Cache(cap: 50);
            var errors = new List<Exception>();
            Parallel.For(0, 8, worker =>
            {
                try
                {
                    var rng = new Random(worker);
                    for (int i = 0; i < 20000; i++)
                    {
                        int key = rng.Next(0, 500);
                        switch (i % 5)
                        {
                            case 0:
                            case 1:
                                cache.Set(key, "v" + key);
                                break;
                            case 2:
                            case 3:
                                if (cache.TryGet(key, out string v) && v != "v" + key)
                                    throw new InvalidOperationException("wrong value for " + key + ": " + v);
                                break;
                            default:
                                if (i % 1000 == 4) cache.Rotate(); else cache.Remove(key);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (errors) errors.Add(ex);
                }
            });

            Assert.Empty(errors);
            Assert.True(cache.CurrentCount <= 50);
            Assert.True(cache.PreviousCount <= 50);
        }
    }

    public class BrowserElementRefTests
    {
        [Fact]
        public void TryCreate_RefusesAnElementWithNeitherRuntimeIdNorWindowHandle()
        {
            Assert.False(BrowserElementRef.TryCreate(null, IntPtr.Zero, out _));
            Assert.False(BrowserElementRef.TryCreate(Array.Empty<int>(), IntPtr.Zero, out _));
        }

        [Fact]
        public void TryCreate_AcceptsARuntimeId_OrAWindowHandleAlone()
        {
            Assert.True(BrowserElementRef.TryCreate(new[] { 42, 7 }, IntPtr.Zero, out var byRuntimeId));
            Assert.Equal(new[] { 42, 7 }, byRuntimeId.RuntimeId);

            Assert.True(BrowserElementRef.TryCreate(Array.Empty<int>(), new IntPtr(0x1234), out var byHandle));
            Assert.Equal(new IntPtr(0x1234), byHandle.Hwnd);
        }

        [Fact]
        public void WindowsWithEmptyRuntimeIds_AreDistinguishedByHandle_WithConsistentEqualityAndHash()
        {
            BrowserElementRef.TryCreate(null, new IntPtr(0x10), out var a);
            BrowserElementRef.TryCreate(Array.Empty<int>(), new IntPtr(0x10), out var a2);
            BrowserElementRef.TryCreate(null, new IntPtr(0x20), out var b);

            Assert.Equal(a, a2);
            Assert.Equal(a.GetHashCode(), a2.GetHashCode());
            Assert.NotEqual(a, b);
            Assert.Equal(2, new HashSet<BrowserElementRef> { a, a2, b }.Count);
        }

        [Fact]
        public void RuntimeIdAndHandle_BothParticipateInEquality()
        {
            var x = new BrowserElementRef(new[] { 1, 2 });
            var y = new BrowserElementRef(new[] { 1, 2 });
            Assert.Equal(x, y);
            Assert.Equal(x.GetHashCode(), y.GetHashCode());
            Assert.NotEqual(x, new BrowserElementRef(new[] { 1, 3 }));
            Assert.NotEqual(x, new BrowserElementRef(new[] { 1, 2 }, new IntPtr(5)));
        }
    }
}
