using System;
using System.IO;

namespace FileWatchAutomation.Tests
{
    /// <summary>
    /// Shared per-test temp-directory harness. Because every FileWatchUtils operation is
    /// plain cross-platform BCL file I/O with zero P/Invoke, tests can exercise real
    /// happy-path behavior (real files, real locks, real FileSystemWatcher events) rather
    /// than only guard clauses - unlike every other component in this suite.
    /// </summary>
    public abstract class TempDirectoryTestBase : IDisposable
    {
        protected readonly string TempDir;
        protected readonly FileWatchUtils Fw = new FileWatchUtils();

        protected TempDirectoryTestBase()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "fwutils-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
        }

        protected string TempFilePath(string name = null) =>
            Path.Combine(TempDir, name ?? (Guid.NewGuid().ToString("N") + ".tmp"));

        protected string TempSubdirectory(string name = null)
        {
            string dir = Path.Combine(TempDir, name ?? (Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void Dispose()
        {
            // Stops any background watch a Watch test left running - a no-op for every
            // other test class, since only StartWatching acquires a live resource.
            try { Fw.Dispose(); } catch { /* best-effort cleanup */ }
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
