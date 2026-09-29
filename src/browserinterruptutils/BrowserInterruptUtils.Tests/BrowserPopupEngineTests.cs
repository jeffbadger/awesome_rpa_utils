using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace BrowserInterruptAutomation.Tests
{
    public class BrowserPopupEngineTests
    {
        private sealed class Harness
        {
            public readonly FakeBrowserPopupProbe Probe = new FakeBrowserPopupProbe();
            public readonly FakeBrowserPopupHookSource Hook = new FakeBrowserPopupHookSource();
            public readonly List<BrowserPopupRecord> Records = new List<BrowserPopupRecord>();
            public readonly BrowserPopupEngine Engine;
            public long Now;

            public Harness(int sweepIntervalMs = 0, int overlaySweepIntervalMs = 0)
            {
                Engine = new BrowserPopupEngine(Probe, Hook, r => Records.Add(r))
                {
                    SweepIntervalMs = sweepIntervalMs,
                    OverlaySweepIntervalMs = overlaySweepIntervalMs
                };
            }

            public BrowserPopupRule AddRule(string name, BrowserPopupScope scope,
                string nameContains = null, string automationId = null, string process = null, string role = null, string message = null,
                BrowserPopupAction action = BrowserPopupAction.WatchOnly, string targetName = null, string targetAutomationId = null,
                bool exactTarget = false)
            {
                var rule = new BrowserPopupRule
                {
                    RuleName = name,
                    Scope = scope,
                    NameContains = nameContains,
                    AutomationIdContains = automationId,
                    ProcessName = process,
                    RoleContains = role,
                    MessageContains = message,
                    Action = action,
                    TargetElementName = targetName,
                    TargetAutomationId = targetAutomationId,
                    ExactTargetElementName = exactTarget
                };
                Assert.True(Engine.AddRule(rule, out string m), m);
                return rule;
            }

            public long Pump(long at)
            {
                Now = at;
                return Engine.Pump(at);
            }

            /// <summary>Reports a native window as the hook would, then runs one pass at the current time.</summary>
            public long AppearWindow(FakeElement window)
            {
                Hook.FireWindowOpened(window);
                return Engine.Pump(Now);
            }

            /// <summary>Signals a watched window's structure changed, then runs one pass at the current time.</summary>
            public long AppearOverlay(FakeElement window)
            {
                Hook.FireStructureChanged(window.Ref);
                return Engine.Pump(Now);
            }

            public IEnumerable<BrowserPopupRecord> Of(BrowserPopupRecordKind kind) => Records.Where(r => r.Kind == kind);

            public void Settle(long from = 100, long to = 3000)
            {
                for (long t = from; t <= to; t += 100)
                    Pump(t);
            }
        }

        // ------------------------------------------------------------------ rule CRUD

        [Fact]
        public void AddRule_RejectsValidateCommonFailures()
        {
            var h = new Harness();
            var rule = new BrowserPopupRule { RuleName = "", Scope = BrowserPopupScope.NativeDialog, NameContains = "x" };

            Assert.False(h.Engine.AddRule(rule, out string message));
            Assert.NotNull(message);
        }

        [Fact]
        public void AddRule_RejectsARuleWithNoMatchCriteria()
        {
            var h = new Harness();
            var rule = new BrowserPopupRule { RuleName = "empty", Scope = BrowserPopupScope.NativeDialog };

            Assert.False(h.Engine.AddRule(rule, out string message));
            Assert.Contains("at least one of", message);
        }

        [Fact]
        public void AddRule_RejectsNull()
        {
            var h = new Harness();
            Assert.False(h.Engine.AddRule(null, out string message));
            Assert.NotNull(message);
        }

        [Fact]
        public void RuleNames_AreUnique_IgnoringCase_AndTheRuleCountIsCapped()
        {
            var h = new Harness();
            h.AddRule("Alpha", BrowserPopupScope.NativeDialog, nameContains: "x");
            Assert.False(h.Engine.AddRule(new BrowserPopupRule { RuleName = "ALPHA", Scope = BrowserPopupScope.NativeDialog, NameContains = "y" }, out string dup));
            Assert.Contains("already exists", dup);

            for (int i = 1; i < BrowserPopupRule.MaxRules; i++)
                h.AddRule("rule" + i, BrowserPopupScope.NativeDialog, nameContains: "x");
            Assert.False(h.Engine.AddRule(new BrowserPopupRule { RuleName = "one too many", Scope = BrowserPopupScope.NativeDialog, NameContains = "y" }, out string full));
            Assert.Contains("At most", full);
        }

        [Fact]
        public void RemoveRule_DropsTheRuleAndItsCount()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert");
            Assert.True(h.Engine.RemoveRule("R"));
            Assert.False(h.Engine.TryGetCount("r", out _));
            Assert.False(h.Engine.RemoveRule("r"));

            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w);
            Assert.True(w.Alive);
        }

        // ------------------------------------------------------------------ Paused / rule changes vs an action in flight

        [Fact]
        public void WaitForIdle_BlocksUntilAnActionAlreadyInFlightFinishes_ThenNothingLandsAfterPause()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var first = h.Probe.AddWindow("Alert");
            h.Probe.AddChild(first, "Yes");
            var entered = new ManualResetEventSlim(false);
            var gate = new ManualResetEventSlim(false);
            h.Probe.ActionEntered = () => entered.Set();
            h.Probe.ActionGate = gate;
            h.Hook.FireWindowOpened(first);

            var pump = new Thread(() => h.Engine.Pump(0)) { IsBackground = true };
            pump.Start();
            Assert.True(entered.Wait(5000), "the action never started");

            bool paused = false;
            var pausing = new Thread(() =>
            {
                h.Engine.Paused = true;
                h.Engine.WaitForIdle();
                paused = true;
            }) { IsBackground = true };
            pausing.Start();
            Assert.False(pausing.Join(400), "Pause returned while an action was still in flight");
            Assert.False(paused);

            gate.Set();
            Assert.True(pausing.Join(5000), "Pause did not return once the action finished");
            Assert.True(pump.Join(5000));
            Assert.False(first.Alive); // the action already under way completed
            Assert.Equal(1, h.Probe.TotalInvokes);

            var second = h.Probe.AddWindow("Alert");
            h.Probe.AddChild(second, "Yes");
            h.Probe.ActionGate = null;
            h.Hook.FireWindowOpened(second);
            h.Engine.Pump(1000);
            Assert.True(second.Alive);
            Assert.Equal(1, h.Probe.TotalInvokes); // nothing further landed after Pause returned
        }

        [Fact]
        public void PauseLandingDuringDiscovery_PreventsTheAction_BecauseItIsRecheckedUnderTheLock()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            // Pause arrives after Evaluate's own Paused check but before the action: during the target walk.
            h.Probe.OnFindOverlayCandidates = () => h.Engine.Paused = true;

            h.AppearWindow(w);

            Assert.Equal(0, h.Probe.TotalInvokes);
            Assert.True(w.Alive);
            Assert.Equal(0, yes.Invokes);

            h.Probe.OnFindOverlayCandidates = null;
            h.Engine.Paused = false;
            h.Settle(500, 1500);
            Assert.Equal(1, yes.Invokes); // and it is dealt with once resumed
        }

        [Fact]
        public void RuleDisabledOrRemovedDuringDiscovery_PreventsTheAction()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            h.Probe.OnFindOverlayCandidates = () => h.Engine.SetRuleEnabled("r", false);
            h.AppearWindow(w);
            Assert.Equal(0, yes.Invokes);

            h.Engine.SetRuleEnabled("r", true);
            h.Probe.OnFindOverlayCandidates = () => h.Engine.RemoveRule("r");
            h.Settle(500, 1500);
            Assert.Equal(0, yes.Invokes);
        }

        [Fact]
        public void ClearRules_RemovesEveryRule_AndForgetsWhatWasUnresolved()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");
            w.IgnoreClose = true;
            h.AppearWindow(w);
            Assert.Equal(1, h.Engine.UnresolvedCount);

            h.Engine.ClearRules();
            h.Pump(500);

            Assert.Equal(0, h.Engine.UnresolvedCount);
            Assert.False(h.Engine.TryGetCount("r", out _));
        }

        [Fact]
        public void DisabledRule_IsSkipped_AndReenablingRetriesAParkedCandidate()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            Assert.True(h.Engine.SetRuleEnabled("r", false));
            var w = h.Probe.AddWindow("Alert");

            h.AppearWindow(w);
            h.Settle();
            Assert.True(w.Alive);
            Assert.Empty(h.Records);

            Assert.True(h.Engine.SetRuleEnabled("r", true));
            h.Pump(3100);

            Assert.False(w.Alive);
        }

        // ------------------------------------------------------------------ matching, per scope

        [Fact]
        public void NativeDialog_MatchesByNameAutomationIdRoleProcessAndMessage()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Session", automationId: "sessionDlg",
                process: "chrome", role: "alert", message: "expire", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Session Timeout", "Your session will expire.", pid: 5, processName: "chrome");
            w.AutomationId = "sessionDlg";
            w.LocalizedControlType = "alert";

            h.AppearWindow(w);

            Assert.False(w.Alive);
        }

        [Fact]
        public void PageOverlay_MatchesByNameAutomationIdRoleProcessAndMessage()
        {
            var h = new Harness();
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Cookie", automationId: "consent",
                process: "chrome", role: "alertdialog", message: "accept", action: BrowserPopupAction.WatchOnly);
            var window = h.Probe.AddWindow("tab", pid: 5, processName: "chrome");
            h.AppearWindow(window);

            var overlay = h.Probe.AddOverlay(window, "Cookie banner", "Please accept our cookies", role: "alertdialog");
            overlay.AutomationId = "consent";
            h.AppearOverlay(window);

            var detected = Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal("cookie", detected.RuleName);
            Assert.Equal("PageOverlay", detected.Scope);
        }

        [Fact]
        public void EveryCriterionThatIsSet_MustMatch()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "otherapp", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert", processName: "chrome"); // name matches, process does not

            h.AppearWindow(w);

            Assert.True(w.Alive);
        }

        [Fact]
        public void MessageCriterion_MustMatchTheMessageText()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", message: "expire", action: BrowserPopupAction.CloseWindowPattern);
            var other = h.Probe.AddWindow("Alert", "Disk almost full");
            var match = h.Probe.AddWindow("Alert", "Your session will EXPIRE soon");

            h.AppearWindow(other);
            h.AppearWindow(match);

            Assert.True(other.Alive);
            Assert.False(match.Alive);
        }

        [Fact]
        public void FirstMatchingEnabledRule_Wins()
        {
            var h = new Harness();
            h.AddRule("watch", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.WatchOnly);
            h.AddRule("close", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");

            h.AppearWindow(w);

            Assert.True(w.Alive);
            Assert.Equal("watch", Assert.Single(h.Records).RuleName);
        }

        // ------------------------------------------------------------------ scope isolation

        [Fact]
        public void NativeDialogCandidate_NeverMatchesAPageOverlayRule()
        {
            var h = new Harness();
            h.AddRule("overlayOnly", BrowserPopupScope.PageOverlay, nameContains: "Alert", action: BrowserPopupAction.WatchOnly);
            var w = h.Probe.AddWindow("Alert");

            h.AppearWindow(w);
            h.Settle();

            Assert.True(w.Alive);
            Assert.Empty(h.Records);
        }

        [Fact]
        public void PageOverlayCandidate_NeverMatchesANativeDialogRule()
        {
            var h = new Harness();
            h.AddRule("nativeOnly", BrowserPopupScope.NativeDialog, nameContains: "Cookie", action: BrowserPopupAction.WatchOnly);
            h.AddRule("watchOverlay", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var window = h.Probe.AddWindow("tab", pid: 5, processName: "chrome");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Cookie banner");
            h.AppearOverlay(window);

            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal("watchOverlay", h.Of(BrowserPopupRecordKind.Detected).Single().RuleName);
        }

        // ------------------------------------------------------------------ retry cascade

        [Fact]
        public void TargetThatAppearsLate_IsStillFound_ViaTheRetryCascade()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var w = h.Probe.AddWindow("Alert");

            h.AppearWindow(w);                 // announced before its button exists
            Assert.True(w.Alive);
            h.Probe.AddChild(w, "OK");
            h.Pump(BrowserPopupEngine.ScheduleMs[1]); // second look

            Assert.False(w.Alive);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
        }

        [Fact]
        public void TargetThatNeverAppears_GivesUpAfterTheFullSchedule_AndReportsFailure()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var w = h.Probe.AddWindow("Alert");

            h.AppearWindow(w);
            for (long t = 100; t <= 4000; t += 100)
                h.Pump(t);

            Assert.True(w.Alive);
            var failure = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Contains("OK", failure.Detail);
        }

        [Fact]
        public void Pump_ReportsWhenTheNextCandidateIsDue()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var w = h.Probe.AddWindow("Alert");

            long next = h.AppearWindow(w);

            Assert.Equal(BrowserPopupEngine.ScheduleMs[1], next);
            Assert.Equal(long.MaxValue, new Harness().Pump(0));
        }

        [Fact]
        public void ClickThatDoesNotDismissThePopup_IsRetried_ThenReportedAsFailed()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 3;
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var w = h.Probe.AddWindow("Alert");
            var button = h.Probe.AddChild(w, "OK");
            button.IgnoreInvoke = true;

            h.AppearWindow(w);
            for (long t = 250; t <= 2000; t += 250)
                h.Pump(t);

            Assert.True(w.Alive);
            Assert.Equal(3, button.Invokes);
            var failure = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(3, failure.Attempts);
            Assert.Equal(1, h.Engine.UnresolvedCount);
        }

        // ------------------------------------------------------------------ actions

        [Fact]
        public void CloseWindowPatternRule_ClosesTheWindow_WithoutInvokingAnything()
        {
            var h = new Harness();
            h.AddRule("toast", BrowserPopupScope.NativeDialog, nameContains: "Update", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Update available");

            h.AppearWindow(w);

            Assert.Equal(1, w.Closes);
            Assert.Equal(0, w.Invokes);
            Assert.Equal("(close)", Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed)).TargetInvoked);
        }

        [Fact]
        public void InvokeByAutomationId_FindsTheTargetByItsAutomationId()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByAutomationId, targetAutomationId: "btnOk");
            var w = h.Probe.AddWindow("Alert");
            h.Probe.AddChild(w, "OK", automationId: "btnOk");

            h.AppearWindow(w);

            Assert.False(w.Alive);
        }

        [Fact]
        public void InvokeByName_ExactMatch_DoesNotMatchAPartialName()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Save", action: BrowserPopupAction.InvokeByName, targetName: "Save", exactTarget: true);
            var w = h.Probe.AddWindow("Save changes?");
            h.Probe.AddChild(w, "Don't Save");
            h.Probe.AddChild(w, "Cancel");

            h.AppearWindow(w);
            h.Settle();

            Assert.True(w.Alive);
            var failure = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Contains("'Save'", failure.Detail);
        }

        [Fact]
        public void InvokeByName_SubstringMatch_WhenNotExact()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Save", action: BrowserPopupAction.InvokeByName, targetName: "save", exactTarget: false);
            var w = h.Probe.AddWindow("Save changes?");
            var dontSave = h.Probe.AddChild(w, "Don't Save");
            h.Probe.AddChild(w, "Cancel");

            h.AppearWindow(w);

            Assert.Equal(1, dontSave.Invokes);
            Assert.False(w.Alive);
        }

        // ------------------------------------------------------------------ perf-gating contract

        [Fact]
        public void OverlaySweep_NeverRunsForAProcessNoPageOverlayRuleTargets()
        {
            var h = new Harness(overlaySweepIntervalMs: 500);
            h.AddRule("native", BrowserPopupScope.NativeDialog, nameContains: "Alert");
            var window = h.Probe.AddWindow("Some Window", pid: 100, processName: "notepad");

            h.AppearWindow(window);
            h.Pump(1000);
            h.Pump(2000);

            Assert.Equal(0, h.Probe.OverlaySearchCalls);
            Assert.Empty(h.Hook.WatchCalls);
        }

        [Fact]
        public void OverlaySweep_RunsOnlyForProcessesAPageOverlayRuleNames()
        {
            var h = new Harness(overlaySweepIntervalMs: 500);
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Accept", process: "chrome");
            var chromeWindow = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            var otherWindow = h.Probe.AddWindow("tab2", pid: 200, processName: "notepad");

            h.AppearWindow(chromeWindow);
            h.AppearWindow(otherWindow);

            Assert.Contains(chromeWindow.Ref, h.Hook.WatchCalls);
            Assert.DoesNotContain(otherWindow.Ref, h.Hook.WatchCalls);
            Assert.True(h.Probe.OverlaySearchCalls >= 1);
            Assert.All(h.Probe.OverlaySearchRoots, r => Assert.Equal(chromeWindow.Ref, r));
        }

        [Fact]
        public void Watch_IsRegistered_WhenARuleIsAddedAfterTheWindowAlreadyExists()
        {
            var h = new Harness();
            var window = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            h.AppearWindow(window);
            Assert.Empty(h.Hook.WatchCalls); // no PageOverlay rule yet

            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Accept", process: "chrome");
            h.Pump(h.Now + 1);

            Assert.Contains(window.Ref, h.Hook.WatchCalls);
        }

        [Fact]
        public void Unwatch_HappensWhenTheOnlyMatchingRuleIsRemoved()
        {
            var h = new Harness();
            var rule = h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Accept", process: "chrome");
            var window = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            h.AppearWindow(window);
            Assert.Contains(window.Ref, h.Hook.WatchCalls);

            h.Engine.RemoveRule("cookie");
            h.Pump(h.Now + 1);

            Assert.Contains(window.Ref, h.Hook.UnwatchCalls);
            Assert.DoesNotContain(window.Ref, h.Hook.CurrentlyWatched);
        }

        [Fact]
        public void Unwatch_HappensWhenTheWatchedWindowCloses()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Accept", process: "chrome");
            var window = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            h.AppearWindow(window);
            Assert.Contains(window.Ref, h.Hook.WatchCalls);

            h.Probe.Destroy(window);
            h.Pump(1000);

            Assert.Contains(window.Ref, h.Hook.UnwatchCalls);
        }

        [Fact]
        public void PageOverlayRuleWithNoProcessName_WatchesEveryDiscoveredWindow()
        {
            var h = new Harness();
            h.AddRule("anyProcess", BrowserPopupScope.PageOverlay, nameContains: "Accept");
            var a = h.Probe.AddWindow("tab", pid: 1, processName: "chrome");
            var b = h.Probe.AddWindow("tab2", pid: 2, processName: "notepad");

            h.AppearWindow(a);
            h.AppearWindow(b);

            Assert.Contains(a.Ref, h.Hook.WatchCalls);
            Assert.Contains(b.Ref, h.Hook.WatchCalls);
        }

        // ------------------------------------------------------------------ runaway breaker

        [Fact]
        public void RuleThatKeepsDismissingTheSamePopup_StopsItselfAndRaisesAnError()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 3;
            var rule = h.AddRule("nag", BrowserPopupScope.NativeDialog, nameContains: "Nag", action: BrowserPopupAction.CloseWindowPattern);

            for (int i = 0; i < 3; i++)
            {
                var w = h.Probe.AddWindow("Nag");
                h.Now += 1000;
                h.AppearWindow(w);
                Assert.False(w.Alive);
            }
            var fourth = h.Probe.AddWindow("Nag");
            h.Now += 1000;
            h.AppearWindow(fourth);

            Assert.True(fourth.Alive);
            Assert.True(h.Engine.IsRuleTripped("nag"));
            Assert.Equal(3, h.Engine.TotalDismissals);
            var error = Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            Assert.Equal("nag", error.RuleName);
            Assert.Contains("SetRuleEnabled", error.Detail);

            Assert.True(h.Engine.SetRuleEnabled("nag", true));
            Assert.False(h.Engine.IsRuleTripped("nag"));
            h.Now += 1000;
            h.Pump(h.Now);
            Assert.False(fourth.Alive);
        }

        [Fact]
        public void RunawayCount_ForgetsDismissalsOlderThanAMinute()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 2;
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);

            for (int i = 0; i < 2; i++)
            {
                h.Now += 1000;
                h.AppearWindow(h.Probe.AddWindow("Alert"));
            }

            h.Now += BrowserPopupEngine.RunawayWindowMs + 1000;
            var late = h.Probe.AddWindow("Alert");
            h.AppearWindow(late);

            Assert.False(late.Alive);
            Assert.False(h.Engine.IsRuleTripped("r"));
        }

        // ------------------------------------------------------------------ own-process popups

        [Fact]
        public void PopupsOwnedByTheAutomationItself_AreNeverTouched_NativeDialog()
        {
            var h = new Harness();
            h.Probe.CurrentProcessId = 4242;
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert", pid: 4242);

            h.AppearWindow(w);
            h.Settle();

            Assert.True(w.Alive);
            Assert.Empty(h.Records);
        }

        [Fact]
        public void PopupsOwnedByTheAutomationItself_AreNeverTouched_PageOverlay()
        {
            var h = new Harness();
            h.Probe.CurrentProcessId = 4242;
            h.AddRule("r", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var window = h.Probe.AddWindow("tab", pid: 4242, processName: "chrome");
            h.AppearWindow(window);
            h.Probe.AddOverlay(window, "Cookie banner", pid: 4242);
            h.AppearOverlay(window);
            h.Settle();

            Assert.Empty(h.Records);
        }

        // ------------------------------------------------------------------ log

        [Fact]
        public void ClearLog_KeepsCounts()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            h.AppearWindow(h.Probe.AddWindow("Alert"));
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
            h.AddRule("watch", BrowserPopupScope.NativeDialog, nameContains: "W", action: BrowserPopupAction.WatchOnly);
            int total = BrowserPopupEngine.LogCapacity + 50;
            for (int i = 0; i < total; i++)
                h.AppearWindow(h.Probe.AddWindow("W " + i));

            var log = h.Engine.GetLog(10000);
            Assert.Equal(BrowserPopupEngine.LogCapacity, log.Length);
            Assert.Equal("W " + (total - 1), log[log.Length - 1].Name);
            Assert.Equal("W 50", log[0].Name);
            Assert.Equal(3, h.Engine.GetLog(3).Length);
        }

        [Fact]
        public void LogRecords_CarryScopeAndRole()
        {
            var h = new Harness();
            h.AddRule("watch", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.WatchOnly);
            var w = h.Probe.AddWindow("Alert");
            w.LocalizedControlType = "alert";

            h.AppearWindow(w);

            var record = Assert.Single(h.Records);
            Assert.Equal("NativeDialog", record.Scope);
            Assert.Equal("alert", record.Role);
        }

        // ------------------------------------------------------------------ pause

        [Fact]
        public void WhilePaused_PopupsAreLeftAlone_AndDismissedAfterResume()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");

            h.Engine.Paused = true;
            h.AppearWindow(w);
            h.Pump(300);
            h.Pump(600);
            Assert.True(w.Alive);
            Assert.Equal(0, w.Closes);

            h.Engine.Paused = false;
            h.Pump(900);

            Assert.False(w.Alive);
        }

        // ------------------------------------------------------------------ defense in depth

        [Fact]
        public void CloseWindowPatternAction_IsNeverAppliedToAPageOverlayCandidate_EvenIfSomehowAsked()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            // Enables overlay discovery for chrome without itself matching the overlay below.
            h.AddRule("watchChrome", BrowserPopupScope.PageOverlay, nameContains: "unrelated-zzz", process: "chrome");
            // A well-formed rule (Scope=NativeDialog) is required to get past AddRule's
            // ValidateCommon call; its Scope is then mutated afterwards to simulate a rule
            // object somehow ending up in an invalid combination the public API (a later phase)
            // should never allow through in the first place.
            var rule = h.AddRule("bad", BrowserPopupScope.NativeDialog, nameContains: "Cookie", action: BrowserPopupAction.CloseWindowPattern);
            rule.Scope = BrowserPopupScope.PageOverlay;

            var window = h.Probe.AddWindow("tab", pid: 5, processName: "chrome");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Cookie banner");
            h.AppearOverlay(window);

            Assert.True(overlay.Alive);
            Assert.Equal(0, overlay.Closes);
            var failure = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Contains("not valid", failure.Detail);
        }

        // ------------------------------------------------------------------ hook faults

        [Fact]
        public void AHookFault_IsRecordedOnTheNextPass_NotWhereItWasReported()
        {
            var h = new Harness();

            h.Engine.EnqueueFault("the event pump failed");
            Assert.Empty(h.Records);

            h.Pump(0);

            var error = Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            Assert.Equal("the event pump failed", error.Detail);
        }

        [Fact]
        public void HookFault_DeliveredViaStart_IsQueuedThroughEnqueueFault()
        {
            var h = new Harness();
            Assert.True(h.Hook.Start(h.Engine.EnqueueFault, out _));

            h.Hook.FireFault("boom");
            h.Pump(0);

            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
        }

        // Fixture sanity, not engine behavior: this exercises FakeBrowserPopupHookSource.Start()
        // directly and asserts nothing about BrowserPopupEngine. It proves the fake will behave
        // correctly when Task 5 reuses it to test Start(...); do not count it toward engine-behavior
        // coverage.
        [Fact]
        public void FakeHookSource_ReportsStartFailureWhenConfigured()
        {
            var hook = new FakeBrowserPopupHookSource { FailToStart = true };

            Assert.False(hook.Start(msg => { }, out string message));

            Assert.NotNull(message);
            Assert.Equal(1, hook.StartCalls);
        }

        // ------------------------------------------------------------------ process-name cache eviction

        /// <summary>
        /// Regresses the PID-reuse staleness bug in <c>_processNameCache</c>: once the last
        /// NativeDialog candidate for a process id is gone, that PID's cached name must be
        /// evicted, not left to be resolved for an unrelated later process. Without the eviction
        /// (in <c>RemoveCandidate</c>/<c>EvictProcessNameIfUnreferenced</c>), a PageOverlay
        /// candidate that happens to carry the reused PID would still resolve to the exited
        /// process's stale name and wrongly match a rule scoped to that stale name.
        /// </summary>
        [Fact]
        public void ProcessNameCache_IsEvictedWithTheLastCandidateForAPid_SoAReusedPidResolvesTheNewName()
        {
            var h = new Harness();
            h.AddRule("closeOld", BrowserPopupScope.NativeDialog, nameContains: "Dlg", process: "oldproc", action: BrowserPopupAction.CloseWindowPattern);
            h.AddRule("matchesNew", BrowserPopupScope.PageOverlay, nameContains: "Popup", process: "newproc");
            h.AddRule("staleMatch", BrowserPopupScope.PageOverlay, nameContains: "Popup", process: "oldproc");
            // Enables overlay watching for every process (no ProcessName criterion), independent
            // of the two rules above, so the still-open, unrelated window below gets watched.
            h.AddRule("watchAny", BrowserPopupScope.PageOverlay, nameContains: "zzz-never-matches-xyz");

            // The old process (pid 77) opens and is immediately dismissed: its only tracked
            // candidate is removed, which must evict pid 77's cached name ("oldproc").
            var oldWindow = h.Probe.AddWindow("Dlg", pid: 77, processName: "oldproc");
            h.AppearWindow(oldWindow);
            Assert.False(oldWindow.Alive);

            // An unrelated, still-open browser window - never itself using pid 77 - hosts a page
            // overlay whose reported ProcessId happens to be the reused pid 77 (Task 1's
            // BrowserElementInfo gives overlays only a ProcessId, so the engine has no way to
            // learn its owner's name except through the cache).
            var otherWindow = h.Probe.AddWindow("Tab", pid: 999, processName: "somebrowser");
            h.AppearWindow(otherWindow);
            var overlay = h.Probe.AddOverlay(otherWindow, "Popup banner", pid: 77);
            h.AppearOverlay(otherWindow);

            // With the stale entry evicted, pid 77 resolves to no name at all here (the engine
            // genuinely does not know it yet) - so it must NOT be wrongly resolved to "oldproc"
            // and match the rule scoped to the exited process.
            Assert.DoesNotContain(h.Of(BrowserPopupRecordKind.Detected), r => r.RuleName == "staleMatch");

            // The genuinely new process now announces itself via its own top-level window,
            // legitimately caching "newproc" for pid 77.
            h.Now += 200;
            var newWindow = h.Probe.AddWindow("NewProcDlg", pid: 77, processName: "newproc");
            h.AppearWindow(newWindow);

            // Advance to the overlay candidate's next scheduled recheck: it must now resolve pid
            // 77 to "newproc" and match the rule scoped to the genuinely new process.
            h.Pump(h.Now + BrowserPopupEngine.ScheduleMs[1]);
            Assert.Contains(h.Of(BrowserPopupRecordKind.Detected), r => r.RuleName == "matchesNew");
        }

        // ------------------------------------------------------------------ sweep discovery

        [Fact]
        public void PopupAlreadyOpen_IsFoundByTheNativeSweep_OnlyWhenTheSweepIsOn()
        {
            var withSweep = new Harness(sweepIntervalMs: 1000);
            withSweep.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var a = withSweep.Probe.AddWindow("Alert");
            withSweep.Pump(0);
            Assert.False(a.Alive);

            var noSweep = new Harness(sweepIntervalMs: 0);
            noSweep.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var b = noSweep.Probe.AddWindow("Alert");
            noSweep.Pump(0);
            noSweep.Pump(5000);
            Assert.True(b.Alive);
        }

        [Fact]
        public void PopupThatDisappearsByItself_IsForgotten_AndNeverReportedAsUnresolved()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var w = h.Probe.AddWindow("Alert");

            h.AppearWindow(w);
            h.Probe.Destroy(w);
            h.Settle(to: 4000);

            Assert.Empty(h.Records);
            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void RuleAddedAfterAPopupIsAlreadyOpen_TakesEffect_EvenWithTheScanOff()
        {
            var h = new Harness(sweepIntervalMs: 0);
            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w);
            h.Settle();
            Assert.True(w.Alive); // no rule yet: parked

            h.AddRule("late", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            h.Pump(3100);

            Assert.False(w.Alive);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
        }
    }
}
