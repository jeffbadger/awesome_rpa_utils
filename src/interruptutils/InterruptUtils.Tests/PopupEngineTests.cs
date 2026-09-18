using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InterruptAutomation.Tests
{
    public class PopupEngineTests
    {
        private sealed class Harness
        {
            public readonly FakeProbe Probe = new FakeProbe();
            public readonly List<PopupRecord> Records = new List<PopupRecord>();
            public readonly PopupEngine Engine;
            public long Now;

            public Harness(int sweepIntervalMs = 0)
            {
                Engine = new PopupEngine(Probe, ms => { }, r => Records.Add(r)) { SweepIntervalMs = sweepIntervalMs };
            }

            public PopupRule AddRule(string name, string title = "", string message = "", string process = "",
                PopupAction action = PopupAction.ClickButtonText, string button = "Yes", bool exact = true, int buttonId = 0, string cls = PopupRule.DialogClass)
            {
                var rule = new PopupRule
                {
                    Name = name, TitleContains = title, MessageContains = message, ProcessName = process,
                    ClassName = cls, Action = action, ButtonText = button, ExactButtonText = exact, ButtonId = buttonId
                };
                Assert.True(Engine.AddRule(rule, out string m), m);
                return rule;
            }

            /// <summary>Reports the window as the hook would, then runs one pass at the current time.</summary>
            public long Appear(FakeWindow w)
            {
                Engine.Enqueue(w.Handle);
                return Engine.Pump(Now);
            }

            public long Pump(long at)
            {
                Now = at;
                return Engine.Pump(at);
            }

            public IEnumerable<PopupRecord> Of(PopupRecordKind kind) => Records.Where(r => r.Kind == kind);
        }

        private static readonly string[] YesNo = { "Yes", "No" };

        // ------------------------------------------------------------------ dismissing

        [Fact]
        public void MatchingPopup_IsDismissedByButtonText_AndRecorded()
        {
            var h = new Harness();
            h.AddRule("session", title: "Session", button: "Yes");
            var w = h.Probe.AddMessageBox("Session Timeout", "Your session will expire.", YesNo);

            h.Appear(w);

            Assert.False(w.Alive);
            Assert.Equal("Yes", w.LastClickedText);
            var record = Assert.Single(h.Records);
            Assert.Equal(PopupRecordKind.Dismissed, record.Kind);
            Assert.Equal("session", record.RuleName);
            Assert.Equal("Session Timeout", record.Title);
            Assert.Equal("Your session will expire.", record.MessageText);
            Assert.Equal("targetapp", record.ProcessName);
            Assert.Equal(4242u, record.ProcessId);
            Assert.Equal("Yes", record.ButtonClicked);
            Assert.Equal(1, record.Attempts);
            Assert.True(h.Engine.TryGetCount("session", out int count));
            Assert.Equal(1, count);
            Assert.Equal(1, h.Engine.TotalDismissals);
            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void ButtonText_IsMatchedIgnoringCase_AndContainsWhenNotExact()
        {
            var h = new Harness();
            h.AddRule("r", title: "Save", button: "save", exact: false);
            var w = h.Probe.AddMessageBox("Save changes?", "", new[] { "Don't Save", "Save All", "Cancel" });

            h.Appear(w);

            Assert.Equal("Don't Save", w.LastClickedText); // first button containing "save", in order
        }

        [Fact]
        public void ExactButtonText_DoesNotMatchAPartialName()
        {
            var h = new Harness();
            h.AddRule("r", title: "Save", button: "Save", exact: true);
            var w = h.Probe.AddMessageBox("Save changes?", "", new[] { "Don't Save", "Cancel" });

            h.Appear(w);
            for (long t = 100; t <= 3000; t += 100)
                h.Pump(t);

            Assert.True(w.Alive);
            Assert.Equal(0, w.Clicks);
            var failure = Assert.Single(h.Of(PopupRecordKind.DismissFailed));
            Assert.Contains("'Don't Save'", failure.Detail);
            Assert.Contains("'Cancel'", failure.Detail);
        }

        [Fact]
        public void DismissByButtonId_ClicksTheButtonWithThatId()
        {
            var h = new Harness();
            h.AddRule("ok", title: "Notice", action: PopupAction.ClickButtonId, buttonId: (int)InterruptButton.No);
            var w = h.Probe.AddMessageBox("Notice", "", new[] { "Yes", "No" });

            h.Appear(w);

            Assert.Equal("No", w.LastClickedText);
            Assert.False(w.Alive);
        }

        [Fact]
        public void CloseRule_ClosesTheWindow_WithoutClickingAnything()
        {
            var h = new Harness();
            h.AddRule("toast", title: "Update", action: PopupAction.CloseWindow);
            var w = h.Probe.AddMessageBox("Update available", "", new string[0]);

            h.Appear(w);

            Assert.Equal(1, w.Closes);
            Assert.Equal(0, w.Clicks);
            Assert.Equal("(close)", Assert.Single(h.Of(PopupRecordKind.Dismissed)).ButtonClicked);
        }

        [Fact]
        public void CloseRule_ThatTheWindowIgnores_IsRetried_ThenReportedAsFailed()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 2;
            h.AddRule("toast", title: "Update", action: PopupAction.CloseWindow);
            var w = h.Probe.AddMessageBox("Update available", "", new string[0]);
            w.IgnoreClose = true;

            h.Appear(w);
            h.Pump(250);
            h.Pump(500);

            Assert.True(w.Alive);
            Assert.Equal(2, w.Closes);
            Assert.Contains("still open after 2 attempts", Assert.Single(h.Of(PopupRecordKind.DismissFailed)).Detail);
        }

        // ------------------------------------------------------------------ matching

        [Fact]
        public void NonMatchingPopup_IsLeftAlone_AndNothingIsRecorded()
        {
            var h = new Harness();
            h.AddRule("r", title: "Session");
            var w = h.Probe.AddMessageBox("Something else", "", YesNo);

            h.Appear(w);
            for (long t = 100; t <= 3000; t += 100)
                h.Pump(t);

            Assert.True(w.Alive);
            Assert.Equal(0, w.Clicks);
            Assert.Empty(h.Records);
        }

        [Fact]
        public void MessageCriterion_MustMatchTheMessageText()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert", message: "expire");
            var other = h.Probe.AddMessageBox("Alert", "Disk almost full", YesNo);
            var match = h.Probe.AddMessageBox("Alert", "Your session will EXPIRE soon", YesNo);

            h.Appear(other);
            h.Appear(match);

            Assert.True(other.Alive);
            Assert.False(match.Alive);
        }

        [Theory]
        [InlineData("targetapp", true)]
        [InlineData("TargetApp.exe", true)]
        [InlineData("otherapp", false)]
        public void ProcessCriterion_IgnoresCaseAndExeSuffix(string ruleProcess, bool shouldDismiss)
        {
            var h = new Harness();
            h.AddRule("r", process: PopupRule.TrimExe(ruleProcess));
            var w = h.Probe.AddMessageBox("Anything", "", YesNo);

            h.Appear(w);

            Assert.Equal(shouldDismiss, !w.Alive);
        }

        [Fact]
        public void EveryCriterionThatIsSet_MustMatch()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert", process: "otherapp");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo); // title matches, process does not

            h.Appear(w);

            Assert.True(w.Alive);
        }

        [Fact]
        public void WindowClass_MustMatch_UnlessTheRuleAcceptsAnyClass()
        {
            var h = new Harness();
            h.AddRule("dialogsOnly", title: "Popup");
            h.AddRule("anyClass", title: "Toast", cls: PopupRule.AnyClass);
            var notDialog = h.Probe.AddMessageBox("Popup", "", YesNo);
            notDialog.Class = "SomeOtherClass";
            var toast = h.Probe.AddMessageBox("Toast", "", YesNo);
            toast.Class = "SomeOtherClass";

            h.Appear(notDialog);
            h.Appear(toast);

            Assert.True(notDialog.Alive);
            Assert.False(toast.Alive);
        }

        [Fact]
        public void FirstMatchingRule_Wins()
        {
            var h = new Harness();
            h.AddRule("watch", title: "Alert", action: PopupAction.WatchOnly);
            h.AddRule("dismiss", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);

            h.Appear(w);

            Assert.True(w.Alive);
            Assert.Equal("watch", Assert.Single(h.Records).RuleName);
        }

        [Fact]
        public void DisabledRule_IsSkipped()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            Assert.True(h.Engine.SetRuleEnabled("r", false));
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);

            h.Appear(w);

            Assert.True(w.Alive);
            Assert.Empty(h.Records);
        }

        [Fact]
        public void PopupsOwnedByTheAutomationItself_AreNeverTouched()
        {
            var h = new Harness();
            h.Probe.CurrentProcessId = 4242;
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo, pid: 4242);

            h.Appear(w);
            for (long t = 100; t <= 3000; t += 100)
                h.Pump(t);

            Assert.True(w.Alive);
            Assert.Empty(h.Records);
        }

        // ------------------------------------------------------------------ timing and retries

        [Fact]
        public void ButtonsThatAppearLate_AreStillFound()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", new string[0]);

            h.Appear(w);                       // announced before its controls exist
            Assert.True(w.Alive);
            h.Probe.AddButton(w, "Yes", 6);
            h.Pump(150);                       // second look

            Assert.False(w.Alive);
            Assert.Single(h.Of(PopupRecordKind.Dismissed));
            Assert.Empty(h.Of(PopupRecordKind.DismissFailed));
        }

        [Fact]
        public void ButtonsThatNeverAppear_EndInAFailureNamingTheProblem()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", new string[0]);

            h.Appear(w);
            for (long t = 100; t <= 4000; t += 100)
                h.Pump(t);

            var failure = Assert.Single(h.Of(PopupRecordKind.DismissFailed));
            Assert.Contains("no native buttons", failure.Detail);
            Assert.Equal(1, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void ClickThatDoesNotClosePopup_IsRetried_ThenReportedAsFailed()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 3;
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            w.IgnoreClicks = true;

            h.Appear(w);
            for (long t = 250; t <= 2000; t += 250)
                h.Pump(t);

            Assert.True(w.Alive);
            Assert.Equal(3, w.Clicks);
            var failure = Assert.Single(h.Of(PopupRecordKind.DismissFailed));
            Assert.Contains("still open after 3 attempts", failure.Detail);
            Assert.Equal(3, failure.Attempts);
            Assert.Equal(1, h.Engine.UnresolvedCount);
            Assert.Equal(0, h.Engine.TotalDismissals);
        }

        [Fact]
        public void PopupThatFailed_IsNotClickedAgainOnLaterSweeps()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.Engine.MaxAttempts = 2;
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            w.IgnoreClicks = true;

            for (long t = 0; t <= 10000; t += 250)
                h.Pump(t);

            Assert.Equal(2, w.Clicks);
            Assert.Single(h.Of(PopupRecordKind.DismissFailed));
        }

        [Fact]
        public void PopupThatDisappearsByItself_IsForgotten_AndNeverReportedAsUnresolved()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", new string[0]);

            h.Appear(w);
            h.Probe.Destroy(w);
            for (long t = 100; t <= 4000; t += 100)
                h.Pump(t);

            Assert.Empty(h.Records);
            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void RepeatedReportsOfTheSameWindow_DoNotDoubleClick()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);

            for (int i = 0; i < 10; i++)
                h.Engine.Enqueue(w.Handle);
            h.Pump(0);
            for (int i = 0; i < 10; i++)
                h.Engine.Enqueue(w.Handle);
            h.Pump(50);

            Assert.Equal(1, w.Clicks);
            Assert.Single(h.Records);
        }

        [Fact]
        public void PopupAlreadyOpen_IsFoundByTheSweep_OnlyWhenTheSweepIsOn()
        {
            var withSweep = new Harness(sweepIntervalMs: 1000);
            withSweep.AddRule("r", title: "Alert");
            var a = withSweep.Probe.AddMessageBox("Alert", "", YesNo);
            withSweep.Pump(0);
            Assert.False(a.Alive);

            var noSweep = new Harness(sweepIntervalMs: 0);
            noSweep.AddRule("r", title: "Alert");
            var b = noSweep.Probe.AddMessageBox("Alert", "", YesNo);
            noSweep.Pump(0);
            noSweep.Pump(5000);
            Assert.True(b.Alive);
        }

        [Fact]
        public void TextThatChangesAfterTheFirstLook_IsPickedUpByALaterSweep()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("r", title: "Alert", message: "expires");
            var w = h.Probe.AddMessageBox("Alert", "loading", YesNo);

            h.Pump(0);
            for (long t = 250; t <= 3000; t += 250)
                h.Pump(t);
            Assert.True(w.Alive);

            w.Message = "Your session expires in 30 seconds";
            h.Pump(4000);

            Assert.False(w.Alive);
        }

        [Fact]
        public void HandleReusedByAnotherProcess_IsNotConfusedWithTheFailedWindow()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            w.IgnoreClicks = true;
            h.Appear(w);
            h.Pump(250);
            Assert.Single(h.Of(PopupRecordKind.DismissFailed));

            // The same handle now belongs to a different window, in a different process, that can be dismissed.
            w.Pid = 9999;
            w.IgnoreClicks = false;
            h.Probe.SetProcessName(9999, "another");
            h.Engine.Enqueue(w.Handle);
            h.Pump(500);

            Assert.False(w.Alive);
            Assert.Single(h.Of(PopupRecordKind.Dismissed));
        }

        // ------------------------------------------------------------------ watch-only

        [Fact]
        public void WatchOnlyRule_ReportsOnce_AndNeverTouchesThePopup()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("watch", title: "Alert", action: PopupAction.WatchOnly);
            var w = h.Probe.AddMessageBox("Alert", "hello", YesNo);

            h.Appear(w);
            for (long t = 250; t <= 5000; t += 250)
                h.Pump(t);

            Assert.True(w.Alive);
            Assert.Equal(0, w.Clicks);
            Assert.Equal(0, w.Closes);
            var record = Assert.Single(h.Records);
            Assert.Equal(PopupRecordKind.Detected, record.Kind);
            Assert.Equal("hello", record.MessageText);
            Assert.Equal(0, record.Attempts);
            Assert.Equal(0, h.Engine.UnresolvedCount); // watching is not a problem to resolve
        }

        // ------------------------------------------------------------------ pause and runaway

        [Fact]
        public void WhilePaused_PopupsAreLeftAlone_AndDismissedAfterResume()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);

            h.Engine.Paused = true;
            h.Appear(w);
            h.Pump(300);
            h.Pump(600);
            Assert.True(w.Alive);
            Assert.Equal(0, w.Clicks);

            h.Engine.Paused = false;
            h.Pump(900);

            Assert.False(w.Alive);
        }

        [Fact]
        public void RuleThatKeepsDismissingTheSamePopup_StopsItselfAndRaisesAnError()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 3;
            var rule = h.AddRule("nag", title: "Nag");

            for (int i = 0; i < 3; i++)
            {
                var w = h.Probe.AddMessageBox("Nag", "", YesNo);
                h.Now += 1000;
                h.Appear(w);
                Assert.False(w.Alive);
            }
            var fourth = h.Probe.AddMessageBox("Nag", "", YesNo);
            h.Now += 1000;
            h.Appear(fourth);

            Assert.True(fourth.Alive);
            Assert.True(rule.Tripped);
            Assert.Equal(3, h.Engine.TotalDismissals);
            var error = Assert.Single(h.Of(PopupRecordKind.Error));
            Assert.Equal("nag", error.RuleName);
            Assert.Contains("SetRuleEnabled", error.Detail);

            // It stays stopped until it is switched back on.
            h.Now += 1000;
            h.Pump(h.Now);
            Assert.True(fourth.Alive);
            Assert.True(h.Engine.SetRuleEnabled("nag", true));
            Assert.False(rule.Tripped);
            h.Engine.Enqueue(fourth.Handle);
            h.Pump(h.Now + 1000);
            Assert.False(fourth.Alive);
        }

        [Fact]
        public void RunawayCount_ForgetsDismissalsOlderThanAMinute()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 2;
            var rule = h.AddRule("r", title: "Alert");

            for (int i = 0; i < 2; i++)
            {
                h.Now += 1000;
                h.Appear(h.Probe.AddMessageBox("Alert", "", YesNo));
            }

            h.Now += PopupEngine.RunawayWindowMs + 1000;
            var late = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Appear(late);

            Assert.False(late.Alive);
            Assert.False(rule.Tripped);
        }

        // ------------------------------------------------------------------ rules, counts, log

        [Fact]
        public void RuleNames_AreUnique_IgnoringCase_AndTheRuleCountIsCapped()
        {
            var h = new Harness();
            h.AddRule("Alpha", title: "x");
            Assert.False(h.Engine.AddRule(new PopupRule { Name = "ALPHA", TitleContains = "y", ClassName = PopupRule.DialogClass }, out string dup));
            Assert.Contains("already exists", dup);

            for (int i = 1; i < PopupEngine.MaxRules; i++)
                h.AddRule("rule" + i, title: "x");
            Assert.False(h.Engine.AddRule(new PopupRule { Name = "one too many", TitleContains = "y", ClassName = PopupRule.DialogClass }, out string full));
            Assert.Contains("At most", full);
        }

        [Fact]
        public void RemoveRule_DropsTheRuleAndItsCount()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            Assert.True(h.Engine.RemoveRule("R"));
            Assert.False(h.Engine.TryGetCount("r", out _));
            Assert.False(h.Engine.RemoveRule("r"));

            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Appear(w);
            Assert.True(w.Alive);
        }

        [Fact]
        public void ClearLog_KeepsCounts()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            h.Appear(h.Probe.AddMessageBox("Alert", "", YesNo));
            Assert.NotEmpty(h.Engine.GetLog(10));

            h.Engine.ClearLog();

            Assert.Empty(h.Engine.GetLog(10));
            Assert.Null(h.Engine.LastRecord());
            h.Engine.TryGetCount("r", out int count);
            Assert.Equal(1, count);
        }

        [Fact]
        public void Log_KeepsOnlyTheNewestEntries()
        {
            var h = new Harness();
            h.AddRule("watch", title: "W", action: PopupAction.WatchOnly);
            int total = PopupEngine.LogCapacity + 50;
            for (int i = 0; i < total; i++)
            {
                var w = h.Probe.AddMessageBox("W " + i, "", YesNo);
                h.Appear(w);
            }

            var log = h.Engine.GetLog(10000);
            Assert.Equal(PopupEngine.LogCapacity, log.Length);
            Assert.Equal("W " + (total - 1), log[log.Length - 1].Title);
            Assert.Equal("W 50", log[0].Title);
            Assert.Equal(3, h.Engine.GetLog(3).Length);
        }

        [Fact]
        public void ASinkThatThrows_DoesNotStopLaterPopupsBeingHandled()
        {
            var probe = new FakeProbe();
            var engine = new PopupEngine(probe, ms => { }, r => throw new InvalidOperationException("boom")) { SweepIntervalMs = 0 };
            Assert.True(engine.AddRule(new PopupRule { Name = "r", TitleContains = "Alert", ClassName = PopupRule.DialogClass, Action = PopupAction.ClickButtonText, ButtonText = "Yes", ExactButtonText = true }, out _));
            var first = probe.AddMessageBox("Alert", "", YesNo);
            var second = probe.AddMessageBox("Alert", "", YesNo);

            engine.Enqueue(first.Handle);
            engine.Pump(0);
            engine.Enqueue(second.Handle);
            engine.Pump(10);

            Assert.False(first.Alive);
            Assert.False(second.Alive);
            Assert.Equal(2, engine.TotalDismissals);
        }

        [Fact]
        public void Pump_ReportsWhenTheNextWindowIsDue()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", new string[0]);

            long next = h.Appear(w);

            Assert.Equal(PopupEngine.ScheduleMs[1], next);
            Assert.Equal(long.MaxValue, new Harness().Pump(0));
        }

        // ------------------------------------------------------------------ rules that change while watching

        private static void Settle(Harness h, long from = 100, long to = 3000)
        {
            for (long t = from; t <= to; t += 100)
                h.Pump(t);
        }

        [Fact]
        public void RuleAddedAfterAPopupIsAlreadyOpen_TakesEffect_EvenWithTheScanOff()
        {
            var h = new Harness(sweepIntervalMs: 0);
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Appear(w);
            Settle(h);
            Assert.True(w.Alive); // no rule yet: parked

            h.AddRule("late", title: "Alert");
            h.Pump(3100);

            Assert.False(w.Alive);
            Assert.Single(h.Of(PopupRecordKind.Dismissed));
        }

        [Fact]
        public void RuleSwitchedBackOn_ReconsidersAPopupLeftOpen_EvenWithTheScanOff()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("r", title: "Alert");
            Assert.True(h.Engine.SetRuleEnabled("r", false));
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Appear(w);
            Settle(h);
            Assert.True(w.Alive);

            Assert.True(h.Engine.SetRuleEnabled("r", true));
            h.Pump(3100);

            Assert.False(w.Alive);
        }

        [Fact]
        public void RemovingTheRuleThatFailed_ForgetsTheFailure_SoAReplacementCanDismissIt()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            w.IgnoreClicks = true;
            h.Appear(w);
            Assert.Equal(1, h.Engine.UnresolvedCount);

            Assert.True(h.Engine.RemoveRule("r"));
            h.Pump(500);
            Assert.Equal(0, h.Engine.UnresolvedCount); // nothing claims it any more

            w.IgnoreClicks = false;
            h.AddRule("replacement", title: "Alert");
            h.Pump(1000);

            Assert.False(w.Alive);
            Assert.Equal("replacement", h.Of(PopupRecordKind.Dismissed).Single().RuleName);
        }

        [Fact]
        public void ClearingTheRules_ForgetsWhatWasUnresolved()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            w.IgnoreClicks = true;
            h.Appear(w);
            Assert.Equal(1, h.Engine.UnresolvedCount);

            h.Engine.ClearRules();
            h.Pump(500);

            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void TrippedRule_KeepsItsPopupCountedAsUnresolved_UntilItIsSwitchedOff()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 1;
            h.AddRule("nag", title: "Nag");
            h.Now = 1000;
            h.Appear(h.Probe.AddMessageBox("Nag", "", YesNo));
            var stuck = h.Probe.AddMessageBox("Nag", "", YesNo);
            h.Now = 2000;
            h.Appear(stuck);
            Assert.True(stuck.Alive);
            Assert.Equal(1, h.Engine.UnresolvedCount);

            h.Pump(3000);
            Assert.Equal(1, h.Engine.UnresolvedCount); // re-looked-at, still stopped: still unresolved

            Assert.True(h.Engine.SetRuleEnabled("nag", false));
            h.Pump(4000);
            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void RemovingARuleWhileItsClickIsInFlight_DoesNotRecreateItsCount()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            h.Probe.ClickEntered = () => h.Engine.RemoveRule("r"); // removed after it was chosen, before the click lands
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);

            h.Appear(w);

            Assert.False(w.Alive);
            Assert.False(h.Engine.TryGetCount("r", out _));
            Assert.Equal(1, h.Engine.TotalDismissals);
        }

        [Fact]
        public void PausedAfterTheRuleWasChosen_ButBeforeTheClick_NothingIsClicked()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            // Evaluate has chosen the rule and is asking for the buttons; a Pause lands right then.
            h.Probe.GetButtonsEntered = () => h.Engine.Paused = true;

            h.Appear(w);

            Assert.Equal(0, w.Clicks);
            Assert.True(w.Alive);

            h.Probe.GetButtonsEntered = null;
            h.Engine.Paused = false;
            h.Pump(1000);
            Assert.False(w.Alive); // and it is dealt with once resumed
        }

        [Fact]
        public void RuleSwitchedOffAfterItWasChosen_ButBeforeTheClick_NothingIsClicked()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Probe.GetButtonsEntered = () => h.Engine.SetRuleEnabled("r", false);

            h.Appear(w);

            Assert.Equal(0, w.Clicks);
            Assert.True(w.Alive);
        }

        [Fact]
        public void RuleRemovedAfterItWasChosen_ButBeforeTheClick_NothingIsClicked()
        {
            var h = new Harness();
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Probe.GetButtonsEntered = () => h.Engine.RemoveRule("r");

            h.Appear(w);

            Assert.Equal(0, w.Clicks);
            Assert.True(w.Alive);
        }

        // ------------------------------------------------------------------ window identity

        [Fact]
        public void HandleGivenToANewWindow_AfterADestroyNotification_IsReportedAgain()
        {
            var h = new Harness();
            h.AddRule("watch", title: "Alert", action: PopupAction.WatchOnly);
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Appear(w);
            Assert.Single(h.Of(PopupRecordKind.Detected));

            // The window is destroyed and the same handle is handed to a new window of the same process.
            h.Engine.EnqueueDestroyed(w.Handle);
            h.Engine.Enqueue(w.Handle);
            h.Pump(500);

            Assert.Equal(2, h.Of(PopupRecordKind.Detected).Count());
        }

        [Fact]
        public void WithoutADestroyNotification_ALiveHandleIsTheSameWindow_AndIsReportedOnce()
        {
            var h = new Harness();
            h.AddRule("watch", title: "Alert", action: PopupAction.WatchOnly);
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Appear(w);

            h.Engine.Enqueue(w.Handle);
            h.Pump(500);

            Assert.Single(h.Of(PopupRecordKind.Detected));
        }

        [Fact]
        public void HandleGivenToANewWindow_AfterAFailure_IsTriedAgain()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("r", title: "Alert");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            w.IgnoreClicks = true;
            h.Appear(w);
            Assert.Single(h.Of(PopupRecordKind.DismissFailed));

            w.IgnoreClicks = false; // a different window, same handle and process
            h.Engine.EnqueueDestroyed(w.Handle);
            h.Engine.Enqueue(w.Handle);
            h.Pump(500);

            Assert.False(w.Alive);
            Assert.Single(h.Of(PopupRecordKind.Dismissed));
        }

        [Fact]
        public void ProcessName_IsResolvedAgainOnEachPass_SoAReusedProcessIdIsNotMisattributed()
        {
            var h = new Harness();
            h.AddRule("r", process: "app1");
            var w = h.Probe.AddMessageBox("Alert", "", YesNo);
            h.Probe.SetProcessName(4242, "otherapp");
            h.Appear(w);
            Assert.True(w.Alive); // first pass: not app1

            h.Probe.SetProcessName(4242, "app1"); // the id now belongs to a different process
            h.Pump(150);

            Assert.False(w.Alive);
        }

        // ------------------------------------------------------------------ helpers

        [Theory]
        [InlineData("&Yes", "Yes")]
        [InlineData("Save &As...", "Save As...")]
        [InlineData("Fish && Chips", "Fish & Chips")]
        [InlineData("Plain", "Plain")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("Trailing&", "Trailing")]
        public void StripMnemonic_RemovesTheAccessKeyMarker(string input, string expected)
        {
            Assert.Equal(expected, PopupRule.StripMnemonic(input));
        }

        [Theory]
        [InlineData("Button", true)]
        [InlineData("button", true)]
        [InlineData("WindowsForms10.BUTTON.app.0.141b42a_r14_ad1", true)]
        [InlineData("Static", false)]
        [InlineData("Edit", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsButtonClass_RecognizesNativeAndWinFormsButtons(string className, bool expected)
        {
            Assert.Equal(expected, Win32PopupProbe.IsButtonClass(className));
        }

        [Theory]
        [InlineData("app", "app", true)]
        [InlineData("APP.EXE", "app", true)]
        [InlineData("app", "app.exe", true)]
        [InlineData("app", "other", false)]
        [InlineData("app", "", false)]
        [InlineData("app", null, false)]
        public void ProcessNamesMatch_IgnoresCaseAndTheExeSuffix(string rule, string actual, bool expected)
        {
            Assert.Equal(expected, PopupRule.ProcessNamesMatch(rule, actual));
        }

        [Theory]
        [InlineData("", "t", "", "", false)]
        [InlineData("  ", "t", "", "", false)]
        [InlineData("name", "", "", "", false)]      // no criterion: would match every dialog
        [InlineData("name", " ", " ", " ", false)]
        [InlineData("name", "t", "", "", true)]
        [InlineData("name", "", "m", "", true)]
        [InlineData("name", "", "", "p", true)]
        public void ValidateCommon_NeedsANameAndAtLeastOneCriterion(string name, string title, string message, string process, bool valid)
        {
            Assert.Equal(valid, PopupRule.ValidateCommon(name, title, message, process) == null);
        }

        [Fact]
        public void ValidateCommon_RejectsAnOverlongName()
        {
            Assert.NotNull(PopupRule.ValidateCommon(new string('x', PopupRule.MaxNameLength + 1), "t", "", ""));
            Assert.Null(PopupRule.ValidateCommon(new string('x', PopupRule.MaxNameLength), "t", "", ""));
        }
    }
}
