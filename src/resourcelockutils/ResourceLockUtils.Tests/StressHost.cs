using System;
using System.IO;
using System.Threading;

namespace ResourceLockAutomation.Tests
{
    /// <summary>
    /// The child process of the multi-process tests: <c>dotnet ResourceLockUtils.Tests.dll stress-host ...</c>. The test assembly is a program
    /// (test projects build as executables), so no separate project is needed, and none can end up in a NuGet package or release.
    /// </summary>
    public static class StressHost
    {
        /// <summary>
        /// stress-host contend &lt;folder&gt; &lt;resource&gt; &lt;iterations&gt; &lt;markerFolder&gt;: acquire, prove exclusivity with a marker file
        /// created with "fail if it exists", release; prints VIOLATION for any overlap, WAITED when a wait ends without the lock, ERROR for a
        /// failed call or a lost lease, and DONE &lt;iterations&gt; at the end.
        /// stress-host hold &lt;folder&gt; &lt;resource&gt; &lt;leaseSeconds&gt;: acquire, print HELD, then wait to be killed without releasing.
        /// </summary>
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] != "stress-host") return 0;                     // xunit runs the tests through its own entry point
            using var c = new ResourceLockUtils();
            if (!c.ConfigureLockFolder(args[2], out string m)) { Console.WriteLine("ERROR " + m); return 2; }
            string resource = args[3];
            if (args[1] == "hold")
            {
                if (!c.TryAcquireLock(LockScope.Machine, resource, "holder-" + Environment.ProcessId, int.Parse(args[4]), out bool acquired, out _, out _, out m) || !acquired)
                {
                    Console.WriteLine("ERROR " + (m ?? "not acquired"));
                    return 3;
                }
                Console.WriteLine("HELD");
                Thread.Sleep(Timeout.Infinite);
            }
            int iterations = int.Parse(args[4]);
            string marker = Path.Combine(args[5], "inside");
            int done = 0;
            for (int i = 0; i < iterations; i++)
            {
                if (!c.AcquireLock(LockScope.Machine, resource, "contender-" + Environment.ProcessId, 60, 20000, out bool acquired, out string token, out _, out m))
                {
                    Console.WriteLine("ERROR " + m + (m.Contains("unexpectedly") ? " " + NeverThrowsGuard.LastUnexpected?.ToString().Replace('\n', ' ') : ""));
                    continue;
                }
                if (!acquired)
                {
                    Console.WriteLine("WAITED");                                                   // no fairness between waiters: not an error
                    done++;
                    continue;
                }
                try
                {
                    using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                }
                catch (IOException)
                {
                    Console.WriteLine("VIOLATION");                                            // another process is inside at the same time
                }
                Thread.SpinWait(2000);
                try { File.Delete(marker); } catch (IOException) { }
                if (!c.ReleaseLock(LockScope.Machine, resource, token, out bool released, out m) || !released) Console.WriteLine("ERROR release " + (m ?? "lost"));
                done++;
            }
            Console.WriteLine("DONE " + done);
            return 0;
        }
    }
}
