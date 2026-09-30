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
                string nameContains = null, string automationId = null, string process = "chrome", string role = null, string message = null,
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

            /// <summary>Runs a pass once the verification delay after the last pass has elapsed (a successful action is only confirmed then).</summary>
            public long PastVerifyDelay() => Pump(Now + BrowserPopupEngine.VerifyDelayMs);

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
        public void AddRule_RejectsARuleWithNoProcessName()
        {
            var h = new Harness();
            var rule = new BrowserPopupRule { RuleName = "noProcess", Scope = BrowserPopupScope.NativeDialog, NameContains = "Alert" };

            Assert.False(h.Engine.AddRule(rule, out string message));
            Assert.Equal(BrowserPopupRule.ProcessRequiredMessage, message);
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
            Assert.False(h.Engine.AddRule(new BrowserPopupRule { RuleName = "ALPHA", Scope = BrowserPopupScope.NativeDialog, NameContains = "y", ProcessName = "chrome" }, out string dup));
            Assert.Contains("already exists", dup);

            for (int i = 1; i < BrowserPopupRule.MaxRules; i++)
                h.AddRule("rule" + i, BrowserPopupScope.NativeDialog, nameContains: "x");
            Assert.False(h.Engine.AddRule(new BrowserPopupRule { RuleName = "one too many", Scope = BrowserPopupScope.NativeDialog, NameContains = "y", ProcessName = "chrome" }, out string full));
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

        [Fact]
        public void StopActionsDuringTargetDiscovery_PreventsActionAndRuntimeResetAllowsItAgain()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert",
                action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var window = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(window, "Yes");
            using var discoveryEntered = new ManualResetEventSlim(false);
            using var finishDiscovery = new ManualResetEventSlim(false);
            h.Probe.OnFindOverlayCandidates = () =>
            {
                discoveryEntered.Set();
                if (!finishDiscovery.Wait(5000))
                    throw new TimeoutException("The test did not release target discovery.");
            };
            h.Hook.FireWindowOpened(window);
            Exception pumpFailure = null;
            var worker = new Thread(() =>
            {
                try { h.Pump(100); }
                catch (Exception ex) { pumpFailure = ex; }
            }) { IsBackground = true };
            worker.Start();

            Assert.True(discoveryEntered.Wait(5000), "target discovery did not start");
            h.Engine.StopActions();
            finishDiscovery.Set();
            Assert.True(worker.Join(5000), "the popup pump did not finish");
            Assert.Null(pumpFailure);
            Assert.Equal(0, yes.Invokes);

            h.Probe.OnFindOverlayCandidates = null;
            h.Engine.ResetRuntime();
            var nextWindow = h.Probe.AddWindow("Alert");
            var nextYes = h.Probe.AddChild(nextWindow, "Yes");
            h.AppearWindow(nextWindow);
            Assert.Equal(1, nextYes.Invokes);
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

        [Theory]
        [InlineData("RemoveRule")]
        [InlineData("ClearRules")]
        [InlineData("SetRuleEnabledFalse")]
        public void RuleSwitchedOff_WaitsForAnActionAlreadyInFlight_ThenNothingFurtherLandsForThatRule(string how)
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var first = h.Probe.AddWindow("Alert");
            var firstYes = h.Probe.AddChild(first, "Yes");
            var entered = new ManualResetEventSlim(false);
            var gate = new ManualResetEventSlim(false);
            h.Probe.ActionEntered = () => entered.Set();
            h.Probe.ActionGate = gate;
            h.Hook.FireWindowOpened(first);

            var pump = new Thread(() => h.Engine.Pump(0)) { IsBackground = true };
            pump.Start();
            Assert.True(entered.Wait(10000), "the action never started");

            int returned = 0;
            var switchingOff = new Thread(() =>
            {
                switch (how)
                {
                    case "RemoveRule": h.Engine.RemoveRule("r"); break;
                    case "ClearRules": h.Engine.ClearRules(); break;
                    default: h.Engine.SetRuleEnabled("r", false); break;
                }
                Volatile.Write(ref returned, 1);
            }) { IsBackground = true };
            switchingOff.Start();
            Assert.False(switchingOff.Join(400), how + " returned while an action was still in flight");
            Assert.Equal(0, Volatile.Read(ref returned));

            gate.Set();
            Assert.True(switchingOff.Join(10000), how + " did not return once the action finished");
            Assert.True(pump.Join(10000));
            Assert.False(first.Alive); // the action already under way completed
            Assert.Equal(1, firstYes.Invokes);

            var second = h.Probe.AddWindow("Alert");
            var secondYes = h.Probe.AddChild(second, "Yes");
            h.Probe.ActionGate = null;
            h.Hook.FireWindowOpened(second);
            h.Engine.Pump(1000);
            h.Engine.Pump(2000);
            Assert.True(second.Alive);
            Assert.Equal(0, secondYes.Invokes); // nothing further landed after the call returned
            Assert.Equal(1, h.Probe.TotalInvokes);
        }

        [Fact]
        public void RuleDisabledDuringDiscovery_DoesNotAct_DoesNotSpendAnAttempt_AndReschedulesTheCandidate()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1; // one spent attempt would be enough to fail the popup
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            h.Probe.OnFindOverlayCandidates = () =>
            {
                h.Probe.OnFindOverlayCandidates = null;
                h.Engine.SetRuleEnabled("r", false); // lands after Evaluate chose the rule, before the action
            };

            long next = h.AppearWindow(w);

            Assert.Equal(0, yes.Invokes);
            Assert.True(w.Alive);
            Assert.Equal(BrowserPopupEngine.PausedRecheckMs, next); // rescheduled for a later look, not parked
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(1, h.Engine.UnresolvedCount); // still open, still tracked (not failed or forgotten)

            // The attempt was not spent: turned back on, the popup is still dismissed on the first try.
            Assert.True(h.Engine.SetRuleEnabled("r", true));
            h.Pump(BrowserPopupEngine.PausedRecheckMs);
            Assert.Equal(1, yes.Invokes);
            Assert.False(w.Alive);
            h.PastVerifyDelay();
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
        }

        [Fact]
        public void RuleRemovedDuringDiscovery_DoesNotAct_DoesNotSpendAnAttempt_AndALaterRuleStillGetsTheFirstTry()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            h.Probe.OnFindOverlayCandidates = () =>
            {
                h.Probe.OnFindOverlayCandidates = null;
                h.Engine.RemoveRule("r");
            };

            long next = h.AppearWindow(w);

            Assert.Equal(0, yes.Invokes);
            Assert.True(w.Alive);
            Assert.Equal(BrowserPopupEngine.PausedRecheckMs, next);
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));

            h.AddRule("r2", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            h.Pump(BrowserPopupEngine.PausedRecheckMs);
            Assert.Equal(1, yes.Invokes);
            h.PastVerifyDelay();
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
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
            h.PastVerifyDelay();
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

        private sealed class StateSnapshot
        {
            public BrowserPopupRecordKind Kind;
            public int Count;
            public int Total;
            public int Unresolved;
            public int Tracked;
        }

        /// <summary>An engine whose sink captures the engine's own state at the moment each record is delivered (on the pumping thread, like the real event).</summary>
        private static BrowserPopupEngine EngineCapturingStateAtDelivery(FakeBrowserPopupProbe probe, FakeBrowserPopupHookSource hook, List<StateSnapshot> seen)
        {
            BrowserPopupEngine engine = null;
            engine = new BrowserPopupEngine(probe, hook, r =>
            {
                engine.TryGetCount("r", out int count);
                seen.Add(new StateSnapshot
                {
                    Kind = r.Kind,
                    Count = count,
                    Total = engine.TotalDismissals,
                    Unresolved = engine.UnresolvedCount,
                    Tracked = engine.TrackedCandidateCountForTests(BrowserPopupScope.NativeDialog)
                });
            })
            { SweepIntervalMs = 0, OverlaySweepIntervalMs = 0 };
            return engine;
        }

        [Fact]
        public void DismissedRecord_IsDeliveredOnlyAfterCountsAndUnresolvedStateAreFinal()
        {
            var probe = new FakeBrowserPopupProbe();
            var hook = new FakeBrowserPopupHookSource();
            var seen = new List<StateSnapshot>();
            var engine = EngineCapturingStateAtDelivery(probe, hook, seen);
            Assert.True(engine.AddRule(new BrowserPopupRule
            {
                RuleName = "r", Scope = BrowserPopupScope.NativeDialog, NameContains = "Alert", ProcessName = "chrome",
                Action = BrowserPopupAction.InvokeByName, TargetElementName = "Yes"
            }, out string m), m);
            var w = probe.AddWindow("Alert");
            probe.AddChild(w, "Yes");

            hook.FireWindowOpened(w);
            engine.Pump(0);
            engine.Pump(BrowserPopupEngine.VerifyDelayMs);

            var dismissed = Assert.Single(seen, s => s.Kind == BrowserPopupRecordKind.Dismissed);
            Assert.Equal(1, dismissed.Count);
            Assert.Equal(1, dismissed.Total);
            Assert.Equal(0, dismissed.Unresolved); // the popup is gone and the unresolved count already reflects it
            Assert.Equal(0, dismissed.Tracked);
        }

        [Fact]
        public void DismissFailedRecord_IsDeliveredOnlyAfterTheFailureStateIsFinal()
        {
            var probe = new FakeBrowserPopupProbe();
            var hook = new FakeBrowserPopupHookSource();
            var seen = new List<StateSnapshot>();
            var engine = EngineCapturingStateAtDelivery(probe, hook, seen);
            engine.MaxAttempts = 1;
            Assert.True(engine.AddRule(new BrowserPopupRule
            {
                RuleName = "r", Scope = BrowserPopupScope.NativeDialog, NameContains = "Alert", ProcessName = "chrome",
                Action = BrowserPopupAction.InvokeByName, TargetElementName = "Missing"
            }, out string m), m);
            var w = probe.AddWindow("Alert");
            probe.AddChild(w, "Yes"); // no "Missing" target: the attempt fails

            hook.FireWindowOpened(w);
            for (long t = 0; t <= 3100; t += 100)
                engine.Pump(t); // walks the retry schedule until the missing target exhausts it

            var failed = Assert.Single(seen, s => s.Kind == BrowserPopupRecordKind.DismissFailed);
            Assert.Equal(0, failed.Count);
            Assert.Equal(0, failed.Total);
            Assert.Equal(1, failed.Unresolved); // still open, and already counted as such when the event fires
            Assert.Equal(1, failed.Tracked);
        }

        [Fact]
        public void RecordsRaisedDuringAPass_AreDeliveredInOrderAfterThePassFinishes()
        {
            var probe = new FakeBrowserPopupProbe();
            var hook = new FakeBrowserPopupHookSource();
            var seen = new List<StateSnapshot>();
            var engine = EngineCapturingStateAtDelivery(probe, hook, seen);
            engine.EnqueueFault("boom");

            engine.Pump(0);

            var error = Assert.Single(seen);
            Assert.Equal(BrowserPopupRecordKind.Error, error.Kind);
            Assert.Single(engine.GetLog(10)); // logged and delivered exactly once
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
            h.PastVerifyDelay();
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
        public void PageOverlayRule_OnlyWatchesAndSweepsWindowsOfItsNamedProcess()
        {
            var h = new Harness(overlaySweepIntervalMs: 100);
            h.AddRule("chromeOnly", BrowserPopupScope.PageOverlay, nameContains: "Accept", process: "chrome");
            var chromeWindow = h.Probe.AddWindow("tab", pid: 101, processName: "chrome");
            var firefoxWindow = h.Probe.AddWindow("tab2", pid: 102, processName: "firefox");
            var notepadWindow = h.Probe.AddWindow("Untitled", pid: 103, processName: "notepad");
            h.Probe.AddOverlay(firefoxWindow, "Accept cookies");
            h.Probe.AddOverlay(notepadWindow, "Accept");

            h.AppearWindow(chromeWindow);
            h.AppearWindow(firefoxWindow);
            h.AppearWindow(notepadWindow);
            h.AppearOverlay(firefoxWindow);
            h.AppearOverlay(notepadWindow);
            h.Settle();

            Assert.Equal(new[] { chromeWindow.Ref }, h.Hook.WatchCalls);
            Assert.DoesNotContain(firefoxWindow.Ref, h.Probe.OverlaySearchRoots);
            Assert.DoesNotContain(notepadWindow.Ref, h.Probe.OverlaySearchRoots);
            Assert.Contains(chromeWindow.Ref, h.Probe.OverlaySearchRoots);
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
        }

        [Fact]
        public void NativeRuleForChrome_NeverDescribesWatchesOrActsOnAnotherProcessesWindow()
        {
            var h = new Harness();
            h.AddRule("confirmClose", BrowserPopupScope.NativeDialog, nameContains: "Confirm", process: "chrome", action: BrowserPopupAction.CloseWindowPattern);
            var firefoxDialog = h.Probe.AddWindow("Confirm", pid: 102, processName: "firefox");
            var notepadDialog = h.Probe.AddWindow("Confirm", pid: 103, processName: "notepad");

            h.AppearWindow(firefoxDialog);
            h.AppearWindow(notepadDialog);
            h.Settle();

            Assert.Equal(0, h.Probe.DescribeWindowCalls);
            Assert.Empty(h.Hook.WatchCalls);
            Assert.Equal(0, h.Probe.OverlaySearchCalls);
            Assert.Equal(0, h.Probe.MessageTextCalls);
            Assert.Equal(0, h.Probe.TotalCloses);
            Assert.True(firefoxDialog.Alive);
            Assert.True(notepadDialog.Alive);
            Assert.Empty(h.Records);
        }

        [Fact]
        public void ProcessNamesWithAndWithoutExe_AreEquivalentForRulesAndWindows()
        {
            var h = new Harness();
            h.AddRule("withExe", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "Chrome.EXE", action: BrowserPopupAction.CloseWindowPattern);
            h.AddRule("overlayNoExe", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "msedge");
            var dialog = h.Probe.AddWindow("Alert", pid: 100, processName: "chrome");
            var edge = h.Probe.AddWindow("edge tab", pid: 200, processName: "msedge.exe");

            h.AppearWindow(dialog);
            h.AppearWindow(edge);
            h.PastVerifyDelay();

            Assert.False(dialog.Alive);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Contains(edge.Ref, h.Hook.WatchCalls);
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

        // ------------------------------------------------------------------ overlay process identity

        [Fact]
        public void Overlay_InheritsItsWindowsProcessIdentity_WhenTheElementReportsADifferentPid()
        {
            var h = new Harness();
            h.AddRule("banner", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome",
                action: BrowserPopupAction.InvokeByName, targetName: "Accept");
            var window = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Cookie banner", pid: 9999); // renderer-owned element
            h.Probe.AddChild(overlay, "Accept");

            h.AppearOverlay(window);
            h.PastVerifyDelay();

            var dismissed = Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(100, dismissed.ProcessId);
            Assert.Equal("chrome", dismissed.ProcessName);
            Assert.False(overlay.Alive);
        }

        [Fact]
        public void OwnProcessSkip_JudgesAnOverlayByItsWindowsIdentity_NotTheElements()
        {
            var h = new Harness();
            h.Probe.CurrentProcessId = 4242;
            h.AddRule("banner", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome",
                action: BrowserPopupAction.InvokeByName, targetName: "Accept");

            // The window is the automation's own; its element reports some other PID: still hands off.
            var ownWindow = h.Probe.AddWindow("own tab", pid: 4242, processName: "chrome");
            h.AppearWindow(ownWindow);
            var ownOverlay = h.Probe.AddOverlay(ownWindow, "Cookie banner", pid: 7);
            var ownButton = h.Probe.AddChild(ownOverlay, "Accept");

            // Another browser's window whose element claims the automation's PID: that is not the automation's popup.
            var otherWindow = h.Probe.AddWindow("other tab", pid: 100, processName: "chrome");
            h.AppearWindow(otherWindow);
            var otherOverlay = h.Probe.AddOverlay(otherWindow, "Cookie banner", pid: 4242);
            h.Probe.AddChild(otherOverlay, "Accept");

            h.AppearOverlay(ownWindow);
            h.AppearOverlay(otherWindow);
            h.PastVerifyDelay();

            Assert.True(ownOverlay.Alive);
            Assert.Equal(0, ownButton.Invokes);
            Assert.False(otherOverlay.Alive);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        [Fact]
        public void Overlay_UnderAWindowOfAnotherProcess_IsNotMatchedByAProcessScopedRule()
        {
            var h = new Harness();
            h.AddRule("chromeOnly", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            // A second rule that names firefox, so the firefox window is watched and walked too.
            h.AddRule("watchFirefox", BrowserPopupScope.PageOverlay, nameContains: "zzz-never-matches-xyz", process: "firefox");
            var chromeWindow = h.Probe.AddWindow("chrome tab", pid: 100, processName: "chrome");
            var firefoxWindow = h.Probe.AddWindow("firefox tab", pid: 200, processName: "firefox");
            h.AppearWindow(chromeWindow);
            h.AppearWindow(firefoxWindow);
            // The element even claims chrome's PID, but it lives under firefox's window.
            h.Probe.AddOverlay(firefoxWindow, "Cookie banner", pid: 100);

            h.AppearOverlay(firefoxWindow);
            h.Settle();

            Assert.Contains(firefoxWindow.Ref, h.Probe.OverlaySearchRoots); // it really was walked
            Assert.Empty(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
        }

        // ------------------------------------------------------------------ log

        [Fact]
        public void ClearLog_KeepsCounts()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            h.AppearWindow(h.Probe.AddWindow("Alert"));
            h.PastVerifyDelay();
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MatchingPopupOpeningDuringPause_IsUnresolved_UntouchedUntilResume_ThenDismissedPromptly(bool useClose)
        {
            var h = new Harness();
            if (useClose)
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            else
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var w = h.Probe.AddWindow("Alert");
            var ok = h.Probe.AddChild(w, "OK");

            h.Engine.Paused = true;
            h.AppearWindow(w);
            for (long t = 300; t <= 1500; t += 300)
                h.Pump(t);

            Assert.True(h.Engine.HasUnresolvedPopup);
            Assert.Equal(1, h.Engine.UnresolvedCount);
            Assert.Equal(0, h.Probe.TotalCloses);
            Assert.Equal(0, h.Probe.TotalInvokes);
            Assert.Equal(0, h.Probe.OverlaySearchCalls); // not even the target walk
            Assert.True(w.Alive);

            h.Engine.Resume();
            h.Pump(h.Now); // one pass at the same instant: no wait for the hold-back to run out

            Assert.Equal(useClose ? 1 : 0, w.Closes);
            Assert.Equal(useClose ? 0 : 1, ok.Invokes);
            h.PastVerifyDelay();
            Assert.False(w.Alive);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.False(h.Engine.HasUnresolvedPopup);
        }

        [Fact]
        public void NonMatchingPopupOpeningDuringPause_IsNotUnresolved()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var other = h.Probe.AddWindow("Something else");

            h.Engine.Paused = true;
            h.AppearWindow(other);
            h.Settle();

            Assert.False(h.Engine.HasUnresolvedPopup);
            Assert.Equal(0, h.Probe.TotalCloses);
            Assert.True(other.Alive);
        }

        [Fact]
        public void WatchOnlyMatchDuringPause_IsReportedOnce_AndNeverCountsAsUnresolved()
        {
            var h = new Harness();
            h.AddRule("watch", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.WatchOnly);
            var w = h.Probe.AddWindow("Alert");

            h.Engine.Paused = true;
            h.AppearWindow(w);
            h.Settle();

            Assert.Single(h.Of(BrowserPopupRecordKind.Detected)); // reporting is not touching the popup
            Assert.False(h.Engine.HasUnresolvedPopup);

            h.Engine.Resume();
            h.Pump(h.Now + 1000);
            Assert.Single(h.Of(BrowserPopupRecordKind.Detected)); // still once
            Assert.True(w.Alive);
        }

        [Fact]
        public void SeveralPopupsDuringPause_AreAllCounted_AndAllDismissedAfterResume()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var first = h.Probe.AddWindow("Alert one");
            var second = h.Probe.AddWindow("Alert two");
            var unrelated = h.Probe.AddWindow("Unrelated");

            h.Engine.Paused = true;
            h.AppearWindow(first);
            h.AppearWindow(unrelated);
            h.Pump(300);
            Assert.Equal(1, h.Engine.UnresolvedCount);
            h.AppearWindow(second);
            h.Pump(600);
            Assert.Equal(2, h.Engine.UnresolvedCount);
            Assert.Equal(0, h.Probe.TotalCloses);

            h.Engine.Resume();
            h.Pump(h.Now);
            h.PastVerifyDelay();

            Assert.False(first.Alive);
            Assert.False(second.Alive);
            Assert.True(unrelated.Alive);
            Assert.Equal(2, h.Of(BrowserPopupRecordKind.Dismissed).Count());
            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void HeldPopup_IsNotReSelectedOrItsMessageReReadOnEveryPausedRecheck()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, message: "sure?", nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert", message: "Are you sure?");

            h.Engine.Paused = true;
            h.AppearWindow(w);
            int readsAfterFirst = h.Probe.MessageTextCalls;
            for (long t = 300; t <= 3000; t += 300)
                h.Pump(t);

            Assert.Equal(1, readsAfterFirst);
            Assert.Equal(readsAfterFirst, h.Probe.MessageTextCalls);
            Assert.True(h.Engine.HasUnresolvedPopup);
        }

        [Fact]
        public void PendingVerification_IsUnchangedByPause_StillConfirmedWhilePaused()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w); // closed, verification pending
            Assert.True(h.Engine.HasUnresolvedPopup);

            h.Engine.Paused = true;
            h.PastVerifyDelay();

            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.False(h.Engine.HasUnresolvedPopup);
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

        // ------------------------------------------------------------------ queue overflow

        private static readonly BrowserWindowInfo AnyWindow = new BrowserWindowInfo { Hwnd = new IntPtr(0x1230), ProcessName = "chrome", ProcessId = 7 };

        private static void FillNative(Harness h, int count)
        {
            for (int i = 0; i < count; i++)
                h.Hook.FireWindowOpened(AnyWindow);
        }

        private static void FillOverlay(Harness h, int count)
        {
            var root = new BrowserElementRef(new[] { 1 }, new IntPtr(0x1230));
            for (int i = 0; i < count; i++)
                h.Hook.FireStructureChanged(root);
        }

        [Fact]
        public void NativeQueueOverflow_RecordsOneErrorPerFillEpisode_AndRearmsOnceDrained()
        {
            var h = new Harness();

            FillNative(h, BrowserPopupEngine.MaxQueuedItems + 25); // many drops, one episode
            h.Pump(0);
            var first = Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            Assert.Contains("window", first.Detail);
            Assert.Contains("sweep", first.Detail);
            Assert.Contains("interval of 0", first.Detail); // says a sweep interval of 0 can lose the event

            h.Pump(100); // nothing new dropped: still one
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));

            FillNative(h, BrowserPopupEngine.MaxQueuedItems + 1); // drained, refilled past the cap
            h.Pump(200);
            Assert.Equal(2, h.Of(BrowserPopupRecordKind.Error).Count());
        }

        [Fact]
        public void NativeQueue_AtExactlyTheCap_DropsNothingAndRecordsNothing()
        {
            var h = new Harness();

            FillNative(h, BrowserPopupEngine.MaxQueuedItems);
            h.Pump(0);

            Assert.Empty(h.Of(BrowserPopupRecordKind.Error));
        }

        [Fact]
        public void OverlayQueueOverflow_RecordsOneErrorPerFillEpisode_AndRearmsOnceDrained()
        {
            var h = new Harness();

            FillOverlay(h, BrowserPopupEngine.MaxQueuedItems + 25);
            h.Pump(0);
            var first = Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            Assert.Contains("page", first.Detail);
            Assert.Contains("sweep", first.Detail);

            FillOverlay(h, BrowserPopupEngine.MaxQueuedItems + 1);
            h.Pump(100);
            Assert.Equal(2, h.Of(BrowserPopupRecordKind.Error).Count());
        }

        [Fact]
        public void OverlayQueue_AtExactlyTheCap_RecordsNothing()
        {
            var h = new Harness();

            FillOverlay(h, BrowserPopupEngine.MaxQueuedItems);
            h.Pump(0);

            Assert.Empty(h.Of(BrowserPopupRecordKind.Error));
        }

        [Fact]
        public void BothQueuesOverflowing_RecordOneErrorEach()
        {
            var h = new Harness();

            FillNative(h, BrowserPopupEngine.MaxQueuedItems + 1);
            FillOverlay(h, BrowserPopupEngine.MaxQueuedItems + 1);
            h.Pump(0);

            Assert.Equal(2, h.Of(BrowserPopupRecordKind.Error).Count());
        }

        [Fact]
        public void ResetRuntime_ForgetsAnUnreportedDrop()
        {
            var h = new Harness();

            FillNative(h, BrowserPopupEngine.MaxQueuedItems + 1);
            h.Engine.ResetRuntime();
            h.Pump(0);

            Assert.Empty(h.Of(BrowserPopupRecordKind.Error));
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
            h.PastVerifyDelay(); // the dismissal is confirmed (and the candidate removed) only after the verify delay

            // The reused pid 77 now belongs to a still-open browser window whose process name could
            // not be resolved (an empty name is never cached). Its page overlay inherits that
            // window's identity, so the engine has no way to learn the name except through the cache.
            var newWindow = h.Probe.AddWindow("Tab", pid: 77, processName: "");
            h.AppearWindow(newWindow);
            h.Probe.AddOverlay(newWindow, "Popup banner");
            h.AppearOverlay(newWindow);

            // With the stale entry evicted, pid 77 resolves to no name at all here (the engine
            // genuinely does not know it yet) - so it must NOT be wrongly resolved to "oldproc"
            // and match the rule scoped to the exited process.
            Assert.DoesNotContain(h.Of(BrowserPopupRecordKind.Detected), r => r.RuleName == "staleMatch");

            // The genuinely new process now announces itself under its real name, legitimately
            // caching "newproc" for pid 77.
            h.Now += 200;
            h.Hook.FireWindowOpened(new BrowserWindowInfo { Hwnd = newWindow.Ref.Hwnd, ProcessId = 77, ProcessName = "newproc" });

            // The next sweep of that window (nothing was admitted while its name was unknown) must
            // now resolve pid 77 to "newproc" and match the rule scoped to the genuinely new process.
            h.AppearOverlay(newWindow);
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
            h.PastVerifyDelay();
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        // ------------------------------------------------------------------ native pre-filter (C1)

        [Fact]
        public void NativeWindow_OfAnUnrelatedProcess_IsNeverDescribed()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("alert", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "chrome", action: BrowserPopupAction.CloseWindowPattern);
            var notepad = h.Probe.AddWindow("Untitled - Notepad", pid: 300, processName: "notepad");
            var explorer = h.Probe.AddWindow("File Explorer", pid: 301, processName: "explorer");

            h.AppearWindow(notepad); // via the hook
            h.Pump(1000);            // via the sweep
            h.Pump(2000);

            Assert.Equal(0, h.Probe.DescribeWindowCalls);
            Assert.Empty(h.Records);
            Assert.True(notepad.Alive && explorer.Alive);
        }

        [Fact]
        public void NativeWindow_OfTheRulesProcess_IsDescribedAndDismissed_WithoutDescribingOthers()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("alert", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "chrome.exe", action: BrowserPopupAction.CloseWindowPattern);
            var notepad = h.Probe.AddWindow("Alert - Notepad", pid: 300, processName: "notepad");
            var dialog = h.Probe.AddWindow("Alert", pid: 100, processName: "chrome");

            h.Pump(0);
            h.Settle();

            Assert.False(dialog.Alive);
            Assert.True(notepad.Alive);
            Assert.True(h.Probe.DescribeWindowCalls > 0);
        }

        [Fact]
        public void NativeWindow_SkippedBeforeARuleExists_IsPickedUpOnTheNextSweepAfterTheRuleIsAdded()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            var dialog = h.Probe.AddWindow("Alert", pid: 100, processName: "chrome");
            h.Pump(0);
            Assert.Equal(0, h.Probe.DescribeWindowCalls);

            h.AddRule("alert", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "chrome", action: BrowserPopupAction.CloseWindowPattern);
            h.Pump(1000);
            h.Settle(1100, 4000);

            Assert.False(dialog.Alive);
        }

        [Fact]
        public void NativeWindow_TrackedOnlyForOverlayWatching_IsNotReDescribedEverySweep()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var browser = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");

            h.Pump(0);
            h.Settle(100, 3000);
            int describes = h.Probe.DescribeWindowCalls;

            Assert.Contains(browser.Ref, h.Hook.CurrentlyWatched);
            Assert.Equal(1, describes); // once, to learn its ref; never for evaluation
            h.Pump(5000);
            h.Pump(6000);
            Assert.Equal(describes, h.Probe.DescribeWindowCalls);
        }

        [Fact]
        public void DisablingTheLastOverlayRule_UnwatchesTheProcess_AndStopsOverlaySearches()
        {
            var h = new Harness(sweepIntervalMs: 1000, overlaySweepIntervalMs: 500);
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var browser = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            h.Pump(0);
            Assert.Contains(browser.Ref, h.Hook.CurrentlyWatched);

            Assert.True(h.Engine.SetRuleEnabled("cookie", false));
            h.Pump(100);

            Assert.Contains(browser.Ref, h.Hook.UnwatchCalls);
            Assert.DoesNotContain(browser.Ref, h.Hook.CurrentlyWatched);
            int searches = h.Probe.OverlaySearchCalls;
            h.Hook.FireStructureChanged(browser.Ref);
            h.Pump(700);
            h.Pump(1500);
            h.Pump(3000);
            Assert.Equal(searches, h.Probe.OverlaySearchCalls); // nothing searches a process no enabled rule wants
        }

        [Fact]
        public void ReEnablingAnOverlayRule_WatchesTheProcessAgain()
        {
            var h = new Harness(sweepIntervalMs: 1000, overlaySweepIntervalMs: 500);
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var browser = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            h.Pump(0);
            Assert.True(h.Engine.SetRuleEnabled("cookie", false));
            h.Pump(100);
            Assert.DoesNotContain(browser.Ref, h.Hook.CurrentlyWatched);

            Assert.True(h.Engine.SetRuleEnabled("cookie", true));
            h.Pump(200);

            Assert.Contains(browser.Ref, h.Hook.CurrentlyWatched);
        }

        [Fact]
        public void DisablingOneOfTwoOverlayRules_KeepsWatchingWhileAnotherEnabledRuleWantsTheProcess()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("a", BrowserPopupScope.PageOverlay, nameContains: "A", process: "chrome");
            h.AddRule("b", BrowserPopupScope.PageOverlay, nameContains: "B", process: "chrome");
            var browser = h.Probe.AddWindow("tab", pid: 100, processName: "chrome");
            h.Pump(0);

            Assert.True(h.Engine.SetRuleEnabled("a", false));
            h.Pump(100);

            Assert.Contains(browser.Ref, h.Hook.CurrentlyWatched);
            Assert.Empty(h.Hook.UnwatchCalls);
        }

        // ------------------------------------------------------------------ message read last (I1)

        [Fact]
        public void MessageText_IsNotFetched_WhenTheCheapCriteriaFail()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Session", message: "expire", action: BrowserPopupAction.CloseWindowPattern);
            var other = h.Probe.AddWindow("Something else", "expire soon");

            h.AppearWindow(other);
            h.Settle();

            Assert.Equal(0, h.Probe.MessageTextCalls);
            Assert.True(other.Alive);
        }

        [Fact]
        public void MessageText_IsFetchedOncePerEvaluation_ForManyMessageRules()
        {
            var h = new Harness();
            for (int i = 0; i < 4; i++)
                h.AddRule("r" + i, BrowserPopupScope.NativeDialog, nameContains: "Session", message: "never-" + i, action: BrowserPopupAction.WatchOnly);
            var w = h.Probe.AddWindow("Session dialog", "hello");

            h.AppearWindow(w);

            Assert.Equal(1, h.Probe.MessageTextCalls);
        }

        [Fact]
        public void OverlayMessageRule_StillMatchesOnceTheCheapCriteriaPass()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.PageOverlay, role: "dialog", message: "cookies");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            h.Probe.AddOverlay(window, "Banner", "We use cookies");
            h.AppearOverlay(window);

            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
        }

        // ------------------------------------------------------------------ dirty queue de-duplication (I2)

        [Fact]
        public void ManyDirtySignals_ForOneWindow_CostOneOverlaySearchPerPump()
        {
            var h = new Harness();
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            int before = h.Probe.OverlaySearchCalls;

            for (int i = 0; i < 25; i++)
                h.Hook.FireStructureChanged(window.Ref);
            h.Engine.Pump(h.Now);

            Assert.Equal(1, h.Probe.OverlaySearchCalls - before);
        }

        [Fact]
        public void DirtySignals_ForTwoWindows_SweepEachOnce_AndPeriodicSweepDoesNotRepeatThemInTheSamePass()
        {
            var h = new Harness(overlaySweepIntervalMs: 1000);
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, role: "dialog");
            var a = h.Probe.AddWindow("a");
            var b = h.Probe.AddWindow("b");
            h.AppearWindow(a);
            h.AppearWindow(b);
            h.Pump(5000);
            int before = h.Probe.OverlaySearchCalls;

            for (int i = 0; i < 5; i++)
            {
                h.Hook.FireStructureChanged(a.Ref);
                h.Hook.FireStructureChanged(b.Ref);
            }
            h.Engine.Pump(6000); // periodic sweep is also due in this pass

            Assert.Equal(2, h.Probe.OverlaySearchCalls - before);
        }

        // ------------------------------------------------------------------ candidate admission (I3)

        [Fact]
        public void LargePage_WithALateMatchingDialog_StillDetectsIt_AndTracksNoIrrelevantElements()
        {
            var h = new Harness();
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Cookie consent", role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            for (int i = 0; i < 3000; i++)
                h.Probe.AddChild(window, "item " + i, role: "text");
            var dialog = h.Probe.AddOverlay(window, "Cookie consent", role: "dialog");
            h.Engine.MaxOverlayNodes = 5000;

            h.AppearOverlay(window);

            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Empty(h.Of(BrowserPopupRecordKind.Error));
            Assert.Equal(1, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
        }

        [Fact]
        public void ElementsMatchingNoOverlayRule_AreNeverTracked()
        {
            var h = new Harness();
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Cookie", role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            for (int i = 0; i < 50; i++)
                h.Probe.AddOverlay(window, "Other " + i, role: "dialog");

            h.AppearOverlay(window);

            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
        }

        [Fact]
        public void CandidateCap_IsReportedOnce_NotEverySweep()
        {
            var h = new Harness(overlaySweepIntervalMs: 500);
            h.AddRule("any", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            for (int i = 0; i < BrowserPopupEngine.MaxTrackedCandidates + 10; i++)
                h.Probe.AddOverlay(window, "d" + i, role: "dialog");
            h.Engine.MaxOverlayNodes = 100000;

            h.AppearOverlay(window);
            h.Pump(h.Now + 600);
            h.Pump(h.Now + 600);

            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
        }

        // ------------------------------------------------------------------ target choice (I6)

        [Fact]
        public void PartialNameTarget_SkipsATextNodeListedBeforeTheButton()
        {
            var h = new Harness();
            h.AddRule("terms", BrowserPopupScope.PageOverlay, role: "dialog", action: BrowserPopupAction.InvokeByName,
                targetName: "accept", exactTarget: false);
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Terms", role: "dialog");
            var text = h.Probe.AddChild(overlay, "Please accept the terms", role: "text");
            text.IgnoreInvoke = true;
            var button = h.Probe.AddChild(overlay, "Accept all", role: "button");

            h.AppearOverlay(window);
            h.Settle();

            Assert.Equal(1, button.Invokes);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal("Accept all", h.Of(BrowserPopupRecordKind.Dismissed).Single().TargetInvoked);
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
        }

        [Fact]
        public void PartialNameTarget_PrefersAButtonOverAnEarlierNonButtonMatch()
        {
            var h = new Harness();
            h.AddRule("terms", BrowserPopupScope.PageOverlay, role: "dialog", action: BrowserPopupAction.InvokeByName,
                targetName: "accept", exactTarget: false);
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Terms", role: "dialog");
            var text = h.Probe.AddChild(overlay, "Please accept the terms", role: "text");
            text.ControlType = "Text";
            var button = h.Probe.AddChild(overlay, "Accept all", role: "button");

            h.AppearOverlay(window);

            Assert.Equal(0, text.Invokes);
            Assert.Equal(1, button.Invokes);
        }

        [Fact]
        public void PartialNameTarget_TriesLaterMatchesWithinTheSameAttempt_WhenNoneIsButtonLike()
        {
            var h = new Harness();
            h.AddRule("terms", BrowserPopupScope.PageOverlay, role: "dialog", action: BrowserPopupAction.InvokeByName,
                targetName: "ok", exactTarget: false);
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Terms", role: "dialog");
            var first = h.Probe.AddChild(overlay, "ok text", role: "text");
            first.ControlType = "Text";
            first.IgnoreInvoke = true;
            var second = h.Probe.AddChild(overlay, "ok link", role: "link");
            second.ControlType = "Hyperlink";

            h.AppearOverlay(window);

            Assert.Equal(1, first.Invokes);
            Assert.Equal(1, second.Invokes);
            h.PastVerifyDelay();
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        // ------------------------------------------------------------------ verify-before-count (I5)

        [Fact]
        public void Dismissal_IsRecordedAndCounted_OnlyOnceTheVerifyDelayHasPassedAndThePopupIsGone()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");

            long next = h.AppearWindow(w);

            Assert.Equal(1, yes.Invokes);
            Assert.Equal(BrowserPopupEngine.VerifyDelayMs, next); // the worker is told to wake for the verification
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.False(h.Engine.TryGetCount("r", out int early) && early != 0);
            Assert.Equal(0, h.Engine.TotalDismissals);
            Assert.True(h.Engine.HasUnresolvedPopup); // still unresolved until seen to close

            h.Pump(BrowserPopupEngine.VerifyDelayMs - 1); // not yet due
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.True(h.Engine.HasUnresolvedPopup);

            h.Pump(BrowserPopupEngine.VerifyDelayMs);
            var dismissed = Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal("Yes", dismissed.TargetInvoked);
            Assert.Equal(1, dismissed.Attempts);
            Assert.True(h.Engine.TryGetCount("r", out int count));
            Assert.Equal(1, count);
            Assert.Equal(1, h.Engine.TotalDismissals);
            Assert.False(h.Engine.HasUnresolvedPopup);
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.NativeDialog));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActionThatSucceedsButLeavesThePopupOpen_NeverRecordsDismissed_SpendsAttempts_AndEndsInDismissFailed(bool useClose)
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 1; // would trip at once if unconfirmed actions counted
            if (useClose)
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            else
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            w.SucceedWithoutClosing = true;
            yes.SucceedWithoutClosing = true;

            h.AppearWindow(w);
            for (long t = 100; t <= 20000; t += 100)
                h.Pump(t);

            // Exactly MaxAttempts side effects, however long the popup stays.
            Assert.Equal(h.Engine.MaxAttempts, useClose ? w.Closes : yes.Invokes);
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.Error));
            Assert.False(h.Engine.IsRuleTripped("r"));
            Assert.True(h.Engine.TryGetCount("r", out int count));
            Assert.Equal(0, count);
            Assert.Equal(0, h.Engine.TotalDismissals);
            var failed = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(h.Engine.MaxAttempts, failed.Attempts);
            Assert.Contains("the action succeeded but the popup is still open", failed.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.True(h.Engine.HasUnresolvedPopup); // given up on, but still open
        }

        [Fact]
        public void Pause_DuringTheVerifyWindow_PreventsTheRetryInvoke_AndResumeRetries()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            yes.SucceedWithoutClosing = true;

            h.AppearWindow(w);
            Assert.Equal(1, yes.Invokes);
            h.Engine.Paused = true;
            h.Engine.WaitForIdle();

            for (long t = 100; t <= 5000; t += 100)
                h.Pump(t);
            Assert.Equal(1, yes.Invokes); // verification found it still open, but the retry is held back
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.True(h.Engine.HasUnresolvedPopup);

            h.Engine.Paused = false;
            h.Pump(5000 + BrowserPopupEngine.PausedRecheckMs); // the parked candidate's next look
            Assert.Equal(2, yes.Invokes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActionIssuedJustBeforePause_IsStillConfirmedWhilePaused_SoDismissedIsRecordedAfterPauseReturned(bool useClose)
        {
            var h = new Harness();
            if (useClose)
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            else
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            h.Probe.AddChild(w, "Yes");

            h.AppearWindow(w); // the call is issued, the browser has (in effect) acted, the outcome is not yet confirmed
            Assert.Equal(1, h.Probe.TotalInvokes + h.Probe.TotalCloses);
            h.Engine.Paused = true;
            h.Engine.WaitForIdle(); // Pause has returned
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));

            h.PastVerifyDelay(); // the confirmation is read-only, so it still runs while paused

            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed)); // documented: raised after Pause returned
            Assert.Equal(1, h.Engine.TotalDismissals);
            Assert.Equal(1, h.Probe.TotalInvokes + h.Probe.TotalCloses); // and no new call started
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PopupStillOpenAfterAnIssuedAction_WhilePaused_StartsNoNewCall_UntilResume_ThenIsDismissed(bool useClose)
        {
            var h = new Harness();
            if (useClose)
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            else
                h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            w.SucceedWithoutClosing = true;
            yes.SucceedWithoutClosing = true;

            h.AppearWindow(w);
            int issued = h.Probe.TotalInvokes + h.Probe.TotalCloses;
            Assert.Equal(1, issued);
            h.Engine.Paused = true;
            h.Engine.WaitForIdle();

            // Verification (due while paused) finds the popup still open; the retry it schedules must not start.
            for (long t = 100; t <= 5000; t += 100)
            {
                h.Pump(t);
                Assert.Equal(issued, h.Probe.TotalInvokes + h.Probe.TotalCloses);
            }
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.True(h.Engine.HasUnresolvedPopup);

            // Resume: the held retry proceeds normally, and this time the browser acts.
            w.SucceedWithoutClosing = false;
            yes.SucceedWithoutClosing = false;
            h.Engine.Paused = false;
            h.Settle(5100, 8000);

            Assert.Equal(2, h.Probe.TotalInvokes + h.Probe.TotalCloses);
            var dismissed = Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(2, dismissed.Attempts);
            Assert.False(h.Engine.HasUnresolvedPopup);
        }

        // ------------------------------------------------------------------ disabling/removing a rule releases what it selected

        /// <summary>Rule "A" (close; the window ignores it, so it fails after one attempt) and rule "B" (press Yes) both match "Alert"; A is first, so A owns the candidate.</summary>
        private static (Harness H, FakeElement W, FakeElement Yes) FailedByAFixture()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("A", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            h.AddRule("B", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            w.IgnoreClose = true;
            var yes = h.Probe.AddChild(w, "Yes");
            h.AppearWindow(w);
            h.Settle(100, 1000);
            Assert.Equal("A", Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed)).RuleName);
            Assert.Equal(0, yes.Invokes); // A owns the candidate, B never got a turn
            return (h, w, yes);
        }

        [Fact]
        public void DisablingTheRuleThatFailed_LetsAnotherMatchingRuleDismissTheSamePopup()
        {
            var (h, w, yes) = FailedByAFixture();

            Assert.True(h.Engine.SetRuleEnabled("A", false));
            h.Settle(h.Now + 100, h.Now + 3000);

            Assert.Equal(1, yes.Invokes);
            var dismissed = Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal("B", dismissed.RuleName);
            Assert.False(w.Alive);
        }

        [Fact]
        public void RemovingTheRuleThatFailed_LetsAnotherMatchingRuleDismissTheSamePopup()
        {
            var (h, w, yes) = FailedByAFixture();

            Assert.True(h.Engine.RemoveRule("A"));
            h.Settle(h.Now + 100, h.Now + 3000);

            Assert.Equal(1, yes.Invokes);
            Assert.Equal("B", Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed)).RuleName);
            Assert.False(w.Alive);
        }

        [Fact]
        public void DisablingAWatchOnlyRuleThatReportedThePopup_LetsAnotherMatchingRuleDismissIt()
        {
            var h = new Harness();
            h.AddRule("A", BrowserPopupScope.NativeDialog, nameContains: "Alert"); // watch-only, first
            h.AddRule("B", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w);
            h.Settle(100, 1000);
            Assert.Equal("A", Assert.Single(h.Of(BrowserPopupRecordKind.Detected)).RuleName);
            Assert.True(w.Alive);

            Assert.True(h.Engine.SetRuleEnabled("A", false));
            h.Settle(h.Now + 100, h.Now + 3000);

            Assert.Equal("B", Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed)).RuleName);
            Assert.False(w.Alive);
        }

        [Fact]
        public void DisablingTheRule_WhileItsVerificationIsPending_KeepsThePendingState_AndStillRecordsTheConfirmation()
        {
            var h = new Harness();
            h.AddRule("A", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            h.AddRule("B", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");

            h.AppearWindow(w);
            Assert.Equal(1, w.Closes); // A acted; its confirmation is pending
            Assert.True(h.Engine.SetRuleEnabled("A", false));
            h.Pump(h.Now); // a pass before the verification deadline: nothing may be released or re-decided
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(0, yes.Invokes);

            h.PastVerifyDelay();

            var dismissed = Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal("A", dismissed.RuleName);
            Assert.Equal(0, yes.Invokes);
            Assert.Equal(1, h.Engine.TotalDismissals);
        }

        [Fact]
        public void DisablingTheRule_WhilePending_AndThePopupIsStillOpenAtTheDeadline_HandsItToTheEnabledRule()
        {
            var h = new Harness();
            h.AddRule("A", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            h.AddRule("B", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            w.SucceedWithoutClosing = true; // A's close returns success but the popup stays
            var yes = h.Probe.AddChild(w, "Yes");

            h.AppearWindow(w);
            Assert.True(h.Engine.SetRuleEnabled("A", false));
            h.PastVerifyDelay(); // still open: released from the disabled A rather than parked under it
            h.Settle(h.Now + 100, h.Now + 3000);

            Assert.Equal(1, yes.Invokes);
            Assert.Equal("B", Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed)).RuleName);
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
        }

        [Fact]
        public void ReEnablingTheDisabledRule_AfterAnotherRuleHandledThePopup_DoesNotHandleItTwice()
        {
            var (h, w, yes) = FailedByAFixture();
            h.Engine.SetRuleEnabled("A", false);
            h.Settle(h.Now + 100, h.Now + 3000);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            int closes = w.Closes;

            Assert.True(h.Engine.SetRuleEnabled("A", true));
            h.Settle(h.Now + 100, h.Now + 3000);

            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(1, yes.Invokes);
            Assert.Equal(closes, w.Closes);
            Assert.Equal(1, h.Engine.TotalDismissals);
        }

        [Fact]
        public void UnknownLivenessAtTheVerificationDeadline_IsAFailedAttemptNotADismissal_ThenALaterConfirmedCloseIsCountedOnce()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");
            w.SucceedWithoutClosing = true; // the call returns success but the popup is in fact still open ...
            w.LivenessUnknown = true;       // ... and the probe cannot say either way (transient provider failure): IsAlive is true

            h.AppearWindow(w);
            h.PastVerifyDelay(); // the deadline: unknown counts as still open, i.e. a failed attempt
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(0, h.Engine.TotalDismissals);
            Assert.True(h.Engine.HasUnresolvedPopup);

            w.SucceedWithoutClosing = false; // the retry (bounded by MaxAttempts) now actually closes it
            w.LivenessUnknown = false;
            h.Settle(1000, 6000);

            var dismissed = Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(2, dismissed.Attempts);
            Assert.Equal(1, h.Engine.TotalDismissals);
            Assert.False(h.Engine.HasUnresolvedPopup);
        }

        [Fact]
        public void UnknownLivenessAtTheDeadline_NeverCountsADismissalItCannotConfirm()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");
            w.LivenessUnknown = true;

            h.AppearWindow(w);
            Assert.False(w.Alive); // it did close, but the probe keeps answering "unknown" (alive)
            h.Settle(100, 6000);

            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed)); // never claimed without a definitive answer
            Assert.Equal(0, h.Engine.TotalDismissals);
        }

        [Fact]
        public void AttemptsSpentBeforePause_DismissFailedIsStillRecordedWhilePaused_WithNoNewCall()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            var yes = h.Probe.AddChild(w, "Yes");
            yes.SucceedWithoutClosing = true;

            h.AppearWindow(w);
            h.Engine.Paused = true;
            h.Engine.WaitForIdle();
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));

            h.PastVerifyDelay();

            Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed)); // documented: may be raised after Pause returned
            Assert.Equal(1, yes.Invokes);
        }

        [Fact]
        public void RemovingTheRule_DuringTheVerifyWindow_RecordsAndCountsNothing()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");

            h.AppearWindow(w);
            Assert.False(w.Alive);
            Assert.True(h.Engine.RemoveRule("r"));
            h.Settle(100, 2000);

            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(0, h.Engine.TotalDismissals);
            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void PopupThatKeepsReturningAfterConfirmedDismissals_StillTripsTheRunawayBreaker()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 2;
            h.AddRule("nag", BrowserPopupScope.NativeDialog, nameContains: "Nag", action: BrowserPopupAction.InvokeByName, targetName: "Yes");

            FakeElement third = null;
            for (int i = 0; i < 3; i++)
            {
                var w = h.Probe.AddWindow("Nag");
                h.Probe.AddChild(w, "Yes");
                h.Now += 1000;
                h.AppearWindow(w);
                if (i < 2)
                {
                    Assert.False(w.Alive);
                    h.PastVerifyDelay(); // confirmed: counted toward the breaker
                }
                else
                {
                    third = w;
                }
            }

            Assert.True(third.Alive); // the third one was refused
            Assert.True(h.Engine.IsRuleTripped("nag"));
            Assert.Equal(2, h.Of(BrowserPopupRecordKind.Dismissed).Count());
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
        }

        // ------------------------------------------------------------------ pinning (Retain/Release)

        private static int TrackedTotal(Harness h) =>
            h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.NativeDialog)
            + h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay);

        /// <summary>Every tracked candidate is pinned exactly once and nothing else is: no leaks, no unbalanced releases.</summary>
        private static void AssertPinsMatchTrackedCandidates(Harness h)
        {
            Assert.Equal(TrackedTotal(h), h.Probe.Retained.Count);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void Pins_AreBalanced_AcrossASuccessfulDismissal()
        {
            var h = new Harness();
            h.AddRule("banner", BrowserPopupScope.PageOverlay, role: "dialog", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Banner", role: "dialog");
            h.Probe.AddChild(overlay, "OK");

            h.AppearOverlay(window);
            Assert.Contains(overlay.Ref, h.Probe.Retained); // pinned while the dismissal is pending
            AssertPinsMatchTrackedCandidates(h);

            h.PastVerifyDelay();

            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.DoesNotContain(overlay.Ref, h.Probe.Retained);
            AssertPinsMatchTrackedCandidates(h);
        }

        [Fact]
        public void Pins_AreBalanced_AcrossAFailedDismissal()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.Engine.MaxAttempts = 1;
            h.AddRule("banner", BrowserPopupScope.PageOverlay, role: "dialog", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Banner", role: "dialog");
            h.Probe.AddChild(overlay, "OK").IgnoreInvoke = true;

            h.AppearOverlay(window);
            h.Settle();

            Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Contains(overlay.Ref, h.Probe.Retained); // parked, still tracked, still pinned
            AssertPinsMatchTrackedCandidates(h);

            h.Probe.Destroy(window);
            h.Pump(h.Now + 2000);

            Assert.Empty(h.Probe.Retained);
            AssertPinsMatchTrackedCandidates(h);
        }

        [Fact]
        public void Pins_AreBalanced_WhenARuleIsRemovedOrRulesAreCleared()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("a", BrowserPopupScope.PageOverlay, nameContains: "Alpha");
            h.AddRule("b", BrowserPopupScope.PageOverlay, nameContains: "Beta");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var alpha = h.Probe.AddOverlay(window, "Alpha", role: "dialog");
            var beta = h.Probe.AddOverlay(window, "Beta", role: "dialog");
            h.AppearOverlay(window);
            AssertPinsMatchTrackedCandidates(h);

            Assert.True(h.Engine.RemoveRule("a"));
            h.Pump(h.Now + 100);
            AssertPinsMatchTrackedCandidates(h);

            h.Engine.ClearRules();
            h.Pump(h.Now + 100);
            AssertPinsMatchTrackedCandidates(h);

            // The unmatched overlays are released when their window goes away.
            h.Probe.Destroy(window);
            h.Pump(h.Now + 2000);
            Assert.Empty(h.Probe.Retained);
            AssertPinsMatchTrackedCandidates(h);
            Assert.DoesNotContain(alpha.Ref, h.Probe.Retained);
            Assert.DoesNotContain(beta.Ref, h.Probe.Retained);
        }

        [Fact]
        public void Pins_AreAllReleased_ByResetRuntime()
        {
            var h = new Harness();
            h.AddRule("banner", BrowserPopupScope.PageOverlay, role: "dialog");
            h.AddRule("native", BrowserPopupScope.NativeDialog, nameContains: "Alert");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            h.Probe.AddOverlay(window, "Banner", role: "dialog");
            h.AppearOverlay(window);
            h.AppearWindow(h.Probe.AddWindow("Alert", processName: "chrome"));
            Assert.True(h.Probe.Retained.Count >= 3);

            h.Engine.ResetRuntime();

            Assert.Empty(h.Probe.Retained);
            Assert.Equal(h.Probe.RetainCalls, h.Probe.ReleaseCalls);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void CapRefusedAdmission_IsNeverRetained()
        {
            var h = new Harness();
            h.AddRule("any", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            for (int i = 0; i < BrowserPopupEngine.MaxTrackedCandidates + 10; i++)
                h.Probe.AddOverlay(window, "d" + i, role: "dialog");
            h.Engine.MaxOverlayNodes = 100000;

            h.AppearOverlay(window);

            Assert.Equal(BrowserPopupEngine.MaxTrackedCandidates, TrackedTotal(h));
            Assert.Equal(BrowserPopupEngine.MaxTrackedCandidates, h.Probe.RetainCalls);
            AssertPinsMatchTrackedCandidates(h);
        }

        [Fact]
        public void Pins_AreReleased_WhenTheOwnerWindowDies()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("banner", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Banner", role: "dialog");
            h.AppearOverlay(window);
            AssertPinsMatchTrackedCandidates(h);
            Assert.Contains(overlay.Ref, h.Probe.Retained);

            h.Probe.Destroy(window);
            h.Pump(h.Now + 2000);

            Assert.Empty(h.Probe.Retained);
            Assert.Equal(0, TrackedTotal(h));
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        // ------------------------------------------------------------------ reaping parked overlays

        /// <summary>A watched window with one watch-only overlay that has been detected and parked.</summary>
        private static FakeElement ParkedWatchOnlyOverlay(Harness h, out FakeElement window)
        {
            h.AddRule("w", BrowserPopupScope.PageOverlay, role: "dialog");
            window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Banner", role: "dialog");
            h.AppearOverlay(window);
            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal(1, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            return overlay;
        }

        [Fact]
        public void Reap_DropsAParkedWatchOnlyOverlayThatDisappears_WithoutADismissedRecord()
        {
            var h = new Harness();
            var overlay = ParkedWatchOnlyOverlay(h, out _);

            h.Probe.Destroy(overlay);
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.DoesNotContain(overlay.Ref, h.Probe.Retained);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void Reap_DropsAParkedFailedDismissalThatDisappears_AndUnresolvedDrops()
        {
            var h = new Harness();
            h.Engine.MaxAttempts = 1;
            h.AddRule("banner", BrowserPopupScope.PageOverlay, role: "dialog", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var overlay = h.Probe.AddOverlay(window, "Banner", role: "dialog");
            h.Probe.AddChild(overlay, "OK").IgnoreInvoke = true;
            h.AppearOverlay(window);
            h.Settle();
            Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(1, h.Engine.UnresolvedCount);

            h.Probe.Destroy(overlay);
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(0, h.Engine.UnresolvedCount);
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        [Fact]
        public void Reap_KeepsAParkedOverlayThatIsStillAlive()
        {
            var h = new Harness();
            var overlay = ParkedWatchOnlyOverlay(h, out _);

            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(1, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            Assert.Contains(overlay.Ref, h.Probe.Retained);
        }

        [Fact]
        public void Reap_DoesNotDropAnOverlayWhoseLivenessIsUnknown()
        {
            var h = new Harness();
            var overlay = ParkedWatchOnlyOverlay(h, out _);
            overlay.LivenessUnknown = true; // the probe cannot say (unpinned, evicted): IsAlive reports true
            h.Probe.Destroy(overlay);

            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(1, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
        }

        [Fact]
        public void Reap_IsRateLimited_AndTheNextDueTimeAccountsForIt()
        {
            var h = new Harness();
            var overlay = ParkedWatchOnlyOverlay(h, out _);

            h.Probe.Destroy(overlay);
            long next = h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs - 1); // too soon
            Assert.Equal(1, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            Assert.NotEqual(long.MaxValue, next); // a parked overlay keeps the worker waking to reap

            h.Pump(next);
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
        }

        [Fact]
        public void Reap_KeepsTheCandidateTableFromFillingUpWithTransientOverlays()
        {
            var h = new Harness();
            h.AddRule("w", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);

            const int count = 3000; // more than MaxTrackedCandidates
            for (int i = 0; i < count; i++)
            {
                var banner = h.Probe.AddOverlay(window, "Banner " + i, role: "dialog");
                h.AppearOverlay(window);
                h.Probe.Destroy(banner); // the page dismisses its own banner
                h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            }

            Assert.Equal(count, h.Of(BrowserPopupRecordKind.Detected).Count());
            Assert.Empty(h.Of(BrowserPopupRecordKind.Error)); // never blocked by the cap
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            Assert.Single(h.Probe.Retained); // just the browser window
        }

        [Fact]
        public void Reap_CarriesAcrossPasses_WhenMoreCandidatesAreParkedThanOnePassChecks()
        {
            var h = new Harness();
            h.AddRule("w", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            var banners = new List<FakeElement>();
            int total = BrowserPopupEngine.ReapMaxChecksPerPass + 44;
            for (int i = 0; i < total; i++)
                banners.Add(h.Probe.AddOverlay(window, "Banner " + i, role: "dialog"));
            h.AppearOverlay(window);
            Assert.Equal(total, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            foreach (var banner in banners)
                h.Probe.Destroy(banner);

            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            // The (live, parked) owner window shares the pass budget, so it may use one of the checks.
            Assert.InRange(h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay), 44, 45);

            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
        }

        // ------------------------------------------------------------------ reaping parked native windows

        [Fact]
        public void Reap_DropsAParkedWatchOnlyNativeWindowThatCloses_WithSweepsOff()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("note", BrowserPopupScope.NativeDialog, nameContains: "Notice");
            var dialog = h.Probe.AddWindow("Notice");
            h.AppearWindow(dialog);
            h.Settle();
            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal(1, TrackedTotal(h));
            Assert.Contains(dialog.Ref, h.Probe.Retained);

            h.Probe.Destroy(dialog); // the page closes it; no native sweep will ever look
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(0, TrackedTotal(h));
            Assert.Empty(h.Probe.Retained);
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(0, h.Probe.UnbalancedReleases);
            Assert.Equal(0, h.Engine.UnresolvedCount);
        }

        [Fact]
        public void Reap_DropsAnOverlayOnlyWatchOwnerWindowThatCloses_AndUnwatchesIt()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("banner", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab"); // tracked only so it can be watched
            h.AppearWindow(window);
            Assert.Contains(window.Ref, h.Hook.CurrentlyWatched);
            Assert.Contains(window.Ref, h.Probe.Retained);

            h.Probe.Destroy(window);
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(0, TrackedTotal(h));
            Assert.Contains(window.Ref, h.Hook.UnwatchCalls);
            Assert.DoesNotContain(window.Ref, h.Hook.CurrentlyWatched);
            Assert.Empty(h.Probe.Retained);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void Reap_OfAnOwnerWindow_AlsoDropsItsOverlays()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("banner", BrowserPopupScope.PageOverlay, role: "dialog");
            var window = h.Probe.AddWindow("tab");
            h.AppearWindow(window);
            h.Probe.AddOverlay(window, "Banner", role: "dialog");
            h.AppearOverlay(window);
            Assert.Equal(2, TrackedTotal(h));

            h.Probe.Destroy(window);
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(0, TrackedTotal(h));
            Assert.Empty(h.Probe.Retained);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void Reap_KeepsALiveParkedNativeWindow_AndOneWhoseLivenessIsUnknown()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("note", BrowserPopupScope.NativeDialog, nameContains: "Notice");
            var live = h.Probe.AddWindow("Notice");
            var unknown = h.Probe.AddWindow("Notice two");
            h.AppearWindow(live);
            h.AppearWindow(unknown);
            h.Settle();
            unknown.LivenessUnknown = true;
            h.Probe.Destroy(unknown);

            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);

            Assert.Equal(2, TrackedTotal(h));
            Assert.Contains(live.Ref, h.Probe.Retained);
        }

        [Fact]
        public void Reap_KeepsTheCandidateTableFromFillingUpWithTransientNativeWindows()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("note", BrowserPopupScope.NativeDialog, nameContains: "Notice");

            const int count = 3000; // more than MaxTrackedCandidates
            for (int i = 0; i < count; i++)
            {
                var dialog = h.Probe.AddWindow("Notice " + i);
                h.AppearWindow(dialog);
                h.Probe.Destroy(dialog); // the page closes its own dialog
                h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            }
            var later = h.Probe.AddWindow("Notice later");
            h.AppearWindow(later);

            Assert.Equal(count + 1, h.Of(BrowserPopupRecordKind.Detected).Count());
            Assert.Empty(h.Of(BrowserPopupRecordKind.Error)); // never blocked by the cap
            Assert.Equal(1, TrackedTotal(h));
            Assert.Single(h.Probe.Retained);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void Reap_CoversBothKindsWithinOnePassBudget_AndCarriesTheRest()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("note", BrowserPopupScope.NativeDialog, nameContains: "Notice");
            var windows = new List<FakeElement>();
            int total = BrowserPopupEngine.ReapMaxChecksPerPass + 44;
            for (int i = 0; i < total; i++)
            {
                var w = h.Probe.AddWindow("Notice " + i);
                windows.Add(w);
                h.Hook.FireWindowOpened(w);
            }
            h.Pump(h.Now);
            h.Settle();
            Assert.Equal(total, TrackedTotal(h));
            foreach (var w in windows)
                h.Probe.Destroy(w);

            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            Assert.Equal(44, TrackedTotal(h));

            h.Pump(h.Now + BrowserPopupEngine.ReapIntervalMs);
            Assert.Equal(0, TrackedTotal(h));
            Assert.Empty(h.Probe.Retained);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void Reap_OfParkedNativeWindows_WakesAWorkerWithSweepsOff()
        {
            var h = new Harness(sweepIntervalMs: 0);
            h.AddRule("note", BrowserPopupScope.NativeDialog, nameContains: "Notice");
            var dialog = h.Probe.AddWindow("Notice");
            h.AppearWindow(dialog);
            h.Settle();

            long next = h.Pump(h.Now + 1);

            Assert.NotEqual(long.MaxValue, next);
        }

        // ------------------------------------------------------------------ main-window refusal

        [Fact]
        public void CloseRule_RefusesAMainWindowLikeWindow_OnceAndNeverCallsTryClose()
        {
            var h = new Harness();
            h.AddRule("closeIt", BrowserPopupScope.NativeDialog, nameContains: "Example", action: BrowserPopupAction.CloseWindowPattern);
            var mainWindow = h.Probe.AddWindow("Example Domain - Google Chrome");
            mainWindow.IsMainWindowLike = true;

            h.AppearWindow(mainWindow);
            h.Settle();

            Assert.True(mainWindow.Alive);
            Assert.Equal(0, mainWindow.Closes);
            Assert.Equal(0, h.Probe.TotalCloses);
            var failed = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(BrowserPopupEngine.MainWindowRefusal, failed.Detail);
            Assert.Equal(0, failed.Attempts); // no retries were burned
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        [Fact]
        public void CloseRule_ClosesADialogLikeWindow()
        {
            var h = new Harness();
            h.AddRule("closeIt", BrowserPopupScope.NativeDialog, nameContains: "Example", action: BrowserPopupAction.CloseWindowPattern);
            var dialog = h.Probe.AddWindow("Example Domain says");

            h.AppearWindow(dialog);
            h.PastVerifyDelay();

            Assert.False(dialog.Alive);
            Assert.Equal(1, dialog.Closes);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
        }

        [Fact]
        public void DismissByNameRule_RefusesAMainWindowLikeWindow_WithoutAnyProbeWalkOrInvoke()
        {
            // A tab titled "Example Domain" makes the browser's main window match; the invoke rule
            // must not walk the page and press its first control named "OK".
            var h = new Harness();
            h.AddRule("press", BrowserPopupScope.NativeDialog, nameContains: "Example", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            var window = h.Probe.AddWindow("Example Domain");
            window.IsMainWindowLike = true;
            var ok = h.Probe.AddChild(window, "OK");

            h.AppearWindow(window);
            h.Settle();

            Assert.Equal(0, ok.Invokes);
            Assert.Equal(0, h.Probe.TotalInvokes);
            Assert.Equal(0, h.Probe.OverlaySearchCalls); // FindTargets never walked the subtree
            Assert.True(window.Alive);
            var failed = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(BrowserPopupEngine.MainWindowRefusal, failed.Detail);
            Assert.Equal(0, failed.Attempts);
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        [Fact]
        public void DismissByAutomationIdRule_RefusesAMainWindowLikeWindow_WithoutAnyProbeWalkOrInvoke()
        {
            var h = new Harness();
            h.AddRule("press", BrowserPopupScope.NativeDialog, nameContains: "Example", action: BrowserPopupAction.InvokeByAutomationId, targetAutomationId: "okButton");
            var window = h.Probe.AddWindow("Example Domain");
            window.IsMainWindowLike = true;
            var ok = h.Probe.AddChild(window, "OK", automationId: "okButton");

            h.AppearWindow(window);
            h.Settle();

            Assert.Equal(0, ok.Invokes);
            Assert.Equal(0, h.Probe.TotalInvokes);
            Assert.Equal(0, h.Probe.OverlaySearchCalls);
            var failed = Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(BrowserPopupEngine.MainWindowRefusal, failed.Detail);
            Assert.Equal(0, failed.Attempts);
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        [Fact]
        public void DismissRules_StillWorkOnADialogLikeWindow()
        {
            var h = new Harness();
            h.AddRule("byName", BrowserPopupScope.NativeDialog, nameContains: "Example", action: BrowserPopupAction.InvokeByName, targetName: "OK");
            h.AddRule("byId", BrowserPopupScope.NativeDialog, nameContains: "Other", action: BrowserPopupAction.InvokeByAutomationId, targetAutomationId: "okButton");
            var first = h.Probe.AddWindow("Example Domain says");
            var firstOk = h.Probe.AddChild(first, "OK");
            var second = h.Probe.AddWindow("Other page says");
            var secondOk = h.Probe.AddChild(second, "OK", automationId: "okButton");

            h.AppearWindow(first);
            h.AppearWindow(second);
            h.PastVerifyDelay();

            Assert.Equal(1, firstOk.Invokes);
            Assert.Equal(1, secondOk.Invokes);
            Assert.Equal(2, h.Of(BrowserPopupRecordKind.Dismissed).Count());
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
        }

        [Fact]
        public void WatchOnlyRule_StillReportsAMainWindowLikeWindow()
        {
            var h = new Harness();
            h.AddRule("watch", BrowserPopupScope.NativeDialog, nameContains: "Example", action: BrowserPopupAction.WatchOnly);
            var window = h.Probe.AddWindow("Example Domain");
            window.IsMainWindowLike = true;

            h.AppearWindow(window);
            h.Settle();

            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Equal(0, h.Probe.TotalInvokes);
            Assert.Equal(0, h.Probe.TotalCloses);
        }

        [Theory]
        [InlineData(0x14CF0000u, true)]  // WS_OVERLAPPEDWINDOW | WS_VISIBLE | WS_CLIPSIBLINGS (typical main window)
        [InlineData(0x00020000u, true)]  // WS_MINIMIZEBOX alone
        [InlineData(0x00010000u, true)]  // WS_MAXIMIZEBOX alone
        [InlineData(0x14C80000u, false)] // caption + sysmenu, no min/max boxes (a dialog)
        [InlineData(0x80880000u, false)] // WS_POPUP | WS_CAPTION | WS_SYSMENU: a message box
        [InlineData(0u, true)]           // unreadable style: refuse
        public void IsMainWindowStyle_ChecksTheMinimizeAndMaximizeBoxes(uint style, bool expected)
        {
            Assert.Equal(expected, NativeMethods.IsMainWindowStyle(style));
        }

        // ------------------------------------------------------------------ post-merge review fixes

        [Fact]
        public void OverlayOrphanedByItsOwnerDyingMidPass_IsNotEvaluatedAfterBeingDropped()
        {
            var h = new Harness();
            // The native rule keeps the owner window due (it has something to evaluate); the overlay
            // rule's target does not exist yet, so the overlay is parked on its retry schedule.
            h.AddRule("owner", BrowserPopupScope.NativeDialog, nameContains: "zzz", process: "chrome");
            h.AddRule("cookie", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome",
                action: BrowserPopupAction.InvokeByName, targetName: "Accept");
            var window = h.Probe.AddWindow("tab", pid: 5, processName: "chrome");
            var sibling = h.Probe.AddWindow("other tab", pid: 5, processName: "chrome"); // keeps the process name cached once window is gone
            h.AppearWindow(window);
            h.AppearWindow(sibling);
            var overlay = h.Probe.AddOverlay(window, "Cookie banner");
            h.AppearOverlay(window);
            Assert.Equal(0, h.Probe.TotalInvokes);
            Assert.Equal(1, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));

            // Both come due at 150 ms; the owner is evaluated first and finds itself dead, which
            // drops the overlay from the tracked set while it still sits in the pass's due snapshot.
            var accept = h.Probe.AddChild(overlay, "Accept");
            h.Probe.Destroy(window);
            h.Pump(BrowserPopupEngine.ScheduleMs[1]);
            h.Settle(200, 3000);

            Assert.Equal(0, accept.Invokes);
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));
            Assert.Equal(0, h.Probe.UnbalancedReleases);
            Assert.DoesNotContain(overlay.Ref, h.Probe.Retained);
        }

        [Fact]
        public void ReportedWatchOnlyNativePopup_HasItsMessageTextReadOnce_AcrossManySweeps()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("w", BrowserPopupScope.NativeDialog, nameContains: "Alert", message: "sure", process: "chrome");
            var w = h.Probe.AddWindow("Alert", message: "Are you sure?");

            h.AppearWindow(w);
            for (long t = 100; t <= 8000; t += 100)
                h.Pump(t);

            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal(1, h.Probe.MessageTextCalls);
        }

        [Fact]
        public void ReportedWatchOnlyOverlay_HasItsMessageTextReadOnce_AcrossManySweeps()
        {
            var h = new Harness(overlaySweepIntervalMs: 500);
            h.AddRule("w", BrowserPopupScope.PageOverlay, nameContains: "Cookie", message: "accept", process: "chrome");
            var window = h.Probe.AddWindow("tab", pid: 5, processName: "chrome");
            h.AppearWindow(window);
            h.Probe.AddOverlay(window, "Cookie banner", "Please accept");
            h.AppearOverlay(window);
            for (long t = 100; t <= 8000; t += 100)
                h.Pump(t);

            Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal(1, h.Probe.MessageTextCalls);
        }

        [Fact]
        public void PopupHeldByATrippedRule_HasItsMessageTextReadOnce_AcrossManySweeps()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.Engine.MaxDismissalsPerMinute = 1;
            h.AddRule("nag", BrowserPopupScope.NativeDialog, nameContains: "Nag", message: "again", action: BrowserPopupAction.CloseWindowPattern);
            h.Now = 1000;
            h.AppearWindow(h.Probe.AddWindow("Nag", message: "again?"));
            h.PastVerifyDelay(); // confirmed: the one dismissal the limit allows
            var second = h.Probe.AddWindow("Nag", message: "again?");
            h.Now += 1000;
            h.AppearWindow(second);
            Assert.True(h.Engine.IsRuleTripped("nag"));
            int readsAtTrip = h.Probe.MessageTextCalls;

            long from = h.Now;
            for (long t = from + 100; t <= from + 8000; t += 100)
                h.Pump(t);

            Assert.Equal(readsAtTrip, h.Probe.MessageTextCalls);
            Assert.True(second.Alive);
        }

        [Fact]
        public void ReportedWatchOnlyPopup_IsStillRedecidedWhenItsRuleIsDisabled()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("w", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "chrome");
            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w);
            h.Settle(100, 2000);
            Assert.True(w.Alive);

            h.AddRule("close", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            Assert.True(w.Alive); // the watch-only rule is earlier, so it still wins

            h.Engine.SetRuleEnabled("w", false);
            h.Settle(2100, 3000);

            Assert.False(w.Alive);
        }

        [Fact]
        public void PopupWhoseScheduleRanOut_GetsTheFullRetryCascade_WhenARuleIsAddedLater()
        {
            var h = new Harness();
            h.AddRule("other", BrowserPopupScope.NativeDialog, nameContains: "zzz", process: "chrome"); // keeps the window evaluated
            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w);
            h.Settle(100, 2600); // unmatched for over 2 s: the retry schedule is spent
            Assert.True(w.Alive);

            h.AddRule("close", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            h.Pump(2700);
            h.Pump(2800);
            var yes = h.Probe.AddChild(w, "Yes"); // the button renders 300 ms after the rule appears
            for (long t = 2900; t <= 4000; t += 100)
                h.Pump(t);

            Assert.Equal(1, yes.Invokes);
            Assert.False(w.Alive);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Empty(h.Of(BrowserPopupRecordKind.DismissFailed));
        }

        [Fact]
        public void PopupFreedByADisabledRule_GetsTheFullRetryCascadeForTheNextRule()
        {
            var h = new Harness();
            h.AddRule("first", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Nope"); // never finds its target: spends the schedule, fails
            h.AddRule("second", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w);
            h.Settle(100, 2600);

            h.Engine.SetRuleEnabled("first", false);
            h.Pump(2700);
            h.Pump(2800);
            var yes = h.Probe.AddChild(w, "Yes");
            for (long t = 2900; t <= 4000; t += 100)
                h.Pump(t);

            Assert.Equal(1, yes.Invokes);
            Assert.Equal("first", Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed)).RuleName);
            Assert.Equal("second", Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed)).RuleName);
        }

        [Fact]
        public void RuleRemovedBeforeThePassObservesIt_ThenThePopupClosing_CountsAndRecordsNothing()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");
            h.AppearWindow(w);
            Assert.False(w.Alive);

            Assert.True(h.Engine.RemoveRule("r"));
            h.Settle(100, 2000); // ApplyRuleChanges sees the removal first and drops the pending dismissal

            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(0, h.Engine.TotalDismissals);
            Assert.False(h.Engine.TryGetCount("r", out _));
        }

        [Fact]
        public void RuleRemovedAfterThePassAppliedRuleChanges_ThenConfirmation_CountsAndRecordsNothing_TotalMatchesPerRuleSum()
        {
            var h = new Harness(overlaySweepIntervalMs: 500);
            h.AddRule("ov", BrowserPopupScope.PageOverlay, nameContains: "zzz", process: "chrome"); // watches the chrome window: its sweep runs mid-pass
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "notepad", action: BrowserPopupAction.CloseWindowPattern);
            var chrome = h.Probe.AddWindow("tab", pid: 5, processName: "chrome");
            var dialog = h.Probe.AddWindow("Alert", pid: 6, processName: "notepad");
            h.AppearWindow(chrome);
            h.AppearWindow(dialog);
            Assert.False(dialog.Alive); // closed; verification pending

            // At 600 ms the pass has already applied rule changes when the overlay sweep runs; the
            // rule is removed from inside it, so the confirmation of the pending dismissal comes
            // first and only afterwards would ApplyRuleChanges (next pass) see the removal.
            bool removed = false;
            h.Probe.OnFindOverlayCandidates = () =>
            {
                if (!removed)
                {
                    removed = true;
                    h.Engine.RemoveRule("r");
                }
            };
            h.Pump(600);

            Assert.True(removed);
            Assert.Empty(h.Of(BrowserPopupRecordKind.Dismissed));
            Assert.Equal(0, h.Engine.TotalDismissals);
            Assert.False(h.Engine.TryGetCount("r", out _));
        }

        [Fact]
        public void ConfirmedDismissal_WithTheRuleStillRegistered_CountsPerRuleAndInTotal()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            h.AppearWindow(h.Probe.AddWindow("Alert"));
            h.PastVerifyDelay();

            Assert.True(h.Engine.TryGetCount("r", out int count));
            Assert.Equal(1, count);
            Assert.Equal(1, h.Engine.TotalDismissals);
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));
        }

        [Fact]
        public void BurstOfSimultaneousPopups_IsCappedAtTheLimit_BeforeAnyConfirmationLands()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 3;
            h.AddRule("nag", BrowserPopupScope.NativeDialog, nameContains: "Nag", action: BrowserPopupAction.CloseWindowPattern);
            var windows = Enumerable.Range(0, 5).Select(_ => h.Probe.AddWindow("Nag")).ToList();

            foreach (var w in windows)
                h.Hook.FireWindowOpened(w);
            h.Pump(0);

            Assert.Equal(3, h.Probe.TotalCloses);
            Assert.True(h.Engine.IsRuleTripped("nag"));
            var error = Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            Assert.Equal("nag", error.RuleName);
            Assert.Contains("SetRuleEnabled", error.Detail);

            h.PastVerifyDelay();
            h.Settle(1000, 4000);

            Assert.Equal(3, h.Probe.TotalCloses);
            Assert.Equal(3, h.Of(BrowserPopupRecordKind.Dismissed).Count());
            Assert.Equal(3, h.Engine.TotalDismissals);
            Assert.Single(h.Of(BrowserPopupRecordKind.Error)); // one trip, one error
            Assert.Equal(2, windows.Count(w => w.Alive));
        }

        [Fact]
        public void ActionThatLeavesThePopupOpen_IsNotCountedAsADismissal_AndStopsCountingTowardTheBreaker()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 2;
            h.Engine.MaxAttempts = 1;
            h.AddRule("nag", BrowserPopupScope.NativeDialog, nameContains: "Nag", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var ignoring = h.Probe.AddWindow("Nag");
            h.Probe.AddChild(ignoring, "Yes").SucceedWithoutClosing = true;
            var normal = h.Probe.AddWindow("Nag");
            h.Probe.AddChild(normal, "Yes");

            h.Hook.FireWindowOpened(ignoring);
            h.Hook.FireWindowOpened(normal);
            h.Pump(0); // two actions issued, none confirmed yet: at the limit, but not over it
            Assert.False(h.Engine.IsRuleTripped("nag"));

            h.PastVerifyDelay(); // the ignored one is still open: DismissFailed, no longer pending; the other is confirmed
            Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed));
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));

            var third = h.Probe.AddWindow("Nag");
            h.Probe.AddChild(third, "Yes");
            h.Now += 100;
            h.AppearWindow(third); // 1 confirmed + 0 pending: room for one more
            h.PastVerifyDelay();

            Assert.False(third.Alive);
            Assert.False(h.Engine.IsRuleTripped("nag"));
            Assert.Equal(2, h.Engine.TotalDismissals);
            Assert.Empty(h.Of(BrowserPopupRecordKind.Error));
        }

        [Fact]
        public void ConfirmedDismissals_AreNotCountedTwice_WhenTheyMoveFromPendingToConfirmed()
        {
            var h = new Harness();
            h.Engine.MaxDismissalsPerMinute = 3;
            h.AddRule("nag", BrowserPopupScope.NativeDialog, nameContains: "Nag", action: BrowserPopupAction.CloseWindowPattern);

            for (int i = 0; i < 3; i++)
            {
                h.Now += 1000;
                h.AppearWindow(h.Probe.AddWindow("Nag"));
                h.PastVerifyDelay(); // each is confirmed before the next appears
            }

            // 3 confirmed, 0 pending: the fourth is over the limit (3 >= 3) exactly once, not earlier.
            Assert.Equal(3, h.Engine.TotalDismissals);
            Assert.False(h.Engine.IsRuleTripped("nag"));
            var fourth = h.Probe.AddWindow("Nag");
            h.Now += 1000;
            h.AppearWindow(fourth);
            Assert.True(fourth.Alive);
            Assert.True(h.Engine.IsRuleTripped("nag"));
        }

        // ------------------------------------------------------------------ unexpected probe/hook exceptions

        private static Exception Boom(string what) => new InvalidOperationException("boom: " + what);

        private static void AssertNothingTrackedAndPinsBalanced(Harness h)
        {
            Assert.Equal(0, TrackedTotal(h));
            Assert.Empty(h.Probe.Retained);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void ThrowingIsAlive_ForOneCandidate_DoesNotStopTheRestOfThePass_ErrorIsBounded_AndPinsStayBalanced()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var bad = h.Probe.AddWindow("Alert");
            var good = h.Probe.AddWindow("Alert");
            h.Probe.Fault = (op, r) => op == "IsAlive" && r.Equals(bad.Ref) ? Boom(op) : null;
            h.Hook.FireWindowOpened(bad);
            h.Hook.FireWindowOpened(good);

            long next = h.Pump(0);

            Assert.False(good.Alive); // handled although the candidate before it threw
            Assert.True(bad.Alive);
            var error = Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            Assert.Contains("boom", error.Detail);
            Assert.True(next > 0 && next <= BrowserPopupEngine.FaultBackoffMs, "the failing candidate is parked on a backoff, not re-run at once");

            for (long t = 100; t <= 20000; t += 100)
                h.Pump(t);
            Assert.Single(h.Of(BrowserPopupRecordKind.Error)); // one error per candidate per FaultLogIntervalMs, however often it fails
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed));

            h.Pump(BrowserPopupEngine.FaultLogIntervalMs + 1000);
            Assert.Equal(2, h.Of(BrowserPopupRecordKind.Error).Count()); // still failing: reported again, later

            h.Probe.Fault = null;
            h.Probe.Destroy(bad);
            h.Settle(40000, 44000);
            AssertNothingTrackedAndPinsBalanced(h);
        }

        [Fact]
        public void ThrowingDescribeWindow_ForOneCandidate_DoesNotStopTheRestOfThePass()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var bad = h.Probe.AddWindow("Alert"); // no button yet: stays tracked, retrying
            h.AppearWindow(bad);
            h.Probe.Fault = (op, r) => op == "DescribeWindow" && r.Equals(bad.Ref) ? Boom(op) : null;
            var good = h.Probe.AddWindow("Alert");
            h.Probe.AddChild(good, "Yes");

            h.Hook.FireWindowOpened(good);
            h.Pump(200); // bad is due (150 ms) and throws; good comes after or before it, either way is handled

            Assert.False(good.Alive);
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            h.Probe.Fault = null;
            h.Probe.Destroy(bad);
            h.Settle(1500, 4000);
            AssertNothingTrackedAndPinsBalanced(h);
        }

        [Fact]
        public void AWindowThatFailsToDescribe_LeavesNoProcessNameCacheEntry()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert");
            var gone = h.Probe.AddWindow("Alert", pid: 501);
            h.Probe.Destroy(gone); // DescribeWindow now returns null

            h.AppearWindow(gone);

            Assert.Equal(0, h.Engine.ProcessNameCacheCountForTests);
            Assert.Equal(0, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.NativeDialog));
        }

        [Fact]
        public void AWindowWhoseDescribeThrows_LeavesNoProcessNameCacheEntry()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert");
            var bad = h.Probe.AddWindow("Alert", pid: 502);
            h.Probe.Fault = (op, r) => op == "DescribeWindow" && r.Equals(bad.Ref) ? Boom(op) : null;

            h.AppearWindow(bad);

            Assert.Equal(0, h.Engine.ProcessNameCacheCountForTests);
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
        }

        [Fact]
        public void AWindowThatFailsToDescribe_KeepsTheNameWhileAnotherCandidateCarriesThePid()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert");
            var tracked = h.Probe.AddWindow("Alert", pid: 503);
            h.AppearWindow(tracked);
            Assert.Equal(1, h.Engine.ProcessNameCacheCountForTests);

            var gone = h.Probe.AddWindow("Alert", pid: 503);
            h.Probe.Destroy(gone);
            h.AppearWindow(gone);

            Assert.Equal(1, h.Engine.ProcessNameCacheCountForTests); // still needed by the tracked window
        }

        [Fact]
        public void ThrowingTryInvoke_SpendsAnAttempt_ReleasesTheActionLock_AndCannotFloodTheLog()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.InvokeByName, targetName: "Yes");
            var bad = h.Probe.AddWindow("Alert");
            var badYes = h.Probe.AddChild(bad, "Yes");
            var good = h.Probe.AddWindow("Alert");
            h.Probe.AddChild(good, "Yes");
            h.Probe.Fault = (op, r) => op == "TryInvoke" && r.Equals(badYes.Ref) ? Boom(op) : null;
            h.Hook.FireWindowOpened(bad);
            h.Hook.FireWindowOpened(good);

            h.Pump(0);
            Assert.False(good.Alive);
            Assert.True(bad.Alive);
            Assert.True(System.Threading.Tasks.Task.Run(() => h.Engine.WaitForIdle()).Wait(3000), "the action lock must not stay held after a throw");

            for (long t = 100; t <= 8000; t += 100)
                h.Pump(t);

            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            Assert.Single(h.Of(BrowserPopupRecordKind.Dismissed)); // only the good one
            Assert.Equal("The popup was still open after 3 attempts.", Assert.Single(h.Of(BrowserPopupRecordKind.DismissFailed)).Detail); // bounded by MaxAttempts
        }

        [Fact]
        public void ThrowingOverlaySearch_OfOneWindow_DoesNotStopTheOtherWindowsSweep()
        {
            var h = new Harness(overlaySweepIntervalMs: 500);
            h.AddRule("w", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var first = h.Probe.AddWindow("tab1", pid: 5, processName: "chrome");
            var second = h.Probe.AddWindow("tab2", pid: 5, processName: "chrome");
            h.AppearWindow(first);
            h.AppearWindow(second);
            h.Probe.AddOverlay(first, "Cookie banner one");
            h.Probe.AddOverlay(second, "Cookie banner two");
            h.Probe.Fault = (op, r) => op == "FindOverlayCandidates" && r.Equals(first.Ref) ? Boom(op) : null;

            h.Pump(600);

            var detected = Assert.Single(h.Of(BrowserPopupRecordKind.Detected));
            Assert.Equal("Cookie banner two", detected.Name);
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
        }

        [Fact]
        public void ThrowingEnumerateTopLevelWindows_DoesNotStopTheRestOfThePass()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var w = h.Probe.AddWindow("Alert");
            h.Probe.Fault = (op, r) => op == "EnumerateTopLevelWindows" ? Boom(op) : null;
            h.Hook.FireWindowOpened(w);

            h.Pump(0);
            h.Pump(1000);

            Assert.False(w.Alive); // found through the hook although the scan failed
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
        }

        [Fact]
        public void FaultRecords_AreCappedPerPass_AndEachCandidateIsReportedOnce()
        {
            var h = new Harness();
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", action: BrowserPopupAction.CloseWindowPattern);
            var windows = Enumerable.Range(0, 20).Select(_ => h.Probe.AddWindow("Alert")).ToList();
            h.Probe.Fault = (op, r) => op == "IsAlive" ? Boom(op) : null;
            foreach (var w in windows)
                h.Hook.FireWindowOpened(w);

            h.Pump(0);
            Assert.Equal(BrowserPopupEngine.MaxFaultRecordsPerPass, h.Of(BrowserPopupRecordKind.Error).Count());

            for (long t = 100; t <= 20000; t += 100)
                h.Pump(t);
            Assert.Equal(windows.Count, h.Of(BrowserPopupRecordKind.Error).Count()); // each once, spread over passes
        }

        [Fact]
        public void ThrowingWatchWindow_DoesNotHalfApplyARuleChange()
        {
            var h = new Harness();
            var first = h.Probe.AddWindow("tab1", pid: 5, processName: "chrome");
            var second = h.Probe.AddWindow("tab2", pid: 5, processName: "chrome");
            var dialog = h.Probe.AddWindow("Alert", pid: 7, processName: "notepad");
            h.Hook.FireWindowOpened(first);
            h.Hook.FireWindowOpened(second);
            h.Hook.FireWindowOpened(dialog);
            h.Pump(0); // no rule wants any of them yet: remembered, undescribed

            h.Hook.Fault = (op, r) => op == "WatchWindow" && r.Equals(first.Ref) ? Boom(op) : null;
            h.AddRule("ov", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            h.AddRule("r", BrowserPopupScope.NativeDialog, nameContains: "Alert", process: "notepad", action: BrowserPopupAction.CloseWindowPattern);
            h.Pump(100); // one rules-version bump handles both rules

            Assert.False(dialog.Alive); // the rest of the rule-change handling ran
            Assert.Contains(second.Ref, h.Hook.CurrentlyWatched);
            Assert.DoesNotContain(first.Ref, h.Hook.CurrentlyWatched);
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));

            int attempts = h.Hook.WatchCalls.Count(r => r.Equals(first.Ref));
            for (long t = 200; t <= 3000; t += 100)
                h.Pump(t);
            Assert.Equal(attempts, h.Hook.WatchCalls.Count(r => r.Equals(first.Ref))); // not re-applied in a loop

            h.Hook.Fault = null;
            h.AddRule("ov2", BrowserPopupScope.PageOverlay, nameContains: "Other", process: "chrome"); // the next rule change offers it again
            h.Pump(3100);
            Assert.Contains(first.Ref, h.Hook.CurrentlyWatched);
        }

        [Fact]
        public void ThrowingUnwatchWindow_OnARuleChange_StillUnwatchesTheOthers_AndTheEngineForgetsTheWatch()
        {
            var h = new Harness();
            h.AddRule("ov", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var first = h.Probe.AddWindow("tab1", pid: 5, processName: "chrome");
            var second = h.Probe.AddWindow("tab2", pid: 5, processName: "chrome");
            h.AppearWindow(first);
            h.AppearWindow(second);
            Assert.Equal(2, h.Hook.CurrentlyWatched.Count);

            h.Hook.Fault = (op, r) => op == "UnwatchWindow" && r.Equals(first.Ref) ? Boom(op) : null;
            Assert.True(h.Engine.SetRuleEnabled("ov", false));
            h.Pump(100);

            Assert.DoesNotContain(second.Ref, h.Hook.CurrentlyWatched);
            Assert.Equal(2, h.Hook.UnwatchCalls.Count);
            Assert.Single(h.Of(BrowserPopupRecordKind.Error));

            h.Hook.Fault = null;
            Assert.True(h.Engine.SetRuleEnabled("ov", true));
            h.Pump(200);
            // The engine forgot both watches, so turning the rule back on watches both again.
            Assert.Equal(2, h.Hook.WatchCalls.Count(r => r.Equals(first.Ref)));
            Assert.Equal(2, h.Hook.WatchCalls.Count(r => r.Equals(second.Ref)));
            Assert.Contains(second.Ref, h.Hook.CurrentlyWatched);
            Assert.Equal(0, h.Probe.UnbalancedReleases);
        }

        [Fact]
        public void ThrowingUnwatchWindow_WhenAWatchedWindowDies_StillDropsItAndItsOverlays()
        {
            var h = new Harness(sweepIntervalMs: 1000);
            h.AddRule("ov", BrowserPopupScope.PageOverlay, nameContains: "Cookie", process: "chrome");
            var window = h.Probe.AddWindow("tab", pid: 5, processName: "chrome");
            h.AppearWindow(window);
            h.Probe.AddOverlay(window, "Cookie banner");
            h.AppearOverlay(window);
            Assert.Equal(1, h.Engine.TrackedCandidateCountForTests(BrowserPopupScope.PageOverlay));

            h.Hook.Fault = (op, r) => op == "UnwatchWindow" ? Boom(op) : null;
            h.Probe.Destroy(window);
            h.Settle(1000, 4000);

            Assert.Single(h.Of(BrowserPopupRecordKind.Error));
            AssertNothingTrackedAndPinsBalanced(h);
        }
    }
}
