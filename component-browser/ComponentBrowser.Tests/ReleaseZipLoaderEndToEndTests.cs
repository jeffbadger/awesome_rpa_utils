using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ComponentBrowser.Tests
{
    /// <summary>
    /// Builds a REAL release zip with the actual <c>scripts/Package-Release.ps1</c> - the
    /// same script every other component in this suite verified its packaging with this
    /// session - then runs <see cref="ReleaseZipLoader"/> and <see cref="ComponentCatalogBuilder"/>
    /// against it. This is the single most valuable test in this project: it proves the app
    /// works against a real shipped artifact, not an imagined one.
    /// <para>
    /// Requires a prior <c>dotnet build src/AwesomeRpaUtils.sln --configuration Release</c>
    /// (this test runs the script with <c>-NoBuild</c>) - a full solution build from inside
    /// this test via a plain <c>dotnet build</c> was tried first and, while logically correct,
    /// proved too slow/unreliable in practice in this environment; reusing an already-built
    /// Release output is the same trade-off <c>Package-Release.ps1 -NoBuild</c> itself exists
    /// to make for CI.
    /// </para>
    /// </summary>
    public class ReleaseZipLoaderEndToEndTests : IDisposable
    {
        private readonly string _artifactsDirectory;

        public ReleaseZipLoaderEndToEndTests()
        {
            _artifactsDirectory = Path.Combine(RepoPaths.RepoRoot, "artifacts");
        }

        public void Dispose()
        {
            if (Directory.Exists(_artifactsDirectory))
                Directory.Delete(_artifactsDirectory, recursive: true);
        }

        [SkippableFactPwsh]
        public void RealReleaseZip_LoadsAndBuildsCatalogWithMultipleComponents()
        {
            string zipPath = BuildRealReleaseZip();

            using LoadedRelease release = ReleaseZipLoader.Load(zipPath);

            Assert.True(Directory.Exists(release.DllDirectory));
            Assert.NotEmpty(Directory.GetFiles(release.DllDirectory, "*.dll"));

            Assert.True(Directory.Exists(release.DocsDirectory));
            string srcDocsRoot = Path.Combine(release.DocsDirectory, "src");
            Assert.True(Directory.Exists(srcDocsRoot), $"Expected nested Documentation.zip content under '{srcDocsRoot}'.");
            Assert.NotEmpty(Directory.GetFiles(srcDocsRoot, "README.md", SearchOption.AllDirectories));

            var catalog = ComponentCatalogBuilder.Build(release);

            Assert.True(catalog.Count >= 10, $"Expected at least 10 components in a real release, found {catalog.Count}.");
            Assert.Contains(catalog, c => c.Component.AssemblyName == "EventLogAutomation");
            Assert.Contains(catalog, c => c.Component.AssemblyName == "SessionAutomation");
            Assert.Contains(catalog, c => c.Component.AssemblyName == "ArchiveAutomation");

            // At least one PME from a real component must have been matched to real
            // documentation text - proves the whole DLL-to-README join actually works end to
            // end, not just that both sides parse independently.
            var eventLogEntry = catalog.Single(c => c.Component.AssemblyName == "EventLogAutomation");
            var writeEntryDetail = eventLogEntry.Details.Single(d => d.Pme.Name == "WriteEntry");
            Assert.False(string.IsNullOrWhiteSpace(writeEntryDetail.Description));
            Assert.False(string.IsNullOrWhiteSpace(writeEntryDetail.NotesAndCaveats));
        }

        /// <summary>
        /// Runs the actual <c>scripts/Package-Release.ps1</c> (with <c>-NoBuild</c> against an
        /// already-built Release output - see the class doc comment) exactly as used to
        /// verify every prior component's release packaging this session. Skips (rather than
        /// fails) if <c>pwsh</c> isn't available, since that's an environment gap, not a code
        /// defect.
        /// </summary>
        private string BuildRealReleaseZip()
        {
            string releaseBinRoot = RepoPaths.Combine("src", "bin", "Release");
            if (!Directory.Exists(releaseBinRoot))
            {
                throw new DirectoryNotFoundException(
                    $"'{releaseBinRoot}' does not exist. This test runs Package-Release.ps1 with " +
                    "-NoBuild, so the solution must already be built in Release config first: " +
                    "'dotnet build src/AwesomeRpaUtils.sln --configuration Release'.");
            }

            Directory.CreateDirectory(_artifactsDirectory);

            // A RELATIVE -ArchivePath, not absolute: Package-Release.ps1's final path
            // resolution unconditionally Join-Paths onto Get-Location, which on Unix
            // silently doubles an already-absolute path into a bogus nested directory
            // (confirmed by hitting this for real while writing this test) - every other
            // verified invocation of this script in this repo's history passes a relative
            // "artifacts/..." path for exactly this reason.
            string archivePathArgument = "artifacts/AwesomeRpaUtils-{tfm}.zip";

            var startInfo = new ProcessStartInfo("pwsh")
            {
                ArgumentList =
                {
                    "-Command",
                    $"./scripts/Package-Release.ps1 -Configuration Release -NoBuild -ArchivePath '{archivePathArgument}'"
                },
                WorkingDirectory = RepoPaths.RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using Process process = Process.Start(startInfo);

            // Read stdout and stderr concurrently, not one ReadToEnd() after the other -
            // sequential reads deadlock (or, as hit for real while writing this test, stall
            // for minutes) once the child's output exceeds the OS pipe buffer on either
            // stream, since the child blocks writing to the unread pipe while this process
            // blocks waiting to finish reading the other one first.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(TimeSpan.FromMinutes(5));
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            Assert.True(exited, $"Package-Release.ps1 did not exit within 5 minutes.\n{stdout}\n{stderr}");
            Assert.True(process.ExitCode == 0, $"Package-Release.ps1 failed (exit {process.ExitCode}):\n{stdout}\n{stderr}");

            string producedZip = Path.Combine(_artifactsDirectory, "AwesomeRpaUtils-net10.0.zip");
            Assert.True(File.Exists(producedZip), $"Expected '{producedZip}' to exist after Package-Release.ps1 ran.\n{stdout}");
            return producedZip;
        }
    }

    /// <summary>
    /// Skips a test unless <c>pwsh</c> is on PATH - mirrors
    /// <c>CommandLineUtils.Tests</c>' <c>SkippableFactPlatformAttribute</c> pattern (a
    /// <see cref="FactAttribute"/> subclass setting its own <see cref="FactAttribute.Skip"/>),
    /// rather than adding a new xunit extension package for one conditional test.
    /// </summary>
    public class SkippableFactPwshAttribute : FactAttribute
    {
        public SkippableFactPwshAttribute()
        {
            if (!IsPwshAvailable())
                Skip = "Requires 'pwsh' on PATH to run scripts/Package-Release.ps1.";
        }

        private static bool IsPwshAvailable()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("pwsh", "-Command \"exit 0\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
