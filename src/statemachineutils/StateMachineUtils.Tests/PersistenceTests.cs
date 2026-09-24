using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace StateMachineAutomation.Tests
{
    public sealed class PersistenceTests : MachineTestBase
    {
        private StateMachineUtils LoadedWithPersistence(string machineName, string json = Defs.Invoice)
        {
            StateMachineUtils machine = Loaded(json);
            Assert.True(machine.EnablePersistence(machineName, out _, out _, out string message), message);
            return machine;
        }

        private static string StatePath(string machineName) => Path.Combine(StateMachineCore.BasePath, machineName, "state.json");

        // Makes the next durable write fail: Windows refuses to replace a file another handle holds
        // open, Unix refuses to create the temp file in a directory without write permission.
        private static IDisposable BlockWrites(string machineName)
        {
            string folder = Path.Combine(StateMachineCore.BasePath, machineName);
            if (OperatingSystem.IsWindows())
                return new FileStream(StatePath(machineName), FileMode.Open, FileAccess.Read, FileShare.None);
            return new Unblocker(folder, blockNow: true);
        }

        private sealed class Unblocker : IDisposable
        {
            private readonly string folder;

            public Unblocker(string folder, bool blockNow)
            {
                this.folder = folder;
                if (blockNow && !OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            public void Dispose()
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        [Fact]
        public void Enable_WritesAnInitialSnapshotAndReturnsThePath()
        {
            string name = NewMachineName();
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out string path, out bool restored, out string message), message);
            Assert.False(restored);
            Assert.Equal(StatePath(name), path);
            Assert.True(File.Exists(path));
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(doc.RootElement.GetProperty("started").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        }

        [Fact]
        public void EveryChange_IsWrittenToDisk()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.True(machine.Start(out _, out _));
            Assert.True(machine.SetContext("amount", "7", out _));
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name)));
            Assert.True(doc.RootElement.GetProperty("started").GetBoolean());
            Assert.Equal("Validated", doc.RootElement.GetProperty("currentState").GetString());
            Assert.Equal("7", doc.RootElement.GetProperty("context").GetProperty("amount").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("history").GetArrayLength());
        }

        [Fact]
        public void AFlowResumesAfterTheComponentIsDisposed()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            Assert.True(first.Fire("validate", out _, out _, out _, out _));
            Assert.True(first.SetContext("amount", "42", out _));
            first.Dispose();

            StateMachineUtils second = Loaded();
            int raised = 0;
            second.StateEntered += (_, _) => raised++;
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.Equal(0, raised); // restoring is silent: nothing changed, the flow is only being picked up again
            Assert.Equal("Validated", second.CurrentState);
            Assert.True(second.IsStarted(out bool started, out _));
            Assert.True(started);
            Assert.True(second.GetContext("amount", out bool exists, out string value, out _));
            Assert.True(exists);
            Assert.Equal("42", value);
            Assert.True(second.GetHistoryJson(out string history, out _));
            Assert.Equal(2, JsonDocument.Parse(history).RootElement.GetArrayLength());

            Assert.True(second.Fire("post", out bool fired, out string newState, out _, out message), message);
            Assert.True(fired);
            Assert.Equal("Posted", newState);
        }

        [Fact]
        public void ARestoredMachine_ReportsTimeInStateFromTheSavedEntryTime()
        {
            string name = NewMachineName();
            DateTime t0 = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
            StateMachineUtils first = Loaded();
            first.Clock = () => t0;
            Assert.True(first.EnablePersistence(name, out _, out _, out _));
            Assert.True(first.Start(out _, out _));
            first.Dispose();

            StateMachineUtils second = Loaded();
            second.Clock = () => t0.AddMinutes(10);
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.True(second.GetSecondsInState(out double seconds, out message), message);
            Assert.Equal(600, seconds, 2);
        }

        [Fact]
        public void ASavedStateFromADifferentDefinition_IsRefusedNotSilentlyResumed()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            first.Dispose();

            StateMachineUtils second = Loaded(Defs.Minimal);
            Assert.False(second.EnablePersistence(name, out string path, out bool restored, out string message));
            Assert.Null(path);
            Assert.False(restored);
            Assert.Contains("different definition", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void DiscardPersistedState_LetsANewDefinitionTakeOver()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            first.Dispose();

            StateMachineUtils second = Loaded(Defs.Minimal);
            Assert.True(second.DiscardPersistedState(name, out bool discarded, out string message), message);
            Assert.True(discarded);
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.False(restored);
        }

        [Fact]
        public void DiscardPersistedState_NothingSaved_IsANormalOutcome()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.DiscardPersistedState(NewMachineName(), out bool discarded, out string message), message);
            Assert.False(discarded);
        }

        [Fact]
        public void DiscardPersistedState_IsRefusedWhileThisComponentHasItEnabled()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.False(machine.DiscardPersistedState(name, out bool discarded, out string message));
            Assert.False(discarded);
            Assert.Contains("DisablePersistence", message);
            Assert.True(File.Exists(StatePath(name)));
        }

        [Fact]
        public void DiscardPersistedState_IsRefusedWhileAnotherOwnerHoldsIt()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name);
            StateMachineUtils other = New();
            Assert.False(other.DiscardPersistedState(name, out bool discarded, out string message));
            Assert.False(discarded);
            Assert.Contains("in use", message);
            Assert.True(File.Exists(StatePath(name)));
        }

        [Fact]
        public void OnlyOneOwnerMayHoldAMachine_AndDisposalReleasesIt()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);

            StateMachineUtils second = Loaded();
            Assert.False(second.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("already open", message);

            first.Dispose();
            Assert.True(second.EnablePersistence(name, out _, out _, out message), message);
        }

        [Fact]
        public void DisablePersistence_ReleasesOwnershipButKeepsTheSavedFile()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            Assert.True(first.DisablePersistence(out string message), message);
            Assert.True(File.Exists(StatePath(name)));

            StateMachineUtils second = Loaded();
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.True(restored);

            Assert.True(first.Fire("validate", out bool fired, out _, out _, out message), message); // first now runs in memory only
            Assert.True(fired);
            Assert.Equal("Received", second.CurrentState);
        }

        [Fact]
        public void DisablePersistence_WhenNotEnabled_IsHarmless()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.DisablePersistence(out string message), message);
            Assert.Null(message);
        }

        [Fact]
        public void AWriteFailure_LeavesTheMachineUnchanged_AndTheChangeCanBeRetried()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.True(machine.Start(out _, out _));
            Assert.True(machine.GetHistoryJson(out string before, out _));

            using (BlockWrites(name))
            {
                Assert.False(machine.Fire("validate", out bool fired, out string newState, out string reason, out string message));
                Assert.False(fired);
                Assert.Null(newState);
                Assert.Null(reason);
                Assert.Contains("could not be saved", message);
                Assert.Equal("Received", machine.CurrentState);

                Assert.False(machine.SetContext("k", "v", out message));
                Assert.Contains("could not be saved", message);
                Assert.True(machine.GetContext("k", out bool exists, out _, out _));
                Assert.False(exists);

                Assert.False(machine.Reset(false, out _, out message));
                Assert.Contains("could not be saved", message);
            }

            Assert.True(machine.GetHistoryJson(out string after, out _));
            Assert.Equal(before, after);
            Assert.True(machine.Fire("validate", out bool retried, out string state, out _, out string ok), ok);
            Assert.True(retried);
            Assert.Equal("Validated", state);
        }

        [Fact]
        public void AWriteFailure_DoesNotRaiseAnyEvents()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.True(machine.Start(out _, out _));
            int raised = 0;
            machine.StateExited += (_, _) => raised++;
            machine.TransitionFired += (_, _) => raised++;
            machine.StateEntered += (_, _) => raised++;
            using (BlockWrites(name))
                Assert.False(machine.Fire("validate", out _, out _, out _, out _));
            Assert.Equal(0, raised);
        }

        [Fact]
        public void ARejectedTrigger_IsNotPersisted_SoItCannotFailOnADisk()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.True(machine.Start(out _, out _));
            using (BlockWrites(name))
            {
                Assert.True(machine.Fire("nonsense", out bool fired, out _, out string reason, out string message), message);
                Assert.False(fired);
                Assert.Equal("NoTransition", reason);
            }
        }

        [Fact]
        public void EnablingMustHappenBeforeStart()
        {
            string name = NewMachineName();
            StateMachineUtils machine = Started();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("before Start", message);
        }

        [Fact]
        public void EnablingTwice_IsRefused()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.False(machine.EnablePersistence(NewMachineName(), out _, out _, out string message));
            Assert.Contains("already enabled", message);
        }

        [Fact]
        public void EnablingWithAnInvalidDefinition_IsRefused()
        {
            StateMachineUtils machine = New();
            Assert.True(machine.AddState("A", out _));
            Assert.False(machine.EnablePersistence(NewMachineName(), out _, out _, out string message));
            Assert.Contains("not valid", message);
        }

        [Fact]
        public void TheDefinitionIsFrozenWhilePersistenceIsEnabled()
        {
            StateMachineUtils machine = LoadedWithPersistence(NewMachineName());
            Assert.False(machine.LoadDefinitionJson(Defs.Minimal, out string message));
            Assert.Contains("DisablePersistence", message);
            Assert.False(machine.ClearDefinition(out message));
            Assert.False(machine.AddState("Extra", out message));
            Assert.True(machine.DisablePersistence(out message), message);
            Assert.True(machine.LoadDefinitionJson(Defs.Minimal, out message), message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("..")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("has\0nul")]
        public void BadMachineNames_AreRejected(string name)
        {
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out string path, out bool restored, out string message));
            Assert.Null(path);
            Assert.False(restored);
            Assert.Contains("machineName", message);
            Assert.False(machine.DiscardPersistedState(name, out _, out message));
        }

        [Fact]
        public void ATooLongMachineName_IsRejected()
        {
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(new string('x', 65), out _, out _, out string message));
            Assert.Contains("at most 64", message);
        }

        [Fact]
        public void ACorruptSavedState_IsRefusedWithAHintNotAnException()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose();
            File.WriteAllText(StatePath(name), "{ definitely not json");

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("corrupt", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void ASavedStateNamingAnUnknownState_IsRefused()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            first.Dispose();

            string text = File.ReadAllText(StatePath(name)).Replace("\"currentState\": \"Received\"", "\"currentState\": \"Nowhere\"");
            File.WriteAllText(StatePath(name), text);

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("Nowhere", message);
        }

        [Fact]
        public void ASavedStateFromAFutureSchema_IsRefused()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose();
            File.WriteAllText(StatePath(name), File.ReadAllText(StatePath(name)).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("schema version 99", message);
        }

        [Fact]
        public void ARestoredHistory_IsTrimmedToTheConfiguredLimit()
        {
            string name = NewMachineName();
            const string cycle = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B" }, { "from": "B", "trigger": "back", "to": "A" } ] }
                """;
            StateMachineUtils first = LoadedWithPersistence(name, cycle);
            Assert.True(first.Start(out _, out _));
            for (int i = 0; i < 4; i++)
            {
                Assert.True(first.Fire("go", out _, out _, out _, out _));
                Assert.True(first.Fire("back", out _, out _, out _, out _));
            }
            first.Dispose();

            StateMachineUtils second = Loaded(cycle);
            Assert.True(second.SetMaximumHistoryEntries(3, out _));
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.True(second.GetHistoryJson(out string history, out _));
            Assert.Equal(3, JsonDocument.Parse(history).RootElement.GetArrayLength());
        }

        // ---- a saved file that parses as JSON but is not something this component wrote

        private string SavedStartedRun(out string name)
        {
            name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            first.Dispose();
            return File.ReadAllText(StatePath(name));
        }

        [Theory]
        [InlineData("\"enteredUtc\": \"", "\"enteredUtc\": \"not-a-time\", \"ignored\": \"", "invalid 'enteredUtc'")]
        [InlineData("\"enteredUtc\": \"", "\"enteredUtc\": \"\", \"ignored\": \"", "invalid 'enteredUtc'")]
        public void AMissingOrInvalidTimestamp_IsRefused_NotReplacedWithTheCurrentTime(string find, string replace, string fragment)
        {
            string json = SavedStartedRun(out string name);
            File.WriteAllText(StatePath(name), json.Replace(find, replace));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains(fragment, message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void ASnapshotWithNoTimestampAtAll_IsRefused()
        {
            string json = SavedStartedRun(out string name);
            using JsonDocument doc = JsonDocument.Parse(json);
            var fields = doc.RootElement.EnumerateObject().Where(p => p.Name != "enteredUtc").ToDictionary(p => p.Name, p => p.Value.Clone());
            File.WriteAllText(StatePath(name), JsonSerializer.Serialize(fields));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("enteredUtc", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Theory]
        [InlineData("context", "no 'context'")]
        [InlineData("history", "no 'history'")]
        [InlineData("currentState", "no 'currentState'")]
        public void ASnapshotMissingARequiredField_IsRefusedAsIncomplete(string field, string fragment)
        {
            string json = SavedStartedRun(out string name);
            using JsonDocument doc = JsonDocument.Parse(json);
            var fields = doc.RootElement.EnumerateObject().Where(p => p.Name != field).ToDictionary(p => p.Name, p => p.Value.Clone());
            File.WriteAllText(StatePath(name), JsonSerializer.Serialize(fields));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains("incomplete", message);
            Assert.Contains(fragment, message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void ASnapshotWithANullContextValue_IsRefused()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.SetContext("k", "v", out _));
            first.Dispose();
            File.WriteAllText(StatePath(name), File.ReadAllText(StatePath(name)).Replace("\"k\": \"v\"", "\"k\": null"));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("null context value for 'k'", message);
        }

        [Fact]
        public void ASnapshotWithContextKeysThatDifferOnlyByCase_IsRefused()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.SetContext("k", "v", out _));
            first.Dispose();
            File.WriteAllText(StatePath(name), File.ReadAllText(StatePath(name)).Replace("\"k\": \"v\"", "\"k\": \"v\", \"K\": \"w\""));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("differ only by case", message);
        }

        [Fact]
        public void ASnapshotWithANegativeSequence_IsRefused()
        {
            string json = SavedStartedRun(out string name);
            File.WriteAllText(StatePath(name), json.Replace("\"sequence\": 1", "\"sequence\": -5"));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("invalid sequence", message);
        }

        [Fact]
        public void AnUnstartedSnapshot_NeedsNoStateOrTimestamp_AndStillRestores()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose(); // saved but never started

            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.True(machine.IsStarted(out bool started, out _));
            Assert.False(started);
        }

        // ---- the history limit is a configuration change, so it must never rewrite persisted state on its own

        [Fact]
        public void LoweringTheHistoryLimit_HidesEntriesAtOnce_ButOnlyTheNextRealChangeRewritesTheFile()
        {
            string name = NewMachineName();
            const string cycle = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B" }, { "from": "B", "trigger": "back", "to": "A" } ] }
                """;
            StateMachineUtils machine = LoadedWithPersistence(name, cycle);
            Assert.True(machine.Start(out _, out _));
            for (int i = 0; i < 3; i++)
            {
                Assert.True(machine.Fire("go", out _, out _, out _, out _));
                Assert.True(machine.Fire("back", out _, out _, out _, out _));
            }
            int OnDisk() => JsonDocument.Parse(File.ReadAllText(StatePath(name))).RootElement.GetProperty("history").GetArrayLength();
            Assert.Equal(7, OnDisk()); // start + 6 transitions
            string before = File.ReadAllText(StatePath(name));

            machine.MaximumHistoryEntries = 2;

            Assert.Equal(before, File.ReadAllText(StatePath(name))); // configuration alone never touches the file
            Assert.True(machine.GetHistoryJson(out string json, out _));
            Assert.Equal(2, JsonDocument.Parse(json).RootElement.GetArrayLength()); // ...but reads honor the limit now

            Assert.True(machine.Fire("go", out _, out _, out _, out _));
            Assert.Equal(2, OnDisk()); // the next real change trims what is stored
            Assert.True(machine.GetHistoryJson(out json, out _));
            Assert.Equal(2, JsonDocument.Parse(json).RootElement.GetArrayLength());
        }

        // ---- DiscardPersistedState must not race a new owner

        [Fact]
        public void DiscardPersistedState_RemovesOnlyTheState_LeavingTheLockMarkerAndFolderInPlace()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose();
            string folder = Path.Combine(StateMachineCore.BasePath, name);
            Assert.True(File.Exists(Path.Combine(folder, ".machine.lock")));

            StateMachineUtils machine = New();
            Assert.True(machine.DiscardPersistedState(name, out bool discarded, out string message), message);
            Assert.True(discarded);
            Assert.False(File.Exists(StatePath(name)));
            // Deleting these after releasing the lock could race a new owner (and on Unix, unlinking an open lock
            // file lets the next caller create a second one), so they must survive.
            Assert.True(File.Exists(Path.Combine(folder, ".machine.lock")));
            Assert.True(Directory.Exists(folder));

            StateMachineUtils next = Loaded();
            Assert.True(next.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.False(restored);
        }

        [Fact]
        public void DiscardPersistedState_DoesNotBreakOwnership_WhenAnotherComponentAcquiresRightAfter()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose();
            StateMachineUtils cleaner = New();
            Assert.True(cleaner.DiscardPersistedState(name, out _, out string message), message);

            StateMachineUtils owner = LoadedWithPersistence(name);
            StateMachineUtils intruder = Loaded();
            Assert.False(intruder.EnablePersistence(name, out _, out _, out message));
            Assert.Contains("already open", message);
            Assert.True(File.Exists(Path.Combine(StateMachineCore.BasePath, name, ".machine.lock")));
            Assert.True(owner.Start(out _, out message), message);
        }

        // ---- a saved context is untrusted input

        private void RewriteSaved(string name, Action<System.Collections.Generic.Dictionary<string, object>> edit)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name)));
            var fields = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
            edit(fields);
            File.WriteAllText(StatePath(name), JsonSerializer.Serialize(fields));
        }

        private string SavedRun(out string name) { SavedStartedRun(out name); return name; }

        [Theory]
        [InlineData("count", "context keys, more than")]
        [InlineData("longKey", "context key that is longer than 128")]
        [InlineData("longValue", "longer than 4096 characters")]
        [InlineData("paddedKey", "context key that has leading or trailing spaces")]
        [InlineData("emptyKey", "context key that is empty")]
        public void ASavedContextThatBreaksTheLimitsSetContextEnforces_IsRefused(string kind, string fragment)
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields =>
            {
                var context = new System.Collections.Generic.Dictionary<string, string>();
                switch (kind)
                {
                    case "count": for (int i = 0; i < StateMachineCore.MaxContextEntries + 1; i++) context["k" + i] = "v"; break;
                    case "longKey": context[new string('k', 129)] = "v"; break;
                    case "longValue": context["k"] = new string('v', StateMachineCore.MaxContextValueLength + 1); break;
                    case "paddedKey": context[" k"] = "v"; break;
                    case "emptyKey": context[""] = "v"; break;
                }
                fields["context"] = context;
            });

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains(fragment, message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void ASavedContextExactlyAtTheLimits_IsAccepted()
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields =>
            {
                var context = new System.Collections.Generic.Dictionary<string, string>();
                for (int i = 0; i < StateMachineCore.MaxContextEntries - 1; i++) context["k" + i] = "v";
                context[new string('k', 128) + ""] = new string('v', StateMachineCore.MaxContextValueLength);
                fields["context"] = context;
            });

            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.True(machine.GetContextJson(out string json, out _));
            Assert.Equal(StateMachineCore.MaxContextEntries, JsonDocument.Parse(json).RootElement.EnumerateObject().Count());
        }

        [Fact]
        public void ASavedHistoryLargerThanThisComponentEverWrites_IsRefused()
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields =>
            {
                fields["history"] = Enumerable.Range(1, 10001).Select(i => new { seq = i, utc = "2026-01-01T00:00:00.0000000Z", kind = "transition" }).ToList();
            });

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("10001 history entries", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void ASavedFileLargerThanTheLoadLimit_IsRefusedBeforeItIsRead()
        {
            string name = SavedRun(out _);
            StateMachineUtils machine = Loaded();
            machine.MaxStateFileBytes = 50; // per component, so no other test is affected
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("larger than the 50-byte limit", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        // ---- restoring must not silently drop what the caller already set

        [Fact]
        public void RestoringASavedRun_IsRefused_WhenContextWasSetBeforehand_RatherThanDroppingItSilently()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.SetContext("saved", "1", out _));
            Assert.True(first.Start(out _, out _));
            first.Dispose();

            StateMachineUtils second = Loaded();
            Assert.True(second.SetContext("precious", "keep me", out _));
            Assert.False(second.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains("SetContext after EnablePersistence", message);
            Assert.True(second.GetContext("precious", out bool stillThere, out string value, out _));
            Assert.True(stillThere); // nothing was lost: the refusal left the component as it was
            Assert.Equal("keep me", value);

            Assert.True(second.ClearContext(out _));
            Assert.True(second.EnablePersistence(name, out _, out restored, out message), message);
            Assert.True(restored);
        }

        [Fact]
        public void ContextSetBeforehand_IsKept_WhenThereIsNoSavedRunToRestore()
        {
            string name = NewMachineName();
            StateMachineUtils machine = Loaded();
            Assert.True(machine.SetContext("preset", "v", out _));
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.False(restored);
            Assert.True(machine.GetContext("preset", out bool exists, out _, out _));
            Assert.True(exists);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name)));
            Assert.Equal("v", doc.RootElement.GetProperty("context").GetProperty("preset").GetString()); // and it is persisted too
        }

        // ---- a crash mid-write leaves temp files behind

        [Fact]
        public void TempFilesLeftByACrashMidWrite_AreSweptWhenPersistenceIsEnabled()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose();
            string folder = Path.Combine(StateMachineCore.BasePath, name);
            string stale = Path.Combine(folder, "state.json." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(stale, "half a snapshot");
            string unrelated = Path.Combine(folder, "notes.txt");
            File.WriteAllText(unrelated, "mine");

            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(unrelated)); // only this component's own temp files are touched
        }

        // ---- a saved history is untrusted too: every record must satisfy what this component writes

        /// <summary>A genuine saved run whose history holds start (1), transition (2) and a rejection (3), sequence counter 3.</summary>
        private string SavedRunWithHistory()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            Assert.True(first.Fire("validate", out _, out _, out _, out _));
            Assert.True(first.Fire("nonsense", out _, out _, out _, out _)); // recorded in memory only...
            Assert.True(first.SetContext("k", "v", out _));                  // ...and reaches disk with the next real change
            first.Dispose();
            return name;
        }

        private void EditHistory(string name, Action<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>, System.Collections.Generic.Dictionary<string, object>> edit)
        {
            RewriteSaved(name, fields =>
            {
                var entries = JsonSerializer.Deserialize<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>>(((JsonElement)fields["history"]).GetRawText());
                edit(entries, fields);
                fields["history"] = entries;
            });
        }

        [Theory]
        [InlineData("unknownKind", "unknown kind 'teleport'")]
        [InlineData("missingKind", "unknown kind ''")]
        [InlineData("missingUtc", "missing or invalid timestamp")]
        [InlineData("badUtc", "missing or invalid timestamp")]
        [InlineData("zeroSeq", "sequence number below 1")]
        [InlineData("negativeSeq", "sequence number below 1")]
        [InlineData("duplicateSeq", "does not increase")]
        [InlineData("decreasingSeq", "does not increase")]
        [InlineData("lastNotSequence", "saved sequence counter is 99")]
        [InlineData("transitionNoTrigger", "missing its trigger, 'from' or 'to'")]
        [InlineData("transitionUnknownState", "names state 'Nowhere'")]
        [InlineData("rejectedNoReason", "missing its trigger or reason")]
        [InlineData("startNoTo", "has no 'to' state")]
        [InlineData("hugeDetail", "detail longer than 16384")]
        [InlineData("longTrigger", "field longer than 128")]
        public void ACorruptHistoryRecord_IsRefused_WithTheDiscardHint(string corruption, string fragment)
        {
            string name = SavedRunWithHistory();
            EditHistory(name, (entries, top) =>
            {
                switch (corruption)
                {
                    case "unknownKind": entries[1]["kind"] = "teleport"; break;
                    case "missingKind": entries[1].Remove("kind"); break;
                    case "missingUtc": entries[1].Remove("utc"); break;
                    case "badUtc": entries[1]["utc"] = "yesterday"; break;
                    case "zeroSeq": entries[0]["seq"] = 0; break;
                    case "negativeSeq": entries[0]["seq"] = -3; break;
                    case "duplicateSeq": entries[1]["seq"] = entries[0]["seq"]; break;
                    case "decreasingSeq": (entries[1]["seq"], entries[2]["seq"]) = (entries[2]["seq"], entries[1]["seq"]); break;
                    case "lastNotSequence": top["sequence"] = 99; break;
                    case "transitionNoTrigger": entries[1].Remove("trigger"); break;
                    case "transitionUnknownState": entries[1]["to"] = "Nowhere"; break;
                    case "rejectedNoReason": entries[2].Remove("reason"); break;
                    case "startNoTo": entries[0].Remove("to"); break;
                    case "hugeDetail": entries[2]["detail"] = new string('d', 16385); break;
                    case "longTrigger": entries[2]["trigger"] = new string('t', 129); break;
                }
            });

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains("invalid history", message);
            Assert.Contains(fragment, message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void AGenuineSavedHistory_RestoresAndTheNextChangeContinuesTheNumbering()
        {
            string name = SavedRunWithHistory();
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);

            Assert.True(machine.GetHistoryJson(out string json, out _));
            Assert.Equal(new long[] { 1, 2, 3 }, JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.GetProperty("seq").GetInt64()));

            Assert.True(machine.Fire("abort", out bool fired, out _, out _, out message), message);
            Assert.True(fired);
            Assert.True(machine.GetHistoryJson(out json, out _));
            Assert.Equal(new long[] { 1, 2, 3, 4 }, JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.GetProperty("seq").GetInt64())); // no reuse, no gap
        }

        [Fact]
        public void AnUnstartedMachineThatRecordedNotStartedRejections_StillRestores()
        {
            string name = NewMachineName();
            StateMachineUtils first = Loaded();
            Assert.True(first.Fire("validate", out bool fired, out _, out string reason, out _)); // before Start: a NotStarted rejection
            Assert.False(fired);
            Assert.Equal("NotStarted", reason);
            Assert.True(first.EnablePersistence(name, out _, out _, out string message), message); // persists that history with started == false
            first.Dispose();

            StateMachineUtils second = Loaded();
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.True(restored);
            Assert.True(second.IsStarted(out bool started, out _));
            Assert.False(started);
            Assert.True(second.GetHistoryJson(out string json, out _));
            JsonElement entry = JsonDocument.Parse(json).RootElement[0];
            Assert.Equal("rejected", entry.GetProperty("kind").GetString());
            Assert.Equal("NotStarted", entry.GetProperty("reason").GetString());
        }

        [Fact]
        public void HistoryWithReentrancyLimitAndRejectionRecords_RestoresToo()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            int refusals = 0;
            first.TransitionRejected += (_, _) => { if (first.Fire("again", out _, out _, out string r, out _) && r == "ReentrancyLimit") refusals++; };
            Assert.True(first.Fire("nonsense", out _, out _, out _, out _));
            Assert.True(first.SetContext("k", "v", out _)); // persist the accumulated rejections
            first.Dispose();

            StateMachineUtils second = Loaded();
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.True(second.GetHistoryJson(out string json, out _, 1000));
            Assert.Contains(JsonDocument.Parse(json).RootElement.EnumerateArray(), e => e.TryGetProperty("reason", out JsonElement r) && r.GetString() == "ReentrancyLimit");
        }

        [Fact]
        public void TheSavedStateFile_IsWrittenIntactWithNoTempFileLeftBehind()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.True(machine.Start(out _, out _));
            for (int i = 0; i < 20; i++) Assert.True(machine.SetContext("k" + i, new string('v', 500), out _));

            string folder = Path.Combine(StateMachineCore.BasePath, name);
            Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp"));
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name))); // complete, parseable JSON
            Assert.Equal(20, doc.RootElement.GetProperty("context").EnumerateObject().Count());
        }

        [Fact]
        public void Persistence_NeverRecordsContextValuesInHistory()
        {
            string name = NewMachineName();
            const string json = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B", "guards": [ { "key": "token", "op": "equals", "value": "expected" } ] } ] }
                """;
            StateMachineUtils machine = LoadedWithPersistence(name, json);
            Assert.True(machine.Start(out _, out _));
            Assert.True(machine.SetContext("token", "s3cr3t-value", out _));
            Assert.True(machine.Fire("go", out _, out _, out _, out _));

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name)));
            string history = doc.RootElement.GetProperty("history").GetRawText();
            Assert.DoesNotContain("s3cr3t-value", history); // context is persisted (documented), but never leaks into the history log
        }
    }
}
