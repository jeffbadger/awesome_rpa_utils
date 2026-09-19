using System;
using System.ComponentModel;
using System.Diagnostics;

namespace CommandLineAutomation
{
    // Net48 compatibility shim for Process.Kill(entireProcessTree: true), which
    // only exists on .NET Core+. taskkill's /T flag terminates the whole tree
    // instead. Throws InvalidOperationException / Win32Exception the same way
    // the BCL overload does so the callers' existing catch blocks keep working.
    internal static class Net48Compat
    {
        internal static void KillProcessTree(Process process)
        {
#if NETFRAMEWORK
            // process.Id throws InvalidOperationException when the process already
            // exited - the same observable failure the BCL overload produces there.
            int pid = process.Id;
            using (Process killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = "/PID " + pid + " /T /F",
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                if (killer == null)
                    throw new Win32Exception("taskkill.exe could not be launched.");
                killer.WaitForExit();
                // A nonzero exit code means taskkill did not terminate the tree;
                // surface it the same way a failed Kill() would be observed here.
                if (killer.ExitCode != 0)
                    throw new Win32Exception(killer.ExitCode);
            }
#else
            process.Kill(entireProcessTree: true);
#endif
        }
    }
}