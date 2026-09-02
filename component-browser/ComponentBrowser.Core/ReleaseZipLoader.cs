using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ComponentBrowser
{
    /// <summary>
    /// A release zip (<c>AwesomeRpaUtils-{tfm}-vX.Y.Z.zip</c>, from
    /// <c>scripts/Package-Release.ps1</c>), extracted to a temp directory for the duration of
    /// one load. Disposing removes the whole temp tree.
    /// </summary>
    public sealed class LoadedRelease : IDisposable
    {
        /// <summary>The directory the release zip itself was extracted into - holds the component DLLs at its root.</summary>
        public string DllDirectory { get; }

        /// <summary>
        /// The directory the nested <c>AwesomeRpaUtils-Documentation.zip</c> was extracted
        /// into (empty if the release zip had no such nested archive - handled as a normal,
        /// non-fatal case, not every release build necessarily bundles it).
        /// </summary>
        public string DocsDirectory { get; }

        /// <summary>
        /// File names of the DLLs that were actually at the outer release zip's root (this
        /// suite's own <c>XxxAutomation.dll</c> component list), captured before the nested
        /// SupportLibraries.zip is extracted on top of the same directory. Some support
        /// packages this suite depends on (e.g. <c>System.Diagnostics.EventLog</c>,
        /// <c>System.ServiceProcess.ServiceController</c>) themselves define a public
        /// <see cref="System.ComponentModel.Component"/>-derived type in the BCL sense
        /// (<c>System.Diagnostics.EventLog</c>, <c>System.ServiceProcess.ServiceController</c>
        /// are literally Component subclasses) - without this distinction,
        /// <see cref="AssemblyInspector"/> would list them as spurious extra "components"
        /// alongside this suite's real ones. Confirmed by hitting this for real: loading a
        /// genuine release zip surfaced "System.Diagnostics.EventLog" as a fake component.
        /// </summary>
        public IReadOnlyCollection<string> PrimaryDllFileNames { get; }

        private readonly string _tempRoot;
        private bool _disposed;

        internal LoadedRelease(string tempRoot, string dllDirectory, string docsDirectory, IReadOnlyCollection<string> primaryDllFileNames)
        {
            _tempRoot = tempRoot;
            DllDirectory = dllDirectory;
            DocsDirectory = docsDirectory;
            PrimaryDllFileNames = primaryDllFileNames;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup - a file still open (e.g. from a previous load) shouldn't
                // block loading a new one. The OS temp directory will eventually be swept.
            }
        }
    }

    /// <summary>
    /// Extracts a release zip (its nested documentation bundle, and its nested support-library
    /// DLLs) for browsing.
    /// </summary>
    public static class ReleaseZipLoader
    {
        private const string NestedDocumentationZipName = "AwesomeRpaUtils-Documentation.zip";

        /// <summary>
        /// Component DLLs like <c>EventLogAutomation.dll</c>/<c>ServiceAutomation.dll</c>
        /// depend on packages (<c>System.Diagnostics.EventLog</c>,
        /// <c>System.ServiceProcess.ServiceController</c>) that <c>Package-Release.ps1</c>
        /// ships separately in this nested archive, not alongside the component DLLs at the
        /// zip root - real Robot Studio deployments place both in the same folder at runtime.
        /// Extracting this into the same directory as the component DLLs (not a subfolder)
        /// mirrors that and is required for <see cref="AssemblyInspector"/> to resolve those
        /// dependent types at all: without it, reflecting a dependent DLL under
        /// <see cref="System.Reflection.MetadataLoadContext"/> fails outright (confirmed by
        /// hitting this for real while testing against a directory holding only the
        /// component DLL).
        /// </summary>
        private const string NestedSupportLibrariesZipName = "AwesomeRpaUtils-SupportLibraries.zip";

        public static LoadedRelease Load(string releaseZipPath)
        {
            if (string.IsNullOrWhiteSpace(releaseZipPath))
                throw new ArgumentException("A release zip path is required.", nameof(releaseZipPath));
            if (!File.Exists(releaseZipPath))
                throw new FileNotFoundException("Release zip not found.", releaseZipPath);

            string tempRoot = Path.Combine(Path.GetTempPath(), "ComponentBrowser-" + Guid.NewGuid().ToString("N"));
            string dllDirectory = Path.Combine(tempRoot, "release");
            string docsDirectory = Path.Combine(tempRoot, "docs");

            Directory.CreateDirectory(dllDirectory);
            Directory.CreateDirectory(docsDirectory);

            ZipFile.ExtractToDirectory(releaseZipPath, dllDirectory, overwriteFiles: true);

            // Captured before the SupportLibraries.zip overlay below adds dependency-only
            // DLLs into the same directory - this is the outer zip's real component list.
            var primaryDllFileNames = Directory.GetFiles(dllDirectory, "*.dll")
                .Select(Path.GetFileName)
                .ToArray();

            string nestedDocsZip = Path.Combine(dllDirectory, NestedDocumentationZipName);
            if (File.Exists(nestedDocsZip))
                ZipFile.ExtractToDirectory(nestedDocsZip, docsDirectory, overwriteFiles: true);

            string nestedSupportZip = Path.Combine(dllDirectory, NestedSupportLibrariesZipName);
            if (File.Exists(nestedSupportZip))
                ZipFile.ExtractToDirectory(nestedSupportZip, dllDirectory, overwriteFiles: true);

            return new LoadedRelease(tempRoot, dllDirectory, docsDirectory, primaryDllFileNames);
        }
    }
}
