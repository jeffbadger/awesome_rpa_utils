using System.IO;
using Xunit;

namespace FileWatchAutomation.Tests
{
    /// <summary>Pure unit tests for FileWatchFilterCore, mirroring EventLogFilterCore's TryParseLevels test shape.</summary>
    public class FileWatchFilterCoreTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryParseChangeKinds_NullOrWhitespace_MeansAllKinds(string csv)
        {
            bool result = FileWatchFilterCore.TryParseChangeKinds(csv, out WatcherChangeTypes types, out string error);

            Assert.True(result);
            Assert.Null(error);
            Assert.Equal(WatcherChangeTypes.All, types);
        }

        [Fact]
        public void TryParseChangeKinds_SingleKind_ParsesCorrectly()
        {
            bool result = FileWatchFilterCore.TryParseChangeKinds("Created", out WatcherChangeTypes types, out string error);

            Assert.True(result);
            Assert.Null(error);
            Assert.Equal(WatcherChangeTypes.Created, types);
        }

        [Fact]
        public void TryParseChangeKinds_MultipleKinds_CombinesFlags()
        {
            bool result = FileWatchFilterCore.TryParseChangeKinds("Created,Deleted", out WatcherChangeTypes types, out string error);

            Assert.True(result);
            Assert.Null(error);
            Assert.Equal(WatcherChangeTypes.Created | WatcherChangeTypes.Deleted, types);
        }

        [Fact]
        public void TryParseChangeKinds_MixedCaseAndWhitespace_ParsesCorrectly()
        {
            bool result = FileWatchFilterCore.TryParseChangeKinds(" created , DELETED ", out WatcherChangeTypes types, out string error);

            Assert.True(result);
            Assert.Equal(WatcherChangeTypes.Created | WatcherChangeTypes.Deleted, types);
        }

        [Fact]
        public void TryParseChangeKinds_UnknownToken_ReturnsFalseWithError()
        {
            bool result = FileWatchFilterCore.TryParseChangeKinds("Created,Bogus", out _, out string error);

            Assert.False(result);
            Assert.Contains("Bogus", error);
            Assert.Contains("Valid values", error);
        }

        [Theory]
        [InlineData(WatcherChangeTypes.Created, FileChangeKind.Created)]
        [InlineData(WatcherChangeTypes.Renamed, FileChangeKind.Renamed)]
        [InlineData(WatcherChangeTypes.Changed, FileChangeKind.Changed)]
        [InlineData(WatcherChangeTypes.Deleted, FileChangeKind.Deleted)]
        public void TryToFileChangeKind_KnownTypes_MapCorrectly(WatcherChangeTypes changeType, FileChangeKind expected)
        {
            bool result = FileWatchFilterCore.TryToFileChangeKind(changeType, out FileChangeKind kind, out string error);

            Assert.True(result);
            Assert.Null(error);
            Assert.Equal(expected, kind);
        }

        [Fact]
        public void TryToFileChangeKind_CombinedFlags_ReturnsFalseWithError()
        {
            bool result = FileWatchFilterCore.TryToFileChangeKind(WatcherChangeTypes.All, out _, out string error);

            Assert.False(result);
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
