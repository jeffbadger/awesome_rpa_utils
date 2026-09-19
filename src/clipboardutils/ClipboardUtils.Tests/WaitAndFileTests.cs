using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ClipboardAutomation.Tests
{
    public class WaitTests
    {
        [Fact]
        public void WaitForChange_ReturnsWhenTheSequenceNumberMoves_PollingAtTheInterval()
        {
            using var rig = new Rig();
            // baseline 500, then unchanged twice, then changed
            rig.Clipboard.ScriptedSequence = new Queue<long>(new long[] { 500, 500, 500, 501 });

            Assert.True(rig.Utils.WaitForClipboardChange(5000, 50, out bool timedOut, out string message), message);

            Assert.False(timedOut);
            Assert.Null(message);
            Assert.Equal(new[] { 50, 50 }, rig.Sleeps);
        }

        [Fact]
        public void WaitForChange_TimesOut_WithTimedOutSetAndNoMessage()
        {
            using var rig = new Rig();
            rig.Clipboard.ScriptedSequence = new Queue<long>(new long[] { 7 }); // never changes

            Assert.False(rig.Utils.WaitForClipboardChange(200, 50, out bool timedOut, out string message));

            Assert.True(timedOut);
            Assert.Null(message);                 // a timeout is a normal outcome, not an error
            Assert.Equal(200, rig.Sleeps.Sum());  // it waited the whole time, no more
            Assert.All(rig.Sleeps, s => Assert.True(s <= 50));
        }

        [Fact]
        public void WaitForChange_TheLastSleepIsShortenedToTheTimeThatRemains()
        {
            using var rig = new Rig();
            rig.Clipboard.ScriptedSequence = new Queue<long>(new long[] { 7 });

            Assert.False(rig.Utils.WaitForClipboardChange(120, 50, out _, out _));

            Assert.Equal(new[] { 50, 50, 20 }, rig.Sleeps);
        }

        [Fact]
        public void WaitForChange_WithZeroTimeout_ChecksOnce()
        {
            using var rig = new Rig();
            rig.Clipboard.ScriptedSequence = new Queue<long>(new long[] { 7 });

            Assert.False(rig.Utils.WaitForClipboardChange(0, 50, out bool timedOut, out _));

            Assert.True(timedOut);
            Assert.Empty(rig.Sleeps);
        }

        [Fact]
        public void WaitForChangeSince_ReturnsAtOnce_IfItHasAlreadyChanged()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.GetClipboardSequenceNumber(out long before, out _));
            Assert.True(rig.Utils.SetClipboardText("changed before the wait began", out _));

            Assert.True(rig.Utils.WaitForClipboardChangeSince(before, 5000, 50, out bool timedOut, out string message), message);

            Assert.False(timedOut);
            Assert.Empty(rig.Sleeps);
        }

        [Fact]
        public void WaitForChange_MissesAChangeMadeBeforeItStarted_WhichIsWhatTheSinceVariantIsFor()
        {
            using var rig = new Rig();
            rig.Utils.SetClipboardText("changed before the wait began", out _);

            // Measured from the call, the earlier change is not seen.
            Assert.False(rig.Utils.WaitForClipboardChange(100, 50, out bool timedOut, out _));
            Assert.True(timedOut);
        }

        [Fact]
        public void WaitForChangeSince_WaitsForAChangeThatHappensDuringTheWait()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.GetClipboardSequenceNumber(out long before, out _));
            int polls = 0;
            rig.OnSleep = ms => { if (++polls == 3) rig.Clipboard.ExternalChange(); };

            Assert.True(rig.Utils.WaitForClipboardChangeSince(before, 5000, 50, out bool timedOut, out _));

            Assert.False(timedOut);
            Assert.Equal(3, rig.Sleeps.Count);
        }

        [Fact]
        public void WaitForFormat_ReturnsAtOnce_IfTheFormatIsAlreadyThere()
        {
            using var rig = new Rig().WithRichContent();

            Assert.True(rig.Utils.WaitForClipboardFormat("HTML Format", 5000, 50, out bool timedOut, out string message), message);

            Assert.False(timedOut);
            Assert.Empty(rig.Sleeps);
        }

        [Fact]
        public void WaitForFormat_WaitsUntilTheFormatAppears()
        {
            using var rig = new Rig();
            int polls = 0;
            rig.OnSleep = ms => { if (++polls == 4) rig.Clipboard.Put(ClipboardFormats.CF_HDROP, new byte[24]); };

            Assert.True(rig.Utils.WaitForClipboardFormat("CF_HDROP", 5000, 50, out bool timedOut, out _));

            Assert.False(timedOut);
            Assert.Equal(4, rig.Sleeps.Count);
        }

        [Fact]
        public void WaitForFormat_TimesOutWithoutAMessage()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.WaitForClipboardFormat("DIB", 100, 25, out bool timedOut, out string message));

            Assert.True(timedOut);
            Assert.Null(message);
        }

        [Fact]
        public void WaitForFormat_RefusesAnEmptyName()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.WaitForClipboardFormat("", 100, 25, out bool timedOut, out string message));

            Assert.False(timedOut);
            Assert.Contains("formatName", message);
        }

        [Theory]
        [InlineData(-1, 50)]
        [InlineData(3600001, 50)]
        [InlineData(1000, 4)]
        [InlineData(1000, 60001)]
        public void EveryWait_RefusesOutOfRangeTimeoutsAndIntervals(int timeoutMs, int pollMs)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.WaitForClipboardChange(timeoutMs, pollMs, out bool t1, out string m1));
            Assert.False(rig.Utils.WaitForClipboardChangeSince(1, timeoutMs, pollMs, out bool t2, out string m2));
            Assert.False(rig.Utils.WaitForClipboardFormat("CF_TEXT", timeoutMs, pollMs, out bool t3, out string m3));

            Assert.False(t1 || t2 || t3);              // an invalid call is an error, not a timeout
            Assert.All(new[] { m1, m2, m3 }, m => Assert.Contains(m.Contains("timeoutMs") ? "timeoutMs" : "pollIntervalMs", m));
            Assert.Empty(rig.Sleeps);
        }

        [Fact]
        public void EveryWait_AcceptsTheEdgesOfItsRanges()
        {
            using var rig = new Rig();
            rig.Clipboard.ScriptedSequence = new Queue<long>(new long[] { 1 });

            Assert.False(rig.Utils.WaitForClipboardChange(0, 5, out _, out string m1)); Assert.Null(m1);
            Assert.False(rig.Utils.WaitForClipboardChange(0, 60000, out _, out string m2)); Assert.Null(m2);
            Assert.False(rig.Utils.WaitForClipboardFormat("DIB", 0, 5, out _, out string m3)); Assert.Null(m3);
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(4294967296L)]
        public void WaitForChangeSince_RefusesANumberThatIsNotASequenceNumber(long sequence)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.WaitForClipboardChangeSince(sequence, 100, 50, out bool timedOut, out string message));

            Assert.False(timedOut);
            Assert.Contains("sequenceNumber", message);
        }
    }

    public class FileDropTests : IDisposable
    {
        private readonly string _folder;

        public FileDropTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "cliptests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); }
            catch (IOException) { }
        }

        private string MakeFile(string name)
        {
            string path = Path.Combine(_folder, name);
            File.WriteAllText(path, "x");
            return path;
        }

        // ------------------------------------------------------------------ set

        [Fact]
        public void SetThenGet_RoundTripsThePathsAndTheEffect()
        {
            using var rig = new Rig();
            string a = MakeFile("report 1.csv");
            string b = MakeFile("données.txt");

            Assert.True(rig.Utils.SetFileDropList(a + "\r\n" + b, FileDropEffect.Move, out string set), set);
            Assert.Null(set);

            Assert.True(rig.Utils.GetFileDropListText(out string text, out int count, out FileDropEffect effect, out string get), get);
            Assert.Equal(2, count);
            Assert.Equal(FileDropEffect.Move, effect);
            Assert.Equal(new[] { a, b }, text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));

            Assert.True(rig.Utils.GetFileDropListJson(out string json, out int jsonCount, out _, out _));
            Assert.Equal(2, jsonCount);
            Assert.Equal(new[] { a, b }, JsonSerializer.Deserialize<string[]>(json));
        }

        [Fact]
        public void Set_WritesAnHdropAndAPreferredDropEffect_AndNothingElse()
        {
            using var rig = new Rig().WithRichContent();
            string file = MakeFile("a.txt");

            Assert.True(rig.Utils.SetFileDropList(file, FileDropEffect.Copy, out _));

            Assert.Equal(2, rig.Clipboard.Items.Count);
            Assert.True(DropFileList.TryParse(rig.Clipboard.DataOf(ClipboardFormats.CF_HDROP), out List<string> paths, out _));
            Assert.Equal(new[] { file }, paths);
            Assert.Equal(1u, BitConverter.ToUInt32(rig.Clipboard.DataOf(ClipboardFormats.PreferredDropEffectName), 0));
        }

        [Theory]
        [InlineData(FileDropEffect.Copy, 1u)]
        [InlineData(FileDropEffect.Move, 2u)]
        [InlineData(FileDropEffect.Link, 4u)]
        public void Set_WritesTheDropEffectTheCallerAskedFor(FileDropEffect effect, uint expected)
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.SetFileDropList(MakeFile("a.txt"), effect, out _));

            Assert.Equal(expected, BitConverter.ToUInt32(rig.Clipboard.DataOf(ClipboardFormats.PreferredDropEffectName), 0));
            Assert.True(rig.Utils.GetFileDropListText(out _, out _, out FileDropEffect read, out _));
            Assert.Equal(effect, read);
        }

        [Fact]
        public void Set_IgnoresBlankLinesAndSurroundingQuotes_AndAcceptsEitherLineBreak()
        {
            using var rig = new Rig();
            string a = MakeFile("a.txt");
            string b = MakeFile("b.txt");

            Assert.True(rig.Utils.SetFileDropList("\r\n\"" + a + "\"\n\n   " + b + "  \r\n", FileDropEffect.Copy, out string message), message);

            Assert.True(rig.Utils.GetFileDropListText(out _, out int count, out _, out _));
            Assert.Equal(2, count);
        }

        [Fact]
        public void Set_MakesRelativePathsAbsolute()
        {
            using var rig = new Rig();
            string file = MakeFile("rel.txt");
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(_folder);
                Assert.True(rig.Utils.SetFileDropList("rel.txt", FileDropEffect.Copy, out string message), message);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }

            Assert.True(rig.Utils.GetFileDropListText(out string text, out _, out _, out _));
            Assert.Equal(file, text);
        }

        [Fact]
        public void Set_AcceptsAFolder()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.SetFileDropList(_folder, FileDropEffect.Copy, out string message), message);
        }

        [Fact]
        public void Set_RefusesAPathThatDoesNotExist_AndLeavesTheClipboardAlone()
        {
            using var rig = new Rig().WithRichContent();
            string real = MakeFile("real.txt");
            string missing = Path.Combine(_folder, "missing.txt");
            long sequence = rig.Clipboard.SequenceNumber;

            Assert.False(rig.Utils.SetFileDropList(real + "\n" + missing, FileDropEffect.Copy, out string message));

            Assert.Contains("do not exist", message);
            Assert.Contains(missing, message);
            Assert.DoesNotContain(real + ";", message);
            Assert.Equal(sequence, rig.Clipboard.SequenceNumber);
            Assert.Equal(0, rig.Clipboard.OpenCount);
        }

        [Fact]
        public void Set_WithRequireExistingOff_PutsNonexistentPathsOnTheClipboardAnyway()
        {
            using var rig = new Rig();
            string missing = Path.Combine(_folder, "will-exist-later.txt");

            Assert.True(rig.Utils.SetFileDropList(missing, FileDropEffect.Copy, out string message, requireExisting: false), message);

            Assert.True(rig.Utils.GetFileDropListText(out string text, out _, out _, out _));
            Assert.Equal(missing, text);
        }

        [Fact]
        public void Set_ListsAtMostFiveMissingPaths()
        {
            using var rig = new Rig();
            string paths = string.Join("\n", Enumerable.Range(1, 8).Select(i => Path.Combine(_folder, "m" + i + ".txt")));

            Assert.False(rig.Utils.SetFileDropList(paths, FileDropEffect.Copy, out string message));

            Assert.Contains("and 3 more", message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        public void Set_RefusesAnEmptyList(string paths)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SetFileDropList(paths, FileDropEffect.Copy, out string message));

            Assert.Contains("empty", message);
        }

        [Fact]
        public void Set_RefusesWildcards_AndInvalidPaths()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SetFileDropList(Path.Combine(_folder, "*.txt"), FileDropEffect.Copy, out string wildcard, requireExisting: false));
            Assert.Contains("wildcard", wildcard);
            Assert.False(rig.Utils.SetFileDropList("C:\\bad|name?.txt", FileDropEffect.Copy, out string invalid, requireExisting: false));
            Assert.Contains("not a valid file path", invalid);
        }

        [Fact]
        public void Set_RefusesAnUndefinedEffect_AndNull()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SetFileDropList(MakeFile("a.txt"), (FileDropEffect)99, out string effect));
            Assert.Contains("FileDropEffect", effect);
            Assert.False(rig.Utils.SetFileDropList(null, FileDropEffect.Copy, out string nul));
            Assert.Contains("null", nul);
        }

        [Fact]
        public void Set_ReportsAClipboardThatRefusesTheList()
        {
            using var rig = new Rig();
            rig.Clipboard.UnwritableIds.Add(ClipboardFormats.CF_HDROP);

            Assert.False(rig.Utils.SetFileDropList(MakeFile("a.txt"), FileDropEffect.Copy, out string message));

            Assert.Contains("refused", message);
            Assert.False(rig.Clipboard.IsOpen);
        }

        [Fact]
        public void Set_ReportsAnEffectThatCouldNotBeWritten()
        {
            using var rig = new Rig();
            rig.Clipboard.UnwritableIds.Add(rig.Clipboard.Register(ClipboardFormats.PreferredDropEffectName));

            Assert.False(rig.Utils.SetFileDropList(MakeFile("a.txt"), FileDropEffect.Move, out string message));

            Assert.Contains("not whether to copy or move", message);
        }

        [Fact]
        public void Set_ARepeatedPathIsKeptAsGiven()
        {
            using var rig = new Rig();
            string a = MakeFile("a.txt");

            Assert.True(rig.Utils.SetFileDropList(a + "\n" + a, FileDropEffect.Copy, out _));

            Assert.True(rig.Utils.GetFileDropListText(out _, out int count, out _, out _));
            Assert.Equal(2, count);
        }

        // ------------------------------------------------------------------ set from JSON

        [Fact]
        public void SetFromJson_RoundTrips()
        {
            using var rig = new Rig();
            string a = MakeFile("a.txt");
            string b = MakeFile("b b.txt");
            string json = JsonSerializer.Serialize(new[] { a, b });

            Assert.True(rig.Utils.SetFileDropListJson(json, FileDropEffect.Move, out string message), message);

            Assert.True(rig.Utils.GetFileDropListJson(out string read, out int count, out FileDropEffect effect, out _));
            Assert.Equal(new[] { a, b }, JsonSerializer.Deserialize<string[]>(read));
            Assert.Equal(2, count);
            Assert.Equal(FileDropEffect.Move, effect);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"a\":1}")]
        [InlineData("[1,2]")]
        [InlineData("\"just a string\"")]
        [InlineData("[\"ok\", ")]
        public void SetFromJson_RefusesAnythingButAnArrayOfStrings(string json)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SetFileDropListJson(json, FileDropEffect.Copy, out string message, requireExisting: false));

            Assert.Contains("JSON array of strings", message);
            Assert.Equal(0, rig.Clipboard.OpenCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void SetFromJson_RefusesEmptyInput(string json)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SetFileDropListJson(json, FileDropEffect.Copy, out string message));

            Assert.Contains("pathsJson", message);
        }

        [Fact]
        public void SetFromJson_RefusesAnEmptyArray_AndNull()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SetFileDropListJson("[]", FileDropEffect.Copy, out string empty));
            Assert.Contains("empty", empty);
            Assert.False(rig.Utils.SetFileDropListJson("null", FileDropEffect.Copy, out string nul));
            Assert.Contains("JSON array of strings", nul);
        }

        // ------------------------------------------------------------------ get

        [Fact]
        public void Get_WithNoFileList_IsASuccessWithNothing()
        {
            using var rig = new Rig().WithRichContent();

            Assert.True(rig.Utils.GetFileDropListJson(out string json, out int count, out FileDropEffect effect, out string message));

            Assert.Equal("[]", json);
            Assert.Equal(0, count);
            Assert.Equal(FileDropEffect.Copy, effect);
            Assert.Null(message);
            Assert.True(rig.Utils.GetFileDropListText(out string text, out int textCount, out _, out _));
            Assert.Equal(string.Empty, text);
            Assert.Equal(0, textCount);
        }

        [Fact]
        public void Get_WhenTheClipboardSaysNothingAboutTheEffect_ReadsItAsCopy()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_HDROP, DropFileList.Build(new[] { @"C:\a.txt" }));

            Assert.True(rig.Utils.GetFileDropListText(out string text, out int count, out FileDropEffect effect, out string message), message);

            Assert.Equal(@"C:\a.txt", text);
            Assert.Equal(1, count);
            Assert.Equal(FileDropEffect.Copy, effect);
        }

        [Theory]
        [InlineData(1u, FileDropEffect.Copy)]
        [InlineData(2u, FileDropEffect.Move)]
        [InlineData(3u, FileDropEffect.Move)]   // copy|move: a move wins, as Explorer treats it
        [InlineData(4u, FileDropEffect.Link)]
        [InlineData(5u, FileDropEffect.Copy)]   // copy|link
        [InlineData(0u, FileDropEffect.Copy)]
        [InlineData(0x80000000u, FileDropEffect.Copy)]
        public void Get_InterpretsThePreferredDropEffectBits(uint bits, FileDropEffect expected)
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_HDROP, DropFileList.Build(new[] { @"C:\a.txt" }));
            rig.Clipboard.PutRegistered(ClipboardFormats.PreferredDropEffectName, BitConverter.GetBytes(bits));

            Assert.True(rig.Utils.GetFileDropListText(out _, out _, out FileDropEffect effect, out _));

            Assert.Equal(expected, effect);
        }

        [Fact]
        public void Get_ReadsAnAnsiFileList()
        {
            using var rig = new Rig();
            byte[] list = Encoding.ASCII.GetBytes("C:\\one.txt\0C:\\two.txt\0\0");
            var data = new byte[20 + list.Length];
            BitConverter.GetBytes(20u).CopyTo(data, 0);
            Buffer.BlockCopy(list, 0, data, 20, list.Length);
            rig.Clipboard.Put(ClipboardFormats.CF_HDROP, data);

            Assert.True(rig.Utils.GetFileDropListJson(out string json, out int count, out _, out string message), message);

            Assert.Equal(2, count);
            Assert.Equal(new[] { "C:\\one.txt", "C:\\two.txt" }, JsonSerializer.Deserialize<string[]>(json));
        }

        [Fact]
        public void Get_ReportsAMalformedFileList()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_HDROP, new byte[5]);

            Assert.False(rig.Utils.GetFileDropListJson(out string json, out int count, out _, out string message));

            Assert.Contains("too short", message);
            Assert.Equal(string.Empty, json);
            Assert.Equal(0, count);
            Assert.False(rig.Clipboard.IsOpen);
        }

        [Fact]
        public void Get_ReportsAnOwnerThatWontGiveTheList()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_HDROP, DropFileList.Build(new[] { @"C:\a.txt" }));
            rig.Clipboard.UnreadableIds.Add(ClipboardFormats.CF_HDROP);

            Assert.False(rig.Utils.GetFileDropListText(out _, out _, out _, out string message));

            Assert.Contains("would not give its file list", message);
        }

        [Fact]
        public void Get_ThePathsComeBackAsJsonWithoutEscapingOrdinaryCharacters()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_HDROP, DropFileList.Build(new[] { @"C:\R&D\été.txt" }));

            Assert.True(rig.Utils.GetFileDropListJson(out string json, out _, out _, out _));

            Assert.Contains("R&D", json);
            Assert.Contains("été", json);
        }

        // ------------------------------------------------------------------ never throws

        [Fact]
        public void EveryFileMethod_AfterDispose_ReportsFailure()
        {
            var rig = new Rig();
            rig.Dispose();

            Assert.False(rig.Utils.GetFileDropListJson(out _, out _, out _, out string m1)); Assert.Contains("disposed", m1);
            Assert.False(rig.Utils.GetFileDropListText(out _, out _, out _, out string m2)); Assert.Contains("disposed", m2);
            Assert.False(rig.Utils.SetFileDropList(_folder, FileDropEffect.Copy, out string m3)); Assert.Contains("disposed", m3);
            Assert.False(rig.Utils.SetFileDropListJson("[\"" + _folder.Replace("\\", "\\\\") + "\"]", FileDropEffect.Copy, out string m4)); Assert.Contains("disposed", m4);
        }
    }
}
