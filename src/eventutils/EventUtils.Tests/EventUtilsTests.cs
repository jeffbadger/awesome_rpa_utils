using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using EventAutomation;
using Xunit;

namespace EventAutomation.Tests
{
    /// <summary>
    /// Tests for EventUtils. Pure-logic tests (filter parsing/matching, category
    /// mapping, debounce, queue overflow, waiter registry) run on any platform.
    /// Real-window integration tests spawn notepad.exe and are gated behind
    /// OperatingSystem.IsWindows() — they self-skip elsewhere.
    /// </summary>
    public class EventUtilsTests
    {
        // ------------------------------------------------------------------
        // EventFilter: JSON parsing
        // ------------------------------------------------------------------

        [Fact]
        public void FromJson_parses_known_keys()
        {
            var f = EventFilter.FromJson("{\"process\":\"notepad\",\"class\":\"#32770\",\"titleContains\":\"Save\"}");
            Assert.NotNull(f);
            Assert.True(f.Matches(Ev("notepad.exe", "#32770", "Save As"), 0));
            Assert.False(f.Matches(Ev("calc.exe", "#32770", "Save As"), 0));
            Assert.False(f.Matches(Ev("notepad.exe", "#32770", "Open"), 0));
        }

        [Fact]
        public void FromJson_ignores_unknown_keys()
        {
            var f = EventFilter.FromJson("{\"bogusKey\":1,\"process\":\"calc\",\"another\":true}");
            Assert.NotNull(f);
            Assert.True(f.Matches(Ev("calc.exe", null, null), 0));
            Assert.False(f.Matches(Ev("notepad.exe", null, null), 0));
        }

        [Fact]
        public void FromJson_bad_json_returns_null()
        {
            Assert.Null(EventFilter.FromJson("{not json"));
            Assert.Null(EventFilter.FromJson("{\"process\":"));
            Assert.Null(EventFilter.FromJson("[]"));
            Assert.Null(EventFilter.FromJson(null));
            Assert.Null(EventFilter.FromJson(""));
        }

        [Fact]
        public void FromJson_parses_processes_array_and_regex()
        {
            var f = EventFilter.FromJson("{\"processes\":[\"notepad\",\"calc\"],\"titleMatches\":\"Report\\\\s*\\\\d{4}\"}");
            Assert.NotNull(f);
            Assert.True(f.Matches(Ev("notepad.exe", null, "Report 2024"), 0));
            Assert.True(f.Matches(Ev("calc.exe", null, "Report2024"), 0));
            Assert.False(f.Matches(Ev("mspaint.exe", null, "Report 2024"), 0));
            Assert.False(f.Matches(Ev("notepad.exe", null, "Report X"), 0));
        }

        // ------------------------------------------------------------------
        // EventFilter: fluent builder + matching
        // ------------------------------------------------------------------

        [Fact]
        public void Fluent_filter_matches_process_case_insensitive_with_exe_suffix()
        {
            var f = EventFilter.Create().Process("notepad");
            Assert.True(f.Matches(Ev("notepad.exe", null, null), 0));
            Assert.True(f.Matches(Ev("NOTEPAD.EXE", null, null), 0));
            Assert.False(f.Matches(Ev("calc.exe", null, null), 0));
        }

        [Fact]
        public void Fluent_filter_matches_title_contains_and_regex()
        {
            var f = EventFilter.Create().TitleContains("Save").TitleMatches(@"\d{4}");
            Assert.True(f.Matches(Ev("notepad.exe", null, "Save Report 2024"), 0));
            Assert.False(f.Matches(Ev("notepad.exe", null, "Save Report"), 0));
        }

        [Fact]
        public void Fluent_filter_any_of_processes()
        {
            var f = EventFilter.Create().AnyOfProcesses("notepad", "calc");
            Assert.True(f.Matches(Ev("notepad.exe", null, null), 0));
            Assert.True(f.Matches(Ev("calc.exe", null, null), 0));
            Assert.False(f.Matches(Ev("mspaint.exe", null, null), 0));
        }

        [Fact]
        public void Fluent_filter_exclude_self()
        {
            var f = EventFilter.Create().ExcludeSelf(true);
            Assert.False(f.Matches(Ev("testhost.exe", null, null, processId: 42), 42));
            Assert.True(f.Matches(Ev("notepad.exe", null, null, processId: 7), 42));
        }

        [Fact]
        public void Empty_filter_matches_everything()
        {
            var f = EventFilter.Create();
            Assert.True(f.Matches(Ev("anything.exe", "SomeClass", "Some Title"), 0));
        }

        // ------------------------------------------------------------------
        // EventCategoryMap
        // ------------------------------------------------------------------

        [Fact]
        public void CategoryMap_event_names_are_stable()
        {
            Assert.Equal("WindowCreated", EventCategoryMap.EventName(0x8000));
            Assert.Equal("WindowDestroyed", EventCategoryMap.EventName(0x8001));
            Assert.Equal("WindowShown", EventCategoryMap.EventName(0x8002));
            Assert.Equal("WindowHidden", EventCategoryMap.EventName(0x8003));
            Assert.Equal("ForegroundChanged", EventCategoryMap.EventName(0x0003));
            Assert.Equal("DialogAppeared", EventCategoryMap.EventName(0x0010));
            Assert.Equal("TitleChanged", EventCategoryMap.EventName(0x800C));
            Assert.Equal("StateChanged", EventCategoryMap.EventName(0x800A));
            Assert.Equal("MenuOpened", EventCategoryMap.EventName(0x0004));
            Assert.Equal("SessionSwitched", EventCategoryMap.EventName(0x0012));
        }

        [Fact]
        public void CategoryMap_maps_events_to_categories()
        {
            Assert.Contains(EventCategory.Windows, EventCategoryMap.CategoriesForEvent(0x8000, null));
            Assert.Contains(EventCategory.Windows, EventCategoryMap.CategoriesForEvent(0x8003, null));
            Assert.Contains(EventCategory.Foreground, EventCategoryMap.CategoriesForEvent(0x0003, null));
            Assert.Contains(EventCategory.Titles, EventCategoryMap.CategoriesForEvent(0x800C, null));
            Assert.Contains(EventCategory.States, EventCategoryMap.CategoriesForEvent(0x800A, null));
            Assert.Contains(EventCategory.Menus, EventCategoryMap.CategoriesForEvent(0x0006, null));
            Assert.Contains(EventCategory.WindowOps, EventCategoryMap.CategoriesForEvent(0x000A, null));
            Assert.Contains(EventCategory.Session, EventCategoryMap.CategoriesForEvent(0x0012, null));
        }

        [Fact]
        public void CategoryMap_dialog_heuristic_adds_dialogs_for_32770()
        {
            var cats = EventCategoryMap.CategoriesForEvent(0x8000, "#32770");
            Assert.Contains(EventCategory.Windows, cats);
            Assert.Contains(EventCategory.Dialogs, cats);
            // A non-dialog class does not get the Dialogs category.
            Assert.DoesNotContain(EventCategory.Dialogs, EventCategoryMap.CategoriesForEvent(0x8000, "Notepad"));
        }

        // ------------------------------------------------------------------
        // ThrottleDebounce
        // ------------------------------------------------------------------

        [Fact]
        public void Debounce_drops_second_event_within_window()
        {
            var d = new ThrottleDebounce();
            d.Set("WindowShown", 150);
            Assert.False(d.ShouldDrop(100, "WindowShown")); // first kept
            Assert.True(d.ShouldDrop(100, "WindowShown"));  // second dropped
            Assert.False(d.ShouldDrop(200, "WindowShown")); // different hwnd kept
        }

        [Fact]
        public void Debounce_keeps_event_after_window_elapses()
        {
            var d = new ThrottleDebounce();
            d.Set("WindowShown", 30);
            Assert.False(d.ShouldDrop(100, "WindowShown"));
            Thread.Sleep(50);
            Assert.False(d.ShouldDrop(100, "WindowShown")); // window elapsed
        }

        [Fact]
        public void Debounce_defaults_show_hide_to_150ms_others_zero()
        {
            var d = new ThrottleDebounce();
            Assert.False(d.ShouldDrop(100, "WindowShown"));
            Assert.True(d.ShouldDrop(100, "WindowShown"));
            Assert.False(d.ShouldDrop(100, "WindowCreated")); // no default debounce
            Assert.False(d.ShouldDrop(100, "WindowCreated"));
        }

        // ------------------------------------------------------------------
        // SubscriptionManager: overflow policies (pure, no interop)
        // ------------------------------------------------------------------

        [Fact]
        public void Queue_overflow_drop_oldest_keeps_newest_five()
        {
            var mgr = new SubscriptionManager();
            Assert.True(mgr.TryAdd("s1", new HashSet<EventCategory> { EventCategory.Windows }, EventFilter.Create(), out _));
            mgr.SetQueueLimits(5, "DropOldest");
            for (int i = 0; i < 50; i++)
                mgr.Deliver(Ev("notepad.exe", null, "t" + i, processId: 1), new List<EventCategory> { EventCategory.Windows }, 0);

            Assert.True(mgr.HasEvents("s1", out int count));
            Assert.Equal(5, count);
            var first = mgr.GetNextEvent("s1", 0, out _);
            Assert.NotNull(first);
            Assert.Equal("t45", first.Title); // oldest (t0..t44) dropped
        }

        [Fact]
        public void Queue_overflow_drop_newest_keeps_oldest()
        {
            var mgr = new SubscriptionManager();
            Assert.True(mgr.TryAdd("s1", new HashSet<EventCategory> { EventCategory.Windows }, EventFilter.Create(), out _));
            mgr.SetQueueLimits(3, "DropNewest");
            for (int i = 0; i < 10; i++)
                mgr.Deliver(Ev("notepad.exe", null, "t" + i, processId: 1), new List<EventCategory> { EventCategory.Windows }, 0);

            Assert.True(mgr.HasEvents("s1", out int count));
            Assert.Equal(3, count);
            Assert.Equal("t0", mgr.GetNextEvent("s1", 0, out _).Title);
            Assert.Equal("t1", mgr.GetNextEvent("s1", 0, out _).Title);
            Assert.Equal("t2", mgr.GetNextEvent("s1", 0, out _).Title);
        }

        [Fact]
        public void Queue_overflow_block_never_exceeds_limit()
        {
            var mgr = new SubscriptionManager();
            Assert.True(mgr.TryAdd("s1", new HashSet<EventCategory> { EventCategory.Windows }, EventFilter.Create(), out _));
            mgr.SetQueueLimits(2, "Block");
            for (int i = 0; i < 10; i++)
                mgr.Deliver(Ev("notepad.exe", null, "t" + i, processId: 1), new List<EventCategory> { EventCategory.Windows }, 0);

            Assert.True(mgr.HasEvents("s1", out int count));
            Assert.Equal(2, count);
        }

        [Fact]
        public void Subscription_delivery_respects_filter_and_categories()
        {
            var mgr = new SubscriptionManager();
            var filter = EventFilter.FromJson("{\"process\":\"notepad\"}");
            Assert.True(mgr.TryAdd("s1", new HashSet<EventCategory> { EventCategory.Windows }, filter, out _));
            mgr.Deliver(Ev("notepad.exe", null, "a", processId: 1), new List<EventCategory> { EventCategory.Windows }, 0);
            mgr.Deliver(Ev("calc.exe", null, "b", processId: 2), new List<EventCategory> { EventCategory.Windows }, 0);
            mgr.Deliver(Ev("notepad.exe", null, "c", processId: 1), new List<EventCategory> { EventCategory.Titles }, 0);

            Assert.True(mgr.HasEvents("s1", out int count));
            Assert.Equal(1, count); // only the notepad Windows event
            Assert.Equal("a", mgr.GetNextEvent("s1", 0, out _).Title);
        }

        // ------------------------------------------------------------------
        // WaiterRegistry (CancelWaits / timeout / match)
        // ------------------------------------------------------------------

        [Fact]
        public async System.Threading.Tasks.Task Waiter_match_completes_with_event()
        {
            var reg = new WaiterRegistry();
            var task = reg.Register(e => e.Category == "WindowCreated", 5000);
            reg.Match(Ev("notepad.exe", null, "t", processId: 1, category: "WindowCreated"));
            var result = await task.WaitAsync(TimeSpan.FromMilliseconds(100));
            Assert.NotNull(result);
            Assert.Equal("WindowCreated", result.Category);
        }

        [Fact]
        public async System.Threading.Tasks.Task Waiter_timeout_completes_with_null()
        {
            var reg = new WaiterRegistry();
            var task = reg.Register(e => false, 50);
            var result = await task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Null(result);
        }

        [Fact]
        public async System.Threading.Tasks.Task CancelWaits_unblocks_all_pending_waits_within_100ms()
        {
            var reg = new WaiterRegistry();
            var t1 = reg.Register(e => e.Category == "WindowCreated", 60000);
            var t2 = reg.Register(e => e.Category == "WindowDestroyed", 60000);
            var sw = Stopwatch.StartNew();
            reg.CancelAll();
            var r1 = await t1.WaitAsync(TimeSpan.FromMilliseconds(100));
            var r2 = await t2.WaitAsync(TimeSpan.FromMilliseconds(100));
            sw.Stop();
            Assert.Null(r1);
            Assert.Null(r2);
            Assert.True(sw.ElapsedMilliseconds < 100, "CancelWaits took " + sw.ElapsedMilliseconds + " ms");
        }

        // ------------------------------------------------------------------
        // Public API: never-throw contract (any platform)
        // ------------------------------------------------------------------

        [Fact]
        public void Subscribe_bad_json_returns_false_never_throws()
        {
            var utils = new EventUtils();
            try
            {
                Assert.False(utils.Subscribe("Windows", "{not json", "s1"));
                Assert.False(utils.Subscribe("Windows", "{\"process\":", "s2"));
                Assert.False(utils.Subscribe("Windows", "[]", "s3"));
                // Unknown keys are ignored → valid filter → True.
                Assert.True(utils.Subscribe("Windows", "{\"bogusKey\":1,\"process\":\"notepad\"}", "s4"));
                // Empty filter = match-all.
                Assert.True(utils.Subscribe("Windows", null, "s5"));
                Assert.True(utils.Subscribe("Windows", "", "s6"));
                // Duplicate id → False.
                Assert.False(utils.Subscribe("Windows", null, "s5"));
                // Invalid category → False.
                Assert.False(utils.Subscribe("Bogus", null, "s7"));
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void WaitFor_without_start_returns_null_and_timed_out()
        {
            var utils = new EventUtils();
            try
            {
                var e = utils.WaitForWindowCreated(null, 100, out bool timedOut);
                Assert.Null(e);
                Assert.True(timedOut);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void EventData_ToJson_returns_valid_json()
        {
            var e = Ev("notepad.exe", "#32770", "Save As", processId: 123, category: "WindowCreated");
            var json = e.ToJson();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("WindowCreated", doc.RootElement.GetProperty("Category").GetString());
            Assert.Equal("notepad.exe", doc.RootElement.GetProperty("ProcessName").GetString());
            Assert.Equal(123u, doc.RootElement.GetProperty("ProcessId").GetUInt32());
        }

        // ------------------------------------------------------------------
        // Windows-only integration tests (self-skip elsewhere)
        // ------------------------------------------------------------------

        [Fact]
        public void WaitForWindowCreated_notepad_returns_event()
        {
            if (!OperatingSystem.IsWindows())
                return;
            using var utils = new EventUtils();
            Assert.True(utils.Initialize());
            Assert.True(utils.Start("Windows"));
            using var proc = StartNotepad();
            if (proc == null)
                return; // notepad unavailable on this image
            try
            {
                var e = utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out bool timedOut);
                Assert.False(timedOut);
                Assert.NotNull(e);
                Assert.Equal("notepad", e.ProcessName, ignoreCase: true);
                Assert.NotEqual(0u, e.Hwnd);
            }
            finally
            {
                Kill(proc);
            }
        }

        [Fact]
        public void WaitForWindowDestroyed_captures_title_before_destruction()
        {
            if (!OperatingSystem.IsWindows())
                return;
            using var utils = new EventUtils();
            Assert.True(utils.Initialize());
            Assert.True(utils.Start("Windows"));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                var created = utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out _);
                Assert.NotNull(created);
                Thread.Sleep(300); // let the window finish coming up
                var destroyed = utils.WaitForWindowDestroyed("{\"process\":\"notepad\"}", 10000, out bool timedOut);
                Kill(proc); // close the window → DESTROY event
                Assert.False(timedOut);
                Assert.NotNull(destroyed);
                Assert.Equal("notepad", destroyed.ProcessName, ignoreCase: true);
                Assert.NotNull(destroyed.Title); // enrichment happened before death
            }
            finally
            {
                Kill(proc);
            }
        }

        [Fact]
        public void Two_subscriptions_do_not_cross_feed()
        {
            if (!OperatingSystem.IsWindows())
                return;
            using var utils = new EventUtils();
            Assert.True(utils.Initialize());
            Assert.True(utils.Start("Windows"));
            Assert.True(utils.Subscribe("Windows", "{\"process\":\"notepad\"}", "subA"));
            Assert.True(utils.Subscribe("Windows", "{\"process\":\"nonexistentprocessxyz\"}", "subB"));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                var e = utils.GetNextEvent("subA", 10000, out bool hasEvent);
                Assert.True(hasEvent);
                Assert.NotNull(e);
                Assert.Equal("notepad", e.ProcessName, ignoreCase: true);
                Assert.False(utils.HasEvents("subB", out int countB));
                Assert.Equal(0, countB);
            }
            finally
            {
                Kill(proc);
            }
        }

        [Fact]
        public void Debounce_coalesces_rapid_show_hide()
        {
            if (!OperatingSystem.IsWindows())
                return;
            using var utils = new EventUtils();
            Assert.True(utils.Initialize());
            Assert.True(utils.Start("Windows"));
            Assert.True(utils.Subscribe("Windows", null, "deb"));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                var created = utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out _);
                Assert.NotNull(created);
                IntPtr hwnd = new IntPtr((long)created.Hwnd);
                Thread.Sleep(300);
                for (int i = 0; i < 10; i++)
                {
                    ShowWindow(hwnd, 0); // SW_HIDE
                    ShowWindow(hwnd, 5); // SW_SHOW
                }
                Thread.Sleep(400); // let the debounce window pass
                var events = utils.GetNextEvents("deb", 100, 0);
                int showCount = 0;
                foreach (var e in events)
                    if (e.Category == "WindowShown")
                        showCount++;
                Assert.True(showCount <= 3, "SHOW count after debounce was " + showCount);
            }
            finally
            {
                Kill(proc);
            }
        }

        [Fact]
        public void Stop_unhooks_events_stop_arriving()
        {
            if (!OperatingSystem.IsWindows())
                return;
            using var utils = new EventUtils();
            Assert.True(utils.Initialize());
            Assert.True(utils.Start("Windows"));
            Assert.True(utils.Subscribe("Windows", null, "s"));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                var e = utils.GetNextEvent("s", 10000, out bool has);
                Assert.True(has);
                Assert.NotNull(e);
                Assert.True(utils.Stop());
                utils.ClearQueue("s");
                using var proc2 = StartNotepad();
                if (proc2 == null)
                    return;
                try
                {
                    var e2 = utils.GetNextEvent("s", 1500, out bool has2);
                    Assert.False(has2);
                    Assert.Null(e2);
                }
                finally
                {
                    Kill(proc2);
                }
            }
            finally
            {
                Kill(proc);
            }
        }

        [Fact]
        public void Dispose_twice_is_safe_and_reinitialize_works()
        {
            if (!OperatingSystem.IsWindows())
                return;
            var utils = new EventUtils();
            Assert.True(utils.Initialize());
            utils.Dispose();
            utils.Dispose(); // second dispose is a no-op
            Assert.True(utils.Initialize()); // re-initialize after dispose works
            utils.Dispose();
        }

        [Fact]
        public void Stress_many_events_no_stall_and_bounded_ring()
        {
            if (!OperatingSystem.IsWindows())
                return;
            using var utils = new EventUtils();
            Assert.True(utils.Initialize());
            Assert.True(utils.Start("Windows"));
            Assert.True(utils.Subscribe("Windows", null, "stress"));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                var created = utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out _);
                Assert.NotNull(created);
                IntPtr hwnd = new IntPtr((long)created.Hwnd);
                Thread.Sleep(300);
                for (int i = 0; i < 1000; i++)
                {
                    ShowWindow(hwnd, 0);
                    ShowWindow(hwnd, 5);
                }
                // The pump must still be delivering: a fresh event arrives promptly.
                var e = utils.GetNextEvent("stress", 5000, out bool hasEvent);
                Assert.True(hasEvent, "pump stalled after stress");
                Assert.NotNull(e);
                // Ring buffer stays bounded at 500.
                var dump = utils.DumpRecentEvents(1000);
                using var doc = JsonDocument.Parse(dump);
                Assert.True(doc.RootElement.GetArrayLength() <= 500, "ring exceeded 500");
            }
            finally
            {
                Kill(proc);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static EventData Ev(string processName, string className, string title, uint processId = 0, string category = "WindowCreated")
        {
            return new EventData
            {
                EventId = Guid.NewGuid().ToString("N"),
                Category = category,
                Timestamp = DateTime.UtcNow.Ticks,
                Hwnd = 0x1234,
                ProcessName = processName,
                ProcessId = processId,
                ClassName = className,
                Title = title,
                State = null
            };
        }

        private static Process StartNotepad()
        {
            try
            {
                string path = Environment.GetFolderPath(Environment.SpecialFolder.System) + "\\notepad.exe";
                return Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
            }
            catch
            {
                return null;
            }
        }

        private static void Kill(Process proc)
        {
            try
            {
                if (proc != null && !proc.HasExited)
                    proc.Kill();
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
