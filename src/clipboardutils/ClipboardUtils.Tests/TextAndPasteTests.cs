using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace ClipboardAutomation.Tests
{
    public class TextAndPasteTests
    {
        // ------------------------------------------------------------------ text

        [Theory]
        [InlineData("hello")]
        [InlineData("")]
        [InlineData("multi\r\nline\ttext")]
        [InlineData("日本語 ünïcödé 😀")]
        public void SetThenGet_RoundTripsText(string text)
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.SetClipboardText(text, out string set), set);
            Assert.Null(set);
            Assert.True(rig.Utils.GetClipboardText(out string read, out bool available, out string get), get);

            Assert.Equal(text, read);
            Assert.True(available);
            Assert.Null(get);
        }

        [Fact]
        public void GetText_OnAnEmptyClipboard_IsASuccessWithNoText()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.GetClipboardText(out string text, out bool available, out string message));

            Assert.Equal(string.Empty, text);
            Assert.False(available);
            Assert.Null(message);
        }

        [Fact]
        public void GetText_OnAClipboardWithoutText_IsASuccessWithNoText()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[] { 1, 2, 3 });

            Assert.True(rig.Utils.GetClipboardText(out string text, out bool available, out _));

            Assert.False(available);
            Assert.Equal(string.Empty, text);
        }

        [Fact]
        public void GetText_FallsBackToAnsiText()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_TEXT, Encoding.ASCII.GetBytes("plain ansi\0"));

            Assert.True(rig.Utils.GetClipboardText(out string text, out bool available, out string message), message);

            Assert.True(available);
            Assert.Equal("plain ansi", text);
        }

        [Fact]
        public void GetText_PrefersUnicodeOverAnsi()
        {
            using var rig = new Rig();
            rig.Clipboard.Put(ClipboardFormats.CF_TEXT, Encoding.ASCII.GetBytes("ansi\0"));
            rig.Clipboard.PutText("unicode");

            Assert.True(rig.Utils.GetClipboardText(out string text, out _, out _));

            Assert.Equal("unicode", text);
        }

        [Fact]
        public void GetText_StopsAtTheTerminator()
        {
            using var rig = new Rig();
            byte[] junk = Encoding.Unicode.GetBytes("abc\0trailing junk");
            rig.Clipboard.Put(ClipboardFormats.CF_UNICODETEXT, junk);

            Assert.True(rig.Utils.GetClipboardText(out string text, out _, out _));

            Assert.Equal("abc", text);
        }

        [Fact]
        public void GetText_OverTheLimit_IsRefused()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            rig.Clipboard.Put(ClipboardFormats.CF_UNICODETEXT, new byte[3 * 1024 * 1024]);

            Assert.False(rig.Utils.GetClipboardText(out string text, out bool available, out string message));

            Assert.Contains("over the 1 MB limit", message);
            Assert.Equal(string.Empty, text);
            Assert.False(available);
        }

        [Fact]
        public void GetText_WhenTheOwnerWontGiveIt_Fails()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("x");
            rig.Clipboard.UnreadableIds.Add(ClipboardFormats.CF_UNICODETEXT);

            Assert.False(rig.Utils.GetClipboardText(out _, out _, out string message));

            Assert.Contains("would not give its text", message);
        }

        [Fact]
        public void SetText_DiscardsEveryOtherFormat()
        {
            using var rig = new Rig().WithRichContent();

            Assert.True(rig.Utils.SetClipboardText("only text now", out _));

            Assert.Single(rig.Clipboard.Items);
            Assert.Equal("only text now", rig.Clipboard.TextOf());
        }

        [Fact]
        public void SetText_WithExcludeFromHistory_MarksTheContentForClipboardMonitors()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.SetClipboardText("secret", out _, excludeFromHistory: true));

            Assert.Equal("secret", rig.Clipboard.TextOf());
            foreach (string name in new[] { ClipboardFormats.ExcludeFromMonitorName, ClipboardFormats.CanIncludeInHistoryName, ClipboardFormats.CanUploadToCloudName })
            {
                byte[] flag = rig.Clipboard.DataOf(name);
                Assert.NotNull(flag);
                Assert.Equal(new byte[4], flag);   // a DWORD 0: not to be included
            }
        }

        [Fact]
        public void SetText_WithoutExcludeFromHistory_AddsNoMarkers()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.SetClipboardText("public", out _));

            Assert.Single(rig.Clipboard.Items);
        }

        [Fact]
        public void SetText_RefusesNull()
        {
            using var rig = new Rig();
            rig.Clipboard.PutText("untouched");

            Assert.False(rig.Utils.SetClipboardText(null, out string message));

            Assert.Contains("null", message);
            Assert.Equal("untouched", rig.Clipboard.TextOf());
        }

        [Fact]
        public void SetText_ReportsAMarkerThatCouldNotBeWritten()
        {
            using var rig = new Rig();
            rig.Clipboard.UnwritableIds.Add(rig.Clipboard.Register(ClipboardFormats.CanIncludeInHistoryName));

            Assert.False(rig.Utils.SetClipboardText("secret", out string message, excludeFromHistory: true));

            Assert.Contains("stay out of the clipboard history", message);
            Assert.Contains(ClipboardFormats.CanIncludeInHistoryName, message);
        }

        [Fact]
        public void Clear_EmptiesTheClipboard()
        {
            using var rig = new Rig().WithRichContent();

            Assert.True(rig.Utils.ClearClipboard(out string message), message);

            Assert.Empty(rig.Clipboard.Items);
        }

        // ------------------------------------------------------------------ paste

        [Fact]
        public void Paste_PutsTheTextOnTheClipboardOnlyForTheKeystroke_ThenRestoresEverything()
        {
            using var rig = new Rig().WithRichContent();
            var original = rig.Contents();
            var during = new List<(uint Id, byte[] Data)>();
            rig.Keys.OnSend = () => during = rig.Contents();

            Assert.True(rig.Utils.PasteText("typed by paste", out string message), message);
            Assert.Null(message);

            // At the moment of Ctrl+V the clipboard held the pasted text and the history markers, nothing else.
            Assert.Equal(4, during.Count);
            Assert.Equal("typed by paste", Encoding.Unicode.GetString(during[0].Data, 0, during[0].Data.Length - 2));
            Assert.DoesNotContain(during, item => item.Id == ClipboardFormats.CF_DIB);

            // Afterwards every original format is back, in order, byte for byte.
            var after = rig.Contents();
            Assert.Equal(original.Count, after.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Id, after[i].Id);
                Assert.Equal(original[i].Data, after[i].Data);
            }
            Assert.Equal(1, rig.Keys.Sent);
            Assert.False(rig.Clipboard.IsOpen);
        }

        [Fact]
        public void Paste_ExcludesTheTextFromHistoryByDefault_AndCanBeToldNotTo()
        {
            using var rig = new Rig();
            int markers = -1;
            rig.Keys.OnSend = () => markers = rig.Clipboard.Items.Count(i => ClipboardFormats.IsRegistered(i.Id));

            Assert.True(rig.Utils.PasteText("a", out _));
            Assert.Equal(3, markers);

            Assert.True(rig.Utils.PasteText("b", out _, excludeFromHistory: false));
            Assert.Equal(0, markers);
        }

        [Fact]
        public void Paste_OnAnEmptyClipboard_LeavesItEmpty()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.PasteText("x", out string message), message);

            Assert.Empty(rig.Clipboard.Items);
        }

        [Fact]
        public void Paste_WaitsThePostPasteDelay_BeforeRestoring()
        {
            using var rig = new Rig().WithRichContent();
            string textDuringTheWait = null;
            rig.OnSleep = ms => textDuringTheWait = rig.Clipboard.TextOf();

            Assert.True(rig.Utils.PasteText("pasted", out _, postPasteDelayMilliseconds: 250));

            Assert.Equal(new[] { 250 }, rig.Sleeps);
            Assert.Equal("pasted", textDuringTheWait);        // still holding the pasted text while the target reads it
            Assert.Equal("hello", rig.Clipboard.TextOf());    // and only then restored
        }

        [Fact]
        public void Paste_WithNoDelay_DoesNotSleep()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.PasteText("x", out _, postPasteDelayMilliseconds: 0));

            Assert.Empty(rig.Sleeps);
        }

        [Fact]
        public void Paste_WhenTheKeystrokeFails_StillPutsTheClipboardBack_AndSaysWhy()
        {
            using var rig = new Rig().WithRichContent();
            var original = rig.Contents();
            rig.Keys.Fail = true;

            Assert.False(rig.Utils.PasteText("x", out string message));

            Assert.Equal("the fake keyboard refused", message);
            var after = rig.Contents();
            Assert.Equal(original.Select(i => i.Id), after.Select(i => i.Id));
            Assert.Empty(rig.Sleeps); // nothing was pasted, so nothing to wait for
        }

        [Fact]
        public void Paste_WhenTheRestoreFails_ReportsIt_EvenThoughThePasteWorked()
        {
            using var rig = new Rig().WithRichContent();
            rig.Keys.OnSend = () => rig.Clipboard.UnwritableIds.Add(ClipboardFormats.CF_DIB);

            Assert.False(rig.Utils.PasteText("x", out string message));

            Assert.Contains("The text was pasted, but restoring the clipboard failed", message);
            Assert.Contains("CF_DIB", message);
        }

        [Fact]
        public void Paste_WhenBothFail_ReportsBoth()
        {
            using var rig = new Rig().WithRichContent();
            rig.Keys.Fail = true;
            rig.Keys.OnSend = () => rig.Clipboard.UnwritableIds.Add(ClipboardFormats.CF_DIB);

            Assert.False(rig.Utils.PasteText("x", out string message));

            Assert.Contains("the fake keyboard refused", message);
            Assert.Contains("Restoring the clipboard also failed", message);
            Assert.Contains("CF_DIB", message);
        }

        [Fact]
        public void Paste_WhenTheTextCannotBeSet_StillRestores_AndDoesNotSendTheKeystroke()
        {
            using var rig = new Rig().WithRichContent();
            var original = rig.Contents();
            rig.Clipboard.UnwritableIds.Add(ClipboardFormats.CF_UNICODETEXT);

            Assert.False(rig.Utils.PasteText("x", out string message));

            Assert.Equal(0, rig.Keys.Sent);
            Assert.Contains("refused", message);
            Assert.Contains("CF_UNICODETEXT", message); // the restore hit the same refusal, and says so
            Assert.Equal(original.Count - 1, rig.Clipboard.Items.Count); // everything but the refused format is back
        }

        [Fact]
        public void Paste_WhenTheClipboardCannotBeRead_TouchesNothing()
        {
            using var rig = new Rig().WithRichContent();
            long sequence = rig.Clipboard.SequenceNumber;
            rig.Clipboard.OpenFailure = "held by another application";

            Assert.False(rig.Utils.PasteText("x", out string message));

            Assert.Contains("The clipboard was not touched", message);
            Assert.Contains("held by another application", message);
            Assert.Equal(0, rig.Keys.Sent);
            Assert.Equal(sequence, rig.Clipboard.SequenceNumber);
        }

        [Fact]
        public void Paste_WithRequireCompleteRestore_RefusesWhenSomethingCouldNotBeSaved_AndTouchesNothing()
        {
            using var rig = new Rig().WithRichContent();
            rig.Clipboard.Put(0x300, new byte[0]); // a GDI handle format that cannot be copied
            var original = rig.Contents();
            long sequence = rig.Clipboard.SequenceNumber;

            Assert.False(rig.Utils.PasteText("x", out string message, requireCompleteRestore: true));

            Assert.Contains("was not touched", message);
            Assert.Contains("CF_GDIOBJ_0x300", message);
            Assert.Equal(0, rig.Keys.Sent);
            Assert.Equal(sequence, rig.Clipboard.SequenceNumber);
            Assert.Equal(original.Count, rig.Clipboard.Items.Count);
        }

        [Fact]
        public void Paste_WithoutRequireCompleteRestore_GoesAheadAndPutsBackWhatItCould()
        {
            using var rig = new Rig().WithRichContent();
            rig.Clipboard.Put(0x300, new byte[0]);

            Assert.True(rig.Utils.PasteText("x", out string message), message);

            Assert.Equal(1, rig.Keys.Sent);
            Assert.Equal("hello", rig.Clipboard.TextOf());
            Assert.Equal(4, rig.Clipboard.Items.Count); // the four that could be copied; the GDI one is gone
        }

        [Fact]
        public void Paste_ASnapshotTooBigToKeep_RefusesBeforeTouchingTheClipboard()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[2 * 1024 * 1024]);
            long sequence = rig.Clipboard.SequenceNumber;

            Assert.False(rig.Utils.PasteText("x", out string message));

            Assert.Contains("The clipboard was not touched", message);
            Assert.Contains("CF_DIB", message);
            Assert.Equal(0, rig.Keys.Sent);
            Assert.Equal(sequence, rig.Clipboard.SequenceNumber);
        }

        [Fact]
        public void Paste_DoesNotUseUpTheSavedSnapshotsRoom()
        {
            using var rig = new Rig();
            rig.Utils.MaximumClipboardMegabytes = 1;
            rig.Clipboard.Put(ClipboardFormats.CF_DIB, new byte[800 * 1024]);
            Assert.True(rig.Utils.SaveClipboard("kept", out _));

            Assert.True(rig.Utils.PasteText("x", out string message), message); // its transient copy is not stored

            Assert.True(rig.Utils.SaveClipboard("kept", out _)); // and the stored total is unchanged
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10001)]
        public void Paste_RefusesAnOutOfRangeDelay(int delay)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.PasteText("x", out string message, postPasteDelayMilliseconds: delay));

            Assert.Contains("postPasteDelayMilliseconds", message);
            Assert.Equal(0, rig.Clipboard.OpenCount);
        }

        [Fact]
        public void Paste_RefusesNull()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.PasteText(null, out string message));

            Assert.Contains("null", message);
            Assert.Equal(0, rig.Clipboard.OpenCount);
        }

        // ------------------------------------------------------------------ formats

        [Fact]
        public void GetFormatsJson_ListsTheFormatsInOrder_WithoutRenderingAnyData()
        {
            using var rig = new Rig().WithRichContent();
            bool rendered = false;
            rig.Clipboard.OnRead = id => rendered = true;

            Assert.True(rig.Utils.GetFormatsJson(out string json, out string message), message);

            Assert.False(rendered);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var items = doc.RootElement.EnumerateArray().ToList();
            Assert.Equal(new[] { "CF_UNICODETEXT", "HTML Format", "CF_DIB", "MyApp Private Data" }, items.Select(i => i.GetProperty("name").GetString()).ToArray());
            Assert.Equal(new[] { false, true, false, true }, items.Select(i => i.GetProperty("registered").GetBoolean()).ToArray());
            Assert.Equal(13u, items[0].GetProperty("id").GetUInt32());
        }

        [Fact]
        public void GetFormatsJson_OnAnEmptyClipboard_IsAnEmptyArray()
        {
            using var rig = new Rig();

            Assert.True(rig.Utils.GetFormatsJson(out string json, out _));

            Assert.Equal("[]", json);
        }

        [Theory]
        [InlineData("CF_UNICODETEXT", true)]
        [InlineData("unicodetext", true)]
        [InlineData("13", true)]
        [InlineData("HTML Format", true)]
        [InlineData("html format", true)]
        [InlineData("CF_HDROP", false)]
        [InlineData("Some Format Nobody Copied", false)]
        public void IsFormatAvailable_AcceptsPredefinedRegisteredAndNumericFormats(string format, bool expected)
        {
            using var rig = new Rig().WithRichContent();

            Assert.True(rig.Utils.IsFormatAvailable(format, out bool available, out string message), message);

            Assert.Equal(expected, available);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void IsFormatAvailable_RefusesAnEmptyName(string format)
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.IsFormatAvailable(format, out bool available, out string message));

            Assert.False(available);
            Assert.Contains("formatName", message);
        }

        [Fact]
        public void IsFormatAvailable_RefusesAnOverlongName()
        {
            using var rig = new Rig();

            Assert.False(rig.Utils.IsFormatAvailable(new string('x', 300), out _, out string message));

            Assert.Contains("at most 255", message);
        }

        [Fact]
        public void SequenceNumber_ChangesWhenTheClipboardChanges()
        {
            using var rig = new Rig();
            Assert.True(rig.Utils.GetClipboardSequenceNumber(out long before, out _));

            Assert.True(rig.Utils.SetClipboardText("x", out _));

            Assert.True(rig.Utils.GetClipboardSequenceNumber(out long after, out _));
            Assert.NotEqual(before, after);
        }

        // ------------------------------------------------------------------ review follow-ups

        [Fact]
        public void Paste_WhenTheKeystrokeThrows_StillPutsTheClipboardBack()
        {
            using var rig = new Rig().WithRichContent();
            var original = rig.Contents();
            rig.Keys.OnSend = () => throw new InvalidOperationException("the keyboard blew up");

            Assert.False(rig.Utils.PasteText("never pasted", out string message));

            Assert.Contains("Sending Ctrl+V", message);
            Assert.Contains("the keyboard blew up", message);
            var after = rig.Contents();
            Assert.Equal(original.Count, after.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].Id, after[i].Id);
                Assert.Equal(original[i].Data, after[i].Data);
            }
            Assert.False(rig.Clipboard.IsOpen);
        }

        [Fact]
        public void Paste_WhenTheWaitAfterTheKeystrokeIsInterrupted_StillPutsTheClipboardBack()
        {
            using var rig = new Rig().WithRichContent();
            var original = rig.Contents();
            rig.OnSleep = ms => throw new System.Threading.ThreadInterruptedException();

            Assert.True(rig.Utils.PasteText("pasted", out string message, postPasteDelayMilliseconds: 100), message);

            Assert.Equal(1, rig.Keys.Sent);
            var after = rig.Contents();
            Assert.Equal(original.Count, after.Count);
            for (int i = 0; i < original.Count; i++)
                Assert.Equal(original[i].Data, after[i].Data);
        }

        [Fact]
        public void Paste_WhenTheRestoreTimesOut_TheSnapshotIsNotWipedUnderIt()
        {
            using var release = new System.Threading.ManualResetEventSlim(false);
            using var rig = new Rig(operationTimeoutMs: 300).WithRichContent();
            var original = rig.Contents();
            bool hang = false;
            rig.Keys.OnSend = () => hang = true;   // from the keystroke on, the next write (the restore's) hangs
            rig.Clipboard.OnWrite = id => { if (hang) release.Wait(10000); };

            Assert.False(rig.Utils.PasteText("pasted", out string message));
            Assert.Contains("did not finish", message);

            release.Set();   // the abandoned restore carries on, and must still have every byte to write
            int deadline = Environment.TickCount + 5000;
            while (rig.Contents().Count < original.Count && Environment.TickCount - deadline < 0)
                System.Threading.Thread.Sleep(10);
            var after = rig.Contents();
            Assert.Equal(original.Count, after.Count);
            for (int i = 0; i < original.Count; i++)
                Assert.Equal(original[i].Data, after[i].Data);
        }

        [Fact]
        public void Paste_WhenSettingTheTextTimesOut_TheOriginalIsPutBackOnceTheSetterFinishes()
        {
            using var release = new System.Threading.ManualResetEventSlim(false);
            using var rig = new Rig(operationTimeoutMs: 300).WithRichContent();
            var original = rig.Contents();
            rig.Clipboard.OnWrite = id => release.Wait(10000);   // the very first write, the temporary text, hangs

            Assert.False(rig.Utils.PasteText("late text", out string message));
            Assert.Contains("put back automatically", message);
            Assert.Equal(0, rig.Keys.Sent);   // never pasted

            release.Set();   // the setter finishes, landing the text late; the kept snapshot must then replace it
            int deadline = Environment.TickCount + 5000;
            while (Environment.TickCount - deadline < 0 && !SameContents(rig.Contents(), original))
                System.Threading.Thread.Sleep(10);
            Assert.True(SameContents(rig.Contents(), original), "the original clipboard was never put back");
            Assert.True(rig.Utils.SetClipboardText("works again", out _));
        }

        private static bool SameContents(List<(uint Id, byte[] Data)> a, List<(uint Id, byte[] Data)> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Id != b[i].Id || !System.Linq.Enumerable.SequenceEqual(a[i].Data, b[i].Data))
                    return false;
            }
            return true;
        }
    }
}
