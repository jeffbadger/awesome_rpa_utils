using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ResourceLockAutomation.Tests
{
    /// <summary>ValidateLockFolder: each check and each warning, in its own temporary folder.</summary>
    public sealed class FolderCheckTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "rl-check-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    if (OperatingSystem.IsLinux()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    Directory.Delete(folder, true);
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private (bool usable, JsonElement report) Validate(string path = null)
        {
            using var c = new ResourceLockUtils();
            Assert.True(c.ConfigureLockFolder(path ?? folder, out string m), m);
            Assert.True(c.ValidateLockFolder(out bool usable, out string json, out m), m);          // the check always runs; usable is its answer
            Assert.Null(m);
            return (usable, JsonDocument.Parse(json).RootElement.Clone());
        }

        private static string[] Warnings(JsonElement report) => report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();

        private static bool Can(JsonElement report, string name) => report.GetProperty(name).GetBoolean();

        [Fact]
        public void AFreshFolder_IsCreated_EveryStepWorks_AndNothingIsLeftBehind()
        {
            var (usable, report) = Validate();
            Assert.True(usable);
            Assert.Equal(folder, report.GetProperty("folder").GetString());
            Assert.True(Can(report, "folderCreated"));
            foreach (string step in new[] { "canOpenFolder", "canList", "canCreate", "canReadOwn", "canRewriteOwn", "canDeleteOwn" }) Assert.True(Can(report, step), step);
            Assert.Equal((0, 0, 0), (report.GetProperty("leaseFiles").GetInt32(), report.GetProperty("unreadableByThisAccount").GetInt32(), report.GetProperty("damaged").GetInt32()));
            Assert.Equal("untested", report.GetProperty("cleanupOfOtherRobotsFiles").GetString());
            Assert.Empty(Warnings(report));
            Assert.Empty(Directory.GetFiles(folder));                                                   // the probe file was removed
            Assert.False(Can(Validate().report, "folderCreated"));                                      // second time: it exists
        }

        [Fact]
        public void AFolderThatCannotBeCreated_IsNotUsable_AndSaysWhatToDo()
        {
            string file = folder + ".file";
            File.WriteAllText(file, "x");                                                               // a file where the folder should be
            try
            {
                var (usable, report) = Validate(Path.Combine(file, "locks"));
                Assert.False(usable);
                Assert.False(Can(report, "canOpenFolder"));
                Assert.Contains(Warnings(report), w => w.StartsWith("The lock folder does not exist and this account cannot create it."));
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void AFolderThisAccountCannotWriteTo_IsNotUsable()
        {
            if (!OperatingSystem.IsLinux()) return;                                                     // permissions set with Unix modes here
            Directory.CreateDirectory(folder);
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try { File.WriteAllText(Path.Combine(folder, "root-check"), "x"); File.Delete(Path.Combine(folder, "root-check")); return; }   // root: cannot test
            catch (UnauthorizedAccessException) { }
            var (usable, report) = Validate();
            Assert.False(usable);
            Assert.True(Can(report, "canList"));
            Assert.False(Can(report, "canCreate"));
            Assert.Contains(Warnings(report), w => w.StartsWith("This account cannot create files in the lock folder"));
        }

        [Fact]
        public void LeaseFilesThisAccountCannotRead_AreCountedAndExplained()
        {
            if (!OperatingSystem.IsLinux()) return;
            Directory.CreateDirectory(folder);
            string secret = Path.Combine(folder, "secret.0.1.lease");
            File.WriteAllText(secret, "{}");
            File.SetUnixFileMode(secret, UnixFileMode.None);
            try
            {
                try { File.ReadAllText(secret); return; } catch (UnauthorizedAccessException) { }        // root: cannot test
                var (usable, report) = Validate();
                Assert.True(usable);                                                                    // locks still work; it is a warning
                Assert.Equal((1, 1), (report.GetProperty("leaseFiles").GetInt32(), report.GetProperty("unreadableByThisAccount").GetInt32()));
                Assert.Contains(Warnings(report), w => w.StartsWith("1 lease file(s) cannot be read by this account"));
            }
            finally { File.SetUnixFileMode(secret, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        }

        [Fact]
        public void DamagedLeaseFiles_AreReported_NotMovedOrDeleted()
        {
            Directory.CreateDirectory(folder);
            string old = Path.Combine(folder, "broken.0.1.lease"), young = Path.Combine(folder, "fresh.0.1.lease");
            File.WriteAllText(old, "{ not a lease");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-1));
            File.WriteAllText(young, "");                                                               // may still be being written: not damaged
            var (usable, report) = Validate();
            Assert.True(usable);
            Assert.Equal((2, 1), (report.GetProperty("leaseFiles").GetInt32(), report.GetProperty("damaged").GetInt32()));
            Assert.Contains(Warnings(report), w => w.StartsWith("1 lease file(s) are damaged"));
            Assert.True(File.Exists(old) && File.Exists(young));                                        // reported, left in place
        }

        [Fact]
        public void CleanupOfOtherRobotsFiles_IsTriedOnlyOnAGenerationTheProtocolAllowsDeleting()
        {
            Directory.CreateDirectory(folder);
            DateTime now = DateTime.UtcNow;
            void Write(string name, int processId, DateTime written)
            {
                var record = new LeaseRecord { Token = Guid.NewGuid().ToString("N"), Holder = "other robot", Kind = LockKind.Lock, Capacity = 1, AcquiredUtc = now,
                    ExpiresUtc = now.AddHours(1), Machine = Environment.MachineName, SessionId = 1, ProcessId = processId, ProcessStart = 1, Released = true };
                string path = Path.Combine(folder, name);
                File.WriteAllText(path, record.ToJson());
                File.SetLastWriteTimeUtc(path, written);
            }
            Write("young.0.1.lease", 999998, now.AddMinutes(-30));
            Write("young.0.2.lease", 999998, now);                                                     // successor too young: 1 must stay
            Assert.Equal("untested", Validate().report.GetProperty("cleanupOfOtherRobotsFiles").GetString());
            Assert.True(File.Exists(Path.Combine(folder, "young.0.1.lease")));

            Write("old.0.1.lease", 999998, now.AddMinutes(-30));
            Write("old.0.2.lease", 999998, now.AddMinutes(-(MachineLocks.SafeDeleteMinutes + 1)));   // successor old enough: 1 may go
            Assert.Equal("yes", Validate().report.GetProperty("cleanupOfOtherRobotsFiles").GetString());
            Assert.False(File.Exists(Path.Combine(folder, "old.0.1.lease")));
            Assert.True(File.Exists(Path.Combine(folder, "old.0.2.lease")));                           // the top is never touched
        }
    }
}
