using System;
using System.Diagnostics;
using System.Text;

namespace BrowserInterruptAutomation
{
    /// <summary>
    /// The one process-name lookup shared by the native sweep (<c>UiaBrowserPopupProbe</c>) and the
    /// window-opened hook (<c>BrowserPopupHookThread</c>), backed by a single
    /// <see cref="ProcessIdentityCache"/>.
    /// </summary>
    /// <remarks>
    /// Not UIA-dependent (only Win32 and <see cref="Process"/>), so it compiles in every
    /// configuration; only its Win32 half is Windows-specific and it is exercised by the real
    /// smoke tests on Windows. The cheap path is <c>OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)</c> +
    /// <c>QueryFullProcessImageNameW</c> + <c>GetProcessTimes</c>; it avoids
    /// <c>Process.GetProcessById(pid).ProcessName</c>, which on .NET for Windows takes a
    /// system-wide process snapshot per call (platform knowledge, not measured here). If the cheap
    /// path fails (for example access denied), the lookup falls back once to
    /// <c>Process.ProcessName</c> and the outcome, including "unknown", is cached.
    /// </remarks>
    internal static class ProcessNames
    {
        private static readonly ProcessIdentityCache Shared = new ProcessIdentityCache(ResolveFull, ReadKey);

        /// <summary>The name of the process with this ID, or <c>string.Empty</c> for an invalid ID or one that no longer exists or cannot be inspected. Never throws.</summary>
        internal static string NameOf(int processId)
        {
            try
            {
                return Shared.TryGetName(processId, out string name) ? name : string.Empty;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return string.Empty;
            }
        }

        /// <summary>Drops every cached name (the probe's <c>ClearCache</c>).</summary>
        internal static void Clear() => Shared.Clear();

        /// <summary>
        /// The name <c>Process.ProcessName</c> reports for an executable image path: the file name
        /// with the directory removed and a trailing <c>.exe</c> (any case) removed, and no other
        /// extension handling, so <c>C:\x\my.app.exe</c> gives <c>my.app</c>.
        /// </summary>
        internal static string NameFromImagePath(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return string.Empty;
            int slash = Math.Max(imagePath.LastIndexOf('\\'), imagePath.LastIndexOf('/'));
            string file = slash >= 0 ? imagePath.Substring(slash + 1) : imagePath;
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - 4);
            return file;
        }

        private static ProcessIdentity? ResolveFull(int pid)
        {
            long key = ProcessIdentity.UnknownKey;
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
                if (handle != IntPtr.Zero)
                {
                    if (NativeMethods.GetProcessTimes(handle, out long created, out _, out _, out _))
                        key = created;
                    string path = QueryImagePath(handle);
                    if (!string.IsNullOrEmpty(path))
                    {
                        string name = NameFromImagePath(path);
                        if (name.Length > 0)
                            return new ProcessIdentity(name, key);
                    }
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                // Fall through to the one-time fallback below.
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    NativeMethods.CloseHandle(handle);
            }

            // Fallback (access denied, or the image name was unavailable): the slow but permissive path.
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    string name = process.ProcessName;
                    return string.IsNullOrEmpty(name) ? (ProcessIdentity?)null : new ProcessIdentity(name, key);
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return null; // gone or inaccessible: cached as "unknown"
            }
        }

        private static long? ReadKey(int pid)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
                if (handle != IntPtr.Zero && NativeMethods.GetProcessTimes(handle, out long created, out _, out _, out _))
                    return created;
                return null;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex))
            {
                return null;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    NativeMethods.CloseHandle(handle);
            }
        }

        private static string QueryImagePath(IntPtr handle)
        {
            foreach (int capacity in new[] { 1024, 32768 })
            {
                var sb = new StringBuilder(capacity);
                uint size = (uint)capacity;
                if (NativeMethods.QueryFullProcessImageNameW(handle, 0, sb, ref size))
                    return sb.ToString(0, (int)Math.Min(size, (uint)sb.Capacity));
                if (System.Runtime.InteropServices.Marshal.GetLastWin32Error() != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
                    break;
            }
            return null;
        }
    }
}
