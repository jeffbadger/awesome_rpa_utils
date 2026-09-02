using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace ArchiveAutomation.Tests
{
    /// <summary>Real functional tests for CreateDiagnosticBundle/Simple.</summary>
    public class ArchiveUtilsDiagnosticBundleTests : TempDirectoryTestBase
    {
        [Fact]
        public void CreateDiagnosticBundle_HappyPath_RoundTripsFilesAndManifest()
        {
            string logPath = TempFilePath("log.txt");
            string configPath = TempFilePath("config.json");
            File.WriteAllText(logPath, "log content");
            File.WriteAllText(configPath, "{}");

            string bundlePath = TempFilePath("bundle.zip");
            bool result = Archive.CreateDiagnosticBundle($"{logPath},{configPath}", bundlePath, false, "failure summary text", out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.True(File.Exists(bundlePath));

            using ZipArchive opened = ZipFile.OpenRead(bundlePath);
            Assert.Equal(3, opened.Entries.Count); // log.txt, config.json, manifest.txt
            Assert.Contains(opened.Entries, e => e.Name == "log.txt");
            Assert.Contains(opened.Entries, e => e.Name == "config.json");
            ZipArchiveEntry manifest = opened.Entries.Single(e => e.Name == "manifest.txt");
            using var reader = new StreamReader(manifest.Open());
            Assert.Equal("failure summary text", reader.ReadToEnd());
        }

        [Fact]
        public void CreateDiagnosticBundle_NullManifestText_OmitsManifestEntry()
        {
            string logPath = TempFilePath("log.txt");
            File.WriteAllText(logPath, "log content");

            string bundlePath = TempFilePath("bundle.zip");
            bool result = Archive.CreateDiagnosticBundle(logPath, bundlePath, false, null, out string message);

            Assert.True(result);
            using ZipArchive opened = ZipFile.OpenRead(bundlePath);
            Assert.Single(opened.Entries);
            Assert.DoesNotContain(opened.Entries, e => e.Name == "manifest.txt");
        }

        [Fact]
        public void CreateDiagnosticBundle_DuplicateFileNames_Disambiguated()
        {
            string dirA = TempSubdirectory("a");
            string dirB = TempSubdirectory("b");
            string fileA = Path.Combine(dirA, "log.txt");
            string fileB = Path.Combine(dirB, "log.txt");
            File.WriteAllText(fileA, "from a");
            File.WriteAllText(fileB, "from b");

            string bundlePath = TempFilePath("bundle.zip");
            bool result = Archive.CreateDiagnosticBundle($"{fileA},{fileB}", bundlePath, false, null, out string message);

            Assert.True(result);
            using ZipArchive opened = ZipFile.OpenRead(bundlePath);
            Assert.Equal(2, opened.Entries.Count);
            Assert.Contains(opened.Entries, e => e.Name == "log.txt");
            Assert.Contains(opened.Entries, e => e.Name == "log_2.txt");
        }

        [Fact]
        public void CreateDiagnosticBundle_MissingSourceFile_ReturnsFalseAndBuildsNothing()
        {
            string bundlePath = TempFilePath("bundle.zip");
            bool result = Archive.CreateDiagnosticBundle(TempFilePath("nope.txt"), bundlePath, false, null, out string message);

            Assert.False(result);
            Assert.False(File.Exists(bundlePath));
            Assert.Empty(Directory.GetFiles(TempDir, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public void CreateDiagnosticBundle_NullOrEmptyCsv_ReturnsFalseWithMessage()
        {
            bool result = Archive.CreateDiagnosticBundle(null, TempFilePath("bundle.zip"), false, null, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        // --- CreateDiagnosticBundleSimple ---

        [Fact]
        public void CreateDiagnosticBundleSimple_AutoNamesArchiveAndReturnsPath()
        {
            string logPath = TempFilePath("log.txt");
            File.WriteAllText(logPath, "content");
            string outputDir = TempSubdirectory("output");

            bool result = Archive.CreateDiagnosticBundleSimple(logPath, outputDir, out string createdPath, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.True(File.Exists(createdPath));
            Assert.StartsWith(Path.Combine(outputDir, "diagnostic-bundle-"), createdPath);
            Assert.EndsWith(".zip", createdPath);
        }

        [Fact]
        public void CreateDiagnosticBundleSimple_OutputDirectoryDoesNotExist_ReturnsFalseWithMessage()
        {
            bool result = Archive.CreateDiagnosticBundleSimple(TempFilePath("log.txt"), Path.Combine(TempDir, "nope"), out string createdPath, out string message);

            Assert.False(result);
            Assert.Null(createdPath);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
