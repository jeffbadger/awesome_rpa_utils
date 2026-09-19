using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace ClipboardAutomation.Tests
{
    public class HistoryTests
    {
        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount - deadline < 0)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        private static int Count(Rig rig)
        {
            Assert.True(rig.Utils.GetClipboardHistoryCount(out int count, out string message), message);
            return count;
        }

        private static string TextAt(Rig rig, int index)
        {
            Assert.True(rig.Utils.GetClipboardHistoryText(index, out string text, out _, out string message), message);
            return text;
        }

        private static void Copy(Rig rig, string text) => rig.Clipboard.ExternalReplace(c => c.PutText(text));

        private static JsonElement Status(Rig rig)
        {
            Assert.True(rig.Utils.GetClipboardHistoryStatusJson(out string json, out string message), message);
            return JsonDocument.Parse(json).RootElement;
        }

        private static void StartOk(Rig rig, int maxItems, ClipboardHistoryMode mode = ClipboardHistoryMode.TextOnly, bool captureCurrent = false)
        {
            Assert.True(rig.Utils.StartClipboardHistory(maxItems, out string message, mode, pollIntervalMs: 25, captureCurrent: captureCurrent), message);
            Assert.Null(message);
        }

        // ------------------------------------------------------------------ starting and stopping

        [Fact]
        public void Start_RefusesOutOfRangeSettings()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.StartClipboardHistory(0, out string m1)); Assert.Contains("maxItems", m1);
            Assert.False(rig.Utils.StartClipboardHistory(1001, out string m2)); Assert.Contains("maxItems", m2);
            Assert.False(rig.Utils.StartClipboardHistory(5, out string m3, pollIntervalMs: 24)); Assert.Contains("pollIntervalMs", m3);
            Assert.False(rig.Utils.StartClipboardHistory(5, out string m4, pollIntervalMs: 60001)); Assert.Contains("pollIntervalMs", m4);
            Assert.False(rig.Utils.StartClipboardHistory(5, out string m5, mode: (ClipboardHistoryMode)7)); Assert.Contains("mode", m5);
            Assert.False(Status(rig).GetProperty("running").GetBoolean());
        }

        [Fact]
        public void Start_AcceptsTheEdgesOfItsRanges()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.StartClipboardHistory(1, out _, pollIntervalMs: 25));
            Assert.True(rig.Utils.StopClipboardHistory(out _));
            Assert.True(rig.Utils.StartClipboardHistory(1000, out _, pollIntervalMs: 60000));
        }

        [Fact]
        public void StartTwice_IsRefused_UntilItIsStopped()
        {
            using var rig = new Rig();
            StartOk(rig, 5);

            Assert.False(rig.Utils.StartClipboardHistory(5, out string message, pollIntervalMs: 25));

            Assert.Contains("already running", message);
            Assert.True(rig.Utils.StopClipboardHistory(out _));
            Assert.True(rig.Utils.StopClipboardHistory(out _)); // stopping again is fine
            StartOk(rig, 5);
        }

        [Fact]
        public void Status_ReportsTheSettingsAndTheCounts()
        {
            using var rig = new Rig();
            StartOk(rig, 7, ClipboardHistoryMode.AllFormats);
            Copy(rig, "one");
            Assert.True(WaitFor(() => Count(rig) == 1));

            var status = Status(rig);

            Assert.True(status.GetProperty("running").GetBoolean());
            Assert.Equal("AllFormats", status.GetProperty("mode").GetString());
            Assert.Equal(7, status.GetProperty("maxItems").GetInt32());
            Assert.Equal(25, status.GetProperty("pollIntervalMs").GetInt32());
            Assert.Equal(1, status.GetProperty("count").GetInt32());
            Assert.Equal(1, status.GetProperty("recorded").GetInt32());
            Assert.True(status.GetProperty("totalBytes").GetInt64() > 0);
            Assert.Equal(0, status.GetProperty("skippedExcluded").GetInt32());
            Assert.Equal(string.Empty, status.GetProperty("lastError").GetString());
        }

        // ------------------------------------------------------------------ what is recorded

        [Fact]
        public void EveryNewCopy_IsRecorded_NewestFirst()
        {
            using var rig = new Rig();
            StartOk(rig, 10);

            foreach (string text in new[] { "alpha", "bravo", "charlie" })
            {
                Copy(rig, text);
                int expected = Count(rig) + 1;
                Assert.True(WaitFor(() => Count(rig) == expected), "not recorded: " + text);
            }

            Assert.Equal("charlie", TextAt(rig, 0));
            Assert.Equal("bravo", TextAt(rig, 1));
            Assert.Equal("alpha", TextAt(rig, 2));
            Assert.True(rig.Utils.GetClipboardHistoryJson(10, out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(new[] { 0, 1, 2 }, doc.RootElement.EnumerateArray().Select(e => e.GetProperty("index").GetInt32()).ToArray());
        }

        [Fact]
        public void OnlyTheLastNItemsAreKept()
        {
            using var rig = new Rig();
            StartOk(rig, 3);

            for (int i = 1; i <= 6; i++)
            {
                Copy(rig, "item " + i);
                int recorded = i;
                Assert.True(WaitFor(() => Status(rig).GetProperty("recorded").GetInt32() == recorded));
            }

            Assert.Equal(3, Count(rig));
            Assert.Equal("item 6", TextAt(rig, 0));
            Assert.Equal("item 4", TextAt(rig, 2));
        }

        [Fact]
        public void TheSameThingCopiedTwiceInARow_IsOneItem()
        {
            using var rig = new Rig();
            StartOk(rig, 10);
            Copy(rig, "same");
            Assert.True(WaitFor(() => Count(rig) == 1));

            Copy(rig, "same");
            Thread.Sleep(400);
            Assert.Equal(1, Count(rig));

            Copy(rig, "different");
            Assert.True(WaitFor(() => Count(rig) == 2));
            Copy(rig, "same");   // not consecutive, so it is a new item
            Assert.True(WaitFor(() => Count(rig) == 3));
        }

        [Fact]
        public void ByDefaultWhatIsAlreadyOnTheClipboardIsNotRecorded_ButCaptureCurrentRecordsIt()
        {
            using var without = new Rig();
            without.Clipboard.PutText("already here");
            StartOk(without, 5);
            Thread.Sleep(400);
            Assert.Equal(0, Count(without));

            using var with = new Rig();
            with.Clipboard.PutText("already here");
            StartOk(with, 5, captureCurrent: true);
            Assert.True(WaitFor(() => Count(with) == 1));
            Assert.Equal("already here", TextAt(with, 0));
        }

        [Fact]
        public void EmptyingTheClipboard_IsNotAnItem()
        {
            using var rig = new Rig();
            StartOk(rig, 5);

            rig.Clipboard.ExternalReplace(c => { });   // someone cleared it
            Thread.Sleep(400);

            Assert.Equal(0, Count(rig));
            Assert.Equal(string.Empty, Status(rig).GetProperty("lastError").GetString());
        }

        [Fact]
        public void ACopyThatIsWrittenInSeveralSteps_IsOneCompleteItem_NotAPartialOne()
        {
            using var rig = new Rig(historySettleMs: 120);
            StartOk(rig, 10, ClipboardHistoryMode.AllFormats);

            // The clipboard changes three times over ~100 ms, as a copy that writes each format in turn does.
            rig.Clipboard.ExternalReplace(c => c.PutText("first step"));
            Thread.Sleep(50);
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[] { 1, 2, 3, 4 });
            Thread.Sleep(50);
            rig.Clipboard.PutRegistered("HTML Format", Encoding.UTF8.GetBytes("<b>x</b>"));

            Assert.True(WaitFor(() => Count(rig) >= 1));
            Thread.Sleep(500);

            Assert.Equal(1, Count(rig));
            Assert.True(rig.Utils.GetClipboardHistoryJson(1, out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(new[] { "CF_UNICODETEXT", "CF_DIB", "HTML Format" },
                doc.RootElement[0].GetProperty("formats").EnumerateArray().Select(f => f.GetString()).ToArray());
        }

        // ------------------------------------------------------------------ text-only versus all formats

        [Fact]
        public void TextOnly_KeepsTheTextFilesAndFormatNames_ButNotTheOtherFormats()
        {
            using var rig = new Rig();
            StartOk(rig, 5);

            rig.Clipboard.ExternalReplace(c =>
            {
                c.PutText("with files and an image");
                c.Put(ClipboardFormats.CF_HDROP, DropFileList.Build(new[] { @"C:\a.txt", @"C:\b b.txt" }));
                c.PutRegistered(ClipboardFormats.PreferredDropEffectName, BitConverter.GetBytes(2u));
                c.Put(ClipboardFormats.CF_DIB, new byte[10 * 1024]);
            });
            Assert.True(WaitFor(() => Count(rig) == 1));

            Assert.True(rig.Utils.GetClipboardHistoryJson(1, out string json, out _, includeText: true));
            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement[0];
            Assert.True(item.GetProperty("hasText").GetBoolean());
            Assert.Equal(2, item.GetProperty("fileCount").GetInt32());
            Assert.Equal("Move", item.GetProperty("effect").GetString());
            Assert.Equal(new[] { @"C:\a.txt", @"C:\b b.txt" }, item.GetProperty("files").EnumerateArray().Select(f => f.GetString()).ToArray());
            var formats = item.GetProperty("formats").EnumerateArray().Select(f => f.GetString()).ToList();
            Assert.Contains("CF_DIB", formats);   // its name is listed...
            Assert.True(item.GetProperty("bytes").GetInt64() < 1000); // ...but its data was never read
        }

        [Fact]
        public void TextOnly_NeverAsksTheOwnerToRenderTheOtherFormats()
        {
            using var rig = new Rig();
            var rendered = new List<uint>();
            rig.Clipboard.OnRead = id => { lock (rendered) rendered.Add(id); };
            StartOk(rig, 5);

            rig.Clipboard.ExternalReplace(c =>
            {
                c.PutText("text");
                c.Put(ClipboardFormats.CF_DIB, new byte[100]);
                c.PutRegistered("HTML Format", new byte[10]);
            });
            Assert.True(WaitFor(() => Count(rig) == 1));

            lock (rendered)
            {
                Assert.Contains(ClipboardFormats.CF_UNICODETEXT, rendered);
                Assert.DoesNotContain(ClipboardFormats.CF_DIB, rendered);
                Assert.DoesNotContain(rig.Clipboard.Register("HTML Format"), rendered);
            }
        }

        [Fact]
        public void TextOnly_RestoresTheFilesWithTheirEffect_OrElseTheText()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            Copy(rig, "some text");
            Assert.True(WaitFor(() => Count(rig) == 1));
            rig.Clipboard.ExternalReplace(c =>
            {
                c.Put(ClipboardFormats.CF_HDROP, DropFileList.Build(new[] { @"C:\report.csv" }));
                c.PutRegistered(ClipboardFormats.PreferredDropEffectName, BitConverter.GetBytes(2u));
            });
            Assert.True(WaitFor(() => Count(rig) == 2));
            Copy(rig, "later");
            Assert.True(WaitFor(() => Count(rig) == 3));

            Assert.True(rig.Utils.RestoreClipboardHistoryItem(1, out string files), files);   // the file list
            Assert.True(rig.Utils.GetFileDropListText(out string paths, out int count, out FileDropEffect effect, out _));
            Assert.Equal(1, count); Assert.Equal(FileDropEffect.Move, effect); Assert.Equal(@"C:\report.csv", paths);

            Assert.True(rig.Utils.RestoreClipboardHistoryItem(2, out string text), text);     // the earliest, text only
            Assert.Equal("some text", rig.Clipboard.TextOf());
        }

        [Fact]
        public void TextOnly_AnItemThatKeptNeitherTextNorFiles_ExplainsHowToRestoreIt()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            rig.Clipboard.ExternalReplace(c => c.Put(ClipboardFormats.CF_DIB, new byte[1000]));
            Assert.True(WaitFor(() => Count(rig) == 1));

            Assert.False(rig.Utils.RestoreClipboardHistoryItem(0, out string message));

            Assert.Contains("neither text nor files", message);
            Assert.Contains("CF_DIB", message);
            Assert.Contains("AllFormats", message);
        }

        [Fact]
        public void AllFormats_RestoresTheWholeClipboardByteForByte()
        {
            using var rig = new Rig();
            StartOk(rig, 5, ClipboardHistoryMode.AllFormats);
            rig.Clipboard.ExternalReplace(c =>
            {
                c.PutText("rich");
                c.PutRegistered("HTML Format", Encoding.UTF8.GetBytes("<b>rich</b>"));
                c.Put(ClipboardFormats.CF_DIB, new byte[] { 40, 0, 0, 0, 9, 9, 9 });
            });
            var original = rig.Contents();
            Assert.True(WaitFor(() => Count(rig) == 1));
            Copy(rig, "then something plain");
            Assert.True(WaitFor(() => Count(rig) == 2));

            Assert.True(rig.Utils.RestoreClipboardHistoryItem(1, out string message), message);

            var after = rig.Contents();
            Assert.Equal(original.Count, after.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Id, after[i].Id);
                Assert.Equal(original[i].Data, after[i].Data);
            }
        }

        // ------------------------------------------------------------------ what is not recorded

        [Fact]
        public void ContentMarkedToStayOutOfClipboardHistory_IsSkipped_AndNeverRead()
        {
            using var rig = new Rig();
            var rendered = new List<uint>();
            rig.Clipboard.OnRead = id => { lock (rendered) rendered.Add(id); };
            StartOk(rig, 5);

            rig.Clipboard.ExternalReplace(c =>
            {
                c.PutText("hunter2");
                c.PutRegistered(ClipboardFormats.ExcludeFromMonitorName, new byte[4]);   // what a password manager sets
            });
            Assert.True(WaitFor(() => Status(rig).GetProperty("skippedExcluded").GetInt32() == 1));

            Assert.Equal(0, Count(rig));
            lock (rendered)
                Assert.DoesNotContain(ClipboardFormats.CF_UNICODETEXT, rendered);   // the secret was not even read
        }

        [Theory]
        [InlineData(0u, true)]    // CanIncludeInClipboardHistory = 0: keep it out
        [InlineData(1u, false)]   // = 1: fine to record
        public void CanIncludeInClipboardHistory_IsHonoured(uint value, bool skipped)
        {
            using var rig = new Rig();
            StartOk(rig, 5);

            rig.Clipboard.ExternalReplace(c =>
            {
                c.PutText("maybe secret");
                c.PutRegistered(ClipboardFormats.CanIncludeInHistoryName, BitConverter.GetBytes(value));
            });

            if (skipped)
            {
                Assert.True(WaitFor(() => Status(rig).GetProperty("skippedExcluded").GetInt32() == 1));
                Assert.Equal(0, Count(rig));
            }
            else
            {
                Assert.True(WaitFor(() => Count(rig) == 1));
                Assert.Equal(0, Status(rig).GetProperty("skippedExcluded").GetInt32());
            }
        }

        /// <summary>Everything that writes to the clipboard on the component's own account, one at a time.</summary>
        public static IEnumerable<object[]> OwnOperations()
        {
            yield return new object[] { "SetClipboardText", (Action<Rig>)(r => Assert.True(r.Utils.SetClipboardText("set by us", out _))) };
            yield return new object[] { "SetClipboardText (excluded)", (Action<Rig>)(r => Assert.True(r.Utils.SetClipboardText("set by us", out _, excludeFromHistory: true))) };
            yield return new object[] { "ClearClipboard", (Action<Rig>)(r => Assert.True(r.Utils.ClearClipboard(out _))) };
            yield return new object[] { "PasteText", (Action<Rig>)(r => Assert.True(r.Utils.PasteText("pasted by us", out _, postPasteDelayMilliseconds: 0, excludeFromHistory: false))) };
            yield return new object[] { "SetFileDropList", (Action<Rig>)(r => Assert.True(r.Utils.SetFileDropList(Path.GetTempPath(), FileDropEffect.Copy, out _))) };
            yield return new object[] { "SetFileDropListJson", (Action<Rig>)(r => Assert.True(r.Utils.SetFileDropListJson(JsonSerializer.Serialize(new[] { Path.GetTempPath() }), FileDropEffect.Move, out _))) };
            yield return new object[] { "RestoreClipboard", (Action<Rig>)(r => Assert.True(r.Utils.RestoreClipboard("snap", out _))) };
            yield return new object[] { "RestoreClipboardHistoryItem", (Action<Rig>)(r => Assert.True(r.Utils.RestoreClipboardHistoryItem(0, out _))) };
        }

        [Theory]
        [MemberData(nameof(OwnOperations))]
        public void WhatTheComponentItselfPutsOnTheClipboard_IsNeverRecorded(string name, object operation)
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("snapshotted text");
            Assert.True(rig.Utils.SaveClipboard("snap", out _));
            StartOk(rig, 20);
            Copy(rig, "a real copy");
            Assert.True(WaitFor(() => Count(rig) == 1));

            ((Action<Rig>)operation)(rig);
            Thread.Sleep(400);   // several polls, well past the settle time

            Assert.True(Count(rig) == 1, name + " was recorded in the history");
            Assert.Equal("a real copy", TextAt(rig, 0));

            // ...and the watcher is still alive: a copy made by someone else afterwards is recorded.
            Copy(rig, "and another real one");
            Assert.True(WaitFor(() => Count(rig) == 2), "the watcher stopped noticing copies after " + name);
            Assert.Equal("and another real one", TextAt(rig, 0));
        }

        public static IEnumerable<object[]> OwnWritesNeedingNoHistory() =>
            OwnOperations().Where(o => (string)o[0] != "RestoreClipboardHistoryItem");

        [Theory]
        [MemberData(nameof(OwnWritesNeedingNoHistory))]
        public void ACopyMadeJustBeforeAnOwnWrite_IsRecordedBeforeTheWriteOverwritesIt(string name, object operation)
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("snapshotted text");
            Assert.True(rig.Utils.SaveClipboard("snap", out _));
            // A poll interval so long that the watcher will not notice anything on its own during this test.
            Assert.True(rig.Utils.StartClipboardHistory(20, out _, pollIntervalMs: 60000));
            Copy(rig, "the user's copy");

            ((Action<Rig>)operation)(rig);   // overwrites the clipboard at once, before any poll

            Assert.True(Count(rig) == 1, "the copy made just before " + name + " was lost or the write was recorded");
            Assert.Equal("the user's copy", TextAt(rig, 0));
        }

        [Fact]
        public void RestoringAnItemRightAfterACopy_TreatsThatCopyAsTheNewestItem()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.StartClipboardHistory(20, out _, pollIntervalMs: 60000));
            Copy(rig, "older");
            Assert.True(rig.Utils.ClearClipboard(out _));    // this write records "older" first
            Copy(rig, "newer");                              // not noticed by the watcher yet

            Assert.True(rig.Utils.RestoreClipboardHistoryItem(0, out string message), message);

            Assert.Equal(2, Count(rig));
            Assert.Equal("newer", TextAt(rig, 0));           // item 0 was "newer" by the time of the call
            Assert.Equal("older", TextAt(rig, 1));
            Assert.Equal("newer", rig.Clipboard.TextOf());
        }

        [Fact]
        public void WhenNothingIsPending_AnOwnWriteDoesNotWaitForTheHistory()
        {
            using var rig = new Rig(historySettleMs: 2000);   // a settle time that would be obvious if it were waited for
            Assert.True(rig.Utils.StartClipboardHistory(20, out _, pollIntervalMs: 60000));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(rig.Utils.SetClipboardText("x", out _));
            watch.Stop();

            Assert.True(watch.ElapsedMilliseconds < 1000, watch.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void RestoringAHistoryItem_IsNotRecordedAsANewItem()
        {
            using var rig = new Rig();
            StartOk(rig, 20);
            Copy(rig, "one");
            Assert.True(WaitFor(() => Count(rig) == 1));
            Copy(rig, "two");
            Assert.True(WaitFor(() => Count(rig) == 2));

            Assert.True(rig.Utils.RestoreClipboardHistoryItem(1, out _));
            Thread.Sleep(500);

            Assert.Equal(2, Count(rig));
            Assert.Equal("one", rig.Clipboard.TextOf());
            Assert.Equal("two", TextAt(rig, 0));
        }

        [Fact]
        public void ContentOverTheLimit_IsSkippedAndCounted_AndTheNextCopyIsStillRecorded()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            StartOk(rig, 5);

            rig.Clipboard.ExternalReplace(c => c.Put(ClipboardFormats.CF_UNICODETEXT, new byte[3 * 1024 * 1024]));
            Assert.True(WaitFor(() => Status(rig).GetProperty("skippedTooLarge").GetInt32() == 1));
            Assert.Equal(0, Count(rig));
            Assert.Contains("limit", Status(rig).GetProperty("lastError").GetString());

            Copy(rig, "small");
            Assert.True(WaitFor(() => Count(rig) == 1));
        }

        // ------------------------------------------------------------------ size limits

        [Fact]
        public void TheSizeLimitDropsTheOldestItems()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            StartOk(rig, 50, ClipboardHistoryMode.AllFormats);

            for (int i = 1; i <= 3; i++)
            {
                byte[] image = new byte[400 * 1024];
                image[0] = (byte)i;   // different content, so it is not a duplicate
                rig.Clipboard.ExternalReplace(c => c.Put(ClipboardFormats.CF_DIB, image));
                int recorded = i;
                Assert.True(WaitFor(() => Status(rig).GetProperty("recorded").GetInt32() == recorded));
            }

            Assert.Equal(2, Count(rig));   // three 400 KB items do not fit in 1 MB
            Assert.True(Status(rig).GetProperty("totalBytes").GetInt64() <= 1024 * 1024);
            Assert.True(rig.Utils.GetClipboardHistoryJson(5, out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.All(doc.RootElement.EnumerateArray(), e => Assert.Equal(400 * 1024, e.GetProperty("bytes").GetInt64()));
        }

        // ------------------------------------------------------------------ reading the history

        [Fact]
        public void TheJsonLeavesTheTextOutUnlessAskedFor_SoLoggingItCannotLeakWhatWasCopied()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            Copy(rig, "line one\r\nline two with a secret");
            Assert.True(WaitFor(() => Count(rig) == 1));

            Assert.True(rig.Utils.GetClipboardHistoryJson(5, out string plain, out _));
            Assert.DoesNotContain("secret", plain);
            using (var doc = JsonDocument.Parse(plain))
            {
                Assert.True(doc.RootElement[0].GetProperty("hasText").GetBoolean());
                Assert.Equal(32, doc.RootElement[0].GetProperty("textLength").GetInt32());
                Assert.False(doc.RootElement[0].TryGetProperty("text", out _));
            }

            Assert.True(rig.Utils.GetClipboardHistoryJson(5, out string full, out _, includeText: true));
            using (var doc = JsonDocument.Parse(full))
                Assert.Equal("line one  line two with a secret", doc.RootElement[0].GetProperty("text").GetString());
        }

        [Fact]
        public void LongTextInTheJson_IsShortened_ButGetTextReturnsAllOfIt()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            string longText = new string('x', 500);
            Copy(rig, longText);
            Assert.True(WaitFor(() => Count(rig) == 1));

            Assert.True(rig.Utils.GetClipboardHistoryJson(1, out string json, out _, includeText: true));
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(123, doc.RootElement[0].GetProperty("text").GetString().Length);
            Assert.Equal(longText, TextAt(rig, 0));
        }

        [Fact]
        public void AnIndexThatIsNotThere_IsReportedEverywhere()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            Copy(rig, "only");
            Assert.True(WaitFor(() => Count(rig) == 1));

            foreach (int index in new[] { -1, 1, 99 })
            {
                Assert.False(rig.Utils.GetClipboardHistoryText(index, out string text, out bool available, out string m1));
                Assert.Contains("no history item at index " + index, m1);
                Assert.Equal(string.Empty, text); Assert.False(available);
                Assert.False(rig.Utils.RestoreClipboardHistoryItem(index, out _));
                Assert.False(rig.Utils.DiscardClipboardHistoryItem(index, out _));
            }
            Assert.Equal(1, Count(rig));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1001)]
        public void TheJsonRefusesAnOutOfRangeCount(int maxEntries)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.GetClipboardHistoryJson(maxEntries, out string json, out string message));

            Assert.Equal(string.Empty, json);
            Assert.Contains("maxEntries", message);
        }

        [Fact]
        public void AnEmptyHistory_IsAnEmptyArray()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.GetClipboardHistoryJson(10, out string json, out _));

            Assert.Equal("[]", json);
        }

        // ------------------------------------------------------------------ discarding

        [Fact]
        public void DiscardRemovesOneItem_AndOverwritesItsBytes()
        {
            using var rig = new Rig();
            StartOk(rig, 5, ClipboardHistoryMode.AllFormats);
            Copy(rig, "secret one");
            Assert.True(WaitFor(() => Count(rig) == 1));
            Copy(rig, "secret two");
            Assert.True(WaitFor(() => Count(rig) == 2));
            byte[] held = HeldBytes(rig, oldestFirst: 0);
            Assert.Contains(held, b => b != 0);

            Assert.True(rig.Utils.DiscardClipboardHistoryItem(1, out string message), message);   // index 1 is the older

            Assert.All(held, b => Assert.Equal(0, b));
            Assert.Equal(1, Count(rig));
            Assert.Equal("secret two", TextAt(rig, 0));
        }

        [Fact]
        public void ClearRemovesEverything_OverwritesTheBytes_AndKeepsWatching()
        {
            using var rig = new Rig();
            StartOk(rig, 5, ClipboardHistoryMode.AllFormats);
            Copy(rig, "secret");
            Assert.True(WaitFor(() => Count(rig) == 1));
            byte[] held = HeldBytes(rig, oldestFirst: 0);

            Assert.True(rig.Utils.ClearClipboardHistory(out string message), message);

            Assert.All(held, b => Assert.Equal(0, b));
            Assert.Equal(0, Count(rig));
            Copy(rig, "after the clear");
            Assert.True(WaitFor(() => Count(rig) == 1));
            Assert.True(rig.Utils.ClearClipboardHistory(out _));
            Assert.True(rig.Utils.ClearClipboardHistory(out _)); // clearing an empty one is fine
        }

        private static byte[] HeldBytes(Rig rig, int oldestFirst)
        {
            var field = typeof(ClipboardUtils).GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = (List<ClipboardHistoryItem>)field.GetValue(rig.Utils);
            lock (typeof(ClipboardUtils)) { }
            return list[oldestFirst].Snapshot.Entries[0].Data;
        }

        // ------------------------------------------------------------------ stop, restart, dispose

        [Fact]
        public void Stop_KeepsTheItems_AndStopsRecording_AndStartResumes()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            Copy(rig, "before the stop");
            Assert.True(WaitFor(() => Count(rig) == 1));

            Assert.True(rig.Utils.StopClipboardHistory(out _));
            Assert.False(Status(rig).GetProperty("running").GetBoolean());
            Copy(rig, "while stopped");
            Thread.Sleep(400);
            Assert.Equal(1, Count(rig));

            StartOk(rig, 5);
            Copy(rig, "after restart");
            Assert.True(WaitFor(() => Count(rig) == 2));
            Assert.Equal("after restart", TextAt(rig, 0));
            Assert.Equal("before the stop", TextAt(rig, 1));   // "while stopped" was never seen
        }

        [Fact]
        public void RestartingWithASmallerLimit_TrimsTheOldestAtOnce()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            for (int i = 1; i <= 4; i++)
            {
                Copy(rig, "n" + i);
                int recorded = i;
                Assert.True(WaitFor(() => Status(rig).GetProperty("recorded").GetInt32() == recorded));
            }
            Assert.True(rig.Utils.StopClipboardHistory(out _));

            Assert.True(rig.Utils.StartClipboardHistory(2, out _, pollIntervalMs: 25));

            Assert.Equal(2, Count(rig));
            Assert.Equal("n4", TextAt(rig, 0));
            Assert.Equal("n3", TextAt(rig, 1));
        }

        [Fact]
        public void Dispose_StopsTheWatcher_OverwritesTheBytes_AndLaterCallsFail()
        {
            var rig = new Rig();
            StartOk(rig, 5, ClipboardHistoryMode.AllFormats);
            Copy(rig, "secret");
            Assert.True(WaitFor(() => Count(rig) == 1));
            byte[] held = HeldBytes(rig, oldestFirst: 0);

            rig.Utils.Dispose();

            Assert.All(held, b => Assert.Equal(0, b));
            Assert.False(rig.Utils.StartClipboardHistory(5, out string start)); Assert.Contains("disposed", start);
            Assert.False(rig.Utils.StopClipboardHistory(out _));
            Assert.False(rig.Utils.GetClipboardHistoryCount(out _, out _));
            Assert.False(rig.Utils.GetClipboardHistoryStatusJson(out _, out _));
            Assert.False(rig.Utils.GetClipboardHistoryJson(5, out _, out _));
            Assert.False(rig.Utils.GetClipboardHistoryText(0, out _, out _, out _));
            Assert.False(rig.Utils.RestoreClipboardHistoryItem(0, out _));
            Assert.False(rig.Utils.DiscardClipboardHistoryItem(0, out _));
            Assert.False(rig.Utils.ClearClipboardHistory(out _));
            rig.Utils.Dispose(); // twice is fine
        }

        // ------------------------------------------------------------------ trouble

        [Fact]
        public void ATransientFailureToOpenTheClipboard_IsRetried_SoTheCopyIsNotLost()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            rig.Clipboard.OpenFailure = "another application holds the clipboard";
            Copy(rig, "copied while it was held");

            Thread.Sleep(90);                       // one or two attempts fail...
            rig.Clipboard.OpenFailure = null;       // ...then the other application lets go

            Assert.True(WaitFor(() => Count(rig) == 1));
            Assert.Equal("copied while it was held", TextAt(rig, 0));
            Assert.Equal(0, Status(rig).GetProperty("failed").GetInt32());
        }

        [Fact]
        public void ACopyThatCannotBeReadAfterRetries_IsCountedAsFailed_WithTheReason_AndWatchingContinues()
        {
            using var rig = new Rig();
            StartOk(rig, 5);
            rig.Clipboard.OpenFailure = "another application holds the clipboard";
            Copy(rig, "never readable");

            Assert.True(WaitFor(() => Status(rig).GetProperty("failed").GetInt32() == 1));
            Assert.Contains("holds the clipboard", Status(rig).GetProperty("lastError").GetString());
            Assert.Equal(0, Count(rig));

            rig.Clipboard.OpenFailure = null;
            Copy(rig, "readable again");
            Assert.True(WaitFor(() => Count(rig) == 1));
        }

        [Fact]
        public void AHungOwner_IsAbandonedByTheWatcher_NotHungWithIt()
        {
            using var release = new ManualResetEventSlim(false);
            using var rig = new Rig(operationTimeoutMs: 250);
            rig.Clipboard.OnRead = id => release.Wait(5000);
            StartOk(rig, 5);

            Copy(rig, "delay rendered by a hung owner");

            Assert.True(WaitFor(() => Status(rig).GetProperty("failed").GetInt32() == 1, 8000));
            string lastError = Status(rig).GetProperty("lastError").GetString();
            Assert.True(lastError.Contains("did not finish") || lastError.Contains("still running") || lastError.Contains("held open"), lastError);   // the timeout, then the retry refused while the abandoned read is still running
            release.Set();
            Assert.True(rig.Utils.StopClipboardHistory(out _)); // and it still stops cleanly
        }

        // ------------------------------------------------------------------ searching

        private static Rig RigWithHistory(params string[] oldestFirst)
        {
            var rig = new Rig();
            Assert.True(rig.Utils.StartClipboardHistory(50, out _, pollIntervalMs: 25));
            foreach (string text in oldestFirst)
            {
                Copy(rig, text);
                int expected = Count(rig) + 1;
                Assert.True(WaitFor(() => Count(rig) == expected), "not recorded: " + text);
            }
            return rig;
        }

        [Fact]
        public void Find_ReturnsTheNewestMatchAndCanContinueFromThere()
        {
            using var rig = RigWithHistory("invoice 100", "note", "invoice 200", "other");

            Assert.True(rig.Utils.FindClipboardHistoryIndex("INVOICE", out int first, out string message), message);
            Assert.Equal(1, first);
            Assert.True(rig.Utils.FindClipboardHistoryIndex("invoice", out int next, out _, startIndex: first + 1));
            Assert.Equal(3, next);
            Assert.True(rig.Utils.FindClipboardHistoryIndex("invoice", out int none, out _, startIndex: next + 1));
            Assert.Equal(-1, none);
        }

        [Fact]
        public void Find_IgnoresCaseUnlessAskedNotTo()
        {
            using var rig = RigWithHistory("Invoice 100");

            Assert.True(rig.Utils.FindClipboardHistoryIndex("invoice", out int loose, out _));
            Assert.Equal(0, loose);
            Assert.True(rig.Utils.FindClipboardHistoryIndex("invoice", out int strict, out _, matchCase: true));
            Assert.Equal(-1, strict);
            Assert.True(rig.Utils.FindClipboardHistoryIndex("Invoice", out int exact, out _, matchCase: true));
            Assert.Equal(0, exact);
        }

        [Fact]
        public void Find_TheIndexWorksWithRestoreAndText()
        {
            using var rig = RigWithHistory("wanted thing", "newer");

            Assert.True(rig.Utils.FindClipboardHistoryIndex("wanted", out int index, out _));
            Assert.Equal("wanted thing", TextAt(rig, index));
            Assert.True(rig.Utils.RestoreClipboardHistoryItem(index, out string message), message);
            Assert.Equal("wanted thing", rig.Clipboard.TextOf());
        }

        [Fact]
        public void Find_RefusesEmptyTextAndNegativeStart()
        {
            using var rig = RigWithHistory("x");

            Assert.False(rig.Utils.FindClipboardHistoryIndex("", out int a, out string m1)); Assert.Equal(-1, a); Assert.Contains("searchText", m1);
            Assert.False(rig.Utils.FindClipboardHistoryIndex(null, out _, out _));
            Assert.False(rig.Utils.FindClipboardHistoryIndex("x", out _, out string m2, startIndex: -1)); Assert.Contains("startIndex", m2);
        }

        [Fact]
        public void Search_ListsEveryMatchWithItsIndexInTheWholeHistory()
        {
            using var rig = RigWithHistory("invoice 100", "note", "invoice 200", "other");

            Assert.True(rig.Utils.SearchClipboardHistoryJson("invoice", out string json, out string message, includeText: true), message);

            using var doc = JsonDocument.Parse(json);
            var hits = doc.RootElement.EnumerateArray().ToList();
            Assert.Equal(new[] { 1, 3 }, hits.Select(h => h.GetProperty("index").GetInt32()).ToArray());
            Assert.Equal("invoice 200", hits[0].GetProperty("text").GetString());
        }

        [Fact]
        public void Search_LeavesTheTextOutUnlessAskedAndHonoursMaxEntries()
        {
            using var rig = RigWithHistory("a1", "a2", "a3");

            Assert.True(rig.Utils.SearchClipboardHistoryJson("a", out string json, out _, maxEntries: 2));

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.False(doc.RootElement[0].TryGetProperty("text", out _));
            Assert.Equal(0, doc.RootElement[0].GetProperty("index").GetInt32());
        }

        [Fact]
        public void Search_WithNoMatchIsAnEmptyListAndBadArgumentsAreRefused()
        {
            using var rig = RigWithHistory("x");

            Assert.True(rig.Utils.SearchClipboardHistoryJson("zzz", out string json, out _));
            Assert.Equal("[]", json);
            Assert.False(rig.Utils.SearchClipboardHistoryJson("", out _, out string m1)); Assert.Contains("searchText", m1);
            Assert.False(rig.Utils.SearchClipboardHistoryJson("x", out _, out string m2, maxEntries: 0)); Assert.Contains("maxEntries", m2);
        }

        [Fact]
        public void Search_AlsoMatchesFilePaths()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.StartClipboardHistory(10, out _, pollIntervalMs: 25));
            rig.Clipboard.ExternalReplace(c => c.Put(ClipboardFormats.CF_HDROP, DropFileList.Build(new[] { @"C:\data\quarterly-report.xlsx" })));
            Assert.True(WaitFor(() => Count(rig) == 1));

            Assert.True(rig.Utils.FindClipboardHistoryIndex("quarterly-report", out int index, out _));
            Assert.Equal(0, index);
        }

        [Fact]
        public void SearchAndFind_WorkOnAStoppedHistory_AndFailOnceDisposed()
        {
            var rig = RigWithHistory("keep me");
            Assert.True(rig.Utils.StopClipboardHistory(out _));
            Assert.True(rig.Utils.FindClipboardHistoryIndex("keep", out int index, out _));
            Assert.Equal(0, index);

            rig.Dispose();
            Assert.False(rig.Utils.FindClipboardHistoryIndex("keep", out _, out _));
            Assert.False(rig.Utils.SearchClipboardHistoryJson("keep", out _, out _));
        }
    }
}
