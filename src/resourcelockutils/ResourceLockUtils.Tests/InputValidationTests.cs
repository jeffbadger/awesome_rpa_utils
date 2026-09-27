using System;
using System.IO;
using Xunit;

namespace ResourceLockAutomation.Tests
{
    /// <summary>Every input rule, at its boundaries, through the public methods and the checks behind them.</summary>
    public sealed class InputValidationTests
    {
        private const string Token = "0123456789abcdef0123456789ABCDEF";

        [Theory]
        [InlineData("SAP-User-BATCH01")]
        [InlineData("a")]
        [InlineData("printer_2.tray")]
        [InlineData("9lives")]
        [InlineData("CONSOLE")]                          // only the exact device names are reserved
        [InlineData("COM10")]
        public void ValidResourceNames_AreAccepted(string resource) => Assert.Null(LockInput.Resource(resource));

        [Theory]
        [InlineData(null, "required")]
        [InlineData("", "required")]
        [InlineData("has space", "only ASCII letters")]
        [InlineData("path/part", "only ASCII letters")]
        [InlineData("back\\slash", "only ASCII letters")]
        [InlineData("colon:x", "only ASCII letters")]
        [InlineData("naïve", "only ASCII letters")]           // file names on every file system: ASCII only
        [InlineData("-lead", "start with a letter or digit")]
        [InlineData(".hidden", "start with a letter or digit")]
        [InlineData("trailing.", "end with a dot")]
        [InlineData("CON", "device name")]
        [InlineData("nul", "device name")]
        [InlineData("Com1.printer", "device name")]      // CON.1.lease would open the device
        [InlineData("LPT9", "device name")]
        public void InvalidResourceNames_AreRefused(string resource, string reason) => Assert.Contains(reason, LockInput.Resource(resource));

        [Fact]
        public void ResourceNames_AreAtMost100Characters()
        {
            Assert.Null(LockInput.Resource(new string('r', 100)));
            Assert.Contains("at most 100", LockInput.Resource(new string('r', 101)));
        }

        [Fact]
        public void Holders_AreFreeText_UpTo128Characters_WithoutControlCharacters()
        {
            Assert.Null(LockInput.Holder("MyServer_3"));
            Assert.Null(LockInput.Holder("Robot 7 (night shift) — Bänk"));
            Assert.Null(LockInput.Holder(new string('h', 128)));
            Assert.Contains("at most 128", LockInput.Holder(new string('h', 129)));
            Assert.Contains("required", LockInput.Holder(null));
            Assert.Contains("required", LockInput.Holder("   "));
            Assert.Contains("control", LockInput.Holder("line\nbreak"));
            Assert.Contains("control", LockInput.Holder("tab\there"));
            Assert.Contains("must not contain |", LockInput.Holder("Robot|Night"));                // | separates holders in GetLockStatus
        }

        [Fact]
        public void NumbersAreCheckedAtTheirBoundaries()
        {
            Assert.Null(LockInput.LeaseSeconds(5));
            Assert.Null(LockInput.LeaseSeconds(86400));
            Assert.NotNull(LockInput.LeaseSeconds(4));
            Assert.NotNull(LockInput.LeaseSeconds(86401));
            Assert.Null(LockInput.WaitMilliseconds(0));
            Assert.Null(LockInput.WaitMilliseconds(3600000));
            Assert.NotNull(LockInput.WaitMilliseconds(-1));
            Assert.NotNull(LockInput.WaitMilliseconds(3600001));
            Assert.Null(LockInput.Capacity(1));
            Assert.Null(LockInput.Capacity(100));
            Assert.NotNull(LockInput.Capacity(0));
            Assert.NotNull(LockInput.Capacity(101));
        }

        [Fact]
        public void Tokens_Are32HexadecimalCharacters()
        {
            Assert.Null(LockInput.Token(Guid.NewGuid().ToString("N")));
            Assert.Null(LockInput.Token(Token));
            Assert.Contains("required", LockInput.Token(""));
            Assert.Contains("not a token", LockInput.Token(Guid.NewGuid().ToString()));            // with hyphens
            Assert.Contains("not a token", LockInput.Token(new string('g', 32)));
            Assert.Contains("not a token", LockInput.Token(Token + "0"));
        }

        [Fact]
        public void Scopes_MustBeKnown()
        {
            Assert.Null(LockInput.Scope(LockScope.Process));
            Assert.Null(LockInput.Scope(LockScope.Machine));
            Assert.Contains("not a known LockScope", LockInput.Scope((LockScope)2));
            Assert.DoesNotContain("2", LockInput.Scope((LockScope)2));                           // the rule, never the value
        }

        [Fact]
        public void FolderPaths_AreAbsoluteAndLocal_OrEmptyForTheDefault()
        {
            string absolute = Path.Combine(Path.GetTempPath(), "locks");
            Assert.Null(LockInput.FolderPath(""));
            Assert.Null(LockInput.FolderPath(absolute));
            Assert.Contains("required", LockInput.FolderPath(null));
            Assert.Contains("absolute", LockInput.FolderPath("relative\\locks"));
            Assert.Contains("absolute", LockInput.FolderPath("locks"));
            Assert.Contains("network", LockInput.FolderPath(@"\\server\share\locks"));
            Assert.Contains("network", LockInput.FolderPath("//server/share/locks"));
            Assert.Contains("at most 200", LockInput.FolderPath(Path.Combine(Path.GetTempPath(), new string('d', 200))));
            Assert.Contains("characters a Windows path cannot have", LockInput.FolderPath(absolute + "\0x"));
            foreach (string bad in new[] { "*", "?", "\"", "<", ">", "|", ":stream" })
                Assert.Contains("characters a Windows path cannot have", LockInput.FolderPath(absolute + bad));
            if (OperatingSystem.IsWindows()) Assert.Null(LockInput.FolderPath(@"C:\RobotLocks"));                    // a drive letter's colon is fine
        }

        [Fact]
        public void TheDefaultFolder_IsMachineWide_NotPerUser()
        {
            string folder = LockInput.DefaultFolder;
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), folder);
            Assert.EndsWith(Path.Combine("AwesomeRpaUtils", "Locks"), folder);
            Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder);
        }

        [Fact]
        public void ConfigureLockFolder_SetsAndResetsTheFolder_AndARefusedPathChangesNothing()
        {
            using var c = new ResourceLockUtils();
            Assert.Equal(LockInput.DefaultFolder, c.LockFolder);
            string custom = Path.Combine(Path.GetTempPath(), "robot-locks");
            Assert.True(c.ConfigureLockFolder(custom, out string m), m);
            Assert.Equal(custom, c.LockFolder);
            Assert.False(c.ConfigureLockFolder("relative", out m));
            Assert.StartsWith("ConfigureLockFolder failed: folderPath must be an absolute path", m);
            Assert.Equal(custom, c.LockFolder);
            Assert.True(c.ConfigureLockFolder("", out m), m);
            Assert.Equal(LockInput.DefaultFolder, c.LockFolder);
        }

        [Fact]
        public void TheFirstInvalidInput_FailsTheCall_AndTheMessageNamesTheRuleNotTheValue()
        {
            using var c = new ResourceLockUtils();
            Assert.False(c.TryAcquireLock(LockScope.Machine, "Secret Customer 4711", "Robot", 60, out bool acquired, out string token, out string holder, out string m));
            Assert.Equal((false, (string)null, (string)null), (acquired, token, holder));
            Assert.Equal("TryAcquireLock failed: resource may contain only ASCII letters, digits, -, _ and ..", m);
            Assert.DoesNotContain("4711", m);
            Assert.False(c.AcquireSlot(LockScope.Process, "Pool", 0, "Robot", 60, 1000, out _, out _, out _, out m));
            Assert.Contains("capacity must be between 1 and 100", m);
            Assert.False(c.RenewLock(LockScope.Process, "Pool", "nope", 60, out _, out _, out m));
            Assert.Contains("not a token", m);
            Assert.False(c.ForceReleaseLock(LockScope.Process, "Pool", false, out _, out m));
            Assert.Contains("confirmForceRelease must be True", m);
        }

        [Fact]
        public void WithValidInput_TheMachineScope_SaysItIsNotImplementedYet()
        {
            using var c = new ResourceLockUtils();
            Assert.False(c.TryAcquireLock(LockScope.Machine, "SAP-User", "Robot 1", 60, out _, out _, out _, out string m));
            Assert.Equal("TryAcquireLock failed: the Machine scope is not implemented yet.", m);
            Assert.False(c.ReleaseLock(LockScope.Machine, "SAP-User", Token, out _, out m));
            Assert.Equal("ReleaseLock failed: the Machine scope is not implemented yet.", m);
            Assert.False(c.ValidateLockFolder(out _, out _, out m));
            Assert.Equal("ValidateLockFolder is not implemented yet.", m);
        }
    }
}
