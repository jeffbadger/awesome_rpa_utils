using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace StateMachineAutomation.Tests
{
    public sealed class DefinitionTests : MachineTestBase
    {
        private static DefinitionReport Parse(string json) => StateMachineCore.ParseDefinition(json);

        private static void AssertError(string json, string expectedFragment)
        {
            DefinitionReport report = Parse(json);
            Assert.False(report.Valid, "expected the definition to be rejected");
            Assert.Contains(report.Errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ValidDefinition_ParsesWithNoErrors()
        {
            DefinitionReport report = Parse(Defs.Invoice);
            Assert.True(report.Valid, string.Join("; ", report.Errors));
            Assert.Equal("InvoiceFlow", report.Definition.Name);
            Assert.Equal(4, report.Definition.States.Count);
            Assert.Equal(4, report.Definition.Transitions.Count);
        }

        [Fact]
        public void StringShorthandStates_CommentsAndTrailingCommas_AreAccepted()
        {
            const string json = """
                {
                  // a comment
                  "initial": "A",
                  "states": [ "A", "B", ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B" }, ]
                }
                """;
            DefinitionReport report = Parse(json);
            Assert.True(report.Valid, string.Join("; ", report.Errors));
        }

        [Theory]
        [InlineData("", "required")]
        [InlineData("   ", "required")]
        [InlineData("{ not json", "not valid JSON")]
        [InlineData("[1,2]", "JSON object")]
        public void MalformedInput_IsRejected(string json, string fragment) => AssertError(json, fragment);

        [Fact]
        public void MissingStatesArray_IsRejected() => AssertError("""{ "initial": "A" }""", "'states' array");

        [Fact]
        public void MissingInitial_IsRejected() => AssertError("""{ "states": ["A"] }""", "missing 'initial'");

        [Fact]
        public void UnknownInitial_IsRejected() => AssertError("""{ "initial": "Z", "states": ["A"] }""", "not a declared state");

        [Fact]
        public void FinalInitialState_IsRejected() => AssertError("""{ "initial": "A", "states": [ { "name": "A", "final": true } ] }""", "cannot be a final state");

        [Fact]
        public void DuplicateState_IsRejected() => AssertError("""{ "initial": "A", "states": ["A", "A"] }""", "Duplicate state");

        [Fact]
        public void StatesDifferingOnlyByCase_AreRejectedAsAmbiguous() =>
            AssertError("""{ "initial": "A", "states": ["Approved", "approved"] }""", "differ only by case");

        [Fact]
        public void WildcardStateName_IsRejected() => AssertError("""{ "initial": "A", "states": ["A", "*"] }""", "reserved");

        [Fact]
        public void EmptyStateName_IsRejected() => AssertError("""{ "initial": "A", "states": ["A", "  "] }""", "is empty");

        [Fact]
        public void TooLongStateName_IsRejected() =>
            AssertError("{ \"initial\": \"A\", \"states\": [\"A\", \"" + new string('x', 129) + "\"] }", "longer than 128");

        [Fact]
        public void TransitionToUnknownState_IsRejected() =>
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "Nope" } ] }""", "not a declared state");

        [Fact]
        public void TransitionFromUnknownState_IsRejected() =>
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "Nope", "trigger": "t", "to": "A" } ] }""", "not a declared state");

        [Fact]
        public void TransitionOutOfFinalState_IsRejected() =>
            AssertError("""{ "initial": "A", "states": ["A", { "name": "B", "final": true }], "transitions": [ { "from": "B", "trigger": "t", "to": "A" } ] }""", "final state");

        [Fact]
        public void WildcardTrigger_IsRejected() =>
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "*", "to": "A" } ] }""", "only valid as a 'from'");

        [Fact]
        public void WildcardDestination_IsRejected() =>
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "*" } ] }""", "not a declared state");

        [Fact]
        public void UnknownGuardOperator_IsRejectedAndListsTheValidOnes() =>
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "A", "guards": [ { "key": "k", "op": "contains", "value": "x" } ] } ] }""", "unknown operator 'contains'");

        [Fact]
        public void NumericGuardWithNonNumericValue_IsRejected() =>
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "A", "guards": [ { "key": "k", "op": "greaterThan", "value": "lots" } ] } ] }""", "needs a numeric value");

        [Fact]
        public void ComparisonGuardWithoutValue_IsRejected() =>
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "A", "guards": [ { "key": "k", "op": "equals" } ] } ] }""", "requires a value");

        [Fact]
        public void ExistsGuard_NeedsNoValue()
        {
            DefinitionReport report = Parse("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "A", "guards": [ { "key": "k", "op": "exists" } ] } ] }""");
            Assert.True(report.Valid, string.Join("; ", report.Errors));
        }

        [Theory]
        [InlineData("exists", "yes")]
        [InlineData("notExists", "yes")]
        [InlineData("exists", "")]
        public void ExistsAndNotExistsGuards_RejectAValue_RatherThanSilentlyIgnoringIt(string op, string value)
        {
            string json = "{ \"initial\": \"A\", \"states\": [\"A\"], \"transitions\": [ { \"from\": \"A\", \"trigger\": \"t\", \"to\": \"A\", \"guards\": [ { \"key\": \"k\", \"op\": \"" + op + "\", \"value\": \"" + value + "\" } ] } ] }";
            AssertError(json, "takes no value");
        }

        [Theory]
        [InlineData("EU,,UK")]
        [InlineData("EU,")]
        [InlineData(",EU")]
        [InlineData(",")]
        [InlineData("")]
        [InlineData("EU, ,UK")]
        public void InAndNotInGuards_RejectAnEmptyListItem(string list)
        {
            foreach (string op in new[] { "in", "notIn" })
            {
                string json = "{ \"initial\": \"A\", \"states\": [\"A\"], \"transitions\": [ { \"from\": \"A\", \"trigger\": \"t\", \"to\": \"A\", \"guards\": [ { \"key\": \"k\", \"op\": \"" + op + "\", \"value\": \"" + list + "\" } ] } ] }";
                AssertError(json, "empty item");
            }
        }

        [Fact]
        public void InGuards_AcceptWellFormedLists_IncludingSpacesAndASingleItem()
        {
            foreach (string list in new[] { "EU", "EU, UK", " EU , UK , US " })
            {
                string json = "{ \"initial\": \"A\", \"states\": [\"A\"], \"transitions\": [ { \"from\": \"A\", \"trigger\": \"t\", \"to\": \"A\", \"guards\": [ { \"key\": \"k\", \"op\": \"in\", \"value\": \"" + list + "\" } ] } ] }";
                DefinitionReport report = Parse(json);
                Assert.True(report.Valid, list + ": " + string.Join("; ", report.Errors));
            }
        }

        [Fact]
        public void MethodApi_AppliesTheSameGuardRules()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.AddState("A", out string message), message);
            Assert.False(machine.AddTransition("A", "t", "A", out message, "k", "exists", "oops"));
            Assert.Contains("takes no value", message);
            Assert.False(machine.AddTransition("A", "t", "A", out message, "k", "in", "EU,,UK"));
            Assert.Contains("empty item", message);
            Assert.True(machine.AddTransition("A", "t", "A", out message, "k", "in", "EU, UK"), message);
            Assert.True(machine.AddTransition("A", "t2", "A", out message, "k", "exists"), message);
        }

        [Fact]
        public void GuardValues_AcceptNumbersAndBooleans()
        {
            DefinitionReport report = Parse("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "A", "guards": [ { "key": "n", "op": "greaterThan", "value": 5 }, { "key": "b", "op": "equals", "value": true } ] } ] }""");
            Assert.True(report.Valid, string.Join("; ", report.Errors));
            Assert.Equal("5", report.Definition.Transitions[0].Guards[0].Value);
            Assert.Equal("true", report.Definition.Transitions[0].Guards[1].Value);
        }

        [Fact]
        public void GuardOperators_AreCaseInsensitiveAndNormalized()
        {
            DefinitionReport report = Parse("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "A", "guards": [ { "key": "k", "op": "LESSTHAN", "value": "3" } ] } ] }""");
            Assert.True(report.Valid, string.Join("; ", report.Errors));
            Assert.Equal("lessThan", report.Definition.Transitions[0].Guards[0].Op);
        }

        [Theory]
        [InlineData("""{ "initial": "A", "states": ["A"], "trigers": [] }""", "Unknown property 'trigers'")]
        [InlineData("""{ "initial": "A", "states": [ { "name": "A", "finel": true } ] }""", "Unknown property 'finel'")]
        [InlineData("""{ "initial": "A", "states": ["A"], "transitions": [ { "from": "A", "trigger": "t", "to": "A", "gaurds": [] } ] }""", "Unknown property 'gaurds'")]
        public void MisspelledProperties_AreRejectedRatherThanIgnored(string json, string fragment) => AssertError(json, fragment);

        [Theory]
        [InlineData("""{ "initial": "A", "initial": "B", "states": ["A", "B"] }""", "'initial' appears more than once")]
        [InlineData("""{ "initial": "A", "states": [ { "name": "A", "name": "Z" } ] }""", "'name' appears more than once")]
        [InlineData("""{ "initial": "A", "states": ["A"], "INITIAL": "A" }""", "appears more than once")]
        public void RepeatedProperties_AreRejectedRatherThanSilentlyPickingOne(string json, string fragment) => AssertError(json, fragment);

        [Fact]
        public void AnOversizedDefinition_IsRejectedBeforeItIsParsed()
        {
            string huge = "{ \"initial\": \"A\", \"states\": [\"A\"], \"name\": \"" + new string('x', StateMachineCore.MaxDefinitionChars) + "\" }";
            AssertError(huge, "longer than");
        }

        [Fact]
        public void WrongTypes_AreReportedWithoutThrowing()
        {
            AssertError("""{ "initial": 5, "states": ["A"] }""", "must be a string");
            AssertError("""{ "initial": "A", "states": [5] }""", "must be a string or an object");
            AssertError("""{ "initial": "A", "states": [ { "name": "A", "final": "yes" } ] }""", "'final' must be true or false");
            AssertError("""{ "initial": "A", "states": [ { "name": "A", "final": null } ] }""", "'final' must be true or false");
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": {} }""", "must be an array");
            AssertError("""{ "initial": "A", "states": ["A"], "transitions": [ 3 ] }""", "must be an object");
        }

        [Fact]
        public void TooManyStates_IsRejected()
        {
            var sb = new StringBuilder("{ \"initial\": \"S0\", \"states\": [");
            for (int i = 0; i <= MachineDefinition.MaxStates; i++) sb.Append(i == 0 ? "" : ",").Append("\"S").Append(i).Append('"');
            sb.Append("] }");
            AssertError(sb.ToString(), "at most 500 states");
        }

        [Fact]
        public void TooManyGuardsOnOneTransition_IsRejected()
        {
            string guards = string.Join(",", Enumerable.Range(0, 21).Select(i => "{ \"key\": \"k" + i + "\", \"op\": \"exists\" }"));
            AssertError("{ \"initial\": \"A\", \"states\": [\"A\"], \"transitions\": [ { \"from\": \"A\", \"trigger\": \"t\", \"to\": \"A\", \"guards\": [" + guards + "] } ] }", "more than 20 guards");
        }

        [Fact]
        public void Warnings_ReportUnreachableDeadEndAndShadowedTransitions()
        {
            const string json = """
                {
                  "initial": "A",
                  "states": [ "A", "B", "Orphan", { "name": "Done", "final": true } ],
                  "transitions": [
                    { "from": "A", "trigger": "go", "to": "B" },
                    { "from": "A", "trigger": "go", "to": "Done" }
                  ]
                }
                """;
            DefinitionReport report = Parse(json);
            Assert.True(report.Valid, string.Join("; ", report.Errors));
            Assert.Contains(report.Warnings, w => w.Contains("'Orphan' is unreachable"));
            Assert.Contains(report.Warnings, w => w.Contains("'B' is not final but has no outgoing"));
            Assert.Contains(report.Warnings, w => w.Contains("Transition #2") && w.Contains("can never fire"));
        }

        [Fact]
        public void WildcardTransition_CountsAsAnExitAndShadowsLaterSameTrigger()
        {
            const string json = """
                {
                  "initial": "A",
                  "states": [ "A", "B", { "name": "End", "final": true } ],
                  "transitions": [
                    { "from": "*", "trigger": "stop", "to": "End" },
                    { "from": "A", "trigger": "stop", "to": "B" },
                    { "from": "A", "trigger": "go", "to": "B" }
                  ]
                }
                """;
            DefinitionReport report = Parse(json);
            Assert.True(report.Valid, string.Join("; ", report.Errors));
            Assert.DoesNotContain(report.Warnings, w => w.Contains("no outgoing"));
            Assert.Contains(report.Warnings, w => w.Contains("Transition #2") && w.Contains("can never fire"));
        }

        [Fact]
        public void ValidateDefinitionJson_ReportsInvalidDefinitionsInsideTheJson()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.ValidateDefinitionJson("""{ "initial": "Z", "states": ["A"] }""", out string report, out string message), message);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.False(doc.RootElement.GetProperty("valid").GetBoolean());
            Assert.NotEmpty(doc.RootElement.GetProperty("errors").EnumerateArray());
        }

        [Fact]
        public void ValidateDefinitionJson_ReportsCountsAndWarningsForAValidDefinition()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.ValidateDefinitionJson(Defs.Invoice, out string report, out string message), message);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.True(doc.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal(4, doc.RootElement.GetProperty("stateCount").GetInt32());
            Assert.Equal(4, doc.RootElement.GetProperty("transitionCount").GetInt32());
        }

        [Fact]
        public void ValidateDefinitionJson_DoesNotChangeTheLoadedDefinition()
        {
            StateMachineUtils machine = Loaded(Defs.Minimal);
            Assert.True(machine.ValidateDefinitionJson(Defs.Invoice, out _, out string message), message);
            Assert.True(machine.GetDefinitionJson(out string json, out message), message);
            Assert.DoesNotContain("InvoiceFlow", json);
        }

        [Fact]
        public void ValidateDefinitionJson_RequiresInput()
        {
            StateMachineUtils machine = New();
            Assert.False(machine.ValidateDefinitionJson(null, out string report, out string message));
            Assert.Null(report);
            Assert.Contains("required", message);
        }

        [Fact]
        public void LoadDefinitionJson_InvalidDefinition_LeavesThePreviousOneInForce()
        {
            StateMachineUtils machine = Loaded(Defs.Invoice);
            Assert.False(machine.LoadDefinitionJson("""{ "initial": "Z", "states": ["A"] }""", out string message));
            Assert.Contains("not valid", message);
            Assert.Equal("InvoiceFlow", machine.MachineName);
            Assert.True(machine.Start(out string state, out message), message);
            Assert.Equal("Received", state);
        }

        [Fact]
        public void LoadDefinitionJson_WhileRunning_StopsTheMachineAndKeepsContext()
        {
            StateMachineUtils machine = Started();
            Assert.True(machine.SetContext("amount", "5", out _));
            Assert.True(machine.LoadDefinitionJson(Defs.Minimal, out string message), message);
            Assert.False(machine.IsFinished);
            Assert.Equal(string.Empty, machine.CurrentState);
            Assert.True(machine.IsStarted(out bool started, out _));
            Assert.False(started);
            Assert.True(machine.GetContext("amount", out bool exists, out string value, out _));
            Assert.True(exists);
            Assert.Equal("5", value);
        }

        [Fact]
        public void GetDefinitionJson_RoundTripsThroughLoad()
        {
            StateMachineUtils first = Loaded(Defs.Invoice);
            Assert.True(first.GetDefinitionJson(out string json, out string message), message);
            StateMachineUtils second = Loaded(json);
            Assert.True(second.GetDefinitionJson(out string again, out message), message);
            Assert.Equal(json, again);
        }

        [Fact]
        public void DefinitionHash_IgnoresWhitespaceButNotContent()
        {
            string a = Parse(Defs.Invoice).Definition.ComputeHash();
            string b = Parse(Defs.Invoice.Replace("\n", " ").Replace("  ", " ")).Definition.ComputeHash();
            string c = Parse(Defs.Invoice.Replace("10000", "20000")).Definition.ComputeHash();
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void MethodApi_BuildsTheSameMachineAsJson()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.AddState("Received", out string message), message);
            Assert.True(machine.AddState("Validated", out message), message);
            Assert.True(machine.AddState("Posted", out message, isFinal: true), message);
            Assert.True(machine.SetInitialState("received", out message), message);
            Assert.True(machine.AddTransition("Received", "validate", "Validated", out message), message);
            Assert.True(machine.AddTransition("validated", "post", "posted", out message, "amount", "lessThan", "100"), message);
            Assert.True(machine.Start(out string state, out message), message);
            Assert.Equal("Received", state);
            Assert.True(machine.GetDefinitionJson(out string json, out message), message);
            Assert.Contains("\"lessThan\"", json);
            Assert.Contains("\"Posted\"", json);
        }

        [Fact]
        public void MethodApi_RejectsBadInputWithMessages()
        {
            StateMachineUtils machine = New();
            Assert.False(machine.AddState("", out string message));
            Assert.Contains("empty", message);
            Assert.False(machine.AddState("*", out message));
            Assert.Contains("reserved", message);
            Assert.True(machine.AddState("A", out message), message);
            Assert.False(machine.AddState("a", out message));
            Assert.Contains("already exists", message);
            Assert.False(machine.SetInitialState("Nope", out message));
            Assert.Contains("AddState first", message);
            Assert.False(machine.AddTransition("A", "t", "Nope", out message));
            Assert.Contains("not a declared state", message);
            Assert.False(machine.AddTransition("Nope", "t", "A", out message));
            Assert.False(machine.AddTransition("A", "t", "A", out message, "k", null, null));
            Assert.Contains("both guardKey and guardOp", message);
            Assert.False(machine.AddTransition("A", "t", "A", out message, "k", "wobbly", "1"));
            Assert.Contains("unknown operator", message);
            Assert.False(machine.AddTransition("A", "t", "A", out message, "k", "greaterThan", "abc"));
            Assert.Contains("numeric", message);
        }

        [Fact]
        public void MethodApi_FinalStateAsInitial_IsRejected()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.AddState("End", out string message, isFinal: true), message);
            Assert.False(machine.SetInitialState("End", out message));
            Assert.Contains("final", message);
        }

        [Fact]
        public void MethodApi_IncompleteDefinition_FailsStartWithAllTheProblems()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.AddState("A", out _));
            Assert.False(machine.Start(out string state, out string message));
            Assert.Null(state);
            Assert.Contains("initial state is required", message);
        }

        [Fact]
        public void EditingTheDefinitionWhileRunning_IsRefusedExceptByClearOrLoad()
        {
            StateMachineUtils machine = Started();
            Assert.False(machine.AddState("Extra", out string message));
            Assert.Contains("running", message);
            Assert.False(machine.SetInitialState("Validated", out message));
            Assert.False(machine.AddTransition("Received", "x", "Validated", out message));
            Assert.True(machine.ClearDefinition(out message), message);
            Assert.True(machine.AddState("Extra", out message), message);
        }
    }
}
