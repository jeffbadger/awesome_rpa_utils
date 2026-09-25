using System.Collections.Generic;
using Xunit;

namespace StateMachineAutomation.Tests
{
    /// <summary>Runs Documentation/Tutorial.md step by step, asserting every outcome the text states.</summary>
    public sealed class TutorialTests : MachineTestBase
    {
        private const string Definition = """
            {
              "name": "ExpenseClaim",
              "initial": "Draft",
              "states": [
                "Draft", "Submitted", "ManagerReview", "Approved",
                { "name": "Paid",      "final": true },
                { "name": "Rejected",  "final": true },
                { "name": "Cancelled", "final": true }
              ],
              "transitions": [
                { "from": "Draft",         "trigger": "submit",  "to": "Submitted" },
                { "from": "Submitted",     "trigger": "decide",  "to": "Approved",
                  "guards": [ { "key": "amount", "op": "lessThan", "value": "500" } ] },
                { "from": "Submitted",     "trigger": "decide",  "to": "ManagerReview" },
                { "from": "ManagerReview", "trigger": "approve", "to": "Approved" },
                { "from": "ManagerReview", "trigger": "reject",  "to": "Rejected" },
                { "from": "Approved",      "trigger": "pay",     "to": "Paid" },
                { "from": "*",             "trigger": "cancel",  "to": "Cancelled" }
              ]
            }
            """;

        [Fact]
        public void Steps4To10_AsWritten()
        {
            StateMachineUtils machine = New();

            // step 4
            Assert.True(machine.ValidateDefinitionJson(Definition, out string report, out string message), message);
            Assert.Equal("{\"valid\":true,\"errors\":[],\"warnings\":[],\"stateCount\":7,\"transitionCount\":7}", report);

            // step 5
            Assert.True(machine.LoadDefinitionJson(Definition, out message), message);
            Assert.True(machine.Start(out string state, out message), message);
            Assert.Equal("Draft", state);

            // step 10 wiring, so the run below also proves the event lines
            var log = new List<string>();
            machine.StateEntered += (s, e) => log.Add($"{e.PreviousState} -> {e.NewState} on '{e.Trigger}'");
            machine.TransitionRejected += (s, e) => log.Add($"'{e.Trigger}' declined in {e.State}: {e.Reason}");
            machine.MachineFinished += (s, e) => log.Add($"claim ended in {e.NewState}");

            // step 6
            Assert.True(machine.Fire("submit", out bool fired, out state, out string reason, out message), message);
            Assert.True(fired);
            Assert.Equal("Submitted", state);
            Assert.Null(reason);

            // step 7
            Assert.True(machine.Fire("pay", out fired, out state, out reason, out message));
            Assert.False(fired);
            Assert.Equal("Submitted", state);
            Assert.Equal("NoTransition", reason);

            // step 8
            Assert.True(machine.SetContext("amount", "120", out message), message);
            Assert.True(machine.Fire("decide", out fired, out state, out reason, out message));
            Assert.True(fired);
            Assert.Equal("Approved", state);

            // step 9
            Assert.True(machine.CanFire("pay", out bool can, out reason, out message));
            Assert.True(can);
            Assert.True(machine.GetAvailableTriggersDelimited(out string triggers, out message));
            Assert.Equal("pay,cancel", triggers);
            Assert.True(machine.GetCurrentState(out state, out message));
            Assert.Equal("Approved", state);
            Assert.True(machine.IsInFinalState(out bool isFinal, out message));
            Assert.False(isFinal);

            Assert.True(machine.Fire("pay", out fired, out state, out reason, out message));
            Assert.Equal("Paid", state);
            Assert.True(machine.IsInFinalState(out isFinal, out message));
            Assert.True(isFinal);
            Assert.True(machine.Fire("cancel", out fired, out state, out reason, out message));
            Assert.False(fired);
            Assert.Equal("Finished", reason);

            // step 10: events and history
            Assert.Contains("Draft -> Submitted on 'submit'", log);
            Assert.Contains("'pay' declined in Submitted: NoTransition", log);
            Assert.Contains("claim ended in Paid", log);
            Assert.True(machine.GetHistoryJson(out string history, out message, maxEntries: 10), message);
            Assert.Contains("\"rejected\"", history);

            // "Start another run"
            Assert.True(machine.Reset(true, out state, out message), message);
            Assert.Equal("Draft", state);
        }

        [Fact]
        public void Step8_ALargeAmountGoesToAManager_AndAMissingKeyFailsItsGuard()
        {
            StateMachineUtils machine = Started(Definition);
            Assert.True(machine.Fire("submit", out _, out _, out _, out _));

            // 900 is not below 500, so the guard fails and the unguarded 'decide' is the one taken (ManagerReview)
            Assert.True(machine.SetContext("amount", "900", out string message));
            Assert.True(machine.Fire("decide", out bool fired, out string state, out _, out message));
            Assert.True(fired);
            Assert.Equal("ManagerReview", state);
            Assert.True(machine.Fire("reject", out _, out state, out _, out _));
            Assert.Equal("Rejected", state);
        }

        [Fact]
        public void Step2_CancelWorksFromAnyNonFinalState()
        {
            foreach (string[] path in new[] { new string[0], new[] { "submit" }, new[] { "submit", "decide" } })
            {
                StateMachineUtils machine = Started(Definition);
                Assert.True(machine.SetContext("amount", "900", out _));
                foreach (string t in path) Assert.True(machine.Fire(t, out _, out _, out _, out _));
                Assert.True(machine.Fire("cancel", out bool fired, out string state, out _, out _));
                Assert.True(fired);
                Assert.Equal("Cancelled", state);
            }
        }

        [Fact]
        public void Step11_PersistenceResumesAfterARestart()
        {
            string name = NewMachineName();
            StateMachineUtils first = New();
            Assert.True(first.LoadDefinitionJson(Definition, out string message), message);
            Assert.True(first.EnablePersistence(name, out string statePath, out bool restored, out message), message);
            Assert.False(restored);
            Assert.True(first.IsStarted(out bool started, out message));
            Assert.False(started);
            Assert.True(first.Start(out _, out message), message);
            Assert.True(first.SetContext("amount", "900", out message), message);
            Assert.True(first.Fire("submit", out _, out _, out _, out message), message);
            first.Dispose(); // "crash"

            StateMachineUtils second = New();
            Assert.True(second.LoadDefinitionJson(Definition, out message), message);
            Assert.True(second.EnablePersistence(name, out statePath, out restored, out message), message);
            Assert.True(restored);
            Assert.True(second.IsStarted(out started, out message));
            Assert.True(started);
            Assert.Equal("Submitted", second.CurrentState);
            Assert.True(second.GetContext("amount", out bool exists, out string value, out message));
            Assert.True(exists);
            Assert.Equal("900", value);
        }
    }
}
