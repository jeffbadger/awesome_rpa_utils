using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ClipboardAutomation.Tests
{
    public class ModelTests
    {
        // ------------------------------------------------------------------ DROPFILES

        [Fact]
        public void DropFileList_BuildThenParse_RoundTrips_IncludingSpacesAndNonAscii()
        {
            var paths = new List<string> { @"C:\My Files\report 1.csv", @"D:\données\résumé.txt", @"\\server\share\a.bin" };

            byte[] data = DropFileList.Build(paths);

            Assert.True(DropFileList.TryParse(data, out List<string> parsed, out string error), error);
            Assert.Equal(paths, parsed);
        }

        [Fact]
        public void DropFileList_Build_ProducesAWellFormedBlock()
        {
            byte[] data = DropFileList.Build(new[] { "C:\\a", "C:\\bb" });

            Assert.Equal(20u, BitConverter.ToUInt32(data, 0));   // pFiles: the list starts right after the 20-byte header
            Assert.Equal(1, BitConverter.ToInt32(data, 16));     // fWide
            // "C:\a\0" + "C:\bb\0" + final "\0", two bytes per character
            Assert.Equal(20 + (4 + 1 + 5 + 1 + 1) * 2, data.Length);
            Assert.Equal(0, data[data.Length - 1]);
            Assert.Equal(0, data[data.Length - 2]);
        }

        [Fact]
        public void DropFileList_ParsesAnAnsiList()
        {
            byte[] list = Encoding.ASCII.GetBytes("C:\\one.txt\0C:\\two.txt\0\0");
            var data = new byte[20 + list.Length];
            BitConverter.GetBytes(20u).CopyTo(data, 0);
            // fWide stays 0
            Buffer.BlockCopy(list, 0, data, 20, list.Length);

            Assert.True(DropFileList.TryParse(data, out List<string> paths, out _));

            Assert.Equal(new[] { "C:\\one.txt", "C:\\two.txt" }, paths);
        }

        [Fact]
        public void DropFileList_ParsesAnEmptyList()
        {
            byte[] data = DropFileList.Build(new string[0]);

            Assert.True(DropFileList.TryParse(data, out List<string> paths, out _));

            Assert.Empty(paths);
        }

        [Fact]
        public void DropFileList_ReadsAsFarAsAnUnterminatedListGoes()
        {
            byte[] full = DropFileList.Build(new[] { "C:\\a.txt" });
            var truncated = new byte[full.Length - 4]; // loses the terminators
            Buffer.BlockCopy(full, 0, truncated, 0, truncated.Length);

            Assert.True(DropFileList.TryParse(truncated, out List<string> paths, out _));

            Assert.Equal(new[] { "C:\\a.txt" }, paths);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(19)]
        public void DropFileList_RejectsABlockShorterThanItsHeader(int length)
        {
            Assert.False(DropFileList.TryParse(new byte[length], out List<string> paths, out string error));
            Assert.Empty(paths);
            Assert.Contains("too short", error);
        }

        [Fact]
        public void DropFileList_RejectsAListThatStartsOutsideTheData()
        {
            byte[] data = DropFileList.Build(new[] { "C:\\a" });
            BitConverter.GetBytes(9999u).CopyTo(data, 0);
            Assert.False(DropFileList.TryParse(data, out _, out string outside));
            Assert.Contains("outside", outside);

            BitConverter.GetBytes(4u).CopyTo(data, 0); // inside the header
            Assert.False(DropFileList.TryParse(data, out _, out _));
        }

        [Fact]
        public void DropFileList_RejectsNull()
        {
            Assert.False(DropFileList.TryParse(null, out _, out string error));
            Assert.NotNull(error);
        }

        // ------------------------------------------------------------------ formats

        [Theory]
        [InlineData(1u, "CF_TEXT")]
        [InlineData(13u, "CF_UNICODETEXT")]
        [InlineData(15u, "CF_HDROP")]
        [InlineData(17u, "CF_DIBV5")]
        public void StandardName_NamesThePredefinedFormats(uint id, string expected)
        {
            Assert.Equal(expected, ClipboardFormats.StandardName(id));
        }

        [Fact]
        public void StandardName_IsNullForARegisteredFormat()
        {
            Assert.Null(ClipboardFormats.StandardName(0xC123));
            Assert.Null(ClipboardFormats.StandardName(0x1234));
        }

        [Theory]
        [InlineData("CF_HDROP", 15u)]
        [InlineData("cf_hdrop", 15u)]
        [InlineData("HDROP", 15u)]
        [InlineData("UnicodeText", 13u)]
        [InlineData("dib", 8u)]
        [InlineData("15", 15u)]
        [InlineData("0x0F", 15u)]
        [InlineData("  CF_TEXT ", 1u)]
        public void TryParseStandard_AcceptsNamesWithOrWithoutThePrefix_AndNumbers(string text, uint expected)
        {
            Assert.True(ClipboardFormats.TryParseStandard(text, out uint id));
            Assert.Equal(expected, id);
        }

        [Theory]
        [InlineData("HTML Format")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("0")]
        [InlineData("49152")]   // 0xC000: a registered number is not a predefined format
        [InlineData("0xC000")]
        [InlineData(null)]
        public void TryParseStandard_RejectsWhatIsNotAPredefinedFormat(string text)
        {
            Assert.False(ClipboardFormats.TryParseStandard(text, out _));
        }

        [Theory]
        [InlineData(2u, true)]    // CF_BITMAP
        [InlineData(3u, true)]    // CF_METAFILEPICT
        [InlineData(9u, true)]    // CF_PALETTE
        [InlineData(0x80u, true)]
        [InlineData(0x82u, true)]
        [InlineData(0x300u, true)]
        [InlineData(0x3FFu, true)]
        [InlineData(1u, false)]
        [InlineData(8u, false)]
        [InlineData(13u, false)]
        [InlineData(14u, false)]  // CF_ENHMETAFILE is a handle, but is copied through GetEnhMetaFileBits
        [InlineData(15u, false)]
        [InlineData(0x81u, false)] // CF_DSPTEXT is plain memory
        [InlineData(0x200u, false)]
        [InlineData(0xC001u, false)]
        public void IsHandleBased_MarksTheFormatsWhoseBytesCannotBeCopied(uint id, bool expected)
        {
            Assert.Equal(expected, ClipboardFormats.IsHandleBased(id));
        }

        [Fact]
        public void IsRebuiltFrom_OnlyCountsWhenTheSourceFormatIsThere()
        {
            var withDib = new HashSet<uint> { ClipboardFormats.CF_DIB, ClipboardFormats.CF_BITMAP };
            var withDibV5 = new HashSet<uint> { ClipboardFormats.CF_DIBV5 };
            var withEmf = new HashSet<uint> { ClipboardFormats.CF_ENHMETAFILE, ClipboardFormats.CF_METAFILEPICT };
            var bitmapOnly = new HashSet<uint> { ClipboardFormats.CF_BITMAP };

            Assert.True(ClipboardFormats.IsRebuiltFrom(ClipboardFormats.CF_BITMAP, withDib));
            Assert.True(ClipboardFormats.IsRebuiltFrom(ClipboardFormats.CF_PALETTE, withDib));
            Assert.True(ClipboardFormats.IsRebuiltFrom(ClipboardFormats.CF_BITMAP, withDibV5));
            Assert.True(ClipboardFormats.IsRebuiltFrom(ClipboardFormats.CF_METAFILEPICT, withEmf));
            Assert.False(ClipboardFormats.IsRebuiltFrom(ClipboardFormats.CF_BITMAP, bitmapOnly));
            Assert.False(ClipboardFormats.IsRebuiltFrom(ClipboardFormats.CF_METAFILEPICT, withDib));
            Assert.False(ClipboardFormats.IsRebuiltFrom(ClipboardFormats.CF_DIB, withDib)); // real data is never "rebuilt"
        }

        [Theory]
        [InlineData(0xC000u, true)]
        [InlineData(0xFFFFu, true)]
        [InlineData(0xBFFFu, false)]
        [InlineData(0x10000u, false)]
        [InlineData(13u, false)]
        public void IsRegistered_CoversTheRegisteredRange(uint id, bool expected)
        {
            Assert.Equal(expected, ClipboardFormats.IsRegistered(id));
        }

        // ------------------------------------------------------------------ snapshots

        [Fact]
        public void Wipe_OverwritesTheBytesAndEmptiesTheSnapshot()
        {
            byte[] secret = Encoding.UTF8.GetBytes("hunter2");
            var snapshot = new ClipboardSnapshot();
            snapshot.Entries.Add(new ClipboardEntry { Id = 1, Name = "CF_TEXT", Data = secret });

            snapshot.Wipe();

            Assert.All(secret, b => Assert.Equal(0, b));
            Assert.Empty(snapshot.Entries);
        }

        [Fact]
        public void HasLoss_IgnoresFormatsThatWindowsRebuilds()
        {
            var snapshot = new ClipboardSnapshot();
            snapshot.Skipped.Add(new SkippedFormat { Name = "CF_BITMAP", IsLoss = false });
            Assert.False(snapshot.HasLoss);

            snapshot.Skipped.Add(new SkippedFormat { Name = "CF_GDIOBJ_0x300", IsLoss = true });
            Assert.True(snapshot.HasLoss);
        }

        [Fact]
        public void TotalBytes_AddsUpTheEntries()
        {
            var snapshot = new ClipboardSnapshot();
            snapshot.Entries.Add(new ClipboardEntry { Data = new byte[10] });
            snapshot.Entries.Add(new ClipboardEntry { Data = new byte[5] });

            Assert.Equal(15, snapshot.TotalBytes);
        }

        [Fact]
        public void DropFileList_AtTheLimitParses_OneOverIsRefusedRatherThanTruncated()
        {
            var atLimit = new List<string>();
            for (int i = 0; i < DropFileList.MaxFiles; i++)
                atLimit.Add("a");

            Assert.True(DropFileList.TryParse(DropFileList.Build(atLimit), out List<string> parsed, out string error), error);
            Assert.Equal(DropFileList.MaxFiles, parsed.Count);

            atLimit.Add("a");
            Assert.False(DropFileList.TryParse(DropFileList.Build(atLimit), out List<string> refused, out string message));
            Assert.Empty(refused);
            Assert.Contains("more than", message);
        }
    }
}
