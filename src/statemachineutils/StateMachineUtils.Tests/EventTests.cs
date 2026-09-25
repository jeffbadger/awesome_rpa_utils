using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace StateMachineAutomation.Tests
{
    public sealed class EventTests : MachineTestBase
    {
        private static List<string> Record(StateMachineUtils machine)
        {
            var log = new List<string>();
            machine.StateExited += (_, e) => log.Add($"exited:{e.PreviousState}>{e.NewState}:{e.Trigger}");
            machine.TransitionFired += (_, e) => log.Add($"fired:{e.PreviousState}>{e.NewState}:{e.Trigger}");
            machine.StateEntered += (_, e) => log.Add($"entered:{e.PreviousState}>{e.NewState}:{e.Trigger}");
            machine.MachineFinished += (_, e) => log.Add($"finished:{e.NewState}");
            machine.TransitionRejected += (_, e) => log.Add($"rejected:{e.State}:{e.Trigger}:{e.Reason}");
            return log;
        }

        [Fact]
        public void Start_RaisesStateEnteredForTheInitialStateOnly()
        {
            StateMachineUtils machine = Loaded();
            List<string> log = Record(machine);
            Assert.True(machine.Start(out _, out _));
            Assert.Equal(new[] { "entered:>Received:" }, log);
        }

        [Fact]
        public void Fire_RaisesExitedThenFiredThenEntered_InThatOrder()
        {
            StateMachineUtils machine = Started();
            List<string> log = Record(machine);
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            Assert.Equal(new[]
            {
                "exited:Received>Validated:validate",
                "fired:Received>Validated:validate",
                "entered:Received>Validated:validate"
            }, log);
        }

        [Fact]
        public void Fire_IntoAFinalState_AlsoRaisesMachineFinishedLast()
        {
            StateMachineUtils machine = Started(Defs.Minimal);
            List<string> log = Record(machine);
            Assert.True(machine.Fire("go", out _, out _, out _, out _));
            Assert.Equal("finished:B", log.Last());
            Assert.Equal(4, log.Count);
        }

        [Fact]
        public void Fire_ReportsTheDeclaredTriggerCasing_NotTheCallersInEvents()
        {
            StateMachineUtils machine = Started();
            List<string> log = Record(machine);
            Assert.True(machine.Fire("VALIDATE", out _, out _, out _, out _));
            Assert.Contains("fired:Received>Validated:validate", log);
        }

        [Theory]
        [InlineData("nonsense", "NoTransition")]
        [InlineData("post", "NoTransition")]
        public void DeclinedTriggers_RaiseOnlyTransitionRejected(string trigger, string reason)
        {
            StateMachineUtils machine = Started();
            List<string> log = Record(machine);
            Assert.True(machine.Fire(trigger, out bool fired, out _, out _, out _));
            Assert.False(fired);
            Assert.Equal(new[] { $"rejected:Received:{trigger}:{reason}" }, log);
        }

        [Fact]
        public void RejectedEvent_CarriesReasonAndDetail()
        {
            StateMachineUtils machine = Started(Defs.Minimal);
            Assert.True(machine.Fire("go", out _, out _, out _, out _));
            StateMachineRejectedEventArgs seen = null;
            machine.TransitionRejected += (_, e) => seen = e;
            Assert.True(machine.Fire("go", out _, out _, out _, out _));
            Assert.NotNull(seen);
            Assert.Equal("Finished", seen.Reason);
            Assert.Equal("B", seen.State);
            Assert.Contains("final state", seen.Detail);
        }

        [Fact]
        public void EventsFire_AfterTheChangeIsCommitted_SoHandlersSeeTheNewState()
        {
            StateMachineUtils machine = Started();
            string seenInHandler = null;
            machine.StateEntered += (_, _) => seenInHandler = machine.CurrentState;
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            Assert.Equal("Validated", seenInHandler);
        }

        [Fact]
        public void EventsFire_OnTheCallingThread_NotAWorkerThread()
        {
            StateMachineUtils machine = Started();
            int handlerThread = -1;
            machine.StateEntered += (_, _) => handlerThread = Thread.CurrentThread.ManagedThreadId;
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            Assert.Equal(Thread.CurrentThread.ManagedThreadId, handlerThread);
        }

        [Fact]
        public void EventsFire_OutsideTheLock_SoAHandlerCanQueryFromAnotherThread()
        {
            StateMachineUtils machine = Started();
            string fromOtherThread = null;
            bool joined = false;
            machine.StateEntered += (_, _) =>
            {
                var worker = new Thread(() => fromOtherThread = machine.CurrentState);
                worker.Start();
                joined = worker.Join(TimeSpan.FromSeconds(5));
            };
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            Assert.True(joined, "another thread could not read the machine from inside a handler: the lock was held while raising events");
            Assert.Equal("Validated", fromOtherThread);
        }

        [Fact]
        public void ASubscriberThatThrows_DoesNotStopOthersOrFailTheFire()
        {
            StateMachineUtils machine = Started();
            bool laterSubscriberRan = false;
            machine.StateEntered += (_, _) => throw new InvalidOperationException("boom");
            machine.StateEntered += (_, _) => laterSubscriberRan = true;
            Assert.True(machine.Fire("validate", out bool fired, out string newState, out _, out string message), message);
            Assert.True(fired);
            Assert.Equal("Validated", newState);
            Assert.True(laterSubscriberRan);
            Assert.Equal("Validated", machine.CurrentState);
        }

        [Fact]
        public void AHandlerMayFireAgain_AChainOfTransitionsRunsDepthFirst()
        {
            const string json = """
                { "initial": "A", "states": [ "A", "B", { "name": "C", "final": true } ],
                  "transitions": [ { "from": "A", "trigger": "next", "to": "B" }, { "from": "B", "trigger": "next", "to": "C" } ] }
                """;
            StateMachineUtils machine = Started(json);
            var order = new List<string>();
            bool nestedFired = false;
            machine.StateEntered += (sender, e) =>
            {
                order.Add("entered:" + e.NewState);
                if (e.NewState == "B") nestedFired = machine.Fire("next", out bool f, out _, out _, out _) && f;
            };
            machine.MachineFinished += (_, e) => order.Add("finished:" + e.NewState);
            Assert.True(machine.Fire("next", out _, out _, out _, out _));
            Assert.True(nestedFired);
            Assert.Equal(new[] { "entered:B", "entered:C", "finished:C" }, order);
            Assert.Equal("C", machine.CurrentState);
        }

        [Fact]
        public void ARunawayResetInsideAHandler_IsCappedInsteadOfOverflowingTheStack()
        {
            StateMachineUtils machine = Loaded();
            int entered = 0;
            int refused = 0;
            string refusal = null;
            machine.StateEntered += (_, _) =>
            {
                entered++;
                if (!machine.Reset(false, out _, out string message))
                {
                    refused++;
                    refusal = message;
                }
            };
            Assert.True(machine.Start(out _, out _));
            Assert.Equal(16, entered);
            Assert.Equal(1, refused);
            Assert.Contains("re-entrancy limit", refusal);
        }

        [Fact]
        public void ARunawayRejectionHandler_IsCappedAndTheOverflowIsRecordedWithoutAnEvent()
        {
            StateMachineUtils machine = Started();
            int rejectedEvents = 0;
            int nestedFailures = 0;
            machine.TransitionRejected += (_, _) =>
            {
                rejectedEvents++;
                if (!machine.Fire("still-nonsense", out _, out _, out _, out _)) nestedFailures++;
            };
            Assert.True(machine.Fire("nonsense", out bool fired, out _, out _, out string message), message);
            Assert.False(fired);
            Assert.Equal(16, rejectedEvents);
            Assert.Equal(0, nestedFailures);

            Assert.True(machine.GetHistoryJson(out string json, out _));
            Assert.Contains(JsonDocument.Parse(json).RootElement.EnumerateArray(),
                e => e.TryGetProperty("reason", out JsonElement reason) && reason.GetString() == "ReentrancyLimit");
        }

        [Fact]
        public void ConcurrentFires_AreSerialized_SoAnotherThreadCannotSlipInBetweenACommitAndItsEvents()
        {
            StateMachineUtils machine = Started();
            var log = new List<string>();
            var handlerRunning = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            machine.StateEntered += (_, e) =>
            {
                lock (log) log.Add("entered:" + e.NewState);
                if (e.NewState == "Validated")
                {
                    handlerRunning.Set();
                    release.Wait(TimeSpan.FromSeconds(10)); // hold the first transition's handlers open
                }
            };

            var first = new Thread(() => machine.Fire("validate", out _, out _, out _, out _));
            first.Start();
            Assert.True(handlerRunning.Wait(TimeSpan.FromSeconds(5)), "the first handler never started");

            bool secondFired = false;
            var second = new Thread(() => secondFired = machine.Fire("abort", out bool f, out _, out _, out _) && f);
            second.Start();

            // While the first transition's handlers are still running, the second Fire must not be able to
            // commit: it waits, so the machine is still in the state the first handler was told about.
            Assert.False(second.Join(TimeSpan.FromMilliseconds(300)), "a second thread committed a transition while the first one's handlers were still running");
            Assert.Equal("Validated", machine.CurrentState);

            release.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            Assert.True(second.Join(TimeSpan.FromSeconds(5)));

            Assert.True(secondFired);
            Assert.Equal("Failed", machine.CurrentState);
            lock (log) Assert.Equal(new[] { "entered:Validated", "entered:Failed" }, log); // delivered in transition order
        }

        [Fact]
        public void ReplacingTheDefinitionFromAnotherThread_WaitsForTheEventBatchInFlight()
        {
            StateMachineUtils machine = Started();
            var log = new List<string>();
            var handlerRunning = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            machine.TransitionFired += (_, e) =>
            {
                lock (log) log.Add("fired:" + e.PreviousState + ">" + e.NewState);
                handlerRunning.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            };
            machine.StateEntered += (_, e) => { lock (log) log.Add("entered:" + e.NewState); };

            var firing = new Thread(() => machine.Fire("validate", out _, out _, out _, out _));
            firing.Start();
            Assert.True(handlerRunning.Wait(TimeSpan.FromSeconds(5)), "the handler never started");

            bool loaded = false;
            var replacing = new Thread(() => loaded = machine.LoadDefinitionJson(Defs.Minimal, out _));
            replacing.Start();

            // The load stops the machine, so it must not run while this transition's events are still being
            // delivered: the definition and state the remaining handlers describe must still exist.
            Assert.False(replacing.Join(TimeSpan.FromMilliseconds(300)), "the definition was replaced while an event batch was still being delivered");
            Assert.Equal("InvoiceFlow", machine.MachineName);
            Assert.Equal("Validated", machine.CurrentState);

            release.Set();
            Assert.True(firing.Join(TimeSpan.FromSeconds(5)));
            Assert.True(replacing.Join(TimeSpan.FromSeconds(5)));

            Assert.True(loaded);
            lock (log) Assert.Equal(new[] { "fired:Received>Validated", "entered:Validated" }, log); // the batch finished intact first
            Assert.Equal(string.Empty, machine.MachineName); // the replacement definition has no name
            Assert.Equal(string.Empty, machine.CurrentState); // then the load stopped the machine
        }

        [Fact]
        public void ClearingTheDefinitionFromAnotherThread_AlsoWaitsForTheEventBatchInFlight()
        {
            StateMachineUtils machine = Started();
            var handlerRunning = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            machine.StateEntered += (_, _) => { handlerRunning.Set(); release.Wait(TimeSpan.FromSeconds(10)); };

            var firing = new Thread(() => machine.Fire("validate", out _, out _, out _, out _));
            firing.Start();
            Assert.True(handlerRunning.Wait(TimeSpan.FromSeconds(5)));

            var clearing = new Thread(() => machine.ClearDefinition(out _));
            clearing.Start();
            Assert.False(clearing.Join(TimeSpan.FromMilliseconds(300)), "the definition was cleared while an event batch was still being delivered");
            Assert.Equal("Validated", machine.CurrentState);

            release.Set();
            Assert.True(firing.Join(TimeSpan.FromSeconds(5)));
            Assert.True(clearing.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(string.Empty, machine.CurrentState);
        }

        [Theory]
        [InlineData("load")]
        [InlineData("clear")]
        public void ReplacingTheDefinitionFromInsideAnEventHandler_IsRefused_AndTheMachineIsUntouched(string which)
        {
            StateMachineUtils machine = Started();
            bool ok = true;
            string refusal = null;
            var remainingEvents = new List<string>();
            machine.StateExited += (_, _) =>
            {
                ok = which == "load" ? machine.LoadDefinitionJson(Defs.Minimal, out refusal) : machine.ClearDefinition(out refusal);
            };
            machine.TransitionFired += (_, e) => remainingEvents.Add("fired:" + e.NewState);
            machine.StateEntered += (_, e) => remainingEvents.Add("entered:" + e.NewState);

            Assert.True(machine.Fire("validate", out bool fired, out string newState, out _, out string message), message);

            Assert.False(ok);
            Assert.Contains("inside an event handler", refusal);
            Assert.True(fired);
            Assert.Equal("Validated", newState);
            Assert.Equal("Validated", machine.CurrentState); // still running, on the definition the events describe
            Assert.Equal("InvoiceFlow", machine.MachineName);
            Assert.Equal(new[] { "fired:Validated", "entered:Validated" }, remainingEvents);
        }

        /// <summary>Saves a genuine started run (Received -> Validated) under a fresh name and returns the name.</summary>
        private string SaveARunningMachine()
        {
            string name = NewMachineName();
            StateMachineUtils writer = Loaded();
            Assert.True(writer.EnablePersistence(name, out _, out _, out string m), m);
            Assert.True(writer.Start(out _, out m), m);
            Assert.True(writer.Fire("validate", out _, out _, out _, out m), m);
            writer.Dispose();
            return name;
        }

        [Fact]
        public void EnablingPersistenceFromAnotherThread_WaitsForTheEventBatchInFlight_BeforeRestoring()
        {
            string name = SaveARunningMachine();
            StateMachineUtils machine = Loaded(); // not started
            var handlerRunning = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            string stateSeenByHandler = null;
            machine.TransitionRejected += (_, _) => { handlerRunning.Set(); release.Wait(TimeSpan.FromSeconds(10)); stateSeenByHandler = machine.CurrentState; };

            var firing = new Thread(() => machine.Fire("validate", out _, out _, out _, out _)); // declined: NotStarted
            firing.Start();
            Assert.True(handlerRunning.Wait(TimeSpan.FromSeconds(5)));

            bool restored = false;
            var enabling = new Thread(() => machine.EnablePersistence(name, out _, out restored, out _));
            enabling.Start();
            Assert.False(enabling.Join(TimeSpan.FromMilliseconds(300)), "the saved run was restored while an event batch was still being delivered");
            Assert.Equal(string.Empty, machine.CurrentState);

            release.Set();
            Assert.True(firing.Join(TimeSpan.FromSeconds(5)));
            Assert.True(enabling.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(string.Empty, stateSeenByHandler); // the handler described the machine as it was when it declined
            Assert.True(restored);
            Assert.Equal("Validated", machine.CurrentState);
        }

        [Fact]
        public void EnablingPersistenceFromInsideAnEventHandler_IsRefused_AndNothingIsRestored()
        {
            string name = SaveARunningMachine();
            StateMachineUtils machine = Loaded();
            bool ok = true;
            string refusal = null;
            machine.TransitionRejected += (_, _) => ok = machine.EnablePersistence(name, out _, out _, out refusal);

            Assert.True(machine.Fire("validate", out bool fired, out _, out _, out _));
            Assert.False(fired);
            Assert.False(ok);
            Assert.Contains("inside an event handler", refusal);
            Assert.Equal(string.Empty, machine.CurrentState);
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message); // and works normally afterwards
            Assert.True(restored);
        }

        [Fact]
        public void TheDefinitionCanStillBeReplacedNormally_OnceNoHandlerIsRunning()
        {
            StateMachineUtils machine = Started();
            machine.StateEntered += (_, _) => { };
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            Assert.True(machine.LoadDefinitionJson(Defs.Minimal, out string message), message);
            Assert.True(machine.ClearDefinition(out message), message);
        }

        /// <summary>Runs <paramref name="wait"/> on a thread and returns once that thread is provably blocked waiting for a lock.</summary>
        private static Thread StartBlocked(Action wait)
        {
            var thread = new Thread(() => wait());
            thread.Start();
            Assert.True(SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)), "the thread never blocked");
            return thread;
        }

        [Fact]
        public void AFireWaitingForTheDispatchLock_WhenTheComponentIsDisposed_RefusesInsteadOfMutatingIt()
        {
            StateMachineUtils machine = Started();
            var log = new List<string>();
            var handlerRunning = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            machine.StateEntered += (_, e) =>
            {
                lock (log) log.Add("entered:" + e.NewState);
                if (e.NewState == "Validated") { handlerRunning.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
            };

            var first = new Thread(() => machine.Fire("validate", out _, out _, out _, out _));
            first.Start();
            Assert.True(handlerRunning.Wait(TimeSpan.FromSeconds(5)));

            bool returned = true, fired = true;
            string message = null;
            Thread second = StartBlocked(() => returned = machine.Fire("abort", out fired, out _, out _, out message)); // passed RequireLive, now queued behind the handler

            // Disposal must not wait for handlers (a handler may be marshalling to the very thread disposing us) ...
            var disposer = new Thread(() => machine.Dispose());
            disposer.Start();
            Assert.True(disposer.Join(TimeSpan.FromSeconds(5)), "Dispose blocked behind a running handler");

            release.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            Assert.True(second.Join(TimeSpan.FromSeconds(5)));

            // ... and the queued Fire, finally getting its turn, must observe the disposal rather than commit.
            Assert.False(returned);
            Assert.False(fired);
            Assert.Contains("disposed", message);
            lock (log) Assert.Equal(new[] { "entered:Validated" }, log); // no transition, no events, for the refused call
        }

        [Fact]
        public void ALoadWaitingForTheDispatchLock_WhenTheComponentIsDisposed_RefusesToo()
        {
            StateMachineUtils machine = Started();
            var handlerRunning = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            machine.StateEntered += (_, e) => { if (e.NewState == "Validated") { handlerRunning.Set(); release.Wait(TimeSpan.FromSeconds(10)); } };
            var first = new Thread(() => machine.Fire("validate", out _, out _, out _, out _));
            first.Start();
            Assert.True(handlerRunning.Wait(TimeSpan.FromSeconds(5)));

            bool loaded = true;
            string message = null;
            Thread loading = StartBlocked(() => loaded = machine.LoadDefinitionJson(Defs.Minimal, out message));
            machine.Dispose();
            release.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            Assert.True(loading.Join(TimeSpan.FromSeconds(5)));

            Assert.False(loaded);
            Assert.Contains("disposed", message);
        }

        [Fact]
        public void ADisposedComponent_NeverThrows_EvenWhenAHandlerDisposesItMidCall()
        {
            StateMachineUtils machine = Started();
            machine.StateEntered += (_, _) => machine.Dispose(); // the handler tears the component down while Fire is still delivering
            Assert.True(machine.Fire("validate", out bool fired, out string newState, out _, out string message), message);
            Assert.True(fired); // the transition committed before disposal
            Assert.Equal("Validated", newState);
            Assert.False(machine.Fire("abort", out _, out _, out _, out message));
            Assert.Contains("disposed", message);
        }

        [Fact]
        public void WhileHandlersRun_OtherThreadsCanStillReadTheMachine()
        {
            StateMachineUtils machine = Started();
            string read = null;
            bool joined = false;
            machine.StateEntered += (_, _) =>
            {
                var reader = new Thread(() => read = machine.CurrentState);
                reader.Start();
                joined = reader.Join(TimeSpan.FromSeconds(5));
            };
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            Assert.True(joined, "a reader was blocked by a running handler");
            Assert.Equal("Validated", read);
        }

        [Fact]
        public void ConcurrentFiresFromManyThreads_NeverInterleaveTheirEventBatches()
        {
            const string cycle = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B" }, { "from": "B", "trigger": "go", "to": "A" } ] }
                """;
            StateMachineUtils machine = Started(cycle);
            var log = new List<string>();
            machine.StateExited += (_, e) => { lock (log) log.Add("exited:" + e.PreviousState); };
            machine.TransitionFired += (_, e) => { lock (log) log.Add("fired:" + e.PreviousState); };
            machine.StateEntered += (_, e) => { lock (log) log.Add("entered:" + e.NewState); };

            var threads = Enumerable.Range(0, 8).Select(worker => new Thread(() =>
            {
                for (int i = 0; i < 50; i++) machine.Fire("go", out _, out _, out _, out _);
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => Assert.True(t.Join(TimeSpan.FromSeconds(30))));

            // 400 transitions alternate A>B, B>A, ... and each contributes exited/fired/entered as one batch.
            Assert.Equal(1200, log.Count);
            for (int i = 0; i < 400; i++)
            {
                string from = i % 2 == 0 ? "A" : "B";
                string to = i % 2 == 0 ? "B" : "A";
                Assert.Equal(new[] { "exited:" + from, "fired:" + from, "entered:" + to }, log.Skip(i * 3).Take(3));
            }
        }

        // ---- StateExited carries how long the machine was in the state it is leaving ----

        [Fact]
        public void StateExited_CarriesTheMillisecondsSpentInTheStateBeingLeft()
        {
            StateMachineUtils machine = Loaded();
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            machine.Clock = () => now;
            Assert.True(machine.Start(out _, out _)); // enters Received at 12:00:00.000

            var exits = new List<StateMachineExitEventArgs>();
            machine.StateExited += (_, e) => exits.Add(e);

            now = now.AddMilliseconds(1250);
            Assert.True(machine.Fire("validate", out _, out string message), message); // leaves Received after 1250 ms
            now = now.AddSeconds(3);
            machine.SetContext("amount", "1", out _);
            Assert.True(machine.Fire("post", out _, out message), message);           // leaves Validated after 3000 ms

            Assert.Equal(2, exits.Count);
            Assert.Equal(("Received", "Validated", "validate", 1250.0), (exits[0].PreviousState, exits[0].NewState, exits[0].Trigger, exits[0].ElapsedMs));
            Assert.Equal(("Validated", "Posted", "post", 3000.0), (exits[1].PreviousState, exits[1].NewState, exits[1].Trigger, exits[1].ElapsedMs));
        }

        [Fact]
        public void StateExited_MillisecondsRestartForEachState_AndAreZeroWhenNoTimePassed()
        {
            StateMachineUtils machine = Started();
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            machine.Clock = () => now;
            Assert.True(machine.Reset(false, out _, out _));   // re-enters Received at 12:00:00
            double first = -1;
            machine.StateExited += (_, e) => first = e.ElapsedMs;
            Assert.True(machine.Fire("validate", out _, out _));
            Assert.Equal(0.0, first);                           // same instant: 0, never negative
        }

        [Fact]
        public void StateExited_MillisecondsIsNeverNegative_EvenIfTheClockWentBackwards()
        {
            StateMachineUtils machine = Loaded();
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            machine.Clock = () => now;
            Assert.True(machine.Start(out _, out _));
            double ms = -1;
            machine.StateExited += (_, e) => ms = e.ElapsedMs;
            now = now.AddMinutes(-5);                           // a clock adjustment
            Assert.True(machine.Fire("validate", out _, out _));
            Assert.Equal(0.0, ms);
        }

        [Fact]
        public void StateExited_ForARestoredRun_CountsFromTheOriginalEntryTime_IncludingDowntime()
        {
            string name = NewMachineName();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            StateMachineUtils first = Loaded();
            first.Clock = () => t0;
            Assert.True(first.EnablePersistence(name, out _, out _, out string message), message);
            Assert.True(first.Start(out _, out message), message);
            first.Dispose();                                    // the crash

            StateMachineUtils second = Loaded();
            second.Clock = () => t0.AddMinutes(10);
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.True(restored);
            double ms = -1;
            second.StateExited += (_, e) => ms = e.ElapsedMs;
            Assert.True(second.Fire("validate", out _, out message), message);
            Assert.Equal(600000.0, ms);                         // 10 minutes since Received was entered, downtime included
        }

        [Fact]
        public void StateExited_IsNotRaisedForADeclinedTrigger_SoNoMillisecondsAreReported()
        {
            StateMachineUtils machine = Started();
            int exits = 0;
            machine.StateExited += (_, _) => exits++;
            Assert.True(machine.Fire("nonsense", out _, out string message));
            Assert.Equal("NoTransition", message);
            Assert.Equal(0, exits);
        }

        [Fact]
        public void StateExited_ElapsedMsExcludesHandlerTime_WhichBelongsToTheNewState()
        {
            StateMachineUtils machine = Loaded();
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            machine.Clock = () => now;
            Assert.True(machine.Start(out _, out _));
            var exits = new List<double>();
            machine.StateExited += (_, e) => { exits.Add(e.ElapsedMs); now = now.AddSeconds(10); }; // a slow handler
            now = now.AddSeconds(1);
            Assert.True(machine.Fire("validate", out _, out _));   // leaves Received after 1 s
            now = now.AddSeconds(2);
            machine.SetContext("amount", "1", out _);
            Assert.True(machine.Fire("post", out _, out _));       // leaves Validated after 10 s (handler) + 2 s
            Assert.Equal(new[] { 1000.0, 12000.0 }, exits);
        }
    }
}
