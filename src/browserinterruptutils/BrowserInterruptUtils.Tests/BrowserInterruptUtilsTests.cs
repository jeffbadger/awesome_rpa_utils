using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    /// <summary>
    /// Tests the public <see cref="BrowserInterruptUtils"/> API against the internal DI
    /// constructor with <see cref="FakeBrowserPopupProbe"/>/<see cref="FakeBrowserPopupHookSource"/>
    /// - never real UI Automation. Mirrors <c>InterruptUtilsTests</c>: rule-method translation,
    /// Start/Stop/Pause/Resume lifecycle (including the instance guard), end-to-end events fired
    /// on the worker thread, JSON shapes, and never-throws behavior. The engine's own decision
    /// logic (matching, retries, runaway trips, overlay discovery gating, etc.) is already covered
    /// by <c>BrowserPopupEngineTests</c> and is not re-tested here.
    /// </summary>
    public class BrowserInterruptUtilsTests
    {
        private sealed class Rig : IDisposable
        {
            public readonly FakeBrowserPopupProbe Probe = new FakeBrowserPopupProbe();
            public readonly FakeBrowserPopupHookSource Hook = new FakeBrowserPopupHookSource();
            public readonly BrowserInterruptUtils Utils;

            public Rig(IInstanceGuard guard = null) => Utils = new BrowserInterruptUtils(Probe, Hook, guard);

            public void Dispose() => Utils.Dispose();

            public void StartOk(int sweepMs = 0, int overlaySweepMs = 0, int maxDismissalsPerMinute = 20)
            {
                Assert.True(Utils.Start(out string m, sweepIntervalMs: sweepMs, overlaySweepIntervalMs: overlaySweepMs,
                    maxDismissalsPerMinute: maxDismissalsPerMinute), m);
                Assert.Null(m);
            }
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount - deadline < 0)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        // ------------------------------------------------------------------ rule methods: translation

        [Fact]
        public void AddNativeDialogDismissRuleByName_TranslatesToTheRightRuleShape()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Session", "expire", "app", "Yes",
                out string message, exactTargetElementName: false, roleContains: "dialog"));
            Assert.Null(message);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            var rule = doc.RootElement[0];
            Assert.Equal("r", rule.GetProperty("name").GetString());
            Assert.Equal("NativeDialog", rule.GetProperty("scope").GetString());
            Assert.Equal("InvokeByName", rule.GetProperty("action").GetString());
            Assert.Equal("Session", rule.GetProperty("nameContains").GetString());
            Assert.Equal("expire", rule.GetProperty("messageContains").GetString());
            Assert.Equal("app", rule.GetProperty("processName").GetString());
            Assert.Equal("dialog", rule.GetProperty("roleContains").GetString());
            Assert.Equal("Yes", rule.GetProperty("target").GetString());
            Assert.False(rule.GetProperty("exactTargetName").GetBoolean());
            Assert.True(rule.GetProperty("enabled").GetBoolean());
            Assert.False(rule.GetProperty("stopped").GetBoolean());
            Assert.Equal(0, rule.GetProperty("dismissals").GetInt32());
        }

        [Fact]
        public void AddNativeDialogDismissRuleByAutomationId_TranslatesToTheRightRuleShape()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.AddNativeDialogDismissRuleByAutomationId("r", "Session", "", "app", "okBtn", out string message));
            Assert.Null(message);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            var rule = doc.RootElement[0];
            Assert.Equal("InvokeByAutomationId", rule.GetProperty("action").GetString());
            Assert.Equal("okBtn", rule.GetProperty("target").GetString());
        }

        [Fact]
        public void AddNativeDialogCloseRule_TranslatesToTheRightRuleShape()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogCloseRule("r", "Session", "", "app", out string message));
            Assert.Null(message);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            var rule = doc.RootElement[0];
            Assert.Equal("CloseWindowPattern", rule.GetProperty("action").GetString());
            Assert.Equal(string.Empty, rule.GetProperty("target").GetString());
        }

        [Fact]
        public void AddNativeDialogWatchOnlyRule_TranslatesToTheRightRuleShape()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("r", "Session", "", "app", out string message));
            Assert.Null(message);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("WatchOnly", doc.RootElement[0].GetProperty("action").GetString());
        }

        [Fact]
        public void AddPageOverlayDismissRuleByName_TranslatesToTheRightRuleShape_IncludingAutomationIdContains()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddPageOverlayDismissRuleByName("r", "Cookie", "", "chrome", "Accept",
                out string message, exactTargetElementName: false, roleContains: "dialog", automationIdContains: "consent"));
            Assert.Null(message);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            var rule = doc.RootElement[0];
            Assert.Equal("PageOverlay", rule.GetProperty("scope").GetString());
            Assert.Equal("InvokeByName", rule.GetProperty("action").GetString());
            Assert.Equal("Accept", rule.GetProperty("target").GetString());
            Assert.Equal("consent", rule.GetProperty("automationIdContains").GetString());
            Assert.False(rule.GetProperty("exactTargetName").GetBoolean());
        }

        [Fact]
        public void AddPageOverlayDismissRuleByAutomationId_TranslatesToTheRightRuleShape()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddPageOverlayDismissRuleByAutomationId("r", "Cookie", "", "chrome", "accept-btn", out string message));
            Assert.Null(message);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            var rule = doc.RootElement[0];
            Assert.Equal("InvokeByAutomationId", rule.GetProperty("action").GetString());
            Assert.Equal("accept-btn", rule.GetProperty("target").GetString());
        }

        [Fact]
        public void AddPageOverlayWatchOnlyRule_TranslatesToTheRightRuleShape_IncludingAutomationIdContains()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddPageOverlayWatchOnlyRule("r", "Cookie", "", "chrome", out string message,
                automationIdContains: "consent"));
            Assert.Null(message);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            var rule = doc.RootElement[0];
            Assert.Equal("WatchOnly", rule.GetProperty("action").GetString());
            Assert.Equal("consent", rule.GetProperty("automationIdContains").GetString());
        }

        [Fact]
        public void RulesWithNoCriterion_AreRefused_SoNoRuleCanMatchEveryPopup()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddNativeDialogDismissRuleByName("r", "", "", "", "Yes", out string m1));
            Assert.Contains("at least one", m1);
            Assert.False(rig.Utils.AddNativeDialogDismissRuleByAutomationId("r", " ", null, "", "id", out string m2));
            Assert.Contains("at least one", m2);
            Assert.False(rig.Utils.AddNativeDialogCloseRule("r", null, null, null, out string m3));
            Assert.Contains("at least one", m3);
            Assert.False(rig.Utils.AddNativeDialogWatchOnlyRule("r", "", "", "", out string m4));
            Assert.Contains("at least one", m4);
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByName("r", "", "", "", "Yes", out string m5));
            Assert.Contains("at least one", m5);
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByAutomationId("r", "", "", "", "id", out string m6));
            Assert.Contains("at least one", m6);
            Assert.False(rig.Utils.AddPageOverlayWatchOnlyRule("r", "", "", "", out string m7));
            Assert.Contains("at least one", m7);
        }

        [Fact]
        public void EmptyTargetName_OrAutomationId_IsRefused_BeforeReachingTheEngine()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddNativeDialogDismissRuleByName("r", "t", "", "", "   ", out string m1));
            Assert.Contains("targetElementName", m1);
            Assert.False(rig.Utils.AddNativeDialogDismissRuleByAutomationId("r", "t", "", "", "", out string m2));
            Assert.Contains("targetAutomationId", m2);
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByName("r", "t", "", "", null, out string m3));
            Assert.Contains("targetElementName", m3);
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByAutomationId("r", "t", "", "", " ", out string m4));
            Assert.Contains("targetAutomationId", m4);

            // No rule was actually added for any of the above.
            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            Assert.Equal("[]", json);
        }

        [Fact]
        public void ProcessNameThatTrimsToNothing_DoesNotCountAsACriterion()
        {
            using var rig = new Rig();
            Assert.False(rig.Utils.AddNativeDialogWatchOnlyRule("r", "", "", ".exe", out string message));
            Assert.Contains("at least one", message);
        }

        [Fact]
        public void DuplicateRuleName_IsRefused_IgnoringCase()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("Alpha", "t", "", "", out _));

            Assert.False(rig.Utils.AddNativeDialogCloseRule("ALPHA", "u", "", "", out string message));
            Assert.Contains("already exists", message);
        }

        [Fact]
        public void RuleNameValidation_IsEnforced()
        {
            using var rig = new Rig();
            Assert.False(rig.Utils.AddNativeDialogWatchOnlyRule("", "t", "", "", out string noName));
            Assert.Contains("ruleName", noName);
            Assert.False(rig.Utils.AddNativeDialogWatchOnlyRule(new string('x', 65), "t", "", "", out string tooLong));
            Assert.Contains("ruleName", tooLong);
        }

        [Fact]
        public void RemoveClearAndEnable_ReportUnknownRules()
        {
            using var rig = new Rig();
            Assert.False(rig.Utils.RemoveRule("nope", out string m1));
            Assert.Contains("nope", m1);
            Assert.False(rig.Utils.SetRuleEnabled("nope", true, out string m2));
            Assert.Contains("nope", m2);
            Assert.False(rig.Utils.GetDismissalCount("nope", out int count, out string m3));
            Assert.Equal(0, count);
            Assert.Contains("nope", m3);

            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("a", "t", "", "", out _));
            Assert.True(rig.Utils.SetRuleEnabled("a", false, out _));
            Assert.True(rig.Utils.RemoveRule("a", out _));
            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("b", "t", "", "", out _));
            Assert.True(rig.Utils.ClearRules(out _));
            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            Assert.Equal("[]", json);
        }

        // ------------------------------------------------------------------ lifecycle

        [Fact]
        public void StartStop_TogglesRunning_AndIsIdempotent()
        {
            using var rig = new Rig();
            Assert.False(rig.Utils.IsRunning());
            Assert.True(rig.Utils.Stop(out string notRunning)); // stopping when not running is fine
            Assert.Null(notRunning);

            rig.StartOk();
            Assert.True(rig.Utils.IsRunning());
            Assert.False(rig.Utils.Start(out string again));
            Assert.Contains("Already running", again);

            Assert.True(rig.Utils.Stop(out _));
            Assert.False(rig.Utils.IsRunning());
            Assert.True(rig.Utils.Stop(out _));
            Assert.Equal(1, rig.Hook.StartCalls);
            Assert.Equal(1, rig.Hook.StopCalls);
        }

        [Fact]
        public void Start_RefusesOutOfRangeSettings_AndDoesNotStart()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.Start(out string m1, sweepIntervalMs: -1));
            Assert.Contains("sweepIntervalMs", m1);
            Assert.False(rig.Utils.Start(out string m2, sweepIntervalMs: 60001));
            Assert.Contains("sweepIntervalMs", m2);
            Assert.False(rig.Utils.Start(out string m3, overlaySweepIntervalMs: -1));
            Assert.Contains("overlaySweepIntervalMs", m3);
            Assert.False(rig.Utils.Start(out string m4, overlaySweepIntervalMs: 60001));
            Assert.Contains("overlaySweepIntervalMs", m4);
            Assert.False(rig.Utils.Start(out string m5, maxAttempts: 0));
            Assert.Contains("maxAttempts", m5);
            Assert.False(rig.Utils.Start(out string m6, maxAttempts: 11));
            Assert.Contains("maxAttempts", m6);
            Assert.False(rig.Utils.Start(out string m7, maxDismissalsPerMinute: 0));
            Assert.Contains("maxDismissalsPerMinute", m7);
            Assert.False(rig.Utils.Start(out string m8, maxOverlayNodes: 0));
            Assert.Contains("maxOverlayNodes", m8);
            Assert.False(rig.Utils.Start(out string m9, maxOverlayDepth: 0));
            Assert.Contains("maxOverlayDepth", m9);
            Assert.False(rig.Utils.IsRunning());
            Assert.Equal(0, rig.Hook.StartCalls);
        }

        [Fact]
        public void Start_ReportsAHookFailure_AndIsNotLeftRunning()
        {
            using var rig = new Rig();
            rig.Hook.FailToStart = true;

            Assert.False(rig.Utils.Start(out string message));

            Assert.Equal("fake hook failed to start", message);
            Assert.False(rig.Utils.IsRunning());

            rig.Hook.FailToStart = false;
            rig.StartOk(); // and a later attempt can succeed
        }

        [Fact]
        public void StopThenStart_KeepsRulesAndCounts()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            rig.StartOk();
            var w = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w, "Yes");
            rig.Hook.FireWindowOpened(w);
            Assert.True(WaitFor(() => !w.Alive));
            // A dismissal is only counted once verified (VerifyDelayMs later); Stop before that drops it.
            Assert.True(WaitFor(() => rig.Utils.GetDismissalCount("r", out int first, out _) && first == 1));
            Assert.True(rig.Utils.Stop(out _));

            rig.StartOk();
            var w2 = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w2, "Yes");
            rig.Hook.FireWindowOpened(w2);
            Assert.True(WaitFor(() => !w2.Alive));

            Assert.True(WaitFor(() =>
            {
                rig.Utils.GetDismissalCount("r", out int count, out _);
                return count == 2;
            }));
        }

        [Fact]
        public void EveryMethod_AfterDispose_ReportsFailure_WithoutThrowing()
        {
            var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("r", "t", "", "", out _));
            rig.StartOk();
            rig.Utils.Dispose();

            Assert.False(rig.Utils.IsRunning());
            Assert.False(rig.Utils.Start(out string start));
            Assert.Contains("disposed", start);
            Assert.True(rig.Utils.Stop(out _));
            Assert.False(rig.Utils.Pause(out string pause));
            Assert.Contains("disposed", pause);
            Assert.False(rig.Utils.Resume(out _));
            Assert.False(rig.Utils.AddNativeDialogWatchOnlyRule("x", "t", "", "", out string add));
            Assert.Contains("disposed", add);
            Assert.False(rig.Utils.AddNativeDialogCloseRule("x", "t", "", "", out _));
            Assert.False(rig.Utils.AddNativeDialogDismissRuleByAutomationId("x", "t", "", "", "id", out _));
            Assert.False(rig.Utils.AddNativeDialogDismissRuleByName("x", "t", "", "", "Yes", out _));
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByName("x", "t", "", "", "Yes", out _));
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByAutomationId("x", "t", "", "", "id", out _));
            Assert.False(rig.Utils.AddPageOverlayWatchOnlyRule("x", "t", "", "", out _));
            Assert.False(rig.Utils.RemoveRule("r", out _));
            Assert.False(rig.Utils.ClearRules(out _));
            Assert.False(rig.Utils.SetRuleEnabled("r", true, out _));
            Assert.False(rig.Utils.ListRulesJson(out string rules, out _));
            Assert.Equal(string.Empty, rules);
            Assert.False(rig.Utils.GetDismissalCount("r", out _, out _));
            Assert.False(rig.Utils.GetTotalDismissals(out _, out _));
            Assert.False(rig.Utils.HasUnresolvedPopup(out _, out _));
            Assert.False(rig.Utils.GetLastEventJson(out string last, out _));
            Assert.Equal(string.Empty, last);
            Assert.False(rig.Utils.GetLogJson(10, out _, out _));
            Assert.False(rig.Utils.ClearLog(out _));
            rig.Utils.Dispose(); // and disposing twice is fine
        }

        [Fact]
        public void Dispose_StopsTheWatch()
        {
            var rig = new Rig();
            rig.StartOk();

            rig.Utils.Dispose();

            Assert.Equal(1, rig.Hook.StopCalls);
        }

        // ------------------------------------------------------------------ one watcher at a time

        private static string GuardName() => "Local\\BrowserInterruptUtilsTests." + Guid.NewGuid().ToString("N");

        [Fact]
        public void Start_StopsAnotherRunningInstanceThatSharesTheGuard()
        {
            string name = GuardName();
            using var first = new Rig(new NamedInstanceGuard(name, 5000));
            using var second = new Rig(new NamedInstanceGuard(name, 5000));
            first.StartOk();

            second.StartOk();

            Assert.True(WaitFor(() => !first.Utils.IsRunning()));
            Assert.True(second.Utils.IsRunning());
        }

        [Fact]
        public void InstancesWithDifferentGuards_DoNotAffectEachOther()
        {
            using var first = new Rig(new NamedInstanceGuard(GuardName(), 2000));
            using var second = new Rig(new NamedInstanceGuard(GuardName(), 2000));
            first.StartOk();

            second.StartOk();

            Assert.True(first.Utils.IsRunning());
            Assert.True(second.Utils.IsRunning());
        }

        [Fact]
        public void AFailedStart_GivesTheGuardBack_SoAnotherInstanceCanStart()
        {
            string name = GuardName();
            using var first = new Rig(new NamedInstanceGuard(name, 2000));
            first.Hook.FailToStart = true;

            Assert.False(first.Utils.Start(out _));

            using var second = new Rig(new NamedInstanceGuard(name, 2000));
            second.StartOk();
            Assert.True(second.Utils.IsRunning());
        }

        // ------------------------------------------------------------------ end-to-end events on the worker thread

        [Fact]
        public void NativeDialog_MatchedByDismissByNameRule_IsInvokedAndReportedEverywhere()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("session", "Session", "expire", "targetapp", "Yes", out _));
            var raised = new List<BrowserPopupEventArgs>();
            var done = new ManualResetEventSlim(false);
            rig.Utils.PopupDismissed += (s, e) => { lock (raised) raised.Add(e); done.Set(); };
            rig.StartOk();

            var w = rig.Probe.AddWindow("Session Timeout", "Your session will expire.", pid: 200, processName: "targetapp");
            rig.Probe.AddChild(w, "Yes");
            rig.Hook.FireWindowOpened(w);

            Assert.True(done.Wait(5000), "PopupDismissed was not raised");
            Assert.False(w.Alive);
            BrowserPopupEventArgs e;
            lock (raised) e = Assert.Single(raised);
            Assert.Equal("session", e.RuleName);
            Assert.Equal("NativeDialog", e.Scope);
            Assert.Equal("Session Timeout", e.Name);
            Assert.Equal("Your session will expire.", e.MessageText);
            Assert.Equal("targetapp", e.ProcessName);
            Assert.Equal("Yes", e.TargetInvoked);
            Assert.Equal(1, e.Attempts);
            Assert.Equal(string.Empty, e.Detail);

            Assert.True(rig.Utils.GetDismissalCount("session", out int count, out _));
            Assert.Equal(1, count);
            Assert.True(rig.Utils.GetTotalDismissals(out int total, out _));
            Assert.Equal(1, total);
            Assert.True(rig.Utils.HasUnresolvedPopup(out bool unresolved, out _));
            Assert.False(unresolved);

            Assert.True(rig.Utils.GetLastEventJson(out string lastJson, out _));
            using (var doc = JsonDocument.Parse(lastJson))
            {
                Assert.Equal("Dismissed", doc.RootElement.GetProperty("kind").GetString());
                Assert.Equal("session", doc.RootElement.GetProperty("rule").GetString());
                Assert.Equal("NativeDialog", doc.RootElement.GetProperty("scope").GetString());
                Assert.Equal("Yes", doc.RootElement.GetProperty("target").GetString());
            }
            Assert.True(rig.Utils.GetLogJson(10, out string logJson, out _));
            using (var doc = JsonDocument.Parse(logJson))
                Assert.Equal(1, doc.RootElement.GetArrayLength());

            Assert.True(rig.Utils.ClearLog(out _));
            Assert.True(rig.Utils.GetLastEventJson(out string emptyLast, out _));
            Assert.Equal("{}", emptyLast);
            Assert.True(rig.Utils.GetLogJson(10, out string emptyLog, out _));
            Assert.Equal("[]", emptyLog);
        }

        [Fact]
        public void PageOverlay_MatchedByDismissByAutomationIdRule_IsInvokedAndReportedEverywhere()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddPageOverlayDismissRuleByAutomationId("cookies", "Cookie", "", "chrome", "accept-btn", out _));
            var raised = new List<BrowserPopupEventArgs>();
            var done = new ManualResetEventSlim(false);
            rig.Utils.PopupDismissed += (s, e) => { lock (raised) raised.Add(e); done.Set(); };
            rig.StartOk();

            var browserWindow = rig.Probe.AddWindow("Google Chrome", pid: 300, processName: "chrome");
            rig.Hook.FireWindowOpened(browserWindow);
            Assert.True(WaitFor(() => rig.Hook.CurrentlyWatched.Contains(browserWindow.Ref)),
                "the browser window was never watched for overlay discovery");

            var overlay = rig.Probe.AddOverlay(browserWindow, "Cookie consent", "We use cookies", pid: 300);
            rig.Probe.AddChild(overlay, "Accept", automationId: "accept-btn");
            rig.Hook.FireStructureChanged(browserWindow.Ref);

            Assert.True(done.Wait(5000), "PopupDismissed was not raised");
            Assert.False(overlay.Alive);
            BrowserPopupEventArgs e;
            lock (raised) e = Assert.Single(raised);
            Assert.Equal("cookies", e.RuleName);
            Assert.Equal("PageOverlay", e.Scope);
            Assert.Equal("Cookie consent", e.Name);
            Assert.Equal("We use cookies", e.MessageText);
            Assert.Equal("chrome", e.ProcessName);
            Assert.Equal("Accept", e.TargetInvoked);
        }

        [Fact]
        public void WatchOnlyPopup_RaisesPopupDetected_AndIsLeftOpen()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("w", "Alert", "", "", out _));
            var detected = new ManualResetEventSlim(false);
            BrowserPopupEventArgs args = null;
            rig.Utils.PopupDetected += (s, e) => { args = e; detected.Set(); };
            rig.StartOk();

            var w = rig.Probe.AddWindow("Alert", "hi");
            rig.Hook.FireWindowOpened(w);

            Assert.True(detected.Wait(5000));
            Assert.True(w.Alive);
            Assert.Equal("w", args.RuleName);
            Assert.Equal(0, args.Attempts);
        }

        [Fact]
        public void PopupThatCannotBeDismissed_RaisesPopupDismissFailed_AndIsUnresolved()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            var failed = new ManualResetEventSlim(false);
            BrowserPopupEventArgs args = null;
            rig.Utils.PopupDismissFailed += (s, e) => { args = e; failed.Set(); };
            rig.StartOk();

            var w = rig.Probe.AddWindow("Alert");
            var button = rig.Probe.AddChild(w, "Yes");
            button.IgnoreInvoke = true;
            rig.Hook.FireWindowOpened(w);

            Assert.True(failed.Wait(10000), "PopupDismissFailed was not raised");
            Assert.Contains("still open", args.Detail);
            Assert.True(WaitFor(() =>
            {
                rig.Utils.HasUnresolvedPopup(out bool u, out _);
                return u;
            }));
        }

        [Fact]
        public void RunawayRule_RaisesInterruptError()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("nag", "Nag", "", "", "Yes", out _));
            var error = new ManualResetEventSlim(false);
            BrowserInterruptErrorEventArgs args = null;
            rig.Utils.InterruptError += (s, e) => { args = e; error.Set(); };
            rig.StartOk(maxDismissalsPerMinute: 2);

            for (int i = 0; i < 3; i++)
            {
                var w = rig.Probe.AddWindow("Nag");
                rig.Probe.AddChild(w, "Yes");
                rig.Hook.FireWindowOpened(w);
                int expected = i + 1;
                if (i < 2) // wait for the verified count, not just the click: only confirmed dismissals feed the breaker
                    Assert.True(WaitFor(() => rig.Utils.GetDismissalCount("nag", out int n, out _) && n == expected));
                else
                    Assert.True(error.Wait(5000));
            }

            Assert.Equal("nag", args.RuleName);
            Assert.Contains("SetRuleEnabled", args.Message);
            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement[0].GetProperty("stopped").GetBoolean());
        }

        [Fact]
        public void ASubscriberThatThrows_DoesNotStopOtherSubscribersOrTheWatch()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            int secondSeen = 0;
            rig.Utils.PopupDismissed += (s, e) => throw new InvalidOperationException("boom");
            rig.Utils.PopupDismissed += (s, e) => Interlocked.Increment(ref secondSeen);
            rig.StartOk();

            for (int i = 0; i < 2; i++)
            {
                var w = rig.Probe.AddWindow("Alert");
                rig.Probe.AddChild(w, "Yes");
                rig.Hook.FireWindowOpened(w);
                Assert.True(WaitFor(() => !w.Alive));
            }

            Assert.True(WaitFor(() => Volatile.Read(ref secondSeen) == 2));
            Assert.True(rig.Utils.IsRunning());
        }

        [Fact]
        public void Pause_WaitsForAnInvokeAlreadyInFlight_ThenNothingFurtherLands()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            rig.StartOk();
            var entered = new ManualResetEventSlim(false);
            var gate = new ManualResetEventSlim(false);
            rig.Probe.ActionEntered = () => entered.Set();
            rig.Probe.ActionGate = gate;
            var first = rig.Probe.AddWindow("Alert");
            var firstYes = rig.Probe.AddChild(first, "Yes");
            rig.Hook.FireWindowOpened(first);
            Assert.True(entered.Wait(5000), "the invoke never started");

            bool result = false;
            var pausing = new Thread(() => result = rig.Utils.Pause(out _)) { IsBackground = true };
            pausing.Start();
            Assert.False(pausing.Join(400), "Pause returned while an invoke was still in flight");
            gate.Set();
            Assert.True(pausing.Join(5000), "Pause did not return once the invoke finished");
            Assert.True(result);
            Assert.False(first.Alive);

            var second = rig.Probe.AddWindow("Alert");
            var secondYes = rig.Probe.AddChild(second, "Yes");
            rig.Hook.FireWindowOpened(second);
            Thread.Sleep(600);
            Assert.True(second.Alive);
            Assert.Equal(0, secondYes.Invokes);
            Assert.Equal(1, firstYes.Invokes);
        }

        [Theory]
        [InlineData("Pause")]
        [InlineData("RemoveRule")]
        [InlineData("ClearRules")]
        [InlineData("SetRuleEnabledFalse")]
        public void HardStopCalledFromAnEventSubscriber_OnTheWorkerThread_DoesNotDeadlock(string how)
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            var done = new ManualResetEventSlim(false);
            bool result = false;
            rig.Utils.PopupDismissed += (s, e) =>
            {
                string m;
                switch (how)
                {
                    case "Pause": result = rig.Utils.Pause(out m); break;
                    case "RemoveRule": result = rig.Utils.RemoveRule("r", out m); break;
                    case "ClearRules": result = rig.Utils.ClearRules(out m); break;
                    default: result = rig.Utils.SetRuleEnabled("r", false, out m); break;
                }
                done.Set();
            };
            rig.StartOk();
            var w = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w, "Yes");
            rig.Hook.FireWindowOpened(w);

            Assert.True(done.Wait(TimeSpan.FromSeconds(10)), how + " from a PopupDismissed subscriber did not return (deadlock)");
            Assert.True(result);
            Assert.True(rig.Utils.IsRunning());
        }

        [Fact]
        public void StopCalledFromAnEventSubscriber_OnTheWorkerThread_ReturnsPromptlyWithoutASelfJoinStall()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            var done = new ManualResetEventSlim(false);
            bool stopped = false;
            TimeSpan elapsed = TimeSpan.Zero;
            rig.Utils.PopupDismissed += (s, e) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                stopped = rig.Utils.Stop(out _);
                elapsed = sw.Elapsed;
                done.Set();
            };
            rig.StartOk();
            var w = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w, "Yes");
            rig.Hook.FireWindowOpened(w);

            Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "Stop from a PopupDismissed subscriber did not return");
            Assert.True(stopped);
            Assert.True(elapsed < TimeSpan.FromSeconds(2), "Stop stalled for " + elapsed + " (a self-join)");
            Assert.False(rig.Utils.IsRunning());

            // The worker finishes its pass and releases the run itself, so the component can be started again.
            Assert.True(WaitFor(() => rig.Utils.Start(out _), 10000), "the component could not be restarted after Stop from a subscriber");
            Assert.True(rig.Utils.IsRunning());
        }

        [Fact]
        public void Events_AreRaisedOnTheWorkerThread_NotTheCallersThread()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            int raisedOn = -1;
            string raisedName = null;
            var done = new ManualResetEventSlim(false);
            rig.Utils.PopupDismissed += (s, e) =>
            {
                raisedOn = Environment.CurrentManagedThreadId;
                raisedName = Thread.CurrentThread.Name;
                done.Set();
            };
            rig.StartOk();
            var w = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w, "Yes");
            rig.Hook.FireWindowOpened(w);

            Assert.True(done.Wait(5000), "PopupDismissed was not raised");
            Assert.NotEqual(Environment.CurrentManagedThreadId, raisedOn);
            Assert.Equal("BrowserInterruptUtils.Worker", raisedName);
        }

        [Fact]
        public void PublicSurface_HasNoPageOverlayCloseMethod()
        {
            // Page overlays have no WindowPattern, so PageOverlay + CloseWindowPattern must stay unrepresentable.
            var offenders = typeof(BrowserInterruptUtils).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name.Contains("PageOverlay") && m.Name.Contains("Close"))
                .Select(m => m.Name)
                .ToList();
            Assert.Empty(offenders);
        }

        [Fact]
        public void PauseAndResume_ControlWhetherPopupsAreTouched()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            rig.StartOk();

            Assert.True(rig.Utils.Pause(out _));
            var w = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w, "Yes");
            rig.Hook.FireWindowOpened(w);
            Thread.Sleep(600);
            Assert.True(w.Alive);
            Assert.Equal(0, w.Invokes);

            Assert.True(rig.Utils.Resume(out _));
            Assert.True(WaitFor(() => !w.Alive));
        }

        [Fact]
        public void DisabledRule_LeavesThePopupAlone_UntilItIsEnabledAgain()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogDismissRuleByName("r", "Alert", "", "", "Yes", out _));
            Assert.True(rig.Utils.SetRuleEnabled("r", false, out _));
            rig.StartOk();

            var w = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w, "Yes");
            rig.Hook.FireWindowOpened(w);
            Thread.Sleep(400);
            Assert.True(w.Alive);

            Assert.True(rig.Utils.SetRuleEnabled("r", true, out _));
            var w2 = rig.Probe.AddWindow("Alert");
            rig.Probe.AddChild(w2, "Yes");
            rig.Hook.FireWindowOpened(w2);
            Assert.True(WaitFor(() => !w2.Alive));
        }

        // ------------------------------------------------------------------ never-throws

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NonsenseArguments_NeverThrow(string junk)
        {
            using var rig = new Rig();

            rig.Utils.AddNativeDialogDismissRuleByName(junk, junk, junk, junk, junk, out _);
            rig.Utils.AddNativeDialogDismissRuleByAutomationId(junk, junk, junk, junk, junk, out _);
            rig.Utils.AddNativeDialogCloseRule(junk, junk, junk, junk, out _);
            rig.Utils.AddNativeDialogWatchOnlyRule(junk, junk, junk, junk, out _);
            rig.Utils.AddPageOverlayDismissRuleByName(junk, junk, junk, junk, junk, out _);
            rig.Utils.AddPageOverlayDismissRuleByAutomationId(junk, junk, junk, junk, junk, out _);
            rig.Utils.AddPageOverlayWatchOnlyRule(junk, junk, junk, junk, out _);
            rig.Utils.RemoveRule(junk, out _);
            rig.Utils.SetRuleEnabled(junk, true, out _);
            rig.Utils.GetDismissalCount(junk, out _, out _);

            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            Assert.Equal("[]", json);
        }

        [Fact]
        public void SuccessfulCalls_LeaveMessageNull()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("r", "t", "", "", out string m1));
            Assert.Null(m1);
            Assert.True(rig.Utils.SetRuleEnabled("r", false, out string m2));
            Assert.Null(m2);
            Assert.True(rig.Utils.ListRulesJson(out _, out string m3));
            Assert.Null(m3);
            Assert.True(rig.Utils.GetTotalDismissals(out _, out string m4));
            Assert.Null(m4);
            Assert.True(rig.Utils.HasUnresolvedPopup(out _, out string m5));
            Assert.Null(m5);
            Assert.True(rig.Utils.GetLastEventJson(out _, out string m6));
            Assert.Null(m6);
            Assert.True(rig.Utils.GetLogJson(10, out _, out string m7));
            Assert.Null(m7);
            Assert.True(rig.Utils.ClearLog(out string m8));
            Assert.Null(m8);
            Assert.True(rig.Utils.RemoveRule("r", out string m9));
            Assert.Null(m9);
            Assert.True(rig.Utils.ClearRules(out string m10));
            Assert.Null(m10);
        }

        [Fact]
        public void GetLogJson_ValidatesMaxEntries()
        {
            using var rig = new Rig();
            Assert.False(rig.Utils.GetLogJson(0, out string logJson, out string message));
            Assert.Equal(string.Empty, logJson);
            Assert.Contains("maxEntries", message);
            Assert.False(rig.Utils.GetLogJson(BrowserPopupEngineLogCapacity() + 1, out _, out string message2));
            Assert.Contains("maxEntries", message2);
        }

        // ------------------------------------------------------------------ rule methods: too-broad rules rejected

        [Fact]
        public void NativeDialogRules_RejectAProcessOnlyRule_WithAnExplanation()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddNativeDialogWatchOnlyRule("w", "", "", "chrome", out string m1));
            Assert.Contains("main window", m1);
            Assert.False(rig.Utils.AddNativeDialogDismissRuleByName("n", "", "", "chrome", "OK", out string m2));
            Assert.Contains("main window", m2);
            Assert.False(rig.Utils.AddNativeDialogDismissRuleByAutomationId("a", "", "", "chrome", "ok", out string m3));
            Assert.Contains("main window", m3);
            Assert.False(rig.Utils.AddNativeDialogCloseRule("c", "", "", "chrome", out string m4));
            Assert.NotNull(m4);
        }

        [Fact]
        public void NativeDialogCloseRule_NeedsANameOrMessage_ARoleAloneIsNotEnough()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddNativeDialogCloseRule("c", "", "", "chrome", out string message, roleContains: "dialog"));
            Assert.Contains("nameContains or messageContains", message);
            Assert.True(rig.Utils.AddNativeDialogCloseRule("c2", "", "sure?", "chrome", out message));
            Assert.True(rig.Utils.AddNativeDialogWatchOnlyRule("w2", "", "", "chrome", out message, roleContains: "dialog"));
        }

        [Fact]
        public void PageOverlayRules_RejectProcessOnlyAndMessageOnlyRules()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddPageOverlayWatchOnlyRule("p", "", "", "chrome", out string m1));
            Assert.Contains("every element on the page", m1);
            Assert.False(rig.Utils.AddPageOverlayWatchOnlyRule("m", "", "cookies", "chrome", out string m2));
            Assert.Contains("every element on the page", m2);
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByName("d", "", "cookies", "chrome", "Accept", out string m3));
            Assert.NotNull(m3);
            Assert.False(rig.Utils.AddPageOverlayDismissRuleByAutomationId("i", "", "cookies", "chrome", "accept", out string m4));
            Assert.NotNull(m4);
            Assert.True(rig.Utils.AddPageOverlayWatchOnlyRule("ok", "", "cookies", "chrome", out string m5, roleContains: "dialog"));
            Assert.Null(m5);
        }

        // BrowserPopupEngine.LogCapacity is internal but visible via InternalsVisibleTo.
        private static int BrowserPopupEngineLogCapacity() => BrowserPopupEngine.LogCapacity;
    }
}
