using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TextExtractAutomation.Tests
{
    public sealed class LabelLocatorTests
    {
        // ------------------------------------------------------------------ helpers

        private static Template TemplateOf(params (string name, string labels, ValuePosition position)[] fields)
        {
            var t = new Template();
            foreach (var f in fields) Assert.Null(t.TryAddLabelField(f.name, f.labels, f.position, FieldType.Text, DecimalStyle.DotDecimal, null, true, Occurrence.RequireUnique));
            return t;
        }

        private static List<LabelCandidate> Locate(Template t, string text) => LabelLocator.Locate(t, text, TextLines.Split(text));

        /// <summary>"Field=value@line" for every candidate, in text order.</summary>
        private static List<string> Found(Template t, string text) =>
            Locate(t, text).Select(c => t.Fields[c.FieldIndex].Name + "=" + c.Value + "@" + c.ValueLine).ToList();

        private static string Single(string labels, string text, ValuePosition position = ValuePosition.SameLine)
        {
            var found = Locate(TemplateOf(("F", labels, position)), text);
            Assert.Single(found);
            return found[0].Value;
        }

        // ------------------------------------------------------------------ normalization

        [Fact]
        public void Lines_AreSplitOnEveryKindOfBreak_AndNumberedAsInTheOriginal()
        {
            List<TextLine> lines = TextLines.Split("a\r\nb\rc\nd\u2028e\u0085f\u000Cg");
            Assert.Equal(new[] { "a", "b", "c", "d", "e", "f", "g" }, lines.Select(l => l.Text));
            Assert.Equal(Enumerable.Range(1, 7), lines.Select(l => l.Number));
            Assert.Equal(new[] { "", "" }, TextLines.Split("\r\n").Select(l => l.Text));   // CRLF counts once
        }

        [Fact]
        public void Normalization_MakesMatchingTolerant_ButRawIsAlwaysTheOriginalText()
        {
            // NBSP, a tab, a ligature, full-width digits, a zero-width space, a soft hyphen, typographic quotes and a dash
            string original = "Pro\u00ADfile\u00A0\u201CA\u201D\tID\uFF1A\uFF11\uFF12\u200B3 \uFB01ne \u2013 x";
            TextLine line = TextLines.Split(original).Single();
            Assert.Equal("Profile \"A\" ID:123 fine - x", line.Text);
            Assert.Equal(original, line.Raw(original, 0, line.Text.Length));                    // the whole line maps back exactly
            int fi = line.Text.IndexOf("fine", StringComparison.Ordinal);
            Assert.Equal("\uFB01ne", line.Raw(original, fi, fi + 4));                         // the ligature is one original character behind two normalized ones
            Assert.Equal("\uFF11\uFF12\u200B3", line.Raw(original, line.Text.IndexOf("123", StringComparison.Ordinal), line.Text.IndexOf("123", StringComparison.Ordinal) + 3));
        }

        [Fact]
        public void SurrogatePairsStayWhole_AndALoneSurrogateDoesNotBreakAnything()
        {
            TextLine line = TextLines.Split("Ref \ud83d\ude80 X \ud800 Y").Single();
            Assert.Contains("\ud83d\ude80", line.Text);
            Assert.Equal("Ref \ud83d\ude80 X \ud800 Y", line.Raw("Ref \ud83d\ude80 X \ud800 Y", 0, line.Text.Length));
        }

        // ------------------------------------------------------------------ label matching

        [Theory]
        [InlineData("Invoice No: 123", "123")]
        [InlineData("INVOICE NO: 123", "123")]
        [InlineData("invoice no 123", "123")]
        [InlineData("Invoice   No   :   123", "123")]           // any run of spaces where the label has one
        [InlineData("Invoice No.: 123", "123")]
        [InlineData("Invoice No # 123", "123")]
        [InlineData("Invoice No = 123", "123")]
        [InlineData("Invoice No......... 123", "123")]          // dot leaders
        [InlineData("Invoice No - 123", "123")]
        [InlineData("  Invoice No: 123  ", "123")]
        public void ALabel_MatchesCaseAndSpacingAndSeparatorsLoosely(string text, string value) => Assert.Equal(value, Single("Invoice No", text));

        [Theory]
        [InlineData("Balance: -12.00", "-12.00")]                // a sign is part of the value
        [InlineData("Balance .50", ".50")]                       // so is a leading point
        [InlineData("Balance: --12", "-12")]                     // a dash before another dash is a separator
        public void Separators_NeverSwallowTheSignOrPointOfANumber(string text, string value) => Assert.Equal(value, Single("Balance", text));

        [Theory]
        [InlineData("Subtotal: 5")]
        [InlineData("Totals: 5")]
        [InlineData("Total5")]
        [InlineData("GrandTotal 5")]
        public void Labels_MatchWholeWordsOnly(string text) => Assert.Empty(Locate(TemplateOf(("F", "Total", ValuePosition.SameLine)), text));

        [Fact]
        public void ALabelEndingInPunctuation_NeedsNoBoundaryAfterIt()
        {
            Assert.Equal("123", Single("Invoice #", "Invoice #123"));
            Assert.Equal("123", Single("Ref:", "Ref:123"));
        }

        [Theory]
        [InlineData("lnvoice N0: 123")]        // l for I and 0 for O: two slips in a nine-letter label
        [InlineData("Invoice No: 123")]
        [InlineData("1nvoice No: 123")]
        [InlineData("|nvoice No: 123")]         // a bar for an I
        public void CommonOcrSlips_AreTolerated_WithinTheBudget(string text) => Assert.Equal("123", Single("Invoice No", text));

        [Fact]
        public void TheSlipBudget_IsOnePerSixLettersAndDigits_AndNoneForShortLabels()
        {
            Assert.Equal("5", Single("Total", "Tota1 5"));                 // 5 letters: one slip
            Assert.Empty(Locate(TemplateOf(("F", "Total", ValuePosition.SameLine)), "T0ta1 5"));   // two slips is too many
            Assert.Empty(Locate(TemplateOf(("F", "Due", ValuePosition.SameLine)), "Dve 5"));
            Assert.Empty(Locate(TemplateOf(("F", "Sum", ValuePosition.SameLine)), "5um 5"));      // three letters: exact only
            Assert.Equal("5", Single("Sum", "SUM 5"));
            Assert.Equal("7", Single("Amount", "Arnount 7"));              // rn read as m
            Assert.Equal("7", Single("Summary", "Surnmary 7"));
            Assert.Empty(Locate(TemplateOf(("F", "Total", ValuePosition.SameLine)), "Tetal 5"));    // e for o is not a listed confusion
        }

        [Fact]
        public void ACandidateRecordsWhetherItsLabelNeededASlip()
        {
            var t = TemplateOf(("F", "Invoice No", ValuePosition.SameLine));
            Assert.False(Locate(t, "Invoice No 1").Single().Slipped);
            Assert.True(Locate(t, "lnvoice No 1").Single().Slipped);
            Assert.False(Locate(t, "INVOICE   NO 1").Single().Slipped);     // case and spacing are not slips
        }

        [Fact]
        public void MatchingDoesNotDependOnTheMachineCulture()
        {
            var t = TemplateOf(("F", "Invoice", ValuePosition.SameLine));
            List<string> invariant = Found(t, "INVOICE 1\ninvoice 2\n\u0130nvoice 3\n\u0131nvoice 4"), turkish = null;
            CultureScope.Run("tr-TR", () => turkish = Found(t, "INVOICE 1\ninvoice 2\n\u0130nvoice 3\n\u0131nvoice 4"));
            Assert.Equal(invariant, turkish ?? invariant);
            Assert.Contains("F=1@1", invariant);
            Assert.Contains("F=2@2", invariant);
        }

        // ------------------------------------------------------------------ several labels on a line

        [Fact]
        public void ASameLineValue_StopsAtTheNextKnownLabel()
        {
            var t = TemplateOf(("Number", "Invoice No", ValuePosition.SameLine), ("Date", "Date", ValuePosition.SameLine), ("Total", "Total", ValuePosition.SameLine));
            Assert.Equal(new[] { "Number=INV-123@1", "Date=2024-01-31@1", "Total=1,234.50@1" }, Found(t, "Invoice No: INV-123   Date: 2024-01-31   Total: 1,234.50"));
        }

        [Fact]
        public void TheLongerOfTwoOverlappingLabelsWins()
        {
            var t = TemplateOf(("Invoiced", "Date", ValuePosition.SameLine), ("Due", "Due Date", ValuePosition.SameLine));
            Assert.Equal(new[] { "Invoiced=01/01@1", "Due=31/01@1" }, Found(t, "Date: 01/01  Due Date: 31/01"));
            var alternatives = TemplateOf(("Number", "Invoice|Invoice Number", ValuePosition.SameLine));
            Assert.Equal(new[] { "Number=77@1" }, Found(alternatives, "Invoice Number: 77"));      // the longer alternative, not "Invoice" with value "Number: 77"
        }

        [Fact]
        public void TwoFieldsWithTheSameLabel_BothSeeIt_ButOneFieldNeverSeesItTwice()
        {
            var t = TemplateOf(("A", "Status", ValuePosition.SameLine), ("B", "Status", ValuePosition.NextLine));
            Assert.Equal(new[] { "A=OPEN@1", "B=next@2" }, Found(t, "Status: OPEN\nnext"));
            var spaced = TemplateOf(("C", "Invoice No|Invoice  No", ValuePosition.SameLine));          // two spellings that match the same text
            Assert.Single(Locate(spaced, "Invoice No 5"));
        }

        [Fact]
        public void ALabelFollowedDirectlyByAnotherLabel_HasNoValue()
        {
            var t = TemplateOf(("Number", "Invoice No", ValuePosition.SameLine), ("Date", "Date", ValuePosition.SameLine));
            List<LabelCandidate> found = Locate(t, "Invoice No:   Date: 2024-01-31");
            Assert.Equal("", found[0].Value);
            Assert.Equal(0, found[0].ValueLine);
            Assert.Equal("2024-01-31", found[1].Value);
        }

        [Fact]
        public void EveryOccurrence_IsACandidate_InTextOrder()
        {
            var t = TemplateOf(("Total", "Total", ValuePosition.SameLine));
            Assert.Equal(new[] { "Total=10@1", "Total=20@3" }, Found(t, "Total 10\nnothing\nTotal 20"));
            Assert.Equal(new[] { "Total=@0" }, Found(t, "Total"));                           // no value: line 0
        }

        // ------------------------------------------------------------------ positions

        [Fact]
        public void NextLine_TakesTheNextNonBlankLine()
        {
            var t = TemplateOf(("Address", "Ship To", ValuePosition.NextLine), ("Terms", "Terms", ValuePosition.SameLine));
            Assert.Equal(new[] { "Address=12 Main St, Springfield@4", "Terms=Net 30@4" }, Found(t, "Ship To:\n\n   \n12 Main St, Springfield   Terms: Net 30"));
            Assert.Equal(new[] { "Address=@0" }, Found(TemplateOf(("Address", "Ship To", ValuePosition.NextLine)), "Ship To:\n\n"));
        }

        [Fact]
        public void Below_TakesTheCellUnderTheLabelsColumns()
        {
            const string screen =
                "Policy No      Holder              Premium\n" +
                "PX-000123      Jane Q Public       1,250.00\n";
            var t = TemplateOf(("Policy", "Policy No", ValuePosition.Below), ("Holder", "Holder", ValuePosition.Below), ("Premium", "Premium", ValuePosition.Below));
            Assert.Equal(new[] { "Policy=PX-000123@2", "Holder=Jane Q Public@2", "Premium=1,250.00@2" }, Found(t, screen));
        }

        [Fact]
        public void Below_UsesTheFirstCellOverlappingTheLabel_AndNothingWhenNoCellIsUnderIt()
        {
            Assert.Equal("ABC", Single("Code", "      Code\n    ABC        XYZ", ValuePosition.Below));    // shifted left but overlapping
            Assert.Equal("", Single("Code", "            Code\nABC", ValuePosition.Below));             // nothing under the label
            Assert.True(LabelLocator.CellUnder("a  bb cc   d", 4, 6, out int s, out int e));
            Assert.Equal((3, 8), (s, e));                                                              // one space keeps "bb cc" together
        }

        [Fact]
        public void Below_SkipsBlankLines_LikeNextLine_ButStillNeedsACellUnderTheLabel()
        {
            Assert.Equal("PX-1", Single("Policy No", "Policy No     Holder\n\n   \nPX-1          Jane", ValuePosition.Below));          // double-spaced OCR output
            Assert.Equal("", Single("Policy No", "Policy No\n\n                    unrelated text far to the right", ValuePosition.Below));   // nothing under the label
        }

        // ------------------------------------------------------------------ golden samples

        [Fact]
        public void Golden_InvoiceEmail()
        {
            const string email =
                "Hello,\r\n\r\n" +
                "Please find our invoice below.\r\n\r\n" +
                "Invoice Number: INV-2026-0042\r\n" +
                "Invoice Date: 26/09/2026    Due Date: 26/10/2026\r\n" +
                "Amount Due: \u20AC1.234,50\r\n" +
                "IBAN: DE89 3704 0044 0532 0130 00\r\n\r\n" +
                "Kind regards,\r\nAccounts\r\n";
            var t = TemplateOf(("Number", "Invoice Number|Invoice No", ValuePosition.SameLine), ("Issued", "Invoice Date", ValuePosition.SameLine),
                ("Due", "Due Date|Payment Due", ValuePosition.SameLine), ("Total", "Amount Due|Total", ValuePosition.SameLine), ("Iban", "IBAN", ValuePosition.SameLine));
            Assert.Equal(new[] { "Number=INV-2026-0042@5", "Issued=26/09/2026@6", "Due=26/10/2026@6", "Total=\u20AC1.234,50@7", "Iban=DE89 3704 0044 0532 0130 00@8" }, Found(t, email));
        }

        [Fact]
        public void Golden_OcrOutputWithSlips()
        {
            const string ocr =
                "ACME C0RP                         lNVOICE\n" +
                "lnvoice N0 : 88731          Date : 2O26-09-26\n" +
                "Tota1 Arnount ...... 1,045.00\n";
            var t = TemplateOf(("Number", "Invoice No", ValuePosition.SameLine), ("Date", "Date", ValuePosition.SameLine), ("Total", "Total Amount", ValuePosition.SameLine));
            // "Date" is four letters, so exactly one slip would be allowed, but here the label itself is clean; the value keeps its O (typed values repair digits later)
            Assert.Equal(new[] { "Number=88731@2", "Date=2O26-09-26@2", "Total=1,045.00@3" }, Found(t, ocr));
        }

        [Fact]
        public void Golden_TerminalScreen()
        {
            const string screen =
                "CLAIMS INQUIRY                                   SCREEN CLM010\n" +
                "Claim Number: 4471-993-02                  Status: OPEN\n" +
                "Claimant     Loss Date     Reserve\n" +
                "SMITH, JOHN  2026-08-14    12,500.00\n" +
                "F3=Exit  F12=Cancel\n";
            var t = TemplateOf(("Claim", "Claim Number", ValuePosition.SameLine), ("Status", "Status", ValuePosition.SameLine),
                ("Claimant", "Claimant", ValuePosition.Below), ("Loss", "Loss Date", ValuePosition.Below), ("Reserve", "Reserve", ValuePosition.Below));
            Assert.Equal(new[] { "Claim=4471-993-02@2", "Status=OPEN@2", "Claimant=SMITH, JOHN@4", "Loss=2026-08-14@4", "Reserve=12,500.00@4" }, Found(t, screen));
        }

        [Fact]
        public void RawValues_AreTheOriginalText_EvenWhenNormalizationChangedTheLine()
        {
            string text = "Reference\u00A0No:\u00A0\u201CAB\u2013123\u201D";
            LabelCandidate c = Locate(TemplateOf(("Ref", "Reference No", ValuePosition.SameLine)), text).Single();
            Assert.Equal("\"AB-123\"", c.Value);
            Assert.Equal("\u201CAB\u2013123\u201D", c.Raw);
        }

        [Fact]
        public void PatternFields_AreNotLocatedByLabel()
        {
            var t = TemplateOf(("A", "Ref", ValuePosition.SameLine));
            Assert.Null(t.TryAddPatternField("P", "(?<value>Ref)", FieldType.Text, DecimalStyle.DotDecimal, null));
            Assert.Equal(new[] { "A=x@1" }, Found(t, "Ref x"));
        }

        [Fact]
        public void ALargeTemplateOnALargeText_IsFast()
        {
            var t = new Template();
            for (int i = 0; i < 200; i++) Assert.Null(t.TryAddLabelField("F" + i, "Label number " + i + "|Other label " + i, ValuePosition.SameLine, FieldType.Text, DecimalStyle.DotDecimal, null, true, Occurrence.RequireUnique));
            string text = string.Join("\n", Enumerable.Range(0, 10000).Select(i => "Label number " + (i % 250) + ": value " + i + "   some other words on the line"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<LabelCandidate> found = Locate(t, text);
            sw.Stop();
            Assert.Equal(8000, found.Count);
            Assert.True(sw.ElapsedMilliseconds < 20000, "took " + sw.ElapsedMilliseconds + " ms");
        }
    }
}
