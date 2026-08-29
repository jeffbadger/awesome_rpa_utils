using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace EventAutomation.Native
{
    /// <summary>
    /// Resolves a process id to its image name (e.g. "notepad"), cached per pid.
    /// The cache is bounded: entries are keyed by pid and evicted when the pid is
    /// reused is not tracked — a pid that exits keeps its name cached, which is
    /// fine because the name is what we want for lookbacks. Call <see cref="Clear"/>
    /// on Stop/Dispose so a long-running robot does not accumulate stale entries.
    /// </summary>
    internal static class ProcessHelpers
    {
        private static readonly ConcurrentDictionary<uint, string> Cache = new ConcurrentDictionary<uint, string>();

        /// <summary>Gets the image name for a pid, or null if it cannot be resolved.</summary>
        public static string GetProcessName(uint pid)
        {
            if (pid == 0)
                return null;
            if (Cache.TryGetValue(pid, out var cached))
                return cached;
            string name = QueryName(pid);
            Cache[pid] = name;
            return name;
        }

        /// <summary>Drops the pid→name cache (called on Stop/Dispose).</summary>
        public static void Clear() => Cache.Clear();

        private static string QueryName(uint pid)
        {
            IntPtr handle = WinEventInterop.OpenProcess(WinEventInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
                return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (!WinEventInterop.QueryFullProcessImageName(handle, 0, sb, ref size))
                    return null;
                return Path.GetFileName(sb.ToString());
            }
            catch
            {
                return null;
            }
            finally
            {
                WinEventInterop.CloseHandle(handle);
            }
        }
    }
}
