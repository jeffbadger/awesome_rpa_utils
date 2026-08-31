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

        [Fact]
        public void Debounce_distinguishes_hwnds_differing_only_in_high_32_bits()
        {
            // Regression test for the Hwnd truncation bug: before the fix, ShouldDrop
            // took a uint, so a handle whose value only differs in its high 32 bits
            // collapsed to the same key as a different handle and was wrongly debounced
            // away as a duplicate of it.
            var d = new ThrottleDebounce();
            d.Set("WindowShown", 150);
            long low = 0x1234;
            long high = unchecked((long)0x1_0000_1234UL); // same low 32 bits, nonzero high bits
            Assert.False(d.ShouldDrop(low, "WindowShown"));  // first hwnd, kept
            Assert.False(d.ShouldDrop(high, "WindowShown")); // a genuinely different hwnd, also kept
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

            Assert.True(mgr.HasEvents("s1", out int count, out _));
            Assert.Equal(5, count);
            var first = mgr.GetNextEvent("s1", 0, out _, out _);
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

            Assert.True(mgr.HasEvents("s1", out int count, out _));
            Assert.Equal(3, count);
            Assert.Equal("t0", mgr.GetNextEvent("s1", 0, out _, out _).Title);
            Assert.Equal("t1", mgr.GetNextEvent("s1", 0, out _, out _).Title);
            Assert.Equal("t2", mgr.GetNextEvent("s1", 0, out _, out _).Title);
        }

        [Fact]
        public void Queue_overflow_block_never_exceeds_limit()
        {
            var mgr = new SubscriptionManager();
            Assert.True(mgr.TryAdd("s1", new HashSet<EventCategory> { EventCategory.Windows }, EventFilter.Create(), out _));
            mgr.SetQueueLimits(2, "Block");
            for (int i = 0; i < 10; i++)
                mgr.Deliver(Ev("notepad.exe", null, "t" + i, processId: 1), new List<EventCategory> { EventCategory.Windows }, 0);

            Assert.True(mgr.HasEvents("s1", out int count, out _));
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

            Assert.True(mgr.HasEvents("s1", out int count, out _));
            Assert.Equal(1, count); // only the notepad Windows event
            Assert.Equal("a", mgr.GetNextEvent("s1", 0, out _, out _).Title);
        }

        [Fact]
        public void Subscription_unknown_id_reports_message()
        {
            var mgr = new SubscriptionManager();
            Assert.False(mgr.HasEvents("nope", out int count, out string msg));
            Assert.Equal(0, count);
            Assert.NotNull(msg);
            Assert.Null(mgr.GetNextEvent("nope", 0, out bool has, out msg));
            Assert.False(has);
            Assert.NotNull(msg);
            Assert.False(mgr.TryRemove("nope", out msg));
            Assert.NotNull(msg);
            Assert.False(mgr.ClearQueue("nope", out msg));
            Assert.NotNull(msg);
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
        public void Subscribe_bad_json_returns_false_with_message_never_throws()
        {
            var utils = new EventUtils();
            try
            {
                Assert.False(utils.Subscribe("Windows", "{not json", "s1", out string m1));
                Assert.NotNull(m1);
                Assert.False(utils.Subscribe("Windows", "{\"process\":", "s2", out _));
                Assert.False(utils.Subscribe("Windows", "[]", "s3", out _));
                // Unknown keys are ignored → valid filter → True.
                Assert.True(utils.Subscribe("Windows", "{\"bogusKey\":1,\"process\":\"notepad\"}", "s4", out _));
                // Empty filter = match-all.
                Assert.True(utils.Subscribe("Windows", null, "s5", out _));
                Assert.True(utils.Subscribe("Windows", "", "s6", out _));
                // Duplicate id → False with a message.
                Assert.False(utils.Subscribe("Windows", null, "s5", out string m2));
                Assert.NotNull(m2);
                // Invalid category → False with a message.
                Assert.False(utils.Subscribe("Bogus", null, "s7", out string m3));
                Assert.NotNull(m3);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void WaitFor_without_start_returns_false_with_message()
        {
            var utils = new EventUtils();
            try
            {
                bool ok = utils.WaitForWindowCreated(null, 100, out EventData e, out bool timedOut, out string message);
                Assert.False(ok);
                Assert.Null(e);
                Assert.False(timedOut); // not a timeout — an engine-not-started error
                Assert.NotNull(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void GetNextEvent_unknown_subscription_returns_false_with_message()
        {
            var utils = new EventUtils();
            try
            {
                bool ok = utils.GetNextEvent("nope", 0, out EventData e, out bool hasEvent, out string message);
                Assert.False(ok);
                Assert.Null(e);
                Assert.False(hasEvent);
                Assert.NotNull(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void SetQueueLimits_invalid_args_return_false_with_message()
        {
            var utils = new EventUtils();
            try
            {
                Assert.False(utils.SetQueueLimits(0, "DropOldest", out string m1));
                Assert.NotNull(m1);
                Assert.False(utils.SetQueueLimits(10, "Bogus", out string m2));
                Assert.NotNull(m2);
                Assert.True(utils.SetQueueLimits(10, "Block", out _));
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

        [Fact]
        public void EventData_Hwnd_does_not_truncate_a_64_bit_handle()
        {
            long fullHandle = unchecked((long)0x00007FF6_12345678UL); // realistic 64-bit handle magnitude
            var e = new EventData { Hwnd = fullHandle };
            Assert.Equal(fullHandle, e.Hwnd);

            using var doc = JsonDocument.Parse(e.ToJson());
            Assert.Equal(fullHandle, doc.RootElement.GetProperty("Hwnd").GetInt64());
        }

        // ------------------------------------------------------------------
        // BuildFilterJson: scalar filter construction, no hand-authored JSON
        // ------------------------------------------------------------------

        [Fact]
        public void BuildFilterJson_noParameters_returnsMatchAll()
        {
            var utils = new EventUtils();
            try
            {
                Assert.Equal("{}", utils.BuildFilterJson());
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void BuildFilterJson_process_roundtrips_through_TryFromJson_and_matches()
        {
            var utils = new EventUtils();
            try
            {
                string json = utils.BuildFilterJson(process: "notepad");
                Assert.True(EventFilter.TryFromJson(json, out var filter, out string error));
                Assert.Null(error);
                Assert.True(filter.Matches(Ev("notepad.exe", null, null), 0));
                Assert.False(filter.Matches(Ev("calc.exe", null, null), 0));
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void BuildFilterJson_processesCsv_matches_any_listed_process()
        {
            var utils = new EventUtils();
            try
            {
                string json = utils.BuildFilterJson(processesCsv: "notepad, calc");
                Assert.True(EventFilter.TryFromJson(json, out var filter, out _));
                Assert.True(filter.Matches(Ev("notepad.exe", null, null), 0));
                Assert.True(filter.Matches(Ev("calc.exe", null, null), 0));
                Assert.False(filter.Matches(Ev("explorer.exe", null, null), 0));
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void BuildFilterJson_invalid_titleMatches_is_rejected_by_TryFromJson()
        {
            var utils = new EventUtils();
            try
            {
                string json = utils.BuildFilterJson(titleMatches: "[Bad");
                Assert.False(EventFilter.TryFromJson(json, out var filter, out string error));
                Assert.Null(filter);
                Assert.NotNull(error);
            }
            finally
            {
                utils.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // EventName / EventOverflowPolicy enum overloads
        // ------------------------------------------------------------------

        [Fact]
        public void SetDebounce_enum_overload_behaves_like_string_overload()
        {
            var utils = new EventUtils();
            try
            {
                Assert.True(utils.SetDebounce(EventName.WindowShown, 250, out string message));
                Assert.Null(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void SetQueueLimits_enum_overload_behaves_like_string_overload()
        {
            var utils = new EventUtils();
            try
            {
                Assert.True(utils.SetQueueLimits(10, EventOverflowPolicy.DropOldest, out string message));
                Assert.Null(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // JSON/handle-companion overloads: same guards as their EventData originals
        // ------------------------------------------------------------------

        [Fact]
        public void GetNextEventJson_unknown_subscription_returns_false_with_message()
        {
            var utils = new EventUtils();
            try
            {
                bool ok = utils.GetNextEvent("nope", 0, out string json, out IntPtr hwnd, out bool hasEvent, out string message);
                Assert.False(ok);
                Assert.Equal("{}", json);
                Assert.Equal(IntPtr.Zero, hwnd);
                Assert.False(hasEvent);
                Assert.NotNull(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void GetNextEventsJson_unknown_subscription_returns_false_with_message()
        {
            var utils = new EventUtils();
            try
            {
                bool ok = utils.GetNextEventsJson("nope", 10, 0, out string json, out string message);
                Assert.False(ok);
                Assert.Equal("[]", json);
                Assert.NotNull(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void WaitForWindowCreatedJson_without_start_returns_false_with_message()
        {
            var utils = new EventUtils();
            try
            {
                bool ok = utils.WaitForWindowCreated(null, 100, out string json, out IntPtr hwnd, out bool timedOut, out string message);
                Assert.False(ok);
                Assert.Equal("{}", json);
                Assert.Equal(IntPtr.Zero, hwnd);
                Assert.False(timedOut);
                Assert.NotNull(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // Review-fix behaviors: input rejection, isolation, wake-on-unsubscribe
        // ------------------------------------------------------------------

        [Fact]
        public void Subscribe_malformed_titleMatches_regex_is_rejected()
        {
            var utils = new EventUtils();
            try
            {
                Assert.False(utils.Subscribe("Windows", "{\"titleMatches\":\"[Bad\"}", "s1", out string m1));
                Assert.NotNull(m1);
                // A valid regex is still accepted.
                Assert.True(utils.Subscribe("Windows", "{\"titleMatches\":\"\\\\d{4}\"}", "s2", out _));
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void Subscribe_unknown_category_message_lists_unknown_names()
        {
            var utils = new EventUtils();
            try
            {
                Assert.False(utils.Subscribe("Windows,Windowz,Forground", null, "s1", out string m1));
                Assert.NotNull(m1);
                Assert.Contains("Windowz", m1);
                Assert.Contains("Forground", m1);
                // Unknown categories are rejected for Start as well — but Start
                // initializes the engine first, so only assert that on Windows.
                if (OperatingSystem.IsWindows())
                {
                    Assert.False(utils.Start("Windows,Bogus", out string m2));
                    Assert.Contains("Bogus", m2);
                }
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void WaitForTitleChanged_malformed_regex_returns_false_never_throws()
        {
            var utils = new EventUtils();
            try
            {
                // Validation happens before the engine check, so this runs anywhere.
                bool ok = utils.WaitForTitleChanged(null, "[Bad", 100, out EventData e, out bool timedOut, out string message);
                Assert.False(ok);
                Assert.Null(e);
                Assert.False(timedOut); // an input error, not a timeout
                Assert.NotNull(message);
                // A valid regex with no engine still fails via the engine check.
                bool ok2 = utils.WaitForStateChanged(null, "Visible", 100, out _, out bool timedOut2, out string message2);
                Assert.False(ok2);
                Assert.False(timedOut2);
                Assert.NotNull(message2);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void WaitFor_malformed_filter_json_returns_false_never_throws()
        {
            var utils = new EventUtils();
            try
            {
                bool ok = utils.WaitForWindowCreated("{not json", 100, out EventData e, out bool timedOut, out string message);
                Assert.False(ok);
                Assert.Null(e);
                Assert.False(timedOut);
                Assert.NotNull(message);
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public void Fluent_filter_invalid_regex_fails_closed()
        {
            var f = EventFilter.Create().TitleMatches("[Bad");
            Assert.False(f.Matches(Ev("notepad.exe", null, "Anything"), 0));
            // Other fields still narrow: with no regex error the filter matches.
            var valid = EventFilter.Create().Process("notepad");
            Assert.True(valid.Matches(Ev("notepad.exe", null, null), 0));
        }

        [Fact]
        public void Stop_before_initialize_returns_false_with_message()
        {
            var utils = new EventUtils();
            try
            {
                Assert.False(utils.Stop(out string message));
                Assert.NotNull(message);
                // After a real Start, Stop succeeds.
                if (OperatingSystem.IsWindows())
                {
                    Assert.True(utils.Initialize(out _));
                    Assert.True(utils.Start("Windows", out _));
                    Assert.True(utils.Stop(out _));
                }
            }
            finally
            {
                utils.Dispose();
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task Waiter_zero_and_negative_timeout_complete_immediately()
        {
            var reg = new WaiterRegistry();
            var t0 = reg.Register(e => true, 0);
            var tNeg = reg.Register(e => true, -5);
            Assert.Null(await t0);       // 0 = immediate timeout
            Assert.Null(await tNeg);
        }

        [Fact]
        public void Delivery_clones_events_per_subscription()
        {
            var mgr = new SubscriptionManager();
            Assert.True(mgr.TryAdd("a", new HashSet<EventCategory> { EventCategory.Windows }, EventFilter.Create(), out _));
            Assert.True(mgr.TryAdd("b", new HashSet<EventCategory> { EventCategory.Windows }, EventFilter.Create(), out _));
            mgr.Deliver(Ev("notepad.exe", null, "orig", processId: 1), new List<EventCategory> { EventCategory.Windows }, 0);

            var a = mgr.GetNextEvent("a", 0, out _, out _);
            a.Title = "mutated-by-consumer-a";
            var b = mgr.GetNextEvent("b", 0, out _, out _);
            Assert.Equal("orig", b.Title); // b did not see a's mutation
        }

        [Fact]
        public async System.Threading.Tasks.Task Unsubscribe_wakes_blocked_GetNextEvent()
        {
            var mgr = new SubscriptionManager();
            Assert.True(mgr.TryAdd("s", new HashSet<EventCategory> { EventCategory.Windows }, EventFilter.Create(), out _));
            var wait = System.Threading.Tasks.Task.Run(() =>
                mgr.GetNextEvent("s", 10000, out _, out string message));
            Thread.Sleep(100); // let it park inside the wait
            Assert.True(mgr.TryRemove("s", out _));
            await wait.WaitAsync(TimeSpan.FromMilliseconds(1000)); // throws (fails) if Unsubscribe did not wake the waiter
        }

        [Fact]
        public void Debounce_prunes_stale_entries()
        {
            var d = new ThrottleDebounce();
            for (uint i = 1; i <= 600; i++)
                d.ShouldDrop(i, "WindowShown"); // 600 distinct keys, default 150 ms window
            Assert.True(d.CachedKeys > 512);
            Thread.Sleep(200); // let every window elapse
            d.ShouldDrop(99999, "WindowShown"); // count exceeded → prune pass
            Assert.True(d.CachedKeys <= 2, "stale entries were not pruned: " + d.CachedKeys);
            // The still-live key (99999) is tracked again, and pruning does not break debounce.
            Assert.False(d.ShouldDrop(99998, "WindowShown"));
        }

        [Fact]
        public void WasWindowCreated_malformed_filter_json_returns_false_never_throws()
        {
            var utils = new EventUtils();
            try
            {
                bool ok = utils.WasWindowCreated("{not json", 500, out bool wasCreated, out string message);
                Assert.False(ok);
                Assert.False(wasCreated);
                Assert.NotNull(message);
            }
            finally
            {
                utils.Dispose();
            }
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
            Assert.True(utils.Initialize(out _));
            Assert.True(utils.Start("Windows", out _));
            using var proc = StartNotepad();
            if (proc == null)
                return; // notepad unavailable on this image
            try
            {
                bool ok = utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out EventData e, out bool timedOut, out _);
                Assert.True(ok);
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
            Assert.True(utils.Initialize(out _));
            Assert.True(utils.Start("Windows", out _));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                Assert.True(utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out EventData created, out _, out _));
                Assert.NotNull(created);
                Thread.Sleep(300); // let the window finish coming up
                bool ok = utils.WaitForWindowDestroyed("{\"process\":\"notepad\"}", 10000, out EventData destroyed, out bool timedOut, out _);
                Kill(proc); // close the window → DESTROY event
                Assert.True(ok);
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
            Assert.True(utils.Initialize(out _));
            Assert.True(utils.Start("Windows", out _));
            Assert.True(utils.Subscribe("Windows", "{\"process\":\"notepad\"}", "subA", out _));
            Assert.True(utils.Subscribe("Windows", "{\"process\":\"nonexistentprocessxyz\"}", "subB", out _));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                bool ok = utils.GetNextEvent("subA", 10000, out EventData e, out bool hasEvent, out _);
                Assert.True(ok);
                Assert.True(hasEvent);
                Assert.NotNull(e);
                Assert.Equal("notepad", e.ProcessName, ignoreCase: true);
                Assert.True(utils.HasEvents("subB", out int countB, out _));
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
            Assert.True(utils.Initialize(out _));
            Assert.True(utils.Start("Windows", out _));
            Assert.True(utils.Subscribe("Windows", null, "deb", out _));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                Assert.True(utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out EventData created, out _, out _));
                Assert.NotNull(created);
                IntPtr hwnd = new IntPtr((long)created.Hwnd);
                Thread.Sleep(300);
                for (int i = 0; i < 10; i++)
                {
                    ShowWindow(hwnd, 0); // SW_HIDE
                    ShowWindow(hwnd, 5); // SW_SHOW
                }
                Thread.Sleep(400); // let the debounce window pass
                Assert.True(utils.GetNextEvents("deb", 100, 0, out EventData[] events, out _));
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
            Assert.True(utils.Initialize(out _));
            Assert.True(utils.Start("Windows", out _));
            Assert.True(utils.Subscribe("Windows", null, "s", out _));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                Assert.True(utils.GetNextEvent("s", 10000, out EventData e, out bool has, out _));
                Assert.True(has);
                Assert.NotNull(e);
                Assert.True(utils.Stop(out _));
                Assert.True(utils.ClearQueue("s", out _));
                using var proc2 = StartNotepad();
                if (proc2 == null)
                    return;
                try
                {
                    Assert.True(utils.GetNextEvent("s", 1500, out EventData e2, out bool has2, out _));
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
        public void Dispose_twice_is_safe_and_reinit_is_refused()
        {
            if (!OperatingSystem.IsWindows())
                return;
            var utils = new EventUtils();
            Assert.True(utils.Initialize(out _));
            utils.Dispose();
            utils.Dispose(); // second dispose is a no-op
            // The instance is final after Dispose — create a new one instead.
            Assert.False(utils.Initialize(out string message));
            Assert.NotNull(message);
            utils.Dispose();
        }

        [Fact]
        public void Stress_many_events_no_stall_and_bounded_ring()
        {
            if (!OperatingSystem.IsWindows())
                return;
            using var utils = new EventUtils();
            Assert.True(utils.Initialize(out _));
            Assert.True(utils.Start("Windows", out _));
            Assert.True(utils.Subscribe("Windows", null, "stress", out _));
            using var proc = StartNotepad();
            if (proc == null)
                return;
            try
            {
                Assert.True(utils.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000, out EventData created, out _, out _));
                Assert.NotNull(created);
                IntPtr hwnd = new IntPtr((long)created.Hwnd);
                Thread.Sleep(300);
                for (int i = 0; i < 1000; i++)
                {
                    ShowWindow(hwnd, 0);
                    ShowWindow(hwnd, 5);
                }
                // The pump must still be delivering: a fresh event arrives promptly.
                Assert.True(utils.GetNextEvent("stress", 5000, out EventData e, out bool hasEvent, out _));
                Assert.True(hasEvent, "pump stalled after stress");
                Assert.NotNull(e);
                // Ring buffer stays bounded at 500.
                Assert.True(utils.DumpRecentEvents(1000, out string dump, out _));
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
