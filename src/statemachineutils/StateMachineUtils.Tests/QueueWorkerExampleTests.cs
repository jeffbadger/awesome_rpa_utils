using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace StateMachineAutomation.Tests
{
    /// <summary>
    /// Runs the worked example in Documentation/WorkingAQueue.md. The definition is read straight out of
    /// the markdown, so the documented example cannot drift out of validity, and the loop below mirrors
    /// the one the page shows, driven against an in-memory stand-in for the queue.
    /// </summary>
    public sealed class QueueWorkerExampleTests : MachineTestBase
    {
        private enum Outcome { Ok, BadData, Technical }

        private sealed class Job
        {
            public string Id;
            public Queue<Outcome> Attempts;
        }

        /// <summary>The bits of LocalQueueUtils the worker uses: take, delayed retries, counts.</summary>
        private sealed class FakeQueue
        {
            public readonly Queue<Job> Ready = new Queue<Job>();
            public readonly List<Job> Delayed = new List<Job>();
            public readonly List<string> Completed = new List<string>();
            public readonly List<string> Rejected = new List<string>();
            public int Processed;

            public FakeQueue(params (string id, Outcome[] attempts)[] jobs)
            {
                foreach ((string id, Outcome[] attempts) in jobs) Ready.Enqueue(new Job { Id = id, Attempts = new Queue<Outcome>(attempts) });
            }

            public void TimePasses() { foreach (Job job in Delayed) Ready.Enqueue(job); Delayed.Clear(); }
        }

        private static string DocDefinition([CallerFilePath] string thisFile = "")
        {
            string doc = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", "Documentation", "WorkingAQueue.md"));
            Assert.True(File.Exists(doc), "expected the worked example at " + doc);
            Match json = Regex.Match(File.ReadAllText(doc), "```json\\s*(.*?)```", RegexOptions.Singleline);
            Assert.True(json.Success, "no ```json block found in WorkingAQueue.md");
            return json.Groups[1].Value;
        }

        private StateMachineUtils NewWorker()
        {
            StateMachineUtils machine = Loaded(DocDefinition());
            Assert.True(machine.SetContext("consecutiveFailures", "0", out string message), message);
            Assert.True(machine.Start(out _, out message), message);
            return machine;
        }

        private static string State(StateMachineUtils machine)
        {
            Assert.True(machine.GetCurrentState(out string state, out string message), message);
            return state;
        }

        private static void Fire(StateMachineUtils machine, string trigger)
        {
            Assert.True(machine.Fire(trigger, out bool fired, out _, out string reason, out string message), message);
            Assert.True(fired, "'" + trigger + "' was declined in " + machine.CurrentState + ": " + reason);
        }

        // ---- the loop from the documentation, one turn at a time ----

        private static Job current;

        private static void Turn(StateMachineUtils machine, FakeQueue queue)
        {
            string state = State(machine);
            if (state == "Starting") Fire(machine, "opened");
            else if (state == "Idle") Poll(machine, queue);
            else if (state == "Waiting") { queue.TimePasses(); Fire(machine, "waitElapsed"); }
            else if (state == "Processing") ProcessCurrent(machine, queue);
        }

        private static void Poll(StateMachineUtils machine, FakeQueue queue)
        {
            bool available = queue.Ready.Count > 0;
            current = available ? queue.Ready.Dequeue() : null;
            Assert.True(machine.SetContext("itemAvailable", available ? "true" : "false", out string message), message);
            Assert.True(machine.SetContext("itemId", current?.Id ?? "", out message), message);
            Assert.True(machine.SetContext("delayedCount", queue.Delayed.Count.ToString(), out message), message);
            Fire(machine, "polled");
        }

        private static void ProcessCurrent(StateMachineUtils machine, FakeQueue queue)
        {
            queue.Processed++;
            Outcome outcome = current.Attempts.Count > 0 ? current.Attempts.Dequeue() : Outcome.Ok;
            if (outcome == Outcome.Ok)
            {
                queue.Completed.Add(current.Id);
                Assert.True(machine.SetContext("consecutiveFailures", "0", out string m1), m1);
                Fire(machine, "succeeded");
            }
            else if (outcome == Outcome.BadData)
            {
                queue.Rejected.Add(current.Id);
                Fire(machine, "rejected");
            }
            else
            {
                queue.Delayed.Add(current);
                Assert.True(machine.GetContext("consecutiveFailures", out _, out string count, out string m2), m2);
                int.TryParse(count, out int failures);
                Assert.True(machine.SetContext("consecutiveFailures", (failures + 1).ToString(), out m2), m2);
                Fire(machine, "failed");
            }
        }

        private static void RunToEnd(StateMachineUtils machine, FakeQueue queue)
        {
            for (int turns = 0; turns < 500 && !machine.IsFinished; turns++) Turn(machine, queue);
            Assert.True(machine.IsFinished, "the worker did not reach a final state within 500 turns");
        }

        private static List<string> TransitionPath(StateMachineUtils machine)
        {
            Assert.True(machine.GetHistoryJson(out string json, out string message, 1000), message);
            return JsonDocument.Parse(json).RootElement.EnumerateArray()
                .Where(e => e.GetProperty("kind").GetString() == "transition")
                .Select(e => e.GetProperty("from").GetString() + ">" + e.GetProperty("to").GetString() + ":" + e.GetProperty("trigger").GetString())
                .ToList();
        }

        // ---- tests ----

        [Fact]
        public void TheDocumentedDefinition_IsValid_WithNoWarnings()
        {
            DefinitionReport report = StateMachineCore.ParseDefinition(DocDefinition());
            Assert.True(report.Valid, string.Join("; ", report.Errors));
            Assert.Empty(report.Warnings); // the page states that this definition produces none
        }

        [Fact]
        public void TheRunTableInTheDocumentation_IsWhatActuallyHappens()
        {
            StateMachineUtils machine = NewWorker();
            var queue = new FakeQueue(
                ("A", new[] { Outcome.Ok }),
                ("B", new[] { Outcome.BadData }),
                ("C", new[] { Outcome.Technical, Outcome.Ok }),
                ("D", new[] { Outcome.Ok }));

            RunToEnd(machine, queue);

            Assert.Equal("Finished", machine.CurrentState);
            Assert.Equal(new[] { "A", "D", "C" }, queue.Completed);
            Assert.Equal(new[] { "B" }, queue.Rejected);
            Assert.Equal(new[]
            {
                "Starting>Idle:opened",
                "Idle>Processing:polled",       // A
                "Processing>Idle:succeeded",
                "Idle>Processing:polled",       // B
                "Processing>Idle:rejected",
                "Idle>Processing:polled",       // C, fails technically
                "Processing>Idle:failed",
                "Idle>Processing:polled",       // D
                "Processing>Idle:succeeded",
                "Idle>Waiting:polled",          // nothing ready, C is delayed
                "Waiting>Idle:waitElapsed",
                "Idle>Processing:polled",       // C's retry
                "Processing>Idle:succeeded",
                "Idle>Finished:polled"
            }, TransitionPath(machine));
        }

        [Fact]
        public void FiveConsecutiveFailures_TripTheBreaker_AndAnInterveningSuccessResetsTheCount()
        {
            StateMachineUtils machine = NewWorker();
            var jobs = new List<(string, Outcome[])>
            {
                ("f1", new[] { Outcome.Technical }), ("f2", new[] { Outcome.Technical }),
                ("ok", new[] { Outcome.Ok }),
                ("g1", new[] { Outcome.Technical }), ("g2", new[] { Outcome.Technical }), ("g3", new[] { Outcome.Technical }),
                ("g4", new[] { Outcome.Technical }), ("g5", new[] { Outcome.Technical }),
                ("never-reached", new[] { Outcome.Ok })
            };
            var queue = new FakeQueue(jobs.ToArray());

            RunToEnd(machine, queue);

            Assert.Equal("Suspended", machine.CurrentState);
            Assert.Equal(new[] { "ok" }, queue.Completed);
            Assert.Equal(8, queue.Processed); // f1 f2 ok g1..g5: the breaker stopped it before "never-reached"
            Assert.True(machine.GetContext("consecutiveFailures", out _, out string count, out _));
            Assert.Equal("5", count);
        }

        [Fact]
        public void TwoFailuresThenSuccess_NeverTripsTheBreaker()
        {
            StateMachineUtils machine = NewWorker();
            var queue = new FakeQueue(("a", new[] { Outcome.Technical, Outcome.Technical, Outcome.Ok }));
            RunToEnd(machine, queue);
            Assert.Equal("Finished", machine.CurrentState);
            Assert.Equal(new[] { "a" }, queue.Completed);
        }

        [Fact]
        public void AnOperatorCanAbortFromAnyState_AndTheMachineThenStaysStopped()
        {
            StateMachineUtils machine = NewWorker();
            var queue = new FakeQueue(("a", new[] { Outcome.Ok }));
            Turn(machine, queue); // Starting -> Idle
            Turn(machine, queue); // Idle -> Processing
            Assert.Equal("Processing", machine.CurrentState);

            Fire(machine, "abort");

            Assert.Equal("Stopped", machine.CurrentState);
            Assert.True(machine.IsFinished);
            Assert.True(machine.Fire("polled", out bool fired, out _, out string reason, out _));
            Assert.False(fired);
            Assert.Equal("Finished", reason);
        }

        [Fact]
        public void ARunThatCrashedMidItem_ResumesAsProcessing_ThenRecoversToIdle()
        {
            string name = NewMachineName();
            StateMachineUtils first = Loaded(DocDefinition());
            Assert.True(first.EnablePersistence(name, out _, out _, out string message), message);
            Assert.True(first.SetContext("consecutiveFailures", "0", out message), message);
            Assert.True(first.Start(out _, out message), message);
            var queue = new FakeQueue(("a", new[] { Outcome.Ok }));
            Turn(first, queue); // Starting -> Idle
            Turn(first, queue); // Idle -> Processing: the item is taken...
            Assert.Equal("Processing", first.CurrentState);
            first.Dispose();    // ...and the robot dies before reporting it

            StateMachineUtils second = Loaded(DocDefinition());
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.True(restored);
            Assert.True(second.IsStarted(out bool started, out _));
            Assert.True(started); // the documented setup takes this branch instead of calling Start

            Assert.Equal("Processing", second.CurrentState);
            Assert.True(second.GetContext("itemId", out bool hasItem, out string itemId, out _));
            Assert.True(hasItem);
            Assert.Equal("a", itemId);

            Fire(second, "recovered");
            Assert.Equal("Idle", second.CurrentState);
        }

        [Fact]
        public void TheFailureCountSurvivesACrash_SoTheBreakerStillTrips()
        {
            string name = NewMachineName();
            StateMachineUtils first = Loaded(DocDefinition());
            Assert.True(first.EnablePersistence(name, out _, out _, out string message), message);
            Assert.True(first.SetContext("consecutiveFailures", "0", out message), message);
            Assert.True(first.Start(out _, out message), message);
            var queue = new FakeQueue(
                ("1", new[] { Outcome.Technical }), ("2", new[] { Outcome.Technical }), ("3", new[] { Outcome.Technical }),
                ("4", new[] { Outcome.Technical }), ("5", new[] { Outcome.Technical }));
            for (int i = 0; i < 1 + 3 * 2; i++) Turn(first, queue); // opened, then three items each polled (1 turn) and failed (1 turn)
            Assert.True(first.GetContext("consecutiveFailures", out _, out string before, out _));
            Assert.Equal("3", before);
            first.Dispose();

            StateMachineUtils second = Loaded(DocDefinition());
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.True(restored);
            RunToEnd(second, queue);

            Assert.Equal("Suspended", second.CurrentState);
            Assert.Equal(5, queue.Processed);
        }
    }
}
