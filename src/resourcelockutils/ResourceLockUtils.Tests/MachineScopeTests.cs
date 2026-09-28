using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace ResourceLockAutomation.Tests
{
    /// <summary>
    /// The Machine scope: lease files in a lock folder. Each test uses its own temporary folder. Lease expiry and dead holders are exercised
    /// through MachineLocks with sub-second leases and hand-written lease files; the multi-process tests run the test assembly as child processes.
    /// </summary>
    public sealed class MachineScopeTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "rl-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private ResourceLockUtils New()
        {
            var c = new ResourceLockUtils();
            Assert.True(c.ConfigureLockFolder(folder, out string m), m);
            return c;
        }

        private static string Acquire(ResourceLockUtils c, string resource, string holder = "Robot 1")
        {
            Assert.True(c.TryAcquireLock(LockScope.Machine, resource, holder, 60, out bool acquired, out string token, out string current, out string m), m);
            Assert.True(acquired, "not acquired; held by " + current);
            return token;
        }

        private string[] Files() => Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        private LeaseRecord Record(string file) => LeaseRecord.Parse(File.ReadAllText(Path.Combine(folder, file)));

        [Fact]
        public void ALock_IsAGenerationFile_TheOtherIsToldWho_AndReleaseMarksItReleasedWithoutDeletingIt()
        {
            using var first = New();
            using var second = New();
            string token = Acquire(first, "SAP-User", "MyServer_1");
            Assert.Equal(new[] { "sap-user.0.1.lease" }, Files());                                          // folder created, names lower-cased
            Assert.True(second.TryAcquireLock(LockScope.Machine, "sap-user", "MyServer_2", 60, out bool acquired, out string none, out string current, out string m), m);
            Assert.Equal((false, (string)null, "MyServer_1"), (acquired, none, current));
            Assert.True(first.ReleaseLock(LockScope.Machine, "SAP-User", token, out bool released, out m), m);
            Assert.True(released);
            Assert.True(Record("sap-user.0.1.lease").Released);                                             // the highest generation is never deleted
            Acquire(second, "SAP-User", "MyServer_2");
            Assert.Equal(new[] { "sap-user.0.1.lease", "sap-user.0.2.lease" }, Files());                    // the older one stays until its successor is 5 minutes old
        }

        [Fact]
        public void TheLeaseFile_RecordsWhoWhereAndUntilWhen()
        {
            using var c = New();
            string token = Acquire(c, "printer", "Robot — Bänk");
            LeaseRecord r = Record("printer.0.1.lease");
            using Process self = Process.GetCurrentProcess();
            Assert.Equal((token, "Robot — Bänk", LockKind.Lock, 1), (r.Token, r.Holder, r.Kind, r.Capacity));
            Assert.Equal((Environment.MachineName, self.Id, self.SessionId), (r.Machine, r.ProcessId, r.SessionId));
            Assert.Equal(MachineLocks.StartIdentity(self), r.ProcessStart);
            Assert.InRange((r.ExpiresUtc - r.AcquiredUtc).TotalSeconds, 59.9, 60.1);
            Assert.False(r.Released);
        }

        [Fact]
        public void Slots_UseOneChainEach_UpToTheCapacity()
        {
            using var c = New();
            var tokens = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                Assert.True(c.TryAcquireSlot(LockScope.Machine, "licenses", 3, "Robot " + i, 60, out bool acquired, out string token, out int count, out string m), m);
                Assert.Equal((true, i), (acquired, count));
                tokens.Add(token);
            }
            Assert.Equal(new[] { "licenses.0.1.lease", "licenses.1.1.lease", "licenses.2.1.lease" }, Files());
            Assert.True(c.TryAcquireSlot(LockScope.Machine, "licenses", 3, "Robot 4", 60, out bool full, out _, out int holders, out string msg), msg);
            Assert.Equal((false, 3), (full, holders));
            Assert.True(c.GetLockStatus(LockScope.Machine, "licenses", out bool held, out string names, out int expires, out int holderCount, out msg), msg);
            Assert.Equal((true, "Robot 1|Robot 2|Robot 3", 3), (held, names, holderCount));
            Assert.InRange(expires, 58, 60);
            Assert.True(c.ReleaseLock(LockScope.Machine, "licenses", tokens[1], out bool released, out msg) && released, msg);
            Assert.True(c.TryAcquireSlot(LockScope.Machine, "licenses", 3, "Robot 4", 60, out bool now, out _, out holders, out msg), msg);
            Assert.Equal((true, 3), (now, holders));
            Assert.Contains("licenses.1.2.lease", Files());                                                 // the freed slot's next generation
        }

        [Fact]
        public void ASlotTakenElsewhereDuringTheAcquire_IsCountedInHolderCount()
        {
            // Two robots see an empty pool and take different slots at the same moment: each must report both holders, not its own reading.
            using Process self = Process.GetCurrentProcess();
            long start = MachineLocks.StartIdentity(self);
            MachineLocks.BeforeCreateForTests = path =>
            {
                MachineLocks.BeforeCreateForTests = null;                                                   // once: another robot takes slot 1 now
                var other = new LeaseRecord { Token = Guid.NewGuid().ToString("N"), Holder = "other robot", Kind = LockKind.Slot, Capacity = 3,
                    AcquiredUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddMinutes(1), Machine = Environment.MachineName, SessionId = 1,
                    ProcessId = self.Id, ProcessStart = start };
                File.WriteAllText(Path.Combine(folder, "race-pool.1.1.lease"), other.ToJson());
            };
            try
            {
                using var c = New();
                Assert.True(c.TryAcquireSlot(LockScope.Machine, "race-pool", 3, "me", 60, out bool acquired, out _, out int holderCount, out string m), m);
                Assert.Equal((true, 2), (acquired, holderCount));
            }
            finally { MachineLocks.BeforeCreateForTests = null; }
        }

        [Fact]
        public void KindAndCapacityMismatches_AreRefused_WhileHeld()
        {
            using var c = New();
            Assert.True(c.TryAcquireSlot(LockScope.Machine, "pool", 2, "a", 60, out _, out string token, out _, out string m), m);
            Assert.False(c.TryAcquireSlot(LockScope.Machine, "pool", 3, "b", 60, out _, out _, out _, out m));
            Assert.Equal("TryAcquireSlot failed: the resource is in use with a capacity of 2; every caller must give the same capacity.", m);
            Assert.False(c.TryAcquireLock(LockScope.Machine, "pool", "b", 60, out _, out _, out _, out m));
            Assert.Contains("in use as a slot pool", m);
            Assert.True(c.ReleaseLock(LockScope.Machine, "pool", token, out _, out m), m);
            Acquire(c, "pool");                                                                             // free again: any use
        }

        [Fact]
        public void ResourceNamesWithDotsAndDigits_DoNotSeeEachOthersFiles()
        {
            using var c = New();
            Acquire(c, "a.1");                                                                              // a.1.0.1.lease: resource "a.1", slot 0
            Acquire(c, "a");                                                                                // "a" is still free
            Acquire(c, "a.1.0");
            Assert.True(c.GetLockStatus(LockScope.Machine, "a", out bool held, out _, out _, out int count, out string m), m);
            Assert.Equal((true, 1), (held, count));
        }

        [Fact]
        public void AnExpiredLease_IsSupersededByTheNextGeneration_AndItsHolderLearnsItLostIt()
        {
            object owner = new object();
            AcquireResult first = MachineLocks.TryAcquire(folder, "job", LockKind.Lock, 1, "hung robot", TimeSpan.FromMilliseconds(400), owner);
            Assert.True(first.Acquired);
            Assert.False(MachineLocks.TryAcquire(folder, "job", LockKind.Lock, 1, "next", TimeSpan.FromSeconds(60), owner).Acquired);
            Thread.Sleep(600);
            AcquireResult second = MachineLocks.TryAcquire(folder, "job", LockKind.Lock, 1, "next", TimeSpan.FromSeconds(60), owner);
            Assert.True(second.Acquired);
            Assert.Equal(new[] { "job.0.1.lease", "job.0.2.lease" }, Files());
            Assert.False(MachineLocks.Renew(folder, "job", first.Token, TimeSpan.FromSeconds(60), out TimeSpan left));   // renewed False
            Assert.Equal(TimeSpan.Zero, left);
            Assert.False(MachineLocks.Release(folder, "job", first.Token));                                               // released False
            Assert.True(MachineLocks.Release(folder, "job", second.Token));
        }

        [Fact]
        public void ALeaseThatExpiredUntouched_CannotBeRenewedOrReleasedAsHeld()
        {
            // Nobody took it over: it is still the top generation, but it has expired, so the holder has lost it all the same.
            object owner = new object();
            AcquireResult lapsed = MachineLocks.TryAcquire(folder, "lapsed", LockKind.Lock, 1, "slow robot", TimeSpan.FromMilliseconds(300), owner);
            Thread.Sleep(500);
            Assert.False(MachineLocks.Renew(folder, "lapsed", lapsed.Token, TimeSpan.FromSeconds(60), out _));
            Assert.Equal(new[] { "lapsed.0.1.lease" }, Files());
            Assert.False(MachineLocks.Release(folder, "lapsed", lapsed.Token));                            // released False: it may have overlapped
            Assert.True(Record("lapsed.0.1.lease").Released);                                               // still tidied up as released
        }

        [Fact]
        public void RenewingInTime_KeepsTheLease_AndMovesItsExpiry()
        {
            object owner = new object();
            AcquireResult held = MachineLocks.TryAcquire(folder, "long", LockKind.Lock, 1, "worker", TimeSpan.FromMilliseconds(1500), owner);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 4; i++)
            {
                Thread.Sleep(500);
                Assert.True(MachineLocks.Renew(folder, "long", held.Token, TimeSpan.FromMilliseconds(1500), out TimeSpan left));
                Assert.Equal(TimeSpan.FromMilliseconds(1500), left);
            }
            Assert.True(clock.ElapsedMilliseconds > 1500);
            Assert.False(MachineLocks.TryAcquire(folder, "long", LockKind.Lock, 1, "other", TimeSpan.FromSeconds(60), owner).Acquired);
            Assert.Equal(new[] { "long.0.1.lease" }, Files());                                              // renewed in place
            Assert.True(MachineLocks.Release(folder, "long", held.Token));
        }

        /// <summary>Writes a lease file as another robot would have.</summary>
        private void WriteLease(string file, string holder, int processId, long startTicks, string machine = null, TimeSpan? lease = null)
        {
            Directory.CreateDirectory(folder);
            DateTime now = DateTime.UtcNow;
            var record = new LeaseRecord
            {
                Token = Guid.NewGuid().ToString("N"), Holder = holder, Kind = LockKind.Lock, Capacity = 1, AcquiredUtc = now,
                ExpiresUtc = now + (lease ?? TimeSpan.FromHours(1)), Machine = machine ?? Environment.MachineName, SessionId = 1,
                ProcessId = processId, ProcessStart = startTicks
            };
            File.WriteAllText(Path.Combine(folder, file), record.ToJson());
        }

        [Fact]
        public void OldGenerations_AreDeletedOnlyOnceTheirSuccessorIsFiveMinutesOld()
        {
            using var c = New();
            for (int i = 0; i < 3; i++)
            {
                string token = Acquire(c, "cleanup");
                Assert.True(c.ReleaseLock(LockScope.Machine, "cleanup", token, out _, out string m), m);
            }
            Assert.Equal(new[] { "cleanup.0.1.lease", "cleanup.0.2.lease", "cleanup.0.3.lease" }, Files());   // all young: nothing deleted
            DateTime old = DateTime.UtcNow.AddMinutes(-(MachineLocks.SafeDeleteMinutes + 1));
            File.SetLastWriteTimeUtc(Path.Combine(folder, "cleanup.0.2.lease"), old);                          // 1's successor is old; 2's is not
            Acquire(c, "cleanup");
            Assert.Equal(new[] { "cleanup.0.2.lease", "cleanup.0.3.lease", "cleanup.0.4.lease" }, Files());
        }

        [Fact]
        public void Cleanup_WorksUpFromTheOldest_StopsAtAYoungSuccessor_AndIsCappedPerCall()
        {
            using var c = New();
            for (int i = 0; i < 45; i++)
            {
                string token = Acquire(c, "backlog");
                Assert.True(c.ReleaseLock(LockScope.Machine, "backlog", token, out _, out string m), m);
            }
            DateTime old = DateTime.UtcNow.AddMinutes(-(MachineLocks.SafeDeleteMinutes + 1));
            for (int g = 1; g <= 45; g++) File.SetLastWriteTimeUtc(Path.Combine(folder, "backlog.0." + g + ".lease"), old);
            File.SetLastWriteTimeUtc(Path.Combine(folder, "backlog.0.41.lease"), DateTime.UtcNow);            // renewed recently: young
            string held = Acquire(c, "backlog");                                                           // generation 46: one cleanup pass
            Assert.Equal(46 - MachineLocks.MaxCleanupPerCall, Files().Length);                              // at most 32 examined (and deleted)
            Assert.Contains("backlog.0.33.lease", Files());
            Assert.True(c.ReleaseLock(LockScope.Machine, "backlog", held, out _, out string msg), msg);   // the release is the next pass
            var left = Files().Where(f => f.StartsWith("backlog.")).ToArray();
            Assert.Contains("backlog.0.40.lease", left);                                                    // its successor 41 is young: the walk stops there
            Assert.Contains("backlog.0.41.lease", left);
            Assert.DoesNotContain("backlog.0.39.lease", left);                                              // 33 to 39 went on the next pass
        }

        [Fact]
        public void TheTop_IsFoundByName_EvenWhenTheListingMissesIt()
        {
            // A listing may miss files created or deleted while it runs; the stress test found two holders when a robot trusted it. Here the
            // listing misses generations 2 and 3 of 3: the lookup by name must still find 3, so the robot sees the holder and never fills a gap.
            object owner = new object();
            AcquireResult r1 = MachineLocks.TryAcquire(folder, "probe", LockKind.Lock, 1, "a", TimeSpan.FromSeconds(60), owner);
            Assert.True(MachineLocks.Release(folder, "probe", r1.Token));
            AcquireResult r2 = MachineLocks.TryAcquire(folder, "probe", LockKind.Lock, 1, "b", TimeSpan.FromSeconds(60), owner);
            Assert.True(MachineLocks.Release(folder, "probe", r2.Token));
            AcquireResult holder = MachineLocks.TryAcquire(folder, "probe", LockKind.Lock, 1, "c", TimeSpan.FromSeconds(60), owner);
            Assert.Contains("probe.0.3.lease", Files());
            MachineLocks.HideFromListingForTests = path => path.EndsWith("probe.0.2.lease") || path.EndsWith("probe.0.3.lease");
            try
            {
                AcquireResult other = MachineLocks.TryAcquire(folder, "probe", LockKind.Lock, 1, "d", TimeSpan.FromSeconds(60), new object());
                Assert.Equal((false, "c"), (other.Acquired, other.CurrentHolder));
            }
            finally { MachineLocks.HideFromListingForTests = null; }
            Assert.Equal(new[] { "probe.0.1.lease", "probe.0.2.lease", "probe.0.3.lease" }, Files());       // no gap was filled, nothing added
            Assert.True(MachineLocks.Release(folder, "probe", holder.Token));
        }

        [Fact]
        public void AHolderWhoseProcessHasEnded_IsFreeAtOnce_WithoutWaitingForItsLease()
        {
            int pid;
            long start;
            using (Process ended = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false }))
            {
                pid = ended.Id;
                try { start = MachineLocks.StartIdentity(ended); } catch (IOException) { start = 1; } catch (InvalidOperationException) { start = 1; }
                ended.WaitForExit();
            }
            WriteLease("crashed.0.7.lease", "crashed robot", pid, start);                                   // an hour left on the lease
            using var c = New();
            Acquire(c, "crashed", "next robot");
            Assert.Contains("crashed.0.8.lease", Files());
        }

        [Fact]
        public void AReusedProcessId_IsNotTakenForTheOriginalHolder()
        {
            using Process self = Process.GetCurrentProcess();
            WriteLease("reused.0.1.lease", "old robot", self.Id, MachineLocks.StartIdentity(self) - 1);      // same ID, another start
            using var c = New();
            Acquire(c, "reused");
        }

        [Fact]
        public async System.Threading.Tasks.Task AnotherRunningProcess_WithTheRecordedIdButAnotherStartTime_IsNotTheHolder()
        {
            // A process ID is reused once its process ends. The lease records the start time too, so a live process that merely has the same
            // ID does not keep a dead holder's lease.
            using Process other = StartHost("hold", folder, "other-process-lock", "3600");
            string line;
            try { line = await other.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(1)); }
            catch (TimeoutException) { other.Kill(true); throw new Xunit.Sdk.XunitException("the child did not start within a minute"); }
            Assert.Equal("HELD", line);
            try
            {
                long started = MachineLocks.StartIdentity(other);
                WriteLease("same-id-other-start.0.1.lease", "dead robot", other.Id, started - 1);           // one unit off is another process
                WriteLease("same-id-same-start.0.1.lease", "live robot", other.Id, started);
                using var c = New();
                Acquire(c, "same-id-other-start");                                                          // the ID was reused: free
                Assert.True(c.TryAcquireLock(LockScope.Machine, "same-id-same-start", "me", 60, out bool acquired, out _, out string current, out string m), m);
                Assert.Equal((false, "live robot"), (acquired, current));                                  // the same process: held
            }
            finally { other.Kill(true); other.WaitForExit(); }
        }

        [Fact]
        public void TheStartIdentity_IsExactAndStable_ForTheSameProcess()
        {
            using Process self = Process.GetCurrentProcess();
            long first = MachineLocks.StartIdentity(self);
            Thread.Sleep(50);
            using Process again = Process.GetProcessById(self.Id);
            Assert.Equal(first, MachineLocks.StartIdentity(again));                                        // exact, no tolerance needed
        }

        [Fact]
        public void ALeaseFileThisAccountMayNotRead_IsHeld_NotSuperseded()
        {
            // Another account's lease this robot cannot read may be live: superseding it after the short grace for half-written files would
            // give the lock to two robots. It is held until it is older than the longest possible lease.
            if (!OperatingSystem.IsLinux()) return;                                                         // permissions are set with Unix modes here
            Directory.CreateDirectory(folder);
            string secret = Path.Combine(folder, "secret.0.5.lease");
            WriteLease("secret.0.5.lease", "other account", 999999, 1);
            File.SetLastWriteTimeUtc(secret, DateTime.UtcNow.AddSeconds(-(MachineLocks.UnreadableGraceSeconds + 5)));
            File.SetUnixFileMode(secret, UnixFileMode.None);
            try
            {
                try { File.ReadAllText(secret); return; } catch (UnauthorizedAccessException) { }        // running as root: cannot test
                using var c = New();
                Assert.True(c.TryAcquireLock(LockScope.Machine, "secret", "me", 60, out bool acquired, out _, out string current, out string m), m);
                Assert.Equal((false, "(an unreadable lease)"), (acquired, current));
                File.SetLastWriteTimeUtc(secret, DateTime.UtcNow.AddSeconds(-(LockLimits.MaxLeaseSeconds + 5)));
                Acquire(c, "secret");                                                                       // older than any lease: superseded
            }
            finally { File.SetUnixFileMode(secret, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        }

        [Fact]
        public void TokenOperations_UseTheFolderTheLeaseWasTakenIn_AfterTheFolderChanges()
        {
            using var c = New();
            string token = Acquire(c, "moved");
            string other = folder + "-other";
            try
            {
                Assert.True(c.ConfigureLockFolder(other, out string m), m);
                Assert.True(c.RenewLock(LockScope.Machine, "moved", token, 120, out bool renewed, out _, out m), m);
                Assert.True(renewed);
                Assert.True(c.ReleaseLock(LockScope.Machine, "moved", token, out bool released, out m), m);
                Assert.True(released);
                Assert.True(Record("moved.0.1.lease").Released);                                            // released in the original folder
            }
            finally { if (Directory.Exists(other)) Directory.Delete(other, true); }
        }

        [Fact]
        public void ATokenGivenWithTheWrongResource_IsNotALostLease_AndStaysTracked()
        {
            var c = New();
            string token = Acquire(c, "right");
            Assert.True(c.ReleaseLock(LockScope.Machine, "wrong", token, out bool released, out string m), m);
            Assert.False(released);
            Assert.True(c.RenewLock(LockScope.Machine, "wrong", token, 60, out bool renewed, out _, out m), m);
            Assert.False(renewed);
            Assert.Equal(1, MachineLocks.HeldByForTests(c));                                               // still tracked ...
            c.Dispose();
            Assert.True(Record("right.0.1.lease").Released);                                                // ... so disposal releases it
        }

        [Fact]
        public void Presence_TellsAMissingFileFromAPresentOne()
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "here.0.1.lease");
            File.WriteAllText(path, "x");
            Assert.True(MachineLocks.Present(path));
            Assert.False(MachineLocks.Present(Path.Combine(folder, "gone.0.1.lease")));
            Assert.False(MachineLocks.Present(Path.Combine(folder, "no-such-folder", "x.0.1.lease")));
        }

        [Fact]
        public void AFolderThatCanBeListedButNotAccessed_KeepsItsLeasesHeld_AndDoesNotLoop()
        {
            // Read without execute on a Linux folder: names can be listed, but every file (and every name) is "access denied". Its lease must
            // count as held (File.Exists would say it is not there), and looking up the next generation must end.
            if (!OperatingSystem.IsLinux()) return;
            using (var c = New()) Acquire(c, "listed", "other account");
            var clock = Stopwatch.StartNew();
            File.SetUnixFileMode(folder, UnixFileMode.UserRead);
            try
            {
                try { File.GetAttributes(Path.Combine(folder, "listed.0.1.lease")); return; } catch (UnauthorizedAccessException) { }   // root: cannot test
                Assert.True(MachineLocks.Present(Path.Combine(folder, "listed.0.1.lease")));
                Assert.False(MachineLocks.Present(Path.Combine(folder, "listed.0.2.lease")));
                using var viewer = New();
                Assert.True(viewer.GetLockStatus(LockScope.Machine, "listed", out bool held, out string holders, out _, out _, out string m), m);
                Assert.Equal((true, "(an unreadable lease)"), (held, holders));
                Assert.InRange(clock.ElapsedMilliseconds, 0, 10000);
            }
            finally { File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        }

        [Fact]
        public void AHeldSlotThatCannotBeRead_BlocksTheRestOfThePool()
        {
            // Its kind and capacity are unknown: taking another slot beside it could put a pool lease next to a lock.
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "pool2.0.1.lease"), "");                                 // young and unreadable: held
            using var c = New();
            Assert.True(c.TryAcquireSlot(LockScope.Machine, "pool2", 3, "me", 60, out bool acquired, out string token, out int count, out string m), m);
            Assert.Equal((false, (string)null, 1), (acquired, token, count));
            Assert.Equal(new[] { "pool2.0.1.lease" }, Files());                                            // no slot 1 created beside it
            Assert.True(c.GetLocksJson(LockScope.Machine, out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement entry = doc.RootElement.GetProperty("locks").EnumerateArray().Single();
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("kind").ValueKind);                         // unknown, and said so
            JsonElement holder = entry.GetProperty("holders").EnumerateArray().Single();
            Assert.Equal("(an unreadable lease)", holder.GetProperty("holder").GetString());
            Assert.Equal(JsonValueKind.Null, holder.GetProperty("expiresInSeconds").ValueKind);
        }

        [Fact]
        public void Cleanup_TouchesOnlyItsOwnSlot()
        {
            using var c = New();
            var tokens = new List<string>();
            for (int i = 0; i < 11; i++)
            {
                Assert.True(c.TryAcquireSlot(LockScope.Machine, "wide", 11, "r" + i, 60, out bool acquired, out string token, out _, out string m) && acquired, m);
                tokens.Add(token);
            }
            Assert.True(c.ReleaseLock(LockScope.Machine, "wide", tokens[10], out _, out string msg), msg);       // slot 10: generation 1 released
            Assert.True(c.TryAcquireSlot(LockScope.Machine, "wide", 11, "again", 60, out bool again, out _, out _, out msg) && again, msg);   // slot 10: generation 2
            File.SetLastWriteTimeUtc(Path.Combine(folder, "wide.10.2.lease"), DateTime.UtcNow.AddMinutes(-(MachineLocks.SafeDeleteMinutes + 1)));
            Assert.True(c.ReleaseLock(LockScope.Machine, "wide", tokens[1], out _, out msg), msg);                // cleanup of slot 1
            Assert.Contains("wide.10.1.lease", Files());                                                     // slot 10 is not slot 1's to clean
        }

        [Fact]
        public void LeasesThatEndedUnvisited_StopCountingAgainstTheLimit()
        {
            object owner = new object();
            for (int i = 0; i < LockLimits.MaxHeldPerInstance; i++)
                Assert.True(MachineLocks.TryAcquire(folder, "bulk-" + i, LockKind.Lock, 1, "one-shot", TimeSpan.FromMilliseconds(1500), owner).Acquired);
            Assert.Equal(LockLimits.MaxHeldPerInstance, MachineLocks.HeldByForTests(owner));
            Thread.Sleep(1700);                                                                             // all expired, none renewed or released
            AcquireResult next = MachineLocks.TryAcquire(folder, "bulk-next", LockKind.Lock, 1, "one-shot", TimeSpan.FromSeconds(60), owner);
            Assert.True(next.Acquired, next.Problem);
            Assert.Equal(1, MachineLocks.HeldByForTests(owner));
        }

        [Fact]
        public void TheLeaseStartsWhenItsFileIsCreated_NotWhenReadingBegan()
        {
            // A slow scan must not eat into the lease: a 1 s lease read for 1.2 s used to be expired by the time it was returned.
            object owner = new object();
            AcquireResult warm = MachineLocks.TryAcquire(folder, "slow", LockKind.Lock, 1, "a", TimeSpan.FromSeconds(60), owner);
            Assert.True(MachineLocks.Release(folder, "slow", warm.Token));                                  // a file for the listing to find
            int calls = 0;
            MachineLocks.HideFromListingForTests = path => { if (Interlocked.Increment(ref calls) == 1) Thread.Sleep(1200); return false; };
            AcquireResult slow;
            try { slow = MachineLocks.TryAcquire(folder, "slow", LockKind.Lock, 1, "b", TimeSpan.FromMilliseconds(1000), owner); }
            finally { MachineLocks.HideFromListingForTests = null; }
            Assert.True(slow.Acquired);
            Assert.True(MachineLocks.Renew(folder, "slow", slow.Token, TimeSpan.FromSeconds(60), out _));  // still live when returned
            Assert.True(MachineLocks.Release(folder, "slow", slow.Token));
        }

        [Fact]
        public void AGenerationCreatedBelowTheTop_NeverHoldsTheLock()
        {
            // Force the gap the review describes: generation 2 gone, generation 3 held but missed by the listing. The robot creates 2, sees 3
            // above it, and gives 2 up at once instead of reporting a second holder.
            object a = new object(), b = new object();
            for (int i = 0; i < 2; i++)
                Assert.True(MachineLocks.Release(folder, "gap", MachineLocks.TryAcquire(folder, "gap", LockKind.Lock, 1, "earlier", TimeSpan.FromSeconds(60), a).Token));
            AcquireResult holder = MachineLocks.TryAcquire(folder, "gap", LockKind.Lock, 1, "holder", TimeSpan.FromSeconds(60), a);
            Assert.Contains("gap.0.3.lease", Files());
            File.Delete(Path.Combine(folder, "gap.0.2.lease"));
            MachineLocks.HideFromListingForTests = path => path.EndsWith("gap.0.3.lease");
            AcquireResult late;
            try { late = MachineLocks.TryAcquire(folder, "gap", LockKind.Lock, 1, "stale reader", TimeSpan.FromSeconds(60), b); }
            finally { MachineLocks.HideFromListingForTests = null; }
            Assert.Equal((false, "holder"), (late.Acquired, late.CurrentHolder));
            Assert.True(Record("gap.0.2.lease").Released);                                                  // the gap was filled, then given up
            Assert.Equal(0, MachineLocks.HeldByForTests(b));
            Assert.True(MachineLocks.Release(folder, "gap", holder.Token));
        }

        [Fact]
        public void AFailureToCreateTheLeaseFile_FailsTheCall_NotALostRace()
        {
            using var c = New();
            MachineLocks.BeforeCreateForTests = path => throw new IOException("the disk is full");
            try
            {
                Assert.False(c.TryAcquireLock(LockScope.Machine, "full-disk", "me", 60, out bool acquired, out string token, out _, out string m));
                Assert.Equal((false, (string)null), (acquired, token));
                Assert.Equal("TryAcquireLock failed: a lease file could not be created in the lock folder (it may have been removed, or the disk is full or failing).", m);
            }
            finally { MachineLocks.BeforeCreateForTests = null; }
        }

        [Fact]
        public void AReleaseFollowedAtOnceByTheNextHolder_IsStillReportedReleased()
        {
            // The first version of the re-check looked after marking the lease released; a robot taking the next generation at that moment made
            // a clean release look lost (the stress test showed it). The check runs before the release, so this handover is a clean release.
            object owner = new object();
            AcquireResult held = MachineLocks.TryAcquire(folder, "handover", LockKind.Lock, 1, "first", TimeSpan.FromSeconds(60), owner);
            MachineLocks.AfterReleaseForTests = path =>
            {
                MachineLocks.AfterReleaseForTests = null;                                                   // once: the next robot takes over now
                using Process self = Process.GetCurrentProcess();
                WriteLease("handover.0.2.lease", "second", self.Id, MachineLocks.StartIdentity(self));
            };
            try { Assert.True(MachineLocks.Release(folder, "handover", held.Token)); }
            finally { MachineLocks.AfterReleaseForTests = null; }
        }

        [Fact]
        public void AReleaseRacingATakeover_ReportsTheLeaseLost()
        {
            object owner = new object();
            AcquireResult held = MachineLocks.TryAcquire(folder, "raced", LockKind.Lock, 1, "holder", TimeSpan.FromSeconds(60), owner);
            MachineLocks.BeforeReleaseCheckForTests = path =>
            {
                MachineLocks.BeforeReleaseCheckForTests = null;                                             // once: another robot takes over now
                using Process self = Process.GetCurrentProcess();
                WriteLease("raced.0.2.lease", "taker", self.Id, MachineLocks.StartIdentity(self));
            };
            try { Assert.False(MachineLocks.Release(folder, "raced", held.Token)); }                       // released False: it had been lost
            finally { MachineLocks.BeforeReleaseCheckForTests = null; }
        }

        [Fact]
        public void ARunningHolder_OrOneThisMachineCannotCheck_KeepsItsLease()
        {
            using Process self = Process.GetCurrentProcess();
            WriteLease("running.0.1.lease", "live robot", self.Id, MachineLocks.StartIdentity(self));
            WriteLease("remote.0.1.lease", "other machine", 999999, 1, machine: "SOME-OTHER-HOST");
            using var c = New();
            Assert.True(c.TryAcquireLock(LockScope.Machine, "running", "me", 60, out bool acquired, out _, out string current, out string m), m);
            Assert.Equal((false, "live robot"), (acquired, current));
            Assert.True(c.TryAcquireLock(LockScope.Machine, "remote", "me", 60, out acquired, out _, out current, out m), m);
            Assert.Equal((false, "other machine"), (acquired, current));                                    // cannot check: the lease expiry decides
        }

        [Fact]
        public void AYoungUnreadableLeaseFile_CountsAsHeld_BecauseItMayStillBeBeingWritten()
        {
            // An exclusive create makes the file before its content is written; superseding it then would give the lock to two robots.
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "fresh.0.3.lease"), "");
            using var c = New();
            Assert.True(c.TryAcquireLock(LockScope.Machine, "fresh", "me", 60, out bool acquired, out _, out string current, out string m), m);
            Assert.Equal((false, "(an unreadable lease)"), (acquired, current));
            Assert.Equal(new[] { "fresh.0.3.lease" }, Files());
        }

        [Fact]
        public void AnOldUnreadableLeaseFile_IsSuperseded_NotTrusted()
        {
            Directory.CreateDirectory(folder);
            string broken = Path.Combine(folder, "broken.0.3.lease");
            File.WriteAllText(broken, "{ not a lease");
            File.SetLastWriteTimeUtc(broken, DateTime.UtcNow.AddSeconds(-(MachineLocks.UnreadableGraceSeconds + 5)));
            using var c = New();
            Acquire(c, "broken");
            Assert.Contains("broken.0.4.lease", Files());
        }

        [Fact]
        public void TwoRobotsRacingForTheSameGeneration_OnlyOneWins()
        {
            // Both read generation 1 as expired and try to create generation 2; the create is atomic, so one of them loses and is told who won.
            object a = new object(), b = new object();
            Assert.True(MachineLocks.TryAcquire(folder, "race", LockKind.Lock, 1, "old", TimeSpan.FromMilliseconds(200), a).Acquired);
            Thread.Sleep(400);
            var results = new AcquireResult[8];
            var threads = Enumerable.Range(0, 8).Select(i => new Thread(() =>
                results[i] = MachineLocks.TryAcquire(folder, "race", LockKind.Lock, 1, "racer " + i, TimeSpan.FromSeconds(60), i % 2 == 0 ? a : b))).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            Assert.Single(results, r => r.Acquired);
            Assert.All(results.Where(r => !r.Acquired), r => Assert.StartsWith("racer ", r.CurrentHolder));
        }

        [Fact]
        public void AcquireLock_WaitsForARelease_ByPolling_AndGivesUpAtTheDeadline()
        {
            using var c = New();
            string token = Acquire(c, "busy", "first");
            var releaser = new Thread(() => { Thread.Sleep(300); c.ReleaseLock(LockScope.Machine, "busy", token, out _, out _); });
            releaser.Start();
            var clock = Stopwatch.StartNew();
            Assert.True(c.AcquireLock(LockScope.Machine, "busy", "second", 60, 10000, out bool acquired, out string mine, out _, out string m), m);
            releaser.Join();
            Assert.True(acquired);
            Assert.InRange(clock.ElapsedMilliseconds, 100, 5000);
            clock.Restart();
            Assert.True(c.AcquireLock(LockScope.Machine, "busy", "third", 60, 400, out acquired, out _, out string current, out m), m);
            Assert.Equal((false, "second"), (acquired, current));
            Assert.InRange(clock.ElapsedMilliseconds, 350, 5000);
            Assert.True(c.AcquireLock(LockScope.Machine, NewFree(), "zero", 60, 0, out acquired, out _, out _, out m) && acquired, m);   // 0: one attempt
            Assert.True(c.ReleaseLock(LockScope.Machine, "busy", mine, out _, out m), m);
        }

        private static string NewFree() => "free-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        [Fact]
        public void ForceRelease_AddsAReleasedGeneration_AndHoldersLearnTheyLostTheLease()
        {
            using var c = New();
            string token = Acquire(c, "stuck");
            Assert.True(c.ForceReleaseLock(LockScope.Machine, "stuck", true, out int count, out string m), m);
            Assert.Equal(1, count);
            Assert.True(Record("stuck.0.2.lease").Released);
            Assert.True(c.RenewLock(LockScope.Machine, "stuck", token, 60, out bool renewed, out _, out m) && !renewed, m);
            Assert.True(c.ReleaseLock(LockScope.Machine, "stuck", token, out bool released, out m) && !released, m);
            Acquire(c, "stuck");
            Assert.Contains("stuck.0.3.lease", Files());                                                    // after the forced (released) generation 2
        }

        [Fact]
        public void Disposing_ReleasesThatInstancesMachineLocks()
        {
            var c = New();
            Acquire(c, "held-by-disposed");
            c.Dispose();
            Assert.True(Record("held-by-disposed.0.1.lease").Released);
            using var other = New();
            Acquire(other, "held-by-disposed");
        }

        [Fact]
        public void AnAcquireAfterDisposal_IsRefused_InTheMachineScopeToo()
        {
            var c = New();
            c.Dispose();
            AcquireResult late = MachineLocks.TryAcquire(folder, "late", LockKind.Lock, 1, "late", TimeSpan.FromSeconds(60), c);
            Assert.Equal((false, "the component has been disposed"), (late.Acquired, late.Problem));
            Assert.Equal(0, MachineLocks.HeldByForTests(c));
            Assert.False(Directory.Exists(folder) && Files().Length > 0);
        }

        [Fact]
        public void TheLocksJson_ShowsHoldersWithMachineSessionAndProcess_ButNeverTokens()
        {
            using var c = New();
            string token = Acquire(c, "reporting", "Robot 9");
            Assert.True(c.GetLocksJson(LockScope.Machine, out string json, out string m), m);
            Assert.DoesNotContain(token, json);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal("Machine", doc.RootElement.GetProperty("scope").GetString());
            JsonElement entry = doc.RootElement.GetProperty("locks").EnumerateArray().Single();
            Assert.Equal(("reporting", "Lock", 1), (entry.GetProperty("resource").GetString(), entry.GetProperty("kind").GetString(), entry.GetProperty("capacity").GetInt32()));
            JsonElement holder = entry.GetProperty("holders").EnumerateArray().Single();
            Assert.Equal(("Robot 9", Environment.MachineName, Environment.ProcessId), (holder.GetProperty("holder").GetString(), holder.GetProperty("machine").GetString(), holder.GetProperty("processId").GetInt32()));
        }

        [Fact]
        public void AFolderThatCannotBeCreated_FailsWithAMessage_NotAnException()
        {
            string file = Path.Combine(Path.GetTempPath(), "rl-file-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(file, "x");                                                                   // a file where the folder should be
            try
            {
                using var c = new ResourceLockUtils();
                Assert.True(c.ConfigureLockFolder(Path.Combine(file, "locks"), out string m), m);
                Assert.False(c.TryAcquireLock(LockScope.Machine, "x", "me", 60, out bool acquired, out string token, out _, out m));
                Assert.Equal((false, (string)null), (acquired, token));
                Assert.Equal("TryAcquireLock failed: the lock folder does not exist and cannot be created; set a usable folder with ConfigureLockFolder (ValidateLockFolder checks one).", m);
                Assert.True(c.GetLockStatus(LockScope.Machine, "x", out bool held, out _, out _, out _, out m) && !held, m);
            }
            finally { File.Delete(file); }
        }

        // ------------------------------------------------------------------ several processes

        private static string TestAssembly => typeof(StressHost).Assembly.Location;

        /// <summary>A child's whole output, or a failed test (the child killed) when it has not finished in time: a broken build must fail, not hang.</summary>
        private static string OutputWithin(Process host, TimeSpan limit)
        {
            var output = host.StandardOutput.ReadToEndAsync();
            if (!host.WaitForExit((int)limit.TotalMilliseconds))
            {
                try { host.Kill(true); } catch (InvalidOperationException) { }
                Assert.Fail("a child process did not finish within " + limit.TotalSeconds + " s");
            }
            Assert.True(host.ExitCode == 0, "a child process exited with code " + host.ExitCode);        // a crash fails the test
            return output.GetAwaiter().GetResult();
        }

        private static Process StartHost(params string[] args)
        {
            var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add(TestAssembly);
            info.ArgumentList.Add("stress-host");
            foreach (string a in args) info.ArgumentList.Add(a);
            return Process.Start(info);
        }

        [Fact]
        public void SeveralProcesses_ContendingForOneLock_NeverHoldItAtTheSameTime()
        {
            string markers = Path.Combine(folder, "markers");
            Directory.CreateDirectory(markers);
            var hosts = Enumerable.Range(0, 4).Select(_ => StartHost("contend", folder, "shared-login", "40", markers)).ToList();
            List<string> outputs;
            try { outputs = hosts.Select(h => OutputWithin(h, TimeSpan.FromMinutes(2))).ToList(); }
            finally
            {
                foreach (Process h in hosts) { try { if (!h.HasExited) h.Kill(true); } catch (InvalidOperationException) { } h.Dispose(); }
            }
            string all = string.Join("\n", outputs);
            Assert.DoesNotContain("VIOLATION", all);
            Assert.DoesNotContain("ERROR", all);
            Assert.All(outputs, o => Assert.Contains("DONE 40", o));
        }

        [Fact]
        public async System.Threading.Tasks.Task AKilledHolder_FreesItsLockAtOnce_ForTheNextProcess()
        {
            using Process holder = StartHost("hold", folder, "killed-holder", "3600");                      // an hour-long lease
            string line;
            try { line = await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(1)); }
            catch (TimeoutException) { holder.Kill(true); throw new Xunit.Sdk.XunitException("the holder did not start within a minute"); }
            Assert.Equal("HELD", line);
            using var c = New();
            Assert.True(c.TryAcquireLock(LockScope.Machine, "killed-holder", "me", 60, out bool acquired, out _, out string current, out string m), m);
            Assert.False(acquired);
            Assert.StartsWith("holder-", current);
            holder.Kill();
            holder.WaitForExit();
            Acquire(c, "killed-holder", "me");                                                              // no waiting for the hour
        }
    }
}
