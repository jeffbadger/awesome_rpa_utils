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
        public void WithPersistenceEnabled_LoadAndClearAreRefused_SoDisablePersistenceComesFirst()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.False(machine.LoadDefinitionJson(Defs.Minimal, out string message));
            Assert.Contains("DisablePersistence", message);
            Assert.False(machine.ClearDefinition(out message));
            Assert.Contains("DisablePersistence", message);

            Assert.True(machine.DisablePersistence(out message), message);
            Assert.True(machine.LoadDefinitionJson(Defs.Minimal, out message), message); // now the definition can be replaced
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
        [InlineData("duplicateSeq", "run consecutively")]
        [InlineData("decreasingSeq", "run consecutively")]
        [InlineData("gapInSequence", "run consecutively")]
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
                    case "gapInSequence": entries[2]["seq"] = 7; top["sequence"] = 7; break;
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

        // ---- every required top-level field must be PRESENT, not merely parse to a default

        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("machineName")]
        [InlineData("definitionHash")]
        [InlineData("started")]
        [InlineData("currentState")]
        [InlineData("enteredUtc")]
        [InlineData("sequence")]
        [InlineData("context")]
        [InlineData("history")]
        public void ASnapshotMissingAnyRequiredTopLevelField_IsRefusedAsIncomplete(string field)
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields => fields.Remove(field));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains("incomplete", message);
            Assert.Contains("no '" + field + "'", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void ARunningSnapshotWhoseStartedFlagWasLost_IsNotSilentlyRestoredAsUnstarted_OrRewritten()
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields => fields.Remove("started"));
            string damaged = File.ReadAllText(StatePath(name));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains("no 'started'", message);
            Assert.True(machine.IsStarted(out bool started, out _));
            Assert.False(started);
            Assert.Equal(damaged, File.ReadAllText(StatePath(name))); // the damaged file was neither used nor overwritten
        }

        [Fact]
        public void SeveralMissingFields_AreAllNamedInOneMessage()
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields => { fields.Remove("started"); fields.Remove("sequence"); });
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("no 'started' and no 'sequence'", message);
        }

        [Fact]
        public void ASnapshotThatIsNotStartedButNamesACurrentState_IsRefusedAsInconsistent()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose(); // saved, never started
            RewriteSaved(name, fields => fields["currentState"] = "Validated");

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("inconsistent", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        // ---- the saved machine name must match the requested one

        [Fact]
        public void AStateFileCopiedFromAnotherMachinesFolder_IsRefused_EvenWithTheSameDefinition()
        {
            string original = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(original);
            Assert.True(first.Start(out _, out _));
            Assert.True(first.Fire("validate", out _, out _, out _, out _));
            first.Dispose();

            string other = NewMachineName();
            Directory.CreateDirectory(Path.Combine(StateMachineCore.BasePath, other));
            File.Copy(StatePath(original), StatePath(other)); // same definition, so the hash matches; only the name differs

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(other, out string path, out bool restored, out string message));
            Assert.Null(path);
            Assert.False(restored);
            Assert.Contains("belongs to machine '" + original + "', not '" + other + "'", message);
            Assert.Contains("DiscardPersistedState", message);
            Assert.True(machine.IsStarted(out bool started, out _));
            Assert.False(started); // nothing from the other machine's run leaked in
        }

        [Fact]
        public void AnEmptyMachineNameInTheFile_IsRefusedToo()
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields => fields["machineName"] = "");
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("belongs to machine ''", message);
        }

        [Fact]
        public void TheMachineNameComparison_IgnoresCase_SoWindowsFolderCaseDoesNotCauseAFalseRefusal()
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields => fields["machineName"] = name.ToUpperInvariant());
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
        }

        [Fact]
        public void ThePersistedFile_RecordsTheTrimmedMachineName_SoSurroundingSpacesInTheRequestStillMatch()
        {
            string name = NewMachineName();
            StateMachineUtils first = Loaded();
            Assert.True(first.EnablePersistence("  " + name + "  ", out _, out _, out string message), message);
            Assert.True(first.Start(out _, out _));
            first.Dispose();

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name)));
            Assert.Equal(name, doc.RootElement.GetProperty("machineName").GetString());

            StateMachineUtils second = Loaded();
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.True(restored);
        }

        // ---- an empty history is only legitimate for a machine that has never recorded anything

        [Fact]
        public void AStartedSnapshotWithAnEmptyHistory_IsRefused_NotResumedWithItsAuditTrailErased()
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields => fields["history"] = new object[0]);

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains("invalid history", message);
            Assert.Contains("a started machine has no history entries", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void AnUnstartedSnapshotWithANonzeroCounterButNoHistory_IsRefused()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose(); // saved, never started: history empty, counter 0
            RewriteSaved(name, fields => fields["sequence"] = 12);

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("saved sequence counter is 12", message);
        }

        [Fact]
        public void TheUntouchedUnstartedSnapshot_StillRestores_WithItsEmptyHistory()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose();
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.True(machine.GetHistoryJson(out string json, out _));
            Assert.Equal("[]", json);
        }

        [Fact]
        public void ARunThatWasStoppedByReplacingTheDefinition_CanStillBePersistedAndRestored()
        {
            // Load/Clear discard the history, so the counter must restart with it: otherwise this machine would save
            // "no history but a nonzero counter" - the very shape the empty-history rule refuses.
            string name = NewMachineName();
            StateMachineUtils first = Started();
            Assert.True(first.Fire("validate", out _, out _, out _, out _));
            Assert.True(first.LoadDefinitionJson(Defs.Minimal, out string message), message);
            Assert.True(first.EnablePersistence(name, out _, out _, out message), message);
            first.Dispose();

            StateMachineUtils second = Loaded(Defs.Minimal);
            Assert.True(second.EnablePersistence(name, out _, out bool restored, out message), message);
            Assert.True(restored);
            Assert.True(second.Start(out _, out message), message);
            Assert.True(second.GetHistoryJson(out string json, out _));
            Assert.Equal(1, JsonDocument.Parse(json).RootElement[0].GetProperty("seq").GetInt64()); // numbering restarted with the history
        }

        // ---- the history limit applies to everything that is persisted, not only to transitions

        [Theory]
        [InlineData("setContext")]
        [InlineData("removeContext")]
        [InlineData("clearContext")]
        public void LoweringTheHistoryLimit_IsEnforcedOnTheNextPersistedChange_EvenWhenItIsOnlyAContextChange(string change)
        {
            string name = NewMachineName();
            const string cycle = """
                { "initial": "A", "states": [ "A", "B" ],
                  "transitions": [ { "from": "A", "trigger": "go", "to": "B" }, { "from": "B", "trigger": "back", "to": "A" } ] }
                """;
            int OnDisk() => JsonDocument.Parse(File.ReadAllText(StatePath(name))).RootElement.GetProperty("history").GetArrayLength();

            StateMachineUtils machine = LoadedWithPersistence(name, cycle);
            Assert.True(machine.Start(out _, out _));
            for (int i = 0; i < 3; i++)
            {
                Assert.True(machine.Fire("go", out _, out _, out _, out _));
                Assert.True(machine.Fire("back", out _, out _, out _, out _));
            }
            Assert.True(machine.SetContext("seed", "1", out _));
            Assert.Equal(7, OnDisk());

            machine.MaximumHistoryEntries = 2;
            Assert.Equal(7, OnDisk()); // configuration alone still never rewrites the file

            bool ok = change switch
            {
                "setContext" => machine.SetContext("k", "v", out _),
                "removeContext" => machine.RemoveContext("seed", out _, out _),
                _ => machine.ClearContext(out _)
            };
            Assert.True(ok);
            Assert.Equal(2, OnDisk()); // a context-only change is a real change: it trims what is stored too
        }

        [Fact]
        public void TheFirstWriteAfterEnablingPersistence_HonorsAHistoryLimitLoweredEarlier()
        {
            string name = NewMachineName();
            StateMachineUtils machine = Loaded();
            for (int i = 0; i < 5; i++) Assert.True(machine.Fire("validate", out _, out _, out _, out _)); // NotStarted rejections, kept in memory
            machine.MaximumHistoryEntries = 2;                                                              // lowered afterwards

            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.False(restored);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name)));
            Assert.Equal(2, doc.RootElement.GetProperty("history").GetArrayLength());
            Assert.Equal(5, doc.RootElement.GetProperty("sequence").GetInt64()); // the counter keeps counting; only the oldest entries go
            Assert.Equal(new long[] { 4, 5 }, doc.RootElement.GetProperty("history").EnumerateArray().Select(e => e.GetProperty("seq").GetInt64()));
        }

        // ---- the sequence counter has a ceiling that restore and the increment agree on

        private string SavedRunWithSequence(long sequence)
        {
            string name = SavedRun(out _);
            RewriteSaved(name, fields =>
            {
                fields["sequence"] = sequence;
                fields["history"] = new[] { new { seq = sequence, utc = "2026-01-01T00:00:00.0000000Z", kind = "start", to = "Received" } };
            });
            return name;
        }

        [Theory]
        [InlineData(long.MaxValue)]
        public void ASavedSequenceAtLongMaxValue_IsRefused_BecauseTheNextIncrementWouldWrap(long sequence)
        {
            string name = SavedRunWithSequence(sequence);
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("invalid sequence number", message);
            Assert.Contains("DiscardPersistedState", message);
        }

        [Fact]
        public void ASavedSequenceExactlyAtTheCeiling_Restores_ThenTheNextChangeIsRefusedNotWrapped_AndResetRecovers()
        {
            string name = SavedRunWithSequence(long.MaxValue - 1);
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);

            Assert.False(machine.Fire("validate", out bool fired, out _, out _, out message)); // would need sequence long.MaxValue
            Assert.False(fired);
            Assert.Contains("sequence counter is exhausted", message);
            Assert.Equal("Received", machine.CurrentState);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name))))
                Assert.Equal(long.MaxValue - 1, doc.RootElement.GetProperty("sequence").GetInt64()); // never wrapped, never persisted negative

            Assert.True(machine.Reset(false, out _, out message), message); // clears the history, so the numbering restarts
            Assert.True(machine.Fire("validate", out fired, out _, out _, out message), message);
            Assert.True(fired);
            Assert.True(machine.GetHistoryJson(out string json, out _));
            Assert.Equal(new long[] { 1, 2 }, JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.GetProperty("seq").GetInt64()));
        }

        [Fact]
        public void EverythingTheComponentWrites_IsWithinTheCeilingItRestoresUpTo()
        {
            string name = NewMachineName();
            StateMachineUtils machine = LoadedWithPersistence(name);
            Assert.True(machine.Start(out _, out _));
            Assert.True(machine.Fire("validate", out _, out _, out _, out _));
            machine.Dispose();
            StateMachineUtils again = Loaded();
            Assert.True(again.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
        }

        // ---- enteredUtc is required of every snapshot, running or not

        [Theory]
        [InlineData("garbage")]
        [InlineData("")]
        [InlineData("   ")]
        public void AnUnstartedSnapshotWithAMalformedTimestamp_IsRefused_NotRewrittenOverTheDamage(string timestamp)
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose(); // saved, never started
            RewriteSaved(name, fields => fields["enteredUtc"] = timestamp);
            string damaged = File.ReadAllText(StatePath(name));

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains("invalid 'enteredUtc'", message);
            Assert.Contains("DiscardPersistedState", message);
            Assert.Equal(damaged, File.ReadAllText(StatePath(name)));
        }

        [Fact]
        public void AnUnstartedSnapshot_IsWrittenWithAFixedParseableTimestamp_NotATimeZoneDependentOne()
        {
            string name = NewMachineName();
            LoadedWithPersistence(name).Dispose();
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(StatePath(name)));
            Assert.Equal("0001-01-01T00:00:00.0000000Z", doc.RootElement.GetProperty("enteredUtc").GetString());

            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
        }

        // ---- a saved history record may only carry what its kind carries, and must agree with the definition and with itself

        /// <summary>Start (to Received), validate (Received to Validated), abort (Validated to Failed, via the wildcard); ends final in Failed.</summary>
        private string SavedThreeMoveRun()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            Assert.True(first.Fire("validate", out _, out _, out _, out _));
            Assert.True(first.Fire("abort", out _, out _, out _, out _));
            first.Dispose();
            return name;
        }

        /// <summary>An unstarted machine whose history holds one NotStarted rejection.</summary>
        private string SavedUnstartedWithARejection()
        {
            string name = NewMachineName();
            StateMachineUtils first = Loaded();
            Assert.True(first.Fire("validate", out _, out _, out _, out _));
            Assert.True(first.EnablePersistence(name, out _, out _, out _));
            first.Dispose();
            return name;
        }

        [Theory]
        [InlineData("unknownReason", "unknown reason 'Because'")]
        [InlineData("reasonOnTransition", "carries a rejection reason or detail")]
        [InlineData("detailOnTransition", "carries a rejection reason or detail")]
        [InlineData("fieldsOnStart", "carries fields that only a transition or a rejection has")]
        [InlineData("triggerOnStart", "carries fields that only a transition or a rejection has")]
        [InlineData("destinationOnRejection", "names a destination state")]
        [InlineData("notStartedInStartedHistory", "has reason 'NotStarted' but the machine is started")]
        [InlineData("undeclaredTransition", "which the definition does not declare")]
        [InlineData("lastMoveMismatch", "last recorded move ends in 'Validated' but the saved current state is 'Received'")]
        public void AHistoryRecordThatDisagreesWithItsKindOrTheDefinition_IsRefused(string corruption, string fragment)
        {
            string name = SavedRunWithHistory(); // start(1) validate(2) rejected NoTransition(3); current state Validated
            EditHistory(name, (entries, top) =>
            {
                switch (corruption)
                {
                    case "unknownReason": entries[2]["reason"] = "Because"; break;
                    case "reasonOnTransition": entries[1]["reason"] = "NoTransition"; break;
                    case "detailOnTransition": entries[1]["detail"] = "made up"; break;
                    case "fieldsOnStart": entries[0]["detail"] = "made up"; break;
                    case "triggerOnStart": entries[0]["trigger"] = "validate"; break;
                    case "destinationOnRejection": entries[2]["to"] = "Failed"; break;
                    case "notStartedInStartedHistory": entries[2]["reason"] = "NotStarted"; break;
                    case "undeclaredTransition": entries[1]["trigger"] = "post"; break;   // 'post' exists, but not from Received
                    case "lastMoveMismatch": top["currentState"] = "Received"; break;
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
        public void AMoveThatDoesNotStartWhereTheLastOneEnded_IsRefused_EvenWhenTheWildcardMakesItDeclared()
        {
            string name = SavedThreeMoveRun();
            EditHistory(name, (entries, _) => entries[2]["from"] = "Received"); // 'abort' is declared from '*', but the machine was in Validated
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("leaves 'Received' but the machine had just moved to 'Validated'", message);
        }

        [Theory]
        [InlineData("start")]
        [InlineData("reset")]
        public void AStartOrResetRecord_ThatEntersAStateOtherThanTheInitialOne_IsRefused(string kind)
        {
            string name = SavedRunWithHistory(); // entry 1 is the start; current state Validated
            EditHistory(name, (entries, _) => { entries[0]["kind"] = kind; entries[0]["to"] = "Failed"; });
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("always enters the initial state 'Received'", message);
        }

        [Fact]
        public void AWildcardMoveOutOfAFinalState_IsRefused()
        {
            string name = SavedThreeMoveRun(); // start, validate, abort (Validated -> Failed, final)
            EditHistory(name, (entries, top) =>
            {
                var extra = new System.Collections.Generic.Dictionary<string, object>(entries[2]);
                extra["seq"] = 4; extra["from"] = "Failed"; extra["to"] = "Failed"; // 'abort' is declared from '*', but a final state accepts nothing
                entries.Add(extra);
                top["sequence"] = 4;
            });
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("leaves final state 'Failed'", message);
        }

        // ---- a field that appears twice is ambiguous, not "last one wins"

        private void EditSavedText(string name, Func<string, string> edit)
        {
            string text = File.ReadAllText(StatePath(name));
            string edited = edit(text);
            Assert.NotEqual(text, edited); // the edit must have found what it meant to duplicate
            File.WriteAllText(StatePath(name), edited);
        }

        [Theory]
        [InlineData("topFirst", "the saved state has the field 'started' more than once")]
        [InlineData("topLast", "the saved state has the field 'started' more than once")]
        [InlineData("topCaseOnly", "more than once")]
        [InlineData("contextKey", "the saved context has the key")]
        [InlineData("contextKeyCaseOnly", "differ only by case")] // has its own, older check
        [InlineData("historyEntryField", "history entry 1 has the field")]
        public void ADuplicatedFieldInTheSavedFile_IsRefusedAsCorrupt_RatherThanReadAsLastWriteWins(string where, string fragment)
        {
            string name = SavedRunWithHistory(); // started, current state Validated, context k=v
            EditSavedText(name, text =>
            {
                switch (where)
                {
                    case "topFirst": return "{\"started\":false," + text.TrimStart().Substring(1);
                    case "topLast": return text.TrimEnd().TrimEnd('}') + ",\"started\":false}";
                    case "topCaseOnly": return text.TrimEnd().TrimEnd('}') + ",\"Started\":false}";
                    case "contextKey": return System.Text.RegularExpressions.Regex.Replace(text, "\"k\"\\s*:\\s*\"v\"", "\"k\":\"v\",\"k\":\"x\"");
                    case "contextKeyCaseOnly": return System.Text.RegularExpressions.Regex.Replace(text, "\"k\"\\s*:\\s*\"v\"", "\"k\":\"v\",\"K\":\"x\"");
                    default: return System.Text.RegularExpressions.Regex.Replace(text, "\"kind\"\\s*:\\s*\"start\"", "\"kind\":\"start\",\"Kind\":\"reset\"");
                }
            });

            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out bool restored, out string message));
            Assert.False(restored);
            Assert.Contains(fragment, message);
            Assert.Contains("DiscardPersistedState", message);
            Assert.Equal(string.Empty, machine.CurrentState); // nothing was resumed
        }

        [Fact]
        public void AGenuineThreeMoveRun_ThroughAWildcardTransition_Restores()
        {
            string name = SavedThreeMoveRun();
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.Equal("Failed", machine.CurrentState);
            Assert.True(machine.IsFinished);
        }

        [Fact]
        public void AStartOrTransitionRecordInAnUnstartedMachinesHistory_IsRefused()
        {
            string name = SavedUnstartedWithARejection();
            EditHistory(name, (entries, _) => { entries[0]["kind"] = "start"; entries[0]["to"] = "Received"; foreach (string f in new[] { "trigger", "reason", "detail", "from" }) entries[0].Remove(f); });
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("is in the history of a machine that is not started", message);
        }

        [Fact]
        public void AFinishedRejectionInAnUnstartedMachinesHistory_IsRefused()
        {
            string name = SavedUnstartedWithARejection();
            EditHistory(name, (entries, _) => entries[0]["reason"] = "Finished");
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("has reason 'Finished' but the machine is not started", message);
        }

        /// <summary>A genuine run that ends in the final state Failed and then declines a trigger with 'Finished'; the rejection is entry 3.</summary>
        private string SavedFinishedRunWithRejection()
        {
            string name = NewMachineName();
            StateMachineUtils first = LoadedWithPersistence(name);
            Assert.True(first.Start(out _, out _));
            Assert.True(first.Fire("abort", out _, out _, out _, out _));
            Assert.True(first.Fire("validate", out bool fired, out _, out string rejection, out _));
            Assert.False(fired);
            Assert.Equal("Finished", rejection);
            Assert.True(first.SetContext("k", "v", out _)); // carries the in-memory rejection to disk
            first.Dispose();
            return name;
        }

        [Fact]
        public void AGenuineFinishedRejection_InAFinalState_Restores()
        {
            string name = SavedFinishedRunWithRejection();
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
            Assert.True(machine.IsFinished);
        }

        [Theory]
        [InlineData("NoTransition")]
        [InlineData("GuardFailed")]
        public void ANonFinishedRejectionInAFinalState_IsRefused(string reason)
        {
            string name = SavedFinishedRunWithRejection();
            EditHistory(name, (entries, _) => entries[entries.Count - 1]["reason"] = reason);
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("is a final state, which declines everything with 'Finished'", message);
        }

        [Fact]
        public void AFinishedRejectionInANonFinalState_IsRefused()
        {
            string name = SavedRunWithHistory(); // current state Validated (not final); entry 3 is a genuine NoTransition
            EditHistory(name, (entries, _) => entries[2]["reason"] = "Finished");
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("has reason 'Finished' but 'Validated' is not a final state", message);
        }

        [Fact]
        public void ARejectionMadeInADifferentStateThanTheMachineWasIn_IsRefused()
        {
            string name = SavedRunWithHistory();
            EditHistory(name, (entries, _) => entries[2]["from"] = "Received");
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("was made in 'Received' but the machine had just moved to 'Validated'", message);
        }

        [Fact]
        public void ANotStartedRejectionThatNamesACurrentState_IsRefused()
        {
            string name = SavedUnstartedWithARejection();
            EditHistory(name, (entries, _) => entries[0]["from"] = "Received");
            StateMachineUtils machine = Loaded();
            Assert.False(machine.EnablePersistence(name, out _, out _, out string message));
            Assert.Contains("has reason 'NotStarted' but names a current state", message);
        }

        [Fact]
        public void NoTransitionAndGuardFailedRejections_AreAcceptedInANonFinalState()
        {
            foreach (string reason in new[] { "NoTransition", "GuardFailed" })
            {
                string name = SavedRunWithHistory();
                EditHistory(name, (entries, _) => entries[2]["reason"] = reason);
                StateMachineUtils machine = Loaded();
                Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), reason + ": " + message);
                Assert.True(restored);
            }
        }

        [Fact]
        public void ANotStartedRejection_InAnUnstartedHistory_Restores()
        {
            string name = SavedUnstartedWithARejection();
            StateMachineUtils machine = Loaded();
            Assert.True(machine.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
        }

        // ---- and the write side keeps to the same rules (the file it writes must always be one it would restore)

        [Fact]
        public void EveryKindOfRecordTheComponentCanWrite_RoundTripsThroughRestore()
        {
            string name = NewMachineName();
            StateMachineUtils m = LoadedWithPersistence(name);
            Assert.True(m.Fire("validate", out _, out _, out _, out _));      // NotStarted rejection
            Assert.True(m.Start(out _, out _));                                // start clears that
            Assert.True(m.Fire("nonsense", out _, out _, out _, out _));      // NoTransition
            Assert.True(m.Fire("validate", out _, out _, out _, out _));      // transition
            Assert.True(m.SetContext("amount", "99999", out _));
            Assert.True(m.Fire("post", out _, out _, out string guardFailedReason, out _)); // guarded -> Failed fallback (fires)
            m.Dispose();

            StateMachineUtils again = Loaded();
            Assert.True(again.EnablePersistence(name, out _, out bool restored, out string message), message);
            Assert.True(restored);
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
