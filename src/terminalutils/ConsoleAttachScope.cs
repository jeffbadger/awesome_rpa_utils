using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerminalAutomation
{
    /// <summary>
    /// Attaches the CALLING PROCESS (not a thread) to a target process's console for the
    /// duration of a <c>using</c> block, then detaches - restoring whatever console the
    /// calling process was attached to beforehand, on a best-effort basis.
    /// <para>
    /// <c>AttachConsole</c>/<c>FreeConsole</c> are process-wide, not per-thread, Win32 APIs:
    /// they change which single console the ENTIRE calling .NET process (the Pega Robot
    /// Studio automation host) is attached to. Because of this, every public
    /// <see cref="TerminalUtils"/> method that touches a target console uses this scope as a
    /// strictly serialized, self-contained attach -&gt; act -&gt; detach transaction - never a
    /// persisted "currently attached" console left as implicit state across separate method
    /// calls, the same convention every other never-throws method in this suite follows for
    /// native handles (e.g. <c>ServiceUtils</c> opens and closes its own
    /// <c>ServiceController</c> per call rather than returning a live one).
    /// </para>
    /// <para>
    /// Restore behavior on <see cref="Dispose"/>: if the calling process had no console
    /// attached before this scope was created (the common case for a GUI/WinExe host,
    /// including a Pega Robot Runtime host), <see cref="Dispose"/> simply calls
    /// <c>FreeConsole()</c> again, leaving the process console-less as it was. If the calling
    /// process DID already have a console attached, this scope best-effort re-attaches to one
    /// of the process IDs that were sharing that original console (via
    /// <c>GetConsoleProcessList</c>), which re-establishes the same console session in the
    /// common case but is not guaranteed to be a perfect restore for every possible prior
    /// state.
    /// </para>
    /// </summary>
    internal sealed class ConsoleAttachScope : IDisposable
    {
        private static readonly object AttachLock = new object();

        private readonly uint[] _originalConsolePids;
        private bool _disposed;

        private ConsoleAttachScope(uint[] originalConsolePids)
        {
            _originalConsolePids = originalConsolePids;
        }

        /// <summary>
        /// Attempts to attach the calling process to <paramref name="processId"/>'s console.
        /// Acquires a process-wide lock that is held until the returned <paramref name="scope"/>
        /// is disposed (or, on failure, released before returning) - always dispose the scope
        /// via a <c>using</c> block, including on an exception path, or the lock is never
        /// released.
        /// </summary>
        internal static bool TryAttach(int processId, out ConsoleAttachScope scope, out string message)
        {
            scope = null;
            message = null;

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    _ = process; // existence check only
                }
            }
            catch (ArgumentException)
            {
                message = $"No process with ID {processId} was found.";
                return false;
            }

            Monitor.Enter(AttachLock);
            bool lockHeld = true;
            try
            {
                uint[] originalPids = CaptureCurrentConsoleProcessIds();

                // FreeConsole() unconditionally first: AttachConsole fails with
                // ERROR_ACCESS_DENIED if the calling process is already attached to any
                // console (even one it allocated itself) - there is no way to attach to a
                // different target without detaching first.
                FreeConsole();

                if (!AttachConsole((uint)processId))
                {
                    message = new Win32Exception(Marshal.GetLastWin32Error(), $"AttachConsole failed for process {processId}. The target process may have no console, may have already exited, or its console session may be inaccessible.").Message;
                    RestoreOriginalConsole(originalPids);
                    return false;
                }

                scope = new ConsoleAttachScope(originalPids);
                lockHeld = false; // ownership of the lock transfers to the scope, released on Dispose
                return true;
            }
            finally
            {
                if (lockHeld)
                    Monitor.Exit(AttachLock);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                FreeConsole();
                RestoreOriginalConsole(_originalConsolePids);
            }
            finally
            {
                Monitor.Exit(AttachLock);
            }
        }

        private static uint[] CaptureCurrentConsoleProcessIds()
        {
            if (GetConsoleWindow() == IntPtr.Zero)
                return Array.Empty<uint>();

            // A generously-sized buffer for the common case; if more processes than this
            // share the console, the extras simply aren't captured for restore purposes -
            // any one valid PID from the original console is enough to re-establish the same
            // console session.
            var buffer = new uint[64];
            uint count = GetConsoleProcessList(buffer, (uint)buffer.Length);
            if (count == 0 || count > buffer.Length)
                return Array.Empty<uint>();

            var result = new uint[count];
            Array.Copy(buffer, result, (int)count);
            return result;
        }

        private static void RestoreOriginalConsole(uint[] originalPids)
        {
            if (originalPids == null || originalPids.Length == 0)
                return;

            // Best-effort: try each captured PID until one re-attaches successfully.
            foreach (uint pid in originalPids)
            {
                if (AttachConsole(pid))
                    return;
            }
        }

        #region P/Invoke

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        #endregion
    }
}
