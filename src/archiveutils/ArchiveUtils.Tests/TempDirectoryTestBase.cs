using System;
using System.IO;

namespace ArchiveAutomation.Tests
{
    /// <summary>
    /// Shared per-test temp-directory harness. Because every ArchiveUtils operation is
    /// plain cross-platform BCL (<see cref="System.IO.Compression"/> + <see cref="System.IO"/>)
    /// with zero P/Invoke, tests can exercise real happy-path behavior (real archives,
    /// real corrupted bytes, real path-traversal attempts) rather than only guard clauses.
    /// </summary>
    public abstract class TempDirectoryTestBase : IDisposable
    {
        protected readonly string TempDir;
        protected readonly ArchiveUtils Archive = new ArchiveUtils();

        protected TempDirectoryTestBase()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "archiveutils-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
        }

        protected string TempFilePath(string name = null) =>
            Path.Combine(TempDir, name ?? (Guid.NewGuid().ToString("N") + ".tmp"));

        protected string TempSubdirectory(string name = null)
        {
            string dir = Path.Combine(TempDir, name ?? Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void Dispose()
        {
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
