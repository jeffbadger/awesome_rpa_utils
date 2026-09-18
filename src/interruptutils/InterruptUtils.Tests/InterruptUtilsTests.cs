using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace InterruptAutomation.Tests
{
    public class InterruptUtilsTests
    {
        private static readonly string[] YesNo = { "Yes", "No" };

        private sealed class Rig : IDisposable
        {
            public readonly FakeProbe Probe = new FakeProbe();
            public readonly FakeHookSource Hook = new FakeHookSource();
            public readonly InterruptUtils Utils;

            public Rig() => Utils = new InterruptUtils(Probe, Hook);

            public void Dispose() => Utils.Dispose();

            public void StartOk(int sweepMs = 0)
            {
                Assert.True(Utils.Start(out string m, sweepIntervalMs: sweepMs), m);
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

        // ------------------------------------------------------------------ rule methods

        [Fact]
        public void AddDismissRuleByText_AcceptsAValidRule()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.AddDismissRuleByText("r", "Session", "", "", "&Yes", out string message));
            Assert.Null(message);
            Assert.True(rig.Utils.ListRulesJson(out string json, out _));
            using var doc = JsonDocument.Parse(json);
            var rule = doc.RootElement[0];
            Assert.Equal("r", rule.GetProperty("name").GetString());
            Assert.Equal("ClickButtonText", rule.GetProperty("action").GetString());
            Assert.Equal("Yes", rule.GetProperty("button").GetString()); // access-key marker stripped
            Assert.Equal("#32770", rule.GetProperty("className").GetString());
            Assert.True(rule.GetProperty("enabled").GetBoolean());
            Assert.False(rule.GetProperty("stopped").GetBoolean());
        }

        [Fact]
        public void RulesWithNoCriterion_AreRefused_SoNoRuleCanMatchEveryDialog()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddDismissRuleByText("r", "", "", "", "Yes", out string m1));
            Assert.Contains("at least one", m1);
            Assert.False(rig.Utils.AddDismissRuleById("r", " ", null, "", InterruptButton.Ok, out string m2));
            Assert.Contains("at least one", m2);
            Assert.False(rig.Utils.AddCloseWindowRule("r", null, null, null, out string m3));
            Assert.Contains("at least one", m3);
            Assert.False(rig.Utils.AddWatchOnlyRule("r", "", "", "", out string m4));
            Assert.Contains("at least one", m4);
        }

        [Fact]
        public void RuleArguments_AreValidated()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddDismissRuleByText("", "t", "", "", "Yes", out string noName));
            Assert.Contains("ruleName", noName);
            Assert.False(rig.Utils.AddDismissRuleByText("r", "t", "", "", "", out string noButton));
            Assert.Contains("buttonText", noButton);
            Assert.False(rig.Utils.AddDismissRuleByText("r", "t", "", "", null, out _));
            Assert.False(rig.Utils.AddDismissRuleById("r", "t", "", "", (InterruptButton)99, out string badButton));
            Assert.Contains("InterruptButton", badButton);
            Assert.False(rig.Utils.AddDismissRuleByText(new string('x', 65), "t", "", "", "Yes", out _));
        }

        [Fact]
        public void DuplicateRuleName_IsRefused_IgnoringCase()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddWatchOnlyRule("Alpha", "t", "", "", out _));

            Assert.False(rig.Utils.AddCloseWindowRule("ALPHA", "u", "", "", out string message));
            Assert.Contains("already exists", message);
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

            Assert.True(rig.Utils.AddWatchOnlyRule("a", "t", "", "", out _));
            Assert.True(rig.Utils.SetRuleEnabled("a", false, out _));
            Assert.True(rig.Utils.RemoveRule("a", out _));
            Assert.True(rig.Utils.AddWatchOnlyRule("b", "t", "", "", out _));
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
            Assert.False(rig.Utils.Start(out string m3, maxAttempts: 0));
            Assert.Contains("maxAttempts", m3);
            Assert.False(rig.Utils.Start(out string m4, maxAttempts: 11));
            Assert.Contains("maxAttempts", m4);
            Assert.False(rig.Utils.Start(out string m5, maxDismissalsPerMinute: 0));
            Assert.Contains("maxDismissalsPerMinute", m5);
            Assert.False(rig.Utils.IsRunning());
            Assert.Equal(0, rig.Hook.StartCalls);
        }

        [Fact]
        public void Start_ReportsAHookFailure_AndIsNotLeftRunning()
        {
            using var rig = new Rig();
            rig.Hook.FailToStart = true;

            Assert.False(rig.Utils.Start(out string message));

            Assert.Equal("fake hook failure", message);
            Assert.False(rig.Utils.IsRunning());

            rig.Hook.FailToStart = false;
            rig.StartOk(); // and a later attempt can succeed
        }

        [Fact]
        public void StopThenStart_KeepsRulesAndCounts()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddDismissRuleByText("r", "Alert", "", "", "Yes", out _));
            rig.StartOk();
            var w = rig.Probe.AddMessageBox("Alert", "", YesNo);
            rig.Hook.Fire(w.Handle);
            Assert.True(WaitFor(() => !w.Alive));
            Assert.True(rig.Utils.Stop(out _));

            rig.StartOk();
            var w2 = rig.Probe.AddMessageBox("Alert", "", YesNo);
            rig.Hook.Fire(w2.Handle);
            Assert.True(WaitFor(() => !w2.Alive));

            // The window reads as closed a moment before the engine has counted the dismissal.
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
            Assert.True(rig.Utils.AddWatchOnlyRule("r", "t", "", "", out _));
            rig.StartOk();
            rig.Utils.Dispose();

            Assert.False(rig.Utils.IsRunning());
            Assert.False(rig.Utils.Start(out string start));
            Assert.Contains("disposed", start);
            Assert.True(rig.Utils.Stop(out _));
            Assert.False(rig.Utils.Pause(out string pause));
            Assert.Contains("disposed", pause);
            Assert.False(rig.Utils.Resume(out _));
            Assert.False(rig.Utils.AddWatchOnlyRule("x", "t", "", "", out string add));
            Assert.Contains("disposed", add);
            Assert.False(rig.Utils.AddCloseWindowRule("x", "t", "", "", out _));
            Assert.False(rig.Utils.AddDismissRuleById("x", "t", "", "", InterruptButton.Ok, out _));
            Assert.False(rig.Utils.AddDismissRuleByText("x", "t", "", "", "Yes", out _));
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

        // ------------------------------------------------------------------ end to end (fake desktop, real worker thread)

        [Fact]
        public void PopupThatAppearsWhileWatching_IsDismissed_AndReportedEverywhere()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddDismissRuleByText("session", "Session", "expire", "targetapp", "Yes", out _));
            var raised = new List<InterruptPopupEventArgs>();
            var done = new ManualResetEventSlim(false);
            rig.Utils.PopupDismissed += (s, e) => { lock (raised) raised.Add(e); done.Set(); };
            rig.StartOk();

            var w = rig.Probe.AddMessageBox("Session Timeout", "Your session will expire.", YesNo);
            rig.Hook.Fire(w.Handle);

            Assert.True(done.Wait(5000), "PopupDismissed was not raised");
            Assert.False(w.Alive);
            InterruptPopupEventArgs e;
            lock (raised) e = Assert.Single(raised);
            Assert.Equal("session", e.RuleName);
            Assert.Equal("Session Timeout", e.Title);
            Assert.Equal("Your session will expire.", e.MessageText);
            Assert.Equal("targetapp", e.ProcessName);
            Assert.Equal("Yes", e.ButtonClicked);
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
                Assert.Equal("Yes", doc.RootElement.GetProperty("button").GetString());
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
        public void PopupAlreadyOpenAtStart_IsFoundByTheSweep()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddDismissRuleByText("r", "Alert", "", "", "Yes", out _));
            var w = rig.Probe.AddMessageBox("Alert", "", YesNo);

            rig.StartOk(sweepMs: 50);

            Assert.True(WaitFor(() => !w.Alive));
        }

        [Fact]
        public void WatchOnlyPopup_RaisesPopupDetected_AndIsLeftOpen()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddWatchOnlyRule("w", "Alert", "", "", out _));
            var detected = new ManualResetEventSlim(false);
            InterruptPopupEventArgs args = null;
            rig.Utils.PopupDetected += (s, e) => { args = e; detected.Set(); };
            rig.StartOk();

            var w = rig.Probe.AddMessageBox("Alert", "hi", YesNo);
            rig.Hook.Fire(w.Handle);

            Assert.True(detected.Wait(5000));
            Assert.True(w.Alive);
            Assert.Equal("w", args.RuleName);
            Assert.Equal(0, args.Attempts);
        }

        [Fact]
        public void PopupThatCannotBeDismissed_RaisesPopupDismissFailed_AndIsUnresolved()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddDismissRuleByText("r", "Alert", "", "", "Yes", out _));
            var failed = new ManualResetEventSlim(false);
            InterruptPopupEventArgs args = null;
            rig.Utils.PopupDismissFailed += (s, e) => { args = e; failed.Set(); };
            rig.StartOk();

            var w = rig.Probe.AddMessageBox("Alert", "", YesNo);
            w.IgnoreClicks = true;
            rig.Hook.Fire(w.Handle);

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
            Assert.True(rig.Utils.AddDismissRuleByText("nag", "Nag", "", "", "Yes", out _));
            var error = new ManualResetEventSlim(false);
            InterruptErrorEventArgs args = null;
            rig.Utils.InterruptError += (s, e) => { args = e; error.Set(); };
            Assert.True(rig.Utils.Start(out string m, sweepIntervalMs: 0, maxDismissalsPerMinute: 2), m);

            for (int i = 0; i < 3; i++)
            {
                var w = rig.Probe.AddMessageBox("Nag", "", YesNo);
                rig.Hook.Fire(w.Handle);
                if (i < 2)
                    Assert.True(WaitFor(() => !w.Alive));
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
            Assert.True(rig.Utils.AddDismissRuleByText("r", "Alert", "", "", "Yes", out _));
            int secondSeen = 0;
            rig.Utils.PopupDismissed += (s, e) => throw new InvalidOperationException("boom");
            rig.Utils.PopupDismissed += (s, e) => Interlocked.Increment(ref secondSeen);
            rig.StartOk();

            for (int i = 0; i < 2; i++)
            {
                var w = rig.Probe.AddMessageBox("Alert", "", YesNo);
                rig.Hook.Fire(w.Handle);
                Assert.True(WaitFor(() => !w.Alive));
            }

            Assert.True(WaitFor(() => Volatile.Read(ref secondSeen) == 2));
            Assert.True(rig.Utils.IsRunning());
        }

        [Fact]
        public void PauseAndResume_ControlWhetherPopupsAreTouched()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddDismissRuleByText("r", "Alert", "", "", "Yes", out _));
            rig.StartOk();

            Assert.True(rig.Utils.Pause(out _));
            var w = rig.Probe.AddMessageBox("Alert", "", YesNo);
            rig.Hook.Fire(w.Handle);
            Thread.Sleep(600);
            Assert.True(w.Alive);
            Assert.Equal(0, w.Clicks);

            Assert.True(rig.Utils.Resume(out _));
            Assert.True(WaitFor(() => !w.Alive));
        }

        [Fact]
        public void DisabledRule_LeavesThePopupAlone_UntilItIsEnabledAgain()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddDismissRuleByText("r", "Alert", "", "", "Yes", out _));
            Assert.True(rig.Utils.SetRuleEnabled("r", false, out _));
            rig.StartOk();

            var w = rig.Probe.AddMessageBox("Alert", "", YesNo);
            rig.Hook.Fire(w.Handle);
            Thread.Sleep(400);
            Assert.True(w.Alive);

            Assert.True(rig.Utils.SetRuleEnabled("r", true, out _));
            var w2 = rig.Probe.AddMessageBox("Alert", "", YesNo);
            rig.Hook.Fire(w2.Handle);
            Assert.True(WaitFor(() => !w2.Alive));
        }

        // ------------------------------------------------------------------ never throws

        [Fact]
        public void NonsenseArguments_NeverThrow()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.AddDismissRuleByText(null, null, null, null, null, out string m1));
            Assert.NotNull(m1);
            Assert.False(rig.Utils.AddDismissRuleById(null, null, null, null, InterruptButton.Ok, out _));
            Assert.False(rig.Utils.AddCloseWindowRule(null, null, null, null, out _));
            Assert.False(rig.Utils.AddWatchOnlyRule(null, null, null, null, out _));
            Assert.False(rig.Utils.RemoveRule(null, out _));
            Assert.False(rig.Utils.SetRuleEnabled(null, true, out _));
            Assert.False(rig.Utils.GetDismissalCount(null, out int c, out _));
            Assert.Equal(0, c);
            Assert.False(rig.Utils.GetLogJson(0, out string log, out string m2));
            Assert.Equal(string.Empty, log);
            Assert.Contains("maxEntries", m2);
            Assert.False(rig.Utils.GetLogJson(501, out _, out _));
            Assert.False(rig.Utils.GetLogJson(-5, out _, out _));
        }

        [Fact]
        public void SuccessfulCalls_LeaveMessageNull()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.AddWatchOnlyRule("a", "t", "", "", out string m1)); Assert.Null(m1);
            Assert.True(rig.Utils.Pause(out string m2)); Assert.Null(m2);
            Assert.True(rig.Utils.Resume(out string m3)); Assert.Null(m3);
            Assert.True(rig.Utils.SetRuleEnabled("a", true, out string m4)); Assert.Null(m4);
            Assert.True(rig.Utils.GetTotalDismissals(out int t, out string m5)); Assert.Null(m5); Assert.Equal(0, t);
            Assert.True(rig.Utils.GetLogJson(1, out string log, out string m6)); Assert.Null(m6); Assert.Equal("[]", log);
            Assert.True(rig.Utils.RemoveRule("a", out string m7)); Assert.Null(m7);
        }

        [Fact]
        public void InterruptButton_ValuesMatchTheStandardDialogButtonIds()
        {
            Assert.Equal(1, (int)InterruptButton.Ok);
            Assert.Equal(2, (int)InterruptButton.Cancel);
            Assert.Equal(3, (int)InterruptButton.Abort);
            Assert.Equal(4, (int)InterruptButton.Retry);
            Assert.Equal(5, (int)InterruptButton.Ignore);
            Assert.Equal(6, (int)InterruptButton.Yes);
            Assert.Equal(7, (int)InterruptButton.No);
        }
    }
}
