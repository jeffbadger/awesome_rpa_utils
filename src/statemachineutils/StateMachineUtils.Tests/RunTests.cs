using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace StateMachineAutomation.Tests
{
    public sealed class RunTests : MachineTestBase
    {
        private static (bool fired, string newState, string reason) Fire(StateMachineUtils machine, string trigger)
        {
            Assert.True(machine.Fire(trigger, out bool fired, out string newState, out string reason, out string message), message);
            Assert.Null(message);
            return (fired, newState, reason);
        }

        // ------------------------------------------------------------ start / reset

        [Fact]
        public void Start_EntersTheInitialState()
        {
            StateMachineUtils machine = Loaded();
            Assert.Equal(string.Empty, machine.CurrentState);
            Assert.True(machine.Start(out string state, out string message), message);
            Assert.Equal("Received", state);
            Assert.Equal("Received", machine.CurrentState);
            Assert.False(machine.IsFinished);
        }

        [Fact]
        public void Start_Twice_IsRefusedWithAHintToUseReset()
        {
            StateMachineUtils machine = Started();
            Assert.False(machine.Start(out string state, out string message));
            Assert.Null(state);
            Assert.Contains("Reset", message);
        }

        [Fact]
        public void Start_WithNoDefinition_FailsWithAMessage()
        {
            StateMachineUtils machine = New();
            Assert.False(machine.Start(out _, out string message));
            Assert.Contains("not valid", message);
        }

        [Fact]
        public void Start_KeepsContextSetBeforehand()
        {
            StateMachineUtils machine = Loaded();
            Assert.True(machine.SetContext("amount", "50", out _));
            Assert.True(machine.Start(out _, out _));
            Assert.True(machine.GetContext("amount", out bool exists, out string value, out _));
            Assert.True(exists);
            Assert.Equal("50", value);
        }

        [Fact]
        public void Reset_ReturnsToTheInitialStateAndClearsHistory()
        {
            StateMachineUtils machine = Started();
            Fire(machine, "validate");
            Assert.True(machine.Reset(false, out string state, out string message), message);
            Assert.Equal("Received", state);
            Assert.True(machine.GetHistoryJson(out string json, out message), message);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal("reset", doc.RootElement[0].GetProperty("kind").GetString());
        }

        [Fact]
        public void Reset_ClearContextOnlyWhenAsked()
        {
            StateMachineUtils machine = Started();
            Assert.True(machine.SetContext("k", "v", out _));
            Assert.True(machine.Reset(false, out _, out _));
            Assert.True(machine.GetContext("k", out bool kept, out _, out _));
            Assert.True(kept);
            Assert.True(machine.Reset(true, out _, out _));
            Assert.True(machine.GetContext("k", out bool stillThere, out _, out _));
            Assert.False(stillThere);
        }

        [Fact]
        public void Reset_RestartsAFinishedMachine()
        {
            StateMachineUtils machine = Started(Defs.Minimal);
            Assert.True(Fire(machine, "go").fired);
            Assert.True(machine.IsFinished);
            Assert.True(machine.Reset(false, out string state, out string message), message);
            Assert.Equal("A", state);
            Assert.False(machine.IsFinished);
        }

        // ------------------------------------------------------------ fire

        [Fact]
        public void Fire_FollowsTheDeclaredTransition()
        {
            StateMachineUtils machine = Started();
            (bool fired, string newState, string reason) = Fire(machine, "validate");
            Assert.True(fired);
            Assert.Equal("Validated", newState);
            Assert.Null(reason);
            Assert.Equal("Validated", machine.CurrentState);
        }

        [Fact]
        public void Fire_IsCaseInsensitiveForTriggersAndReturnsTheDeclaredStateCasing()
        {
            StateMachineUtils machine = Started();
            (bool fired, string newState, _) = Fire(machine, "  VALIDATE ");
            Assert.True(fired);
            Assert.Equal("Validated", newState);
        }

        [Fact]
        public void Fire_FirstPassingGuardWins_AndTheUnguardedFallbackCatchesTheRest()
        {
            StateMachineUtils passing = Started();
            Fire(passing, "validate");
            Assert.True(passing.SetContext("amount", "500", out _));
            Assert.Equal("Posted", Fire(passing, "post").newState);
            Assert.True(passing.IsFinished);

            StateMachineUtils failing = Started();
            Fire(failing, "validate");
            Assert.True(failing.SetContext("amount", "25000", out _));
            Assert.Equal("Failed", Fire(failing, "post").newState);

            StateMachineUtils missing = Started();
            Fire(missing, "validate");
            Assert.Equal("Failed", Fire(missing, "post").newState); // no amount: the guard fails closed, fallback applies
        }

        [Fact]
        public void Fire_WildcardTransitionAppliesFromAnyNonFinalState()
        {
            StateMachineUtils early = Started();
            Assert.Equal("Failed", Fire(early, "abort").newState);

            StateMachineUtils late = Started();
            Fire(late, "validate");
            Assert.Equal("Failed", Fire(late, "abort").newState);
        }

        [Fact]
        public void Fire_UnknownTrigger_IsANormalRejection()
        {
            StateMachineUtils machine = Started();
            (bool fired, string newState, string reason) = Fire(machine, "nonsense");
            Assert.False(fired);
            Assert.Equal("Received", newState);
            Assert.Equal("NoTransition", reason);
            Assert.Equal("Received", machine.CurrentState);
        }

        [Fact]
        public void Fire_GuardFailure_IsANormalRejectionNamingTheGuardButNotItsValue()
        {
            const string json = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B", "guards": [ { "key": "secret", "op": "equals", "value": "yes" } ] } ] }
                """;
            StateMachineUtils machine = Started(json);
            Assert.True(machine.SetContext("secret", "hunter2", out _));
            (bool fired, _, string reason) = Fire(machine, "go");
            Assert.False(fired);
            Assert.Equal("GuardFailed", reason);
            Assert.True(machine.GetHistoryJson(out string history, out _));
            Assert.Contains("secret equals yes", history);
            Assert.DoesNotContain("hunter2", history);
        }

        [Fact]
        public void Fire_AfterTheMachineFinished_IsRejectedAsFinished()
        {
            StateMachineUtils machine = Started(Defs.Minimal);
            Fire(machine, "go");
            (bool fired, string newState, string reason) = Fire(machine, "go");
            Assert.False(fired);
            Assert.Equal("B", newState);
            Assert.Equal("Finished", reason);
        }

        [Fact]
        public void Fire_BeforeStart_IsRejectedAsNotStarted()
        {
            StateMachineUtils machine = Loaded();
            (bool fired, _, string reason) = Fire(machine, "validate");
            Assert.False(fired);
            Assert.Equal("NotStarted", reason);
        }

        [Fact]
        public void Fire_WithNoDefinition_IsAnErrorNotARejection()
        {
            StateMachineUtils machine = New();
            Assert.False(machine.Fire("go", out bool fired, out string newState, out string reason, out string message));
            Assert.False(fired);
            Assert.Null(newState);
            Assert.Null(reason);
            Assert.Contains("No definition", message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Fire_BadTrigger_IsAnErrorWithAMessage(string trigger)
        {
            StateMachineUtils machine = Started();
            Assert.False(machine.Fire(trigger, out bool fired, out _, out string reason, out string message));
            Assert.False(fired);
            Assert.Null(reason);
            Assert.Contains("trigger", message);
        }

        [Fact]
        public void Fire_FullPath_ReachesAFinalStateAndSetsIsFinished()
        {
            StateMachineUtils machine = Started();
            Fire(machine, "validate");
            Assert.True(machine.SetContext("amount", "1", out _));
            Fire(machine, "post");
            Assert.True(machine.IsFinished);
            Assert.True(machine.IsInFinalState(out bool isFinal, out string message), message);
            Assert.True(isFinal);
        }

        // ------------------------------------------------------------ CanFire / available triggers

        [Fact]
        public void CanFire_ReportsWithoutFiring()
        {
            StateMachineUtils machine = Started();
            Assert.True(machine.CanFire("validate", out bool can, out string reason, out string message), message);
            Assert.True(can);
            Assert.Null(reason);
            Assert.Equal("Received", machine.CurrentState);

            Assert.True(machine.CanFire("post", out can, out reason, out message), message);
            Assert.False(can);
            Assert.Equal("NoTransition", reason);
        }

        [Fact]
        public void CanFire_ReportsGuardFailureNotStartedAndFinished()
        {
            StateMachineUtils notStarted = Loaded();
            Assert.True(notStarted.CanFire("validate", out bool can, out string reason, out _));
            Assert.False(can);
            Assert.Equal("NotStarted", reason);

            StateMachineUtils finished = Started(Defs.Minimal);
            Fire(finished, "go");
            Assert.True(finished.CanFire("go", out can, out reason, out _));
            Assert.False(can);
            Assert.Equal("Finished", reason);
        }

        [Fact]
        public void CanFire_DoesNotRaiseEventsOrTouchHistory()
        {
            StateMachineUtils machine = Started();
            int raised = 0;
            machine.TransitionRejected += (_, _) => raised++;
            machine.TransitionFired += (_, _) => raised++;
            Assert.True(machine.CanFire("nonsense", out _, out _, out _));
            Assert.True(machine.CanFire("validate", out _, out _, out _));
            Assert.Equal(0, raised);
            Assert.True(machine.GetHistoryJson(out string json, out _));
            Assert.Single(JsonDocument.Parse(json).RootElement.EnumerateArray());
        }

        [Fact]
        public void GetAvailableTriggers_EvaluatesGuardsAndListsEachTriggerOnce()
        {
            StateMachineUtils machine = Started();
            Fire(machine, "validate");

            Assert.True(machine.GetAvailableTriggersDelimited(out string triggers, out string message), message);
            Assert.Equal("post,abort", triggers); // "post" has an unguarded fallback so it is always available

            Assert.True(machine.GetAvailableTriggersDelimited(out triggers, out message, "|"), message);
            Assert.Equal("post|abort", triggers);

            Assert.True(machine.GetAvailableTriggersJson(out string json, out message), message);
            Assert.Equal(new[] { "post", "abort" }, JsonSerializer.Deserialize<string[]>(json));
        }

        [Fact]
        public void GetAvailableTriggers_HidesGuardedTriggersWhoseGuardsFail()
        {
            const string json = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "gated", "to": "B", "guards": [ { "key": "ok", "op": "equals", "value": "yes" } ] },
                                   { "from": "A", "trigger": "open", "to": "B" } ] }
                """;
            StateMachineUtils machine = Started(json);
            Assert.True(machine.GetAvailableTriggersDelimited(out string triggers, out _));
            Assert.Equal("open", triggers);
            Assert.True(machine.SetContext("ok", "YES", out _));
            Assert.True(machine.GetAvailableTriggersDelimited(out triggers, out _));
            Assert.Equal("gated,open", triggers);
        }

        [Fact]
        public void GetAvailableTriggers_InAFinalState_IsEmpty_AndRequiresAStartedMachine()
        {
            StateMachineUtils finished = Started(Defs.Minimal);
            Fire(finished, "go");
            Assert.True(finished.GetAvailableTriggersDelimited(out string triggers, out _));
            Assert.Equal(string.Empty, triggers);

            StateMachineUtils notStarted = Loaded();
            Assert.False(notStarted.GetAvailableTriggersDelimited(out triggers, out string message));
            Assert.Null(triggers);
            Assert.Contains("not been started", message);
        }

        // ------------------------------------------------------------ queries

        [Fact]
        public void GetCurrentState_IsEmptyBeforeStart()
        {
            StateMachineUtils machine = Loaded();
            Assert.True(machine.GetCurrentState(out string state, out string message), message);
            Assert.Equal(string.Empty, state);
            Assert.True(machine.IsStarted(out bool started, out message), message);
            Assert.False(started);
            Assert.True(machine.IsInFinalState(out bool isFinal, out message), message);
            Assert.False(isFinal);
        }

        [Fact]
        public void GetSecondsInState_UsesTheClock_AndResetsOnTransition()
        {
            DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            StateMachineUtils machine = Loaded();
            machine.Clock = () => now;
            Assert.True(machine.Start(out _, out _));

            now = now.AddSeconds(90);
            Assert.True(machine.GetSecondsInState(out double seconds, out string message), message);
            Assert.Equal(90, seconds, 3);

            Fire(machine, "validate");
            now = now.AddSeconds(5);
            Assert.True(machine.GetSecondsInState(out seconds, out message), message);
            Assert.Equal(5, seconds, 3);
        }

        [Fact]
        public void GetSecondsInState_BeforeStart_IsAnError()
        {
            StateMachineUtils machine = Loaded();
            Assert.False(machine.GetSecondsInState(out double seconds, out string message));
            Assert.Equal(0, seconds);
            Assert.Contains("not been started", message);
        }

        // ------------------------------------------------------------ history

        [Fact]
        public void History_RecordsStartTransitionsAndRejections_InOrder()
        {
            StateMachineUtils machine = Started();
            Fire(machine, "nonsense");
            Fire(machine, "validate");
            Assert.True(machine.GetHistoryJson(out string json, out string message), message);
            JsonElement[] entries = JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();
            Assert.Equal(new[] { "start", "rejected", "transition" }, entries.Select(e => e.GetProperty("kind").GetString()));
            Assert.Equal(new long[] { 1, 2, 3 }, entries.Select(e => e.GetProperty("seq").GetInt64()));
            Assert.Equal("NoTransition", entries[1].GetProperty("reason").GetString());
            Assert.Equal("Received", entries[2].GetProperty("from").GetString());
            Assert.Equal("Validated", entries[2].GetProperty("to").GetString());
        }

        [Fact]
        public void History_IsBoundedByMaximumHistoryEntries_DroppingTheOldest()
        {
            StateMachineUtils machine = Started();
            Assert.True(machine.SetMaximumHistoryEntries(3, out string message), message);
            for (int i = 0; i < 10; i++) Fire(machine, "nonsense");
            Assert.True(machine.GetHistoryJson(out string json, out message), message);
            JsonElement[] entries = JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();
            Assert.Equal(3, entries.Length);
            Assert.Equal(new long[] { 9, 10, 11 }, entries.Select(e => e.GetProperty("seq").GetInt64()));
        }

        [Fact]
        public void GetHistoryJson_MaxEntriesTakesTheMostRecent()
        {
            StateMachineUtils machine = Started();
            for (int i = 0; i < 5; i++) Fire(machine, "nonsense");
            Assert.True(machine.GetHistoryJson(out string json, out _, 2));
            Assert.Equal(2, JsonDocument.Parse(json).RootElement.GetArrayLength());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(10001)]
        public void History_BadLimits_AreRejected(int limit)
        {
            StateMachineUtils machine = Started();
            Assert.False(machine.GetHistoryJson(out string json, out string message, limit));
            Assert.Null(json);
            Assert.Contains("maxEntries", message);
            Assert.False(machine.SetMaximumHistoryEntries(limit, out message));
            Assert.Contains("maximumEntries", message);
        }

        [Fact]
        public void MaximumHistoryEntriesProperty_ValidatesAndTrimsExistingHistory()
        {
            StateMachineUtils machine = Started();
            for (int i = 0; i < 6; i++) Fire(machine, "nonsense");
            machine.MaximumHistoryEntries = 2;
            Assert.Equal(2, machine.MaximumHistoryEntries);
            Assert.True(machine.GetHistoryJson(out string json, out _));
            Assert.Equal(2, JsonDocument.Parse(json).RootElement.GetArrayLength());
            Assert.Throws<ArgumentOutOfRangeException>(() => machine.MaximumHistoryEntries = 0);
        }

        // ------------------------------------------------------------ context and guard operators

        [Fact]
        public void Context_SetGetRemoveClear_RoundTrip()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.SetContext("Name", "Alice", out string message), message);
            Assert.True(machine.GetContext("NAME", out bool exists, out string value, out message), message); // keys are case-insensitive
            Assert.True(exists);
            Assert.Equal("Alice", value);
            Assert.True(machine.SetContext("name", "Bob", out message), message);
            Assert.True(machine.GetContextJson(out string json, out message), message);
            Assert.Equal("""{"Name":"Bob"}""", json);
            Assert.True(machine.RemoveContext("name", out bool removed, out message), message);
            Assert.True(removed);
            Assert.True(machine.RemoveContext("name", out removed, out message), message);
            Assert.False(removed); // a missing key is a normal outcome
            Assert.True(machine.SetContext("a", "1", out _));
            Assert.True(machine.SetContext("b", "2", out _));
            Assert.True(machine.ClearContext(out message), message);
            Assert.True(machine.GetContextJson(out json, out _));
            Assert.Equal("{}", json);
        }

        [Fact]
        public void Context_GetMissingKey_IsANormalOutcome()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.GetContext("nope", out bool exists, out string value, out string message), message);
            Assert.False(exists);
            Assert.Null(value);
        }

        [Fact]
        public void Context_BadInput_IsRejectedWithMessages()
        {
            StateMachineUtils machine = New();
            Assert.False(machine.SetContext("", "v", out string message));
            Assert.Contains("key", message);
            Assert.False(machine.SetContext("k", null, out message));
            Assert.Contains("RemoveContext", message);
            Assert.False(machine.SetContext("k", new string('x', 4097), out message));
            Assert.Contains("longer than", message);
            Assert.False(machine.GetContext(null, out _, out _, out message));
            Assert.False(machine.RemoveContext(" ", out _, out message));
        }

        [Fact]
        public void Context_IsBoundedInSize()
        {
            StateMachineUtils machine = New();
            for (int i = 0; i < StateMachineCore.MaxContextEntries; i++) Assert.True(machine.SetContext("k" + i, "v", out string ok), ok);
            Assert.False(machine.SetContext("one-too-many", "v", out string message));
            Assert.Contains("full", message);
            Assert.True(machine.SetContext("k0", "changed", out message), message); // overwriting an existing key is still fine
        }

        [Theory]
        [InlineData("equals", "Approved", "approved", true)]
        [InlineData("equals", "Approved", "rejected", false)]
        [InlineData("notEquals", "Approved", "rejected", true)]
        [InlineData("notEquals", "Approved", "APPROVED", false)]
        [InlineData("in", "a, b ,c", "B", true)]
        [InlineData("in", "a,b,c", "d", false)]
        [InlineData("notIn", "a,b,c", "d", true)]
        [InlineData("notIn", "a,b,c", "c", false)]
        [InlineData("greaterThan", "10", "10.5", true)]
        [InlineData("greaterThan", "10", "10", false)]
        [InlineData("lessThan", "10", "9.99", true)]
        [InlineData("lessThan", "10", "1e3", false)]
        [InlineData("greaterThan", "10", "lots", false)]
        [InlineData("lessThan", "10", "", false)]
        public void GuardOperators_EvaluateAsDocumented(string op, string guardValue, string actual, bool expected)
        {
            var guard = new GuardDef { Key = "k", Op = op, Value = guardValue };
            var context = new System.Collections.Generic.Dictionary<string, string> { ["k"] = actual };
            Assert.Equal(expected, StateMachineCore.EvaluateGuard(guard, context));
        }

        [Theory]
        [InlineData("exists", false)]
        [InlineData("notExists", true)]
        [InlineData("equals", false)]
        [InlineData("notEquals", false)]
        [InlineData("in", false)]
        [InlineData("notIn", false)]
        [InlineData("greaterThan", false)]
        [InlineData("lessThan", false)]
        public void GuardOperators_AMissingKeyFailsEveryComparison_ExceptNotExists(string op, bool expected)
        {
            var guard = new GuardDef { Key = "k", Op = op, Value = "1" };
            Assert.Equal(expected, StateMachineCore.EvaluateGuard(guard, new System.Collections.Generic.Dictionary<string, string>()));
        }

        [Fact]
        public void GuardOperator_ExistsPassesForAnEmptyStringValue()
        {
            var guard = new GuardDef { Key = "k", Op = "exists" };
            Assert.True(StateMachineCore.EvaluateGuard(guard, new System.Collections.Generic.Dictionary<string, string> { ["k"] = string.Empty }));
        }

        [Fact]
        public void MultipleGuardsOnOneTransition_AreAnded()
        {
            const string json = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B",
                    "guards": [ { "key": "x", "op": "equals", "value": "1" }, { "key": "y", "op": "equals", "value": "2" } ] } ] }
                """;
            StateMachineUtils machine = Started(json);
            Assert.True(machine.SetContext("x", "1", out _));
            Assert.False(Fire(machine, "go").fired);
            Assert.True(machine.SetContext("y", "2", out _));
            Assert.True(Fire(machine, "go").fired);
        }

        // ------------------------------------------------------------ misuse after dispose

        [Fact]
        public void AfterDispose_EveryCallFailsCleanlyInsteadOfThrowing()
        {
            StateMachineUtils machine = Started();
            machine.Dispose();
            Assert.False(machine.Fire("validate", out bool fired, out _, out _, out string message));
            Assert.False(fired);
            Assert.Contains("disposed", message);
            Assert.False(machine.Start(out _, out message));
            Assert.False(machine.SetContext("k", "v", out message));
            Assert.False(machine.GetHistoryJson(out _, out message));
            Assert.Equal(string.Empty, machine.CurrentState);
            Assert.False(machine.IsFinished);
        }

        // ---- the real Fire / FireSimple contract: True = the call worked; message empty = fired, text = the decline reason ----

        [Fact]
        public void Fire_WhenItFires_IsTrueWithNoMessage_AndTheNewState()
        {
            StateMachineUtils machine = Started();
            Assert.True(machine.Fire("validate", out string newState, out string message));
            Assert.Null(message);
            Assert.Equal("Validated", newState);
        }

        [Theory]
        [InlineData("nonsense", "NoTransition")]
        public void Fire_WhenDeclined_IsStillTrue_AndTheMessageIsTheReasonCode(string trigger, string reason)
        {
            StateMachineUtils machine = Started();
            Assert.True(machine.Fire(trigger, out string newState, out string message));
            Assert.Equal(reason, message);
            Assert.Equal("Received", newState); // still where it was
        }

        [Fact]
        public void Fire_EveryDeclineReason_ComesBackAsTheMessage()
        {
            StateMachineUtils unstarted = Loaded();
            Assert.True(unstarted.Fire("validate", out _, out string message));
            Assert.Equal("NotStarted", message);

            StateMachineUtils guarded = Started(GuardedDefinition);
            Assert.True(guarded.Fire("go", out _, out message));
            Assert.Equal("GuardFailed", message);

            StateMachineUtils finished = Started(Defs.Minimal);
            Assert.True(finished.Fire("go", out _, out message));
            Assert.Null(message);
            Assert.True(finished.Fire("go", out _, out message));
            Assert.Equal("Finished", message);
        }

        private const string GuardedDefinition = """
            { "initial": "A", "states": [ "A", "B" ], "transitions": [ { "from": "A", "trigger": "go", "to": "B", "guards": [ { "key": "k", "op": "equals", "value": "yes" } ] } ] }
            """;

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Fire_BadInput_IsFalse_AndTheMessageIsTheError(string trigger)
        {
            StateMachineUtils machine = Started();
            Assert.False(machine.Fire(trigger, out string newState, out string message));
            Assert.Null(newState);
            Assert.Contains("trigger", message);
            Assert.Equal("Received", machine.CurrentState); // unchanged
        }

        [Fact]
        public void Fire_WithNoDefinition_IsFalse_WithAnErrorMessage()
        {
            Assert.False(New().Fire("go", out _, out string message));
            Assert.Contains("No definition", message);
        }

        [Fact]
        public void FireSimple_HasTheSameContract_ResultTrueMessageEmptyMeansFired()
        {
            StateMachineUtils machine = Started();
            Assert.True(machine.FireSimple("nonsense", out string message));    // declined
            Assert.Equal("NoTransition", message);
            Assert.Equal("Received", machine.CurrentState);

            Assert.True(machine.FireSimple("validate", out message));           // fired
            Assert.Null(message);
            Assert.Equal("Validated", machine.CurrentState);

            Assert.False(machine.FireSimple("", out message));                  // error
            Assert.Contains("trigger", message);
        }

        [Fact]
        public void FireSimple_RaisesTheSameEventsAsFire_AndRecordsDeclinedTriggers()
        {
            StateMachineUtils machine = Started();
            var log = new System.Collections.Generic.List<string>();
            machine.StateEntered += (_, e) => log.Add("entered:" + e.NewState);
            machine.TransitionRejected += (_, e) => log.Add("rejected:" + e.Reason);
            Assert.True(machine.FireSimple("post", out string message));
            Assert.Equal("NoTransition", message);
            Assert.True(machine.FireSimple("validate", out message));
            Assert.Null(message);
            Assert.Equal(new[] { "rejected:NoTransition", "entered:Validated" }, log);
        }
    }
}
