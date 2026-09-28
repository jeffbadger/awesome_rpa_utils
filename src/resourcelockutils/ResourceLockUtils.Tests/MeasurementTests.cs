using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace ResourceLockAutomation.Tests
{
    /// <summary>
    /// Opt-in measurements (set RESOURCELOCK_MEASURE=1; RESOURCELOCK_MEASURE_FOLDER picks the file system for the Machine scope, default the
    /// temporary folder), for example <c>RESOURCELOCK_MEASURE=1 dotnet test -c Release --filter Measure_ --logger "console;verbosity=detailed"</c>.
    /// Each result line is written to the test output and, when RESOURCELOCK_MEASURE_OUT names a file, appended to it. Without the variable each
    /// test returns immediately.
    /// </summary>
    public sealed class MeasurementTests : IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly string folder;

        public MeasurementTests(ITestOutputHelper output)
        {
            this.output = output;
            string root = Environment.GetEnvironmentVariable("RESOURCELOCK_MEASURE_FOLDER") ?? Path.GetTempPath();
            folder = Path.Combine(root, "rl-measure-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static bool Enabled => Environment.GetEnvironmentVariable("RESOURCELOCK_MEASURE") == "1";

        private void Report(string line)
        {
            line += " folder=" + Path.GetDirectoryName(folder) + " runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " cores=" + Environment.ProcessorCount;
            string file = Environment.GetEnvironmentVariable("RESOURCELOCK_MEASURE_OUT");
            if (file != null) File.AppendAllText(file, line + Environment.NewLine);
            output.WriteLine(line);
        }

        private ResourceLockUtils New()
        {
            var c = new ResourceLockUtils();
            Assert.True(c.ConfigureLockFolder(folder, out string m), m);
            return c;
        }

        /// <summary>Microseconds per acquire-and-release pair, after a warm-up.</summary>
        private static double PairMicroseconds(ResourceLockUtils c, LockScope scope, string resource, int pairs)
        {
            for (int i = 0; i < 50; i++) Pair(c, scope, resource);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < pairs; i++) Pair(c, scope, resource);
            return clock.Elapsed.TotalMilliseconds * 1000 / pairs;
        }

        private static void Pair(ResourceLockUtils c, LockScope scope, string resource)
        {
            Assert.True(c.TryAcquireLock(scope, resource, "measure", 60, out bool acquired, out string token, out _, out string m) && acquired, m);
            Assert.True(c.ReleaseLock(scope, resource, token, out bool released, out m) && released, m);
        }

        [Fact]
        public void Measure_ProcessScope_AcquireReleasePair()
        {
            if (!Enabled) return;
            using var c = New();
            double us = PairMicroseconds(c, LockScope.Process, "measure-process-" + Guid.NewGuid().ToString("N"), 100000);
            Report($"processPair: pairs=100000 usPerPair={us:F2}");
        }

        [Fact]
        public void Measure_MachineScope_AcquireReleasePair()
        {
            if (!Enabled) return;
            using var c = New();
            double us = PairMicroseconds(c, LockScope.Machine, "measure-machine", 2000);
            Report($"machinePair: pairs=2000 usPerPair={us:F0} filesAfter={Directory.GetFiles(folder).Length}");
        }

        [Fact]
        public void Measure_MachineScope_BusyFolder()
        {
            // a folder as a busy server would have it: 50 other resources with 10 generations each, and 300 young generations of this lock
            if (!Enabled) return;
            using var c = New();
            for (int r = 0; r < 50; r++)
                for (int i = 0; i < 10; i++) Pair(c, LockScope.Machine, "other-" + r);
            for (int i = 0; i < 300; i++) Pair(c, LockScope.Machine, "busy");
            int files = Directory.GetFiles(folder).Length;
            double us = PairMicroseconds(c, LockScope.Machine, "busy", 500);
            Report($"machineBusyFolder: files={files} pairs=500 usPerPair={us:F0}");
        }

        [Fact]
        public async System.Threading.Tasks.Task Measure_MachineScope_EightProcessesContending()
        {
            if (!Enabled) return;
            string markers = Path.Combine(folder, "markers");
            Directory.CreateDirectory(markers);
            var clock = Stopwatch.StartNew();
            var hosts = Enumerable.Range(0, 8).Select(_ =>
            {
                var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, UseShellExecute = false };
                foreach (string a in new[] { typeof(StressHost).Assembly.Location, "stress-host", "contend", folder, "contended", "300", markers }) info.ArgumentList.Add(a);
                return Process.Start(info);
            }).ToList();
            var outputs = new List<string>();
            try
            {
                foreach (Process h in hosts)
                {
                    var read = h.StandardOutput.ReadToEndAsync();
                    Assert.True(h.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds), "a child did not finish within 10 minutes");
                    Assert.True(h.ExitCode == 0, "a child exited with code " + h.ExitCode);                // a crash is a failed measurement
                    outputs.Add(await read);
                }
            }
            finally
            {
                foreach (Process h in hosts) { try { if (!h.HasExited) h.Kill(true); } catch (InvalidOperationException) { } h.Dispose(); }
            }
            clock.Stop();
            string all = string.Join("\n", outputs);
            Assert.DoesNotContain("VIOLATION", all);
            Assert.DoesNotContain("ERROR", all);
            Assert.All(outputs, o => Assert.Contains("DONE 300", o));                                     // every child ran every iteration
            int waited = all.Split('\n').Count(l => l.Trim() == "WAITED");
            int acquisitions = 8 * 300 - waited;                                                          // a wait that ended acquired nothing
            Report($"machineContention: processes=8 attemptsEach=300 acquisitions={acquisitions} totalSeconds={clock.Elapsed.TotalSeconds:F1} acquisitionsPerSecond={acquisitions / clock.Elapsed.TotalSeconds:F0} waitsThatEnded={waited} (includes process start-up)");
        }

        [Fact]
        public void Measure_MachineScope_HandoverDelay()
        {
            // how long a waiting AcquireLock takes to notice a release, after it has already been waiting a while (its pauses have grown)
            if (!Enabled) return;
            var delays = new List<double>();
            for (int round = 0; round < 10; round++)
            {
                using var holder = New();
                using var waiter = New();
                string resource = "handover-" + round;
                Assert.True(holder.TryAcquireLock(LockScope.Machine, resource, "holder", 60, out _, out string token, out _, out string m), m);
                double acquiredAt = 0;
                var clock = Stopwatch.StartNew();
                var thread = new Thread(() =>
                {
                    Assert.True(waiter.AcquireLock(LockScope.Machine, resource, "waiter", 60, 30000, out bool acquired, out _, out _, out string wm) && acquired, wm);
                    acquiredAt = clock.Elapsed.TotalMilliseconds;
                });
                thread.Start();
                Thread.Sleep(3000 + round * 97);                                                       // a long wait, released at varied moments
                double releasedAt = clock.Elapsed.TotalMilliseconds;
                Assert.True(holder.ReleaseLock(LockScope.Machine, resource, token, out _, out m), m);
                thread.Join();
                delays.Add(acquiredAt - releasedAt);
            }
            Report($"machineHandover: rounds=10 averageMs={delays.Average():F0} maxMs={delays.Max():F0}");
        }

        [Fact]
        public void Measure_ProcessScope_HandoverDelay()
        {
            if (!Enabled) return;
            var delays = new List<double>();
            for (int round = 0; round < 10; round++)
            {
                using var c = New();
                string resource = "p-handover-" + Guid.NewGuid().ToString("N");
                Assert.True(c.TryAcquireLock(LockScope.Process, resource, "holder", 60, out _, out string token, out _, out string m), m);
                double acquiredAt = 0;
                var clock = Stopwatch.StartNew();
                var thread = new Thread(() =>
                {
                    Assert.True(c.AcquireLock(LockScope.Process, resource, "waiter", 60, 30000, out bool acquired, out _, out _, out string wm) && acquired, wm);
                    acquiredAt = clock.Elapsed.TotalMilliseconds;
                });
                thread.Start();
                Thread.Sleep(500);
                double releasedAt = clock.Elapsed.TotalMilliseconds;
                Assert.True(c.ReleaseLock(LockScope.Process, resource, token, out _, out m), m);
                thread.Join();
                delays.Add(acquiredAt - releasedAt);
            }
            Report($"processHandover: rounds=10 averageMs={delays.Average():F2} maxMs={delays.Max():F2}");
        }

        [Fact]
        public void Measure_ValidateLockFolder()
        {
            if (!Enabled) return;
            using var c = New();
            for (int r = 0; r < 50; r++) Pair(c, LockScope.Machine, "v-" + r);
            Assert.True(c.ValidateLockFolder(out _, out _, out string m), m);                           // warm up
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) Assert.True(c.ValidateLockFolder(out _, out _, out m), m);
            Report($"validateLockFolder: leaseFiles=50 msPerCall={clock.Elapsed.TotalMilliseconds / 20:F1}");
        }
    }
}
