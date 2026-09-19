using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace ClipboardAutomation.Tests
{
    public class SnapshotTests
    {
        private static void AssertClosed(FakeClipboardApi clipboard)
        {
            Assert.False(clipboard.IsOpen);
            Assert.Equal(clipboard.OpenCount, clipboard.CloseCount + (clipboard.OpenFailure != null ? clipboard.OpenCount : 0));
        }

        // ------------------------------------------------------------------ save and restore

        [Fact]
        public void SaveThenRestore_PutsBackEveryFormat_InTheOriginalOrder()
        {
            using var rig = new Rig().WithRichContent();
            var original = rig.Contents();

            Assert.True(rig.Utils.SaveClipboard("before", out string save), save);
            Assert.Null(save);

            Assert.True(rig.Utils.SetClipboardText("something else entirely", out _));
            Assert.NotEqual(original.Count, rig.Clipboard.Items.Count); // the rich content really is gone

            Assert.True(rig.Utils.RestoreClipboard("before", out string restore), restore);
            Assert.Null(restore);

            var after = rig.Contents();
            Assert.Equal(original.Count, after.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Id, after[i].Id);
                Assert.Equal(original[i].Data, after[i].Data);
            }
            AssertClosed(rig.Clipboard);
        }

        [Fact]
        public void Save_DoesNotChangeTheClipboard()
        {
            using var rig = new Rig().WithRichContent();
            long before = rig.Clipboard.SequenceNumber;

            Assert.True(rig.Utils.SaveClipboard("s", out _));

            Assert.Equal(before, rig.Clipboard.SequenceNumber);
        }

        [Fact]
        public void Restore_KeepsTheSnapshot_SoItCanBeRestoredAgain()
        {
            using var rig = new Rig().WithRichContent();
            Assert.True(rig.Utils.SaveClipboard("s", out _));

            for (int i = 0; i < 3; i++)
            {
                Assert.True(rig.Utils.ClearClipboard(out _));
                Assert.Empty(rig.Clipboard.Items);
                Assert.True(rig.Utils.RestoreClipboard("s", out string message), message);
                Assert.Equal("hello", rig.Clipboard.TextOf());
            }
        }

        [Fact]
        public void Restore_PutsFormatsBackByName_EvenIfTheirNumbersHaveChanged()
        {
            using var rig = new Rig().WithRichContent();
            Assert.True(rig.Utils.SaveClipboard("s", out _));
            Assert.True(rig.Utils.ClearClipboard(out _));

            rig.Clipboard.RenumberRegisteredFormats(); // the same names now have different numbers

            Assert.True(rig.Utils.RestoreClipboard("s", out string message), message);
            Assert.Equal("<b>hello</b>", Encoding.UTF8.GetString(rig.Clipboard.DataOf("HTML Format")));
            Assert.Equal(new byte[] { 9, 8, 7, 6 }, rig.Clipboard.DataOf("MyApp Private Data"));
        }

        [Fact]
        public void AnEmptyClipboard_IsAValidSnapshot_AndRestoringItEmptiesTheClipboard()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.SaveClipboard("empty", out string message), message);
            Assert.True(rig.Utils.GetSnapshotInfoJson("empty", out string json, out _));
            using (var doc = JsonDocument.Parse(json))
            {
                Assert.Equal(0, doc.RootElement.GetProperty("formatCount").GetInt32());
                Assert.True(doc.RootElement.GetProperty("complete").GetBoolean());
            }

            rig.Clipboard.PutText("something");
            Assert.True(rig.Utils.RestoreClipboard("empty", out _));

            Assert.Empty(rig.Clipboard.Items);
        }

        [Fact]
        public void SavingUnderAnExistingName_ReplacesIt()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("first");
            Assert.True(rig.Utils.SaveClipboard("s", out _));
            Assert.True(rig.Utils.SetClipboardText("second", out _));
            Assert.True(rig.Utils.SaveClipboard("S", out _)); // names ignore case

            Assert.True(rig.Utils.ClearClipboard(out _));
            Assert.True(rig.Utils.RestoreClipboard("s", out _));

            Assert.Equal("second", rig.Clipboard.TextOf());
            Assert.True(rig.Utils.ListSnapshotsJson(out string list, out _));
            using var doc = JsonDocument.Parse(list);
            Assert.Equal(1, doc.RootElement.GetArrayLength());
        }

        [Fact]
        public void AFailedSave_LeavesTheEarlierSnapshotOfThatNameIntact()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("keep me");
            Assert.True(rig.Utils.SaveClipboard("s", out _));

            rig.Clipboard.OpenFailure = "another application holds the clipboard";
            Assert.False(rig.Utils.SaveClipboard("s", out string message));
            Assert.Contains("holds the clipboard", message);

            rig.Clipboard.OpenFailure = null;
            Assert.True(rig.Utils.ClearClipboard(out _));
            Assert.True(rig.Utils.RestoreClipboard("s", out _));
            Assert.Equal("keep me", rig.Clipboard.TextOf());
        }

        // ------------------------------------------------------------------ what cannot be copied

        [Fact]
        public void ABitmapWindowsRebuildsFromTheDib_IsNotALoss()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[] { 1, 2, 3 });
            rig.Clipboard.Put(ClipboardFormats.CF_BITMAP, new byte[0]); // an HBITMAP, as the fake lists it
            rig.Clipboard.Put(ClipboardFormats.CF_PALETTE, new byte[0]);

            Assert.True(rig.Utils.SaveClipboard("s", out string message, requireCompleteCopy: true), message);

            Assert.True(rig.Utils.GetSnapshotInfoJson("s", out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("formatCount").GetInt32());
            var skipped = doc.RootElement.GetProperty("skipped");
            Assert.Equal(2, skipped.GetArrayLength());
            Assert.All(skipped.EnumerateArray(), s => Assert.False(s.GetProperty("loss").GetBoolean()));
        }

        [Theory]
        [InlineData("DataObject")]
        [InlineData("Ole Private Data")]
        [InlineData("OleClipboardPersistOnFlush")]
        [InlineData("ole private data")]
        public void OleBookkeeping_IsLeftOutOnPurpose_WithoutBeingALoss_AndNeverRendered(string name)
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("real data");
            rig.Clipboard.PutRegistered(name, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            var rendered = new System.Collections.Generic.List<uint>();
            rig.Clipboard.OnRead = id => rendered.Add(id);

            Assert.True(rig.Utils.SaveClipboard("s", out string message, requireCompleteCopy: true), message);

            Assert.DoesNotContain(rig.Clipboard.Register(name), rendered); // no time spent asking the owner for it
            Assert.True(rig.Utils.GetSnapshotInfoJson("s", out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("formatCount").GetInt32());
            var skipped = doc.RootElement.GetProperty("skipped")[0];
            Assert.False(skipped.GetProperty("loss").GetBoolean());
            Assert.Contains("OLE", skipped.GetProperty("reason").GetString());

            Assert.True(rig.Utils.ClearClipboard(out _));
            Assert.True(rig.Utils.RestoreClipboard("s", out _));
            Assert.Single(rig.Clipboard.Items);   // the stale OLE format did not come back
            Assert.Equal("real data", rig.Clipboard.TextOf());
        }

        [Fact]
        public void OnlyTheOleNames_AreTreatedAsBookkeeping()
        {
            Assert.True(ClipboardFormats.IsOleBookkeeping("DataObject"));
            Assert.False(ClipboardFormats.IsOleBookkeeping("HTML Format"));
            Assert.False(ClipboardFormats.IsOleBookkeeping("MyApp Private Data"));
            Assert.False(ClipboardFormats.IsOleBookkeeping("Data"));
            Assert.False(ClipboardFormats.IsOleBookkeeping(null));
            Assert.False(ClipboardFormats.IsOleBookkeeping(string.Empty));
        }

        [Fact]
        public void AGdiHandleFormat_IsRecordedAsALoss_AndTheRestIsStillCopied()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("text");
            rig.Clipboard.Put(0x300, new byte[0]); // a CF_GDIOBJFIRST format

            Assert.True(rig.Utils.SaveClipboard("s", out string message), message);

            Assert.True(rig.Utils.GetSnapshotInfoJson("s", out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("formatCount").GetInt32());
            var skipped = doc.RootElement.GetProperty("skipped")[0];
            Assert.True(skipped.GetProperty("loss").GetBoolean());
            Assert.Contains("GDI handle", skipped.GetProperty("reason").GetString());
        }

        [Fact]
        public void AFormatTheOwnerWontGive_IsRecordedAsALoss()
        {
            using var rig = new Rig().WithRichContent();
            rig.Clipboard.UnreadableIds.Add(rig.Clipboard.Register("HTML Format"));

            Assert.True(rig.Utils.SaveClipboard("s", out string message), message);

            Assert.True(rig.Utils.GetSnapshotInfoJson("s", out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("complete").GetBoolean());
            Assert.Equal(3, doc.RootElement.GetProperty("formatCount").GetInt32());
            Assert.Equal("HTML Format", doc.RootElement.GetProperty("skipped")[0].GetProperty("name").GetString());
        }

        [Fact]
        public void RequireCompleteCopy_FailsAndKeepsNothing_WhenSomethingWouldBeLost()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("text");
            rig.Clipboard.Put(0x300, new byte[0]);

            Assert.False(rig.Utils.SaveClipboard("s", out string message, requireCompleteCopy: true));

            Assert.Contains("nothing was kept", message);
            Assert.Contains("CF_GDIOBJ_0x300", message);
            Assert.True(rig.Utils.HasSnapshot("s", out bool exists, out _));
            Assert.False(exists);
        }

        // ------------------------------------------------------------------ limits

        [Fact]
        public void AClipboardOverTheLimit_IsRefusedNotPartlyCopied_AndNamesTheFormat()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            rig.Clipboard.PutText("small");
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[2 * 1024 * 1024]);

            Assert.False(rig.Utils.SaveClipboard("s", out string message));

            Assert.Contains("CF_DIB", message);
            Assert.Contains("2 MB", message);
            Assert.Contains("MaximumClipboardMegabytes", message);
            Assert.True(rig.Utils.HasSnapshot("s", out bool exists, out _));
            Assert.False(exists);
            AssertClosed(rig.Clipboard);
        }

        [Fact]
        public void TheLimitCoversAllSnapshotsTogether_AndReplacingOneFreesItsRoom()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[600 * 1024]);

            Assert.True(rig.Utils.SaveClipboard("a", out string first), first);
            Assert.False(rig.Utils.SaveClipboard("b", out string second));
            Assert.Contains("over the 1 MB limit", second);

            // Replacing "a" with something of the same size is fine: its own bytes do not count twice.
            Assert.True(rig.Utils.SaveClipboard("a", out string again), again);

            // Discarding makes room.
            Assert.True(rig.Utils.DiscardSnapshot("a", out _));
            Assert.True(rig.Utils.SaveClipboard("b", out string third), third);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1025)]
        public void MaximumClipboardMegabytes_RejectsOutOfRangeValues(int value)
        {
            using var rig = new Rig();
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Utils.MaximumClipboardMegabytes = value);
            Assert.Equal(128, rig.Utils.MaximumClipboardMegabytes);
        }

        [Fact]
        public void MaximumClipboardMegabytes_AcceptsTheEdgesOfItsRange()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            Assert.Equal(1, rig.Utils.MaximumClipboardMegabytes);
            rig.Utils.MaximumClipboardMegabytes = 1024;
            Assert.Equal(1024, rig.Utils.MaximumClipboardMegabytes);
        }

        // ------------------------------------------------------------------ restore problems

        [Fact]
        public void RestoreWithAFormatTheClipboardRefuses_NamesItAndStillPutsBackTheRest()
        {
            using var rig = new Rig().WithRichContent();
            Assert.True(rig.Utils.SaveClipboard("s", out _));
            Assert.True(rig.Utils.ClearClipboard(out _));
            rig.Clipboard.UnwritableIds.Add(ClipboardFormats.CF_DIB);

            Assert.False(rig.Utils.RestoreClipboard("s", out string message));

            Assert.Contains("not fully restored", message);
            Assert.Contains("CF_DIB", message);
            Assert.Equal("hello", rig.Clipboard.TextOf());
            Assert.NotNull(rig.Clipboard.DataOf("HTML Format"));
            AssertClosed(rig.Clipboard);
        }

        [Fact]
        public void RestoreNoticesAFormatThatIsNotThereAfterwards()
        {
            using var rig = new Rig().WithRichContent();
            Assert.True(rig.Utils.SaveClipboard("s", out _));
            Assert.True(rig.Utils.ClearClipboard(out _));
            rig.Clipboard.ListedButAbsentAfterWrite.Add(ClipboardFormats.CF_DIB);

            Assert.False(rig.Utils.RestoreClipboard("s", out string message));

            Assert.Contains("CF_DIB", message);
            Assert.Contains("not on the clipboard afterwards", message);
        }

        [Fact]
        public void RestoreWhenTheClipboardCannotBeEmptied_Fails()
        {
            using var rig = new Rig().WithRichContent();
            Assert.True(rig.Utils.SaveClipboard("s", out _));
            rig.Clipboard.EmptyFailure = "empty refused";

            Assert.False(rig.Utils.RestoreClipboard("s", out string message));

            Assert.Equal("empty refused", message);
            AssertClosed(rig.Clipboard);
        }

        [Fact]
        public void RestoringAnUnknownSnapshot_Fails()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.RestoreClipboard("nope", out string message));

            Assert.Contains("no snapshot named 'nope'", message);
            Assert.Equal(0, rig.Clipboard.OpenCount); // never touched the clipboard
        }

        // ------------------------------------------------------------------ housekeeping

        [Fact]
        public void HasDiscardClearAndList_Work()
        {
            using var rig = new Rig().WithRichContent();
            Assert.True(rig.Utils.SaveClipboard("first", out _));
            Assert.True(rig.Utils.SaveClipboard("second", out _));

            Assert.True(rig.Utils.HasSnapshot("FIRST", out bool has, out _));
            Assert.True(has);
            Assert.True(rig.Utils.ListSnapshotsJson(out string list, out _));
            using (var doc = JsonDocument.Parse(list))
            {
                Assert.Equal(new[] { "first", "second" }, doc.RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray());
                Assert.Equal(4, doc.RootElement[0].GetProperty("formatCount").GetInt32());
            }

            Assert.True(rig.Utils.DiscardSnapshot("first", out _));
            Assert.False(rig.Utils.DiscardSnapshot("first", out string gone));
            Assert.Contains("no snapshot named 'first'", gone);

            Assert.True(rig.Utils.ClearSnapshots(out _));
            Assert.True(rig.Utils.ListSnapshotsJson(out string empty, out _));
            Assert.Equal("[]", empty);
            Assert.True(rig.Utils.ClearSnapshots(out _)); // clearing none is fine
        }

        [Fact]
        public void SnapshotInfo_ListsEachFormatWithItsSize()
        {
            using var rig = new Rig().WithRichContent();
            Assert.True(rig.Utils.SaveClipboard("s", out _));

            Assert.True(rig.Utils.GetSnapshotInfoJson("s", out string json, out _));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("s", root.GetProperty("name").GetString());
            Assert.Equal(4, root.GetProperty("formatCount").GetInt32());
            Assert.True(root.GetProperty("complete").GetBoolean());
            Assert.Equal(rig.Clipboard.Items.Sum(i => (long)i.Data.Length), root.GetProperty("totalBytes").GetInt64());
            var names = root.GetProperty("formats").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToArray();
            Assert.Equal(new[] { "CF_UNICODETEXT", "HTML Format", "CF_DIB", "MyApp Private Data" }, names);
            Assert.True(DateTime.TryParse(root.GetProperty("capturedUtc").GetString(), out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ANameThatIsEmpty_IsRefusedEverywhere(string name)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SaveClipboard(name, out string save)); Assert.Contains("snapshotName", save);
            Assert.False(rig.Utils.RestoreClipboard(name, out _));
            Assert.False(rig.Utils.HasSnapshot(name, out _, out _));
            Assert.False(rig.Utils.DiscardSnapshot(name, out _));
            Assert.False(rig.Utils.GetSnapshotInfoJson(name, out string json, out _));
            Assert.Equal(string.Empty, json);
        }

        [Fact]
        public void ANameOverSixtyFourCharacters_IsRefused_AndSixtyFourIsFine()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.SaveClipboard(new string('x', 65), out string message));
            Assert.Contains("at most 64", message);
            Assert.True(rig.Utils.SaveClipboard(new string('x', 64), out _));
        }

        /// <summary>The bytes a snapshot holds, taken from the component's private store so a test can see them being wiped.</summary>
        private static byte[] HeldBytes(Rig rig, string name)
        {
            var field = typeof(ClipboardUtils).GetField("_snapshots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var store = (System.Collections.IDictionary)field.GetValue(rig.Utils);
            return ((ClipboardSnapshot)store[name]).Entries[0].Data;
        }

        [Fact]
        public void Dispose_OverwritesTheSavedBytes_AndLaterCallsFail()
        {
            var rig = new Rig();
            rig.Clipboard.PutText("secret");
            Assert.True(rig.Utils.SaveClipboard("s", out _));
            byte[] held = HeldBytes(rig, "s");
            Assert.Contains(held, b => b != 0); // it really holds the text

            rig.Utils.Dispose();

            Assert.All(held, b => Assert.Equal(0, b));
            Assert.False(rig.Utils.SaveClipboard("s", out string message));
            Assert.Contains("disposed", message);
            Assert.False(rig.Utils.RestoreClipboard("s", out _));
            Assert.False(rig.Utils.ListSnapshotsJson(out _, out _));
            rig.Utils.Dispose(); // twice is fine
        }

        [Fact]
        public void DiscardAndClear_OverwriteTheSavedBytes_AndReplacingOneOverwritesTheOldOne()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("secret one");
            Assert.True(rig.Utils.SaveClipboard("a", out _));
            byte[] discarded = HeldBytes(rig, "a");
            Assert.True(rig.Utils.DiscardSnapshot("a", out _));
            Assert.All(discarded, b => Assert.Equal(0, b));

            Assert.True(rig.Utils.SaveClipboard("b", out _));
            byte[] replaced = HeldBytes(rig, "b");
            rig.Clipboard.Items.Clear();
            rig.Clipboard.PutText("secret two");
            Assert.True(rig.Utils.SaveClipboard("b", out _));
            Assert.All(replaced, b => Assert.Equal(0, b));

            byte[] cleared = HeldBytes(rig, "b");
            Assert.True(rig.Utils.ClearSnapshots(out _));
            Assert.All(cleared, b => Assert.Equal(0, b));
        }

        // ------------------------------------------------------------------ never hangs, never leaves the clipboard open

        [Fact]
        public void AnOwnerThatNeverAnswers_ProducesAFailure_NotAHang()
        {
            using var release = new ManualResetEventSlim(false);
            using var rig = new Rig(operationTimeoutMs: 300);
            rig.Clipboard.PutText("delay-rendered");
            rig.Clipboard.OnRead = id => release.Wait(10000);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool saved = rig.Utils.SaveClipboard("s", out string message);
            watch.Stop();
            release.Set(); // let the abandoned operation finish

            Assert.False(saved);
            Assert.Contains("did not finish within 300 ms", message);
            Assert.Contains("may be hung", message);
            Assert.InRange(watch.ElapsedMilliseconds, 250, 5000);
        }

        [Fact]
        public void EveryFailureLeavesTheClipboardClosed()
        {
            using var rig = new Rig().WithRichContent();
            rig.Clipboard.UnreadableIds.Add(ClipboardFormats.CF_DIB);
            Assert.True(rig.Utils.SaveClipboard("s", out _));
            rig.Clipboard.UnwritableIds.Add(ClipboardFormats.CF_UNICODETEXT);
            Assert.False(rig.Utils.RestoreClipboard("s", out _));
            rig.Clipboard.UnwritableIds.Clear();
            rig.Clipboard.EmptyFailure = "no";
            Assert.False(rig.Utils.SetClipboardText("x", out _));
            Assert.False(rig.Utils.ClearClipboard(out _));

            Assert.False(rig.Clipboard.IsOpen);
            Assert.Equal(rig.Clipboard.OpenCount, rig.Clipboard.CloseCount);
        }

        // ------------------------------------------------------------------ review follow-ups

        [Fact]
        public void ABitmapWhoseDibCouldNotBeCopied_IsALoss_NotAssumedRebuildable()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[] { 1, 2, 3 });
            rig.Clipboard.Put(ClipboardFormats.CF_BITMAP, new byte[0]);
            rig.Clipboard.UnreadableIds.Add(ClipboardFormats.CF_DIB);

            Assert.False(rig.Utils.SaveClipboard("strict", out string strict, requireCompleteCopy: true));
            Assert.Contains("CF_BITMAP", strict);

            Assert.True(rig.Utils.SaveClipboard("loose", out _));
            Assert.True(rig.Utils.GetSnapshotInfoJson("loose", out string json, out _));
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("complete").GetBoolean());
            Assert.All(doc.RootElement.GetProperty("skipped").EnumerateArray(), s => Assert.True(s.GetProperty("loss").GetBoolean()));
        }

        [Fact]
        public void AFailureToListTheFormats_IsReported_NotTakenForAPartialClipboard()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("hello");
            rig.Clipboard.EnumerateFailure = "The clipboard's formats could not be listed (Win32 error 5).";

            Assert.False(rig.Utils.SaveClipboard("s", out string message));
            Assert.Contains("could not be listed", message);
            Assert.True(rig.Utils.HasSnapshot("s", out bool exists, out _));
            Assert.False(exists);
            Assert.False(rig.Utils.GetFormatsJson(out _, out string listMessage));
            Assert.Contains("could not be listed", listMessage);
            Assert.False(rig.Clipboard.IsOpen);
        }

        [Fact]
        public void WhileATimedOutOperationIsStillRunning_NoOtherClipboardOperationStarts()
        {
            using var release = new ManualResetEventSlim(false);
            using var rig = new Rig(operationTimeoutMs: 300);
            rig.Clipboard.PutText("delay-rendered");
            rig.Clipboard.OnRead = id => release.Wait(10000);
            Assert.False(rig.Utils.SaveClipboard("s", out _));   // times out, its worker is still waiting on the owner

            Assert.False(rig.Utils.SetClipboardText("later", out string message));
            Assert.Contains("still running", message);
            Assert.Equal("delay-rendered", rig.Clipboard.TextOf());   // the later operation never touched the clipboard

            release.Set();   // the owner answers, the abandoned operation finishes
            int deadline = Environment.TickCount + 5000;
            bool ok = false;
            while (!ok && Environment.TickCount - deadline < 0)
            {
                ok = rig.Utils.SetClipboardText("later", out _);
                if (!ok)
                    Thread.Sleep(10);
            }
            Assert.True(ok, "operations never resumed after the abandoned one finished");
        }
    }
}
