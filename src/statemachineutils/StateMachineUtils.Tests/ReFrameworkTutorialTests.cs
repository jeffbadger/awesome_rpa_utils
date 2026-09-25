using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StateMachineAutomation.Tests
{
    /// <summary>
    /// Runs Documentation/UiPathReFramework.md. The definition is read out of the markdown, and the loop and
    /// handlers below mirror the ones the page shows, against an in-memory stand-in for the queue.
    /// </summary>
    public sealed class ReFrameworkTutorialTests : MachineTestBase
    {
        private const int MaxConsecutiveSystemExceptions = 3;

        private enum Outcome { Ok, Business, System }

        private sealed class Item { public string Id; public Queue<Outcome> Script; }

        private sealed class Robot
        {
            public readonly StateMachineUtils Machine;
            public readonly Queue<Item> Ready = new Queue<Item>();
            public readonly List<string> Completed = new List<string>();
            public readonly List<string> Rejected = new List<string>();
            public readonly List<string> Trace = new List<string>();   // state at the start of each turn
            public bool InitFails;
            public int InitRuns;
            public Item Current;

            public Robot(StateMachineUtils machine, params (string id, Outcome[] script)[] items)
            {
                Machine = machine;
                foreach ((string id, Outcome[] script) in items) Ready.Enqueue(new Item { Id = id, Script = new Queue<Outcome>(script) });
            }

            private void Fire(string trigger)
            {
                Assert.True(Machine.Fire(trigger, out _, out string message), message);
                Assert.True(message == null, "'" + trigger + "' was declined in " + Machine.CurrentState + ": " + message);
            }

            public void Turn()
            {
                Machine.GetCurrentState(out string state, out _);
                Trace.Add(state);
                if (state == "Init") RunInit();
                else if (state == "GetTransactionData") RunGetTransactionData();
                else if (state == "ProcessTransaction") RunProcessTransaction();
            }

            public void RunToEnd(int maxTurns = 200)
            {
                for (int i = 0; i < maxTurns && Machine.CurrentState != "EndProcess"; i++) Turn();
                Assert.Equal("EndProcess", Machine.CurrentState);
            }

            private void RunInit()
            {
                InitRuns++;
                if (!InitFails) Fire("initialized"); else Fire("systemException");
            }

            private void RunGetTransactionData()
            {
                bool found = Ready.Count > 0;
                Current = found ? Ready.Dequeue() : null;
                Assert.True(Machine.SetContext("transactionFound", found ? "true" : "false", out string message), message);
                Fire("fetched");
            }

            private void RunProcessTransaction()
            {
                Outcome outcome = Current.Script.Count > 0 ? Current.Script.Dequeue() : Outcome.Ok;
                if (outcome == Outcome.Ok)
                {
                    Completed.Add(Current.Id);
                    Machine.SetContext("consecutiveSystemExceptions", "0", out _);
                    Machine.SetContext("maxConsecutiveReached", "false", out _);
                    Fire("success");
                }
                else if (outcome == Outcome.Business)
                {
                    Rejected.Add(Current.Id);
                    Fire("businessException");
                }
                else
                {
                    Ready.Enqueue(Current);        // RetryItem with no delay: due again straight away (queue attempt limit not modelled)
                    OnSystemException();
                }
            }

            public void OnSystemException()
            {
                Machine.GetContext("consecutiveSystemExceptions", out _, out string text, out _);
                int.TryParse(text, out int count);
                count++;
                Machine.SetContext("consecutiveSystemExceptions", count.ToString(), out _);
                Machine.SetContext("maxConsecutiveReached", count >= MaxConsecutiveSystemExceptions ? "true" : "false", out _);
                Fire("systemException");
            }
        }

        private static string DocDefinition([CallerFilePath] string thisFile = "")
        {
            string doc = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", "Documentation", "UiPathReFramework.md"));
            Assert.True(File.Exists(doc), "expected the tutorial at " + doc);
            Match json = Regex.Match(File.ReadAllText(doc), "```json\\s*(.*?)```", RegexOptions.Singleline);
            Assert.True(json.Success, "no ```json block found");
            return json.Groups[1].Value;
        }

        private Robot NewRobot(params (string id, Outcome[] script)[] items)
        {
            StateMachineUtils machine = Loaded(DocDefinition());
            Assert.True(machine.SetContext("consecutiveSystemExceptions", "0", out string message), message);
            Assert.True(machine.SetContext("maxConsecutiveReached", "false", out message), message);
            Assert.True(machine.SetContext("transactionFound", "false", out message), message);
            Assert.True(machine.Start(out string state, out message), message);
            Assert.Equal("Init", state);
            return new Robot(machine, items);
        }

        [Fact]
        public void TheDocumentedDefinition_HasNoErrorsOrWarnings()
        {
            Assert.True(New().ValidateDefinitionJson(DocDefinition(), out string report, out string message), message);
            Assert.Equal("{\"valid\":true,\"errors\":[],\"warnings\":[],\"stateCount\":4,\"transitionCount\":10}", report);
        }

        [Fact]
        public void Step12_TheDocumentedRun_FollowsTheDocumentedTurns()
        {
            Robot robot = NewRobot(("A", new Outcome[0]), ("B", new[] { Outcome.Business }), ("C", new[] { Outcome.System }), ("D", new Outcome[0]));
            robot.RunToEnd();

            Assert.Equal(new[]
            {
                "Init", "GetTransactionData", "ProcessTransaction",   // 1-3: A
                "GetTransactionData", "ProcessTransaction",           // 4-5: B rejected
                "GetTransactionData", "ProcessTransaction",           // 6-7: C fails
                "Init", "GetTransactionData", "ProcessTransaction",   // 8-10: re-init, D
                "GetTransactionData", "ProcessTransaction",           // 11-12: C again, done
                "GetTransactionData"                                  // 13: nothing left
            }, robot.Trace);
            Assert.Equal(new[] { "A", "D", "C" }, robot.Completed); // C's retry queues behind D, as the real queue orders it
            Assert.Equal(new[] { "B" }, robot.Rejected);
            robot.Machine.GetContext("consecutiveSystemExceptions", out _, out string count, out _);
            Assert.Equal("0", count); // C's later success reset it
        }

        [Fact]
        public void BusinessExceptions_AreNeverCounted_SoAnyNumberOfThemStillFinishesNormally()
        {
            var items = new List<(string, Outcome[])>();
            for (int i = 0; i < 10; i++) items.Add(("B" + i, new[] { Outcome.Business }));
            Robot robot = NewRobot(items.ToArray());
            robot.RunToEnd();
            Assert.Equal(10, robot.Rejected.Count);
            Assert.Equal(1, robot.InitRuns); // never sent back to Init
            robot.Machine.GetContext("consecutiveSystemExceptions", out _, out string count, out _);
            Assert.Equal("0", count);
        }

        [Fact]
        public void ConsecutiveSystemExceptions_AtTheLimit_EndTheRunInsteadOfRetryingForever()
        {
            // one item that fails technically every time
            Robot robot = NewRobot(("X", new[] { Outcome.System, Outcome.System, Outcome.System, Outcome.System, Outcome.System }));
            robot.RunToEnd();
            Assert.Empty(robot.Completed);
            Assert.Equal(MaxConsecutiveSystemExceptions, robot.InitRuns); // Init, then two re-inits; the third failure ended the run
            Assert.Equal("EndProcess", robot.Machine.CurrentState);
            Assert.True(robot.Machine.IsFinished);
        }

        [Fact]
        public void TwoFailuresThenASuccess_ResetTheCount_SoALaterRunOfFailuresGetsAFreshAllowance()
        {
            // Four items fail once each in two pairs, with a success between the pairs. Never three in a row, so the run survives.
            Robot robot = NewRobot(
                ("A", new[] { Outcome.System }), ("B", new[] { Outcome.System }), ("C", new Outcome[0]),
                ("D", new[] { Outcome.System }), ("E", new[] { Outcome.System }), ("F", new Outcome[0]));
            robot.RunToEnd();
            robot.Completed.Sort(StringComparer.Ordinal);
            Assert.Equal(new[] { "A", "B", "C", "D", "E", "F" }, robot.Completed);
            Assert.Equal(5, robot.InitRuns); // Init once, then one re-init per failure
        }

        [Fact]
        public void AFailureInsideInit_EndsTheRun()
        {
            Robot robot = NewRobot(("A", new Outcome[0]));
            robot.InitFails = true;
            robot.RunToEnd();
            Assert.Equal(new[] { "Init" }, robot.Trace);
            Assert.Empty(robot.Completed);
        }

        [Fact]
        public void AnEmptyQueue_GoesInitThenGetThenEnd()
        {
            Robot robot = NewRobot();
            robot.RunToEnd();
            Assert.Equal(new[] { "Init", "GetTransactionData" }, robot.Trace);
        }

        [Fact]
        public void AMisorderedTrigger_IsDeclinedAndRecorded_NotAcceptedSilently()
        {
            Robot robot = NewRobot();
            Assert.True(robot.Machine.Fire("success", out _, out string reason));
            Assert.Equal("NoTransition", reason); // 'success' means nothing in Init
        }

        [Fact]
        public void Step13_ARobotThatDiedMidRun_ResumesAsASystemException_AndAnEndedRunStartsFresh()
        {
            string name = NewMachineName();

            StateMachineUtils first = New();
            Assert.True(first.LoadDefinitionJson(DocDefinition(), out string message), message);
            Assert.True(first.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.False(restored);
            Assert.True(first.SetContext("consecutiveSystemExceptions", "0", out message), message);
            Assert.True(first.SetContext("maxConsecutiveReached", "false", out message), message);
            Assert.True(first.SetContext("transactionFound", "false", out message), message);
            Assert.True(first.Start(out _, out message), message);
            var robot = new Robot(first, ("A", new Outcome[0]));
            robot.Turn(); robot.Turn();   // Init, GetTransactionData -> now in ProcessTransaction
            Assert.Equal("ProcessTransaction", first.CurrentState);
            first.Dispose();              // the crash

            StateMachineUtils second = New();
            Assert.True(second.LoadDefinitionJson(DocDefinition(), out message), message);
            Assert.True(second.EnablePersistence(name, out _, out restored, out message), message);
            Assert.True(restored);
            Assert.True(second.IsStarted(out bool started, out _));
            Assert.True(started);
            Assert.Equal("ProcessTransaction", second.CurrentState);

            var resumed = new Robot(second, ("A", new Outcome[0]));
            resumed.OnSystemException();  // "a crash is a system exception"
            Assert.Equal("Init", second.CurrentState);
            second.GetContext("consecutiveSystemExceptions", out _, out string count, out _);
            Assert.Equal("1", count);
            resumed.RunToEnd();
            Assert.Equal(new[] { "A" }, resumed.Completed);

            // the run has ended; the next launch restores EndProcess and must Reset to begin a new one
            second.Dispose();
            StateMachineUtils third = New();
            Assert.True(third.LoadDefinitionJson(DocDefinition(), out message), message);
            Assert.True(third.EnablePersistence(name, out _, out restored, out message), message);
            Assert.True(restored);
            Assert.Equal("EndProcess", third.CurrentState);
            Assert.True(third.Reset(true, out string state, out message), message);
            Assert.Equal("Init", state);
        }

        [Fact]
        public void TheOptionalStopTransition_WorksFromEveryNonFinalState()
        {
            string withStop = DocDefinition().Replace("\"transitions\": [",
                "\"transitions\": [\n    { \"from\": \"*\", \"trigger\": \"stop\", \"to\": \"EndProcess\" },");
            foreach (string[] path in new[] { new string[0], new[] { "initialized" } })
            {
                StateMachineUtils machine = Started(withStop);
                foreach (string t in path) Assert.True(machine.Fire(t, out _, out _));
                Assert.True(machine.Fire("stop", out string state, out string message));
                Assert.Null(message);
                Assert.Equal("EndProcess", state);
            }
        }
    }
}
