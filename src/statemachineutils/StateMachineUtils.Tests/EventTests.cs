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
        public void TheReentrancyCounter_IsPerThread()
        {
            StateMachineUtils machine = Started();
            bool otherThreadFired = false;
            machine.StateEntered += (_, _) =>
            {
                var worker = new Thread(() => otherThreadFired = machine.Fire("abort", out bool f, out _, out _, out _) && f);
                worker.Start();
                worker.Join(TimeSpan.FromSeconds(5));
            };
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            Assert.True(otherThreadFired);
            Assert.Equal("Failed", machine.CurrentState);
        }
    }
}
