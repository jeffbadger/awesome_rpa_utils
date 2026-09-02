using System.IO;
using System.Text;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>Real functional tests for the Hash methods, including a streamed multi-MB file.</summary>
    public class FileWatchUtilsHashTests : TempDirectoryTestBase
    {
        // Known SHA-256 of the ASCII string "hello world" (verified against a reference implementation).
        private const string HelloWorldSha256 = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ComputeFileHashSha256_NullOrEmptyPath_ReturnsFalseWithMessage(string path)
        {
            bool result = Fw.ComputeFileHashSha256(path, out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ComputeFileHashSha256_NonexistentFile_ReturnsFalseWithMessage()
        {
            bool result = Fw.ComputeFileHashSha256(TempFilePath(), out _, out string message);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void ComputeFileHashSha256_KnownContent_MatchesKnownHash()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "hello world");

            bool result = Fw.ComputeFileHashSha256(path, out string hashHex, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal(HelloWorldSha256, hashHex);
        }

        [Fact]
        public void ComputeFileHashSha256_LargeFile_StreamsSuccessfully()
        {
            string path = TempFilePath();
            // ~5MB, exercises the streamed read path rather than a tiny fixture.
            byte[] chunk = Encoding.UTF8.GetBytes(new string('a', 1024));
            using (var stream = File.Create(path))
            {
                for (int i = 0; i < 5 * 1024; i++)
                    stream.Write(chunk, 0, chunk.Length);
            }

            bool result = Fw.ComputeFileHashSha256(path, out string hashHex, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.Equal(64, hashHex.Length); // SHA-256 hex length
        }

        [Theory]
        [InlineData("SHA256")]
        [InlineData("sha256")]
        [InlineData("SHA1")]
        [InlineData("MD5")]
        [InlineData("SHA384")]
        [InlineData("SHA512")]
        public void ComputeFileHash_SupportedAlgorithms_Succeed(string algorithmName)
        {
            string path = TempFilePath();
            File.WriteAllText(path, "hello world");

            bool result = Fw.ComputeFileHash(path, algorithmName, out string hashHex, out string message);

            Assert.True(result);
            Assert.Null(message);
            Assert.False(string.IsNullOrEmpty(hashHex));
        }

        [Fact]
        public void ComputeFileHash_SameAlgorithmAsSha256Method_ProducesSameHash()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "hello world");

            Fw.ComputeFileHashSha256(path, out string viaSimple, out _);
            Fw.ComputeFileHash(path, "SHA256", out string viaGeneral, out _);

            Assert.Equal(viaSimple, viaGeneral);
        }

        [Fact]
        public void ComputeFileHash_UnknownAlgorithm_ReturnsFalseWithMessage()
        {
            string path = TempFilePath();
            File.WriteAllText(path, "hello world");

            bool result = Fw.ComputeFileHash(path, "ROT13", out _, out string message);

            Assert.False(result);
            Assert.Contains("Unknown hash algorithm", message);
        }

        // --- AreFilesIdenticalByHash ---

        [Fact]
        public void AreFilesIdenticalByHash_IdenticalContent_ReturnsTrue()
        {
            string pathA = TempFilePath();
            string pathB = TempFilePath();
            File.WriteAllText(pathA, "same content");
            File.WriteAllText(pathB, "same content");

            bool result = Fw.AreFilesIdenticalByHash(pathA, pathB, out bool identical, out string message);

            Assert.True(result);
            Assert.True(identical);
            Assert.Null(message);
        }

        [Fact]
        public void AreFilesIdenticalByHash_DifferingContent_ReturnsFalseIdentical()
        {
            string pathA = TempFilePath();
            string pathB = TempFilePath();
            File.WriteAllText(pathA, "content A");
            File.WriteAllText(pathB, "content B");

            bool result = Fw.AreFilesIdenticalByHash(pathA, pathB, out bool identical, out string message);

            Assert.True(result);
            Assert.False(identical);
            Assert.Null(message);
        }

        [Fact]
        public void AreFilesIdenticalByHash_MissingFile_ReturnsFalseWithMessage()
        {
            string pathA = TempFilePath();
            File.WriteAllText(pathA, "exists");

            bool result = Fw.AreFilesIdenticalByHash(pathA, TempFilePath(), out bool identical, out string message);

            Assert.False(result);
            Assert.False(identical);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
