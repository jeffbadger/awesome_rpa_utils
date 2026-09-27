using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TextExtractAutomation.Tests
{
    public sealed class ExtractTests
    {
        private const string Email =
            "Hello,\r\n\r\n" +
            "Invoice Number: INV-2026-0042\r\n" +
            "Invoice Date: 26/09/2026    Due Date: 26/10/2026\r\n" +
            "Amount Due: EUR 1.234,50\r\n" +
            "IBAN: DE89 3704 0044 0532 0130 00\r\n" +
            "Contact: Accounts@Example.COM\r\n\r\n" +
            "Kind regards\r\n";

        private static TextExtractUtils Invoice()
        {
            var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("InvoiceNumber", "Invoice Number|Invoice No", FieldType.Code, out string m), m);
            Assert.True(c.AddLabelField("Issued", "Invoice Date", ValuePosition.SameLine, FieldType.Date, DecimalStyle.DotDecimal, "dd/MM/yyyy", true, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddLabelField("Due", "Due Date|Payment Due", ValuePosition.SameLine, FieldType.Date, DecimalStyle.DotDecimal, "dd/MM/yyyy", true, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddLabelField("Total", "Amount Due|Total", ValuePosition.SameLine, FieldType.Amount, DecimalStyle.CommaDecimal, null, true, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddLabelFieldSimple("Iban", "IBAN", FieldType.Iban, out m), m);
            Assert.True(c.AddLabelFieldSimple("Contact", "Contact", FieldType.Email, out m), m);
            return c;
        }

        private static (bool found, string value, string raw, string reason, int line) Get(TextExtractUtils c, string name)
        {
            Assert.True(c.GetField(name, out bool found, out string value, out string raw, out string reason, out int line, out string m), m);
            Assert.Null(m);
            return (found, value, raw, reason, line);
        }

        [Fact]
        public void TheInvoiceEmail_IsExtractedEndToEnd()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText(Email, out int found, out int missing, out string m), m);
            Assert.Null(m);
            Assert.Equal((6, 0), (found, missing));
            Assert.Equal((true, "INV-2026-0042", "INV-2026-0042", (string)null, 3), Get(c, "InvoiceNumber"));
            Assert.Equal((true, "2026-09-26", "26/09/2026", (string)null, 4), Get(c, "Issued"));
            Assert.Equal((true, "2026-10-26", "26/10/2026", (string)null, 4), Get(c, "Due"));
            Assert.Equal((true, "1234.50", "EUR 1.234,50", (string)null, 5), Get(c, "Total"));
            Assert.Equal((true, "DE89370400440532013000", "DE89 3704 0044 0532 0130 00", (string)null, 6), Get(c, "Iban"));
            Assert.Equal((true, "Accounts@example.com", "Accounts@Example.COM", (string)null, 7), Get(c, "Contact"));
            Assert.Equal((true, "1234.50", "EUR 1.234,50", (string)null, 5), Get(c, "TOTAL"));          // names are matched ignoring case
        }

        [Fact]
        public void Raw_IsOnlyThePartThatWasRead_NotTheWholeRestOfTheLine()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText("Invoice Number: INV-7 (copy)\nAmount Due: EUR 1.234,50 incl. VAT\nContact: write to ap@acme.com today", out _, out _, out string m), m);
            Assert.Equal("INV-7", Get(c, "InvoiceNumber").raw);
            Assert.Equal("EUR 1.234,50", Get(c, "Total").raw);
            Assert.Equal("ap@acme.com", Get(c, "Contact").raw);
        }

        [Fact]
        public void TheResultJson_CarriesEveryField_WithItsDetails()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText(Email.Replace("IBAN: DE89 3704 0044 0532 0130 00", "IBAN: DE89 3704 0044 0532 0130 01"), out _, out _, out string m), m);
            Assert.True(c.GetResultJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            Assert.Equal(5, root.GetProperty("foundCount").GetInt32());
            Assert.Equal(1, root.GetProperty("missingRequiredCount").GetInt32());
            JsonElement[] fields = root.GetProperty("fields").EnumerateArray().ToArray();
            Assert.Equal(new[] { "InvoiceNumber", "Issued", "Due", "Total", "Iban", "Contact" }, fields.Select(f => f.GetProperty("name").GetString()));
            JsonElement total = fields[3];
            Assert.Equal("EUR", total.GetProperty("currency").GetString());
            Assert.Equal("Amount Due", total.GetProperty("label").GetString());
            Assert.Equal(1, total.GetProperty("occurrences").GetInt32());
            JsonElement iban = fields[4];
            Assert.False(iban.GetProperty("found").GetBoolean());
            Assert.Equal("InvalidValue", iban.GetProperty("reason").GetString());
            Assert.Equal(JsonValueKind.Null, iban.GetProperty("value").ValueKind);
            Assert.Equal("DE89 3704 0044 0532 0130 01", iban.GetProperty("raw").GetString());          // what was there, for a person to look at
            Assert.Contains("mod 97", iban.GetProperty("explanation").GetString());
            Assert.DoesNotContain("3704", iban.GetProperty("explanation").GetString());                   // explanations never quote the text
        }

        [Fact]
        public void EveryReason_IsReported()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Missing", "Policy Number", FieldType.Code, out string m), m);
            Assert.True(c.AddLabelFieldSimple("Empty", "Reference", FieldType.Code, out m), m);
            Assert.True(c.AddLabelFieldSimple("Bad", "Total", FieldType.Amount, out m), m);
            Assert.True(c.AddLabelFieldSimple("Twice", "Status", FieldType.Text, out m), m);
            Assert.True(c.ExtractFromText("Reference:\nTotal: n/a\nStatus: OPEN\nStatus: CLOSED", out int found, out int missing, out m), m);
            Assert.Equal((0, 4), (found, missing));
            Assert.Equal((false, (string)null, (string)null, "MissingLabel", 0), Get(c, "Missing"));
            Assert.Equal((false, (string)null, (string)null, "MissingValue", 1), Get(c, "Empty"));        // the label's line
            Assert.Equal((false, (string)null, "n/a", "InvalidValue", 2), Get(c, "Bad"));
            Assert.Equal((false, (string)null, (string)null, "AmbiguousValue", 3), Get(c, "Twice"));
        }

        [Fact]
        public void RequireUnique_AcceptsTheSameValueRepeated_ButNotTwoDifferentOnes()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Total", "Total", FieldType.Amount, out string m), m);
            Assert.True(c.ExtractFromText("Total: 1,234.50\n...\nTotal $1234.50", out int found, out _, out m), m);
            Assert.Equal(1, found);                                                                       // the same amount in a header and a footer
            Assert.Equal((true, "1234.50", "1,234.50", (string)null, 1), Get(c, "Total"));
            Assert.True(c.ExtractFromText("Total: 12.00\nTotal: n/a", out found, out _, out m), m);
            Assert.Equal((true, "12.00", "12.00", (string)null, 1), Get(c, "Total"));                    // an invalid occurrence does not make it ambiguous
            Assert.True(c.ExtractFromText("Total: n/a\nTotal: -", out found, out _, out m), m);
            Assert.Equal("InvalidValue", Get(c, "Total").reason);                                        // none valid: the first occurrence's reason
        }

        [Fact]
        public void FirstAndLast_TakeThatOccurrenceAsItIs()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelField("First", "Status", ValuePosition.SameLine, FieldType.Text, DecimalStyle.DotDecimal, null, true, Occurrence.First, out string m), m);
            Assert.True(c.AddLabelField("Last", "Status", ValuePosition.SameLine, FieldType.Text, DecimalStyle.DotDecimal, null, true, Occurrence.Last, out m), m);
            Assert.True(c.ExtractFromText("Status: OPEN\nStatus: PENDING\nStatus: CLOSED", out int found, out _, out m), m);
            Assert.Equal(2, found);
            Assert.Equal("OPEN", Get(c, "First").value);
            Assert.Equal("CLOSED", Get(c, "Last").value);
            Assert.True(c.GetResultJson(out string json, out m), m);
            Assert.Contains("\"occurrences\":3", json);
        }

        [Fact]
        public void OptionalFields_DoNotCountAsMissing()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelField("Po", "PO Number", ValuePosition.SameLine, FieldType.Code, DecimalStyle.DotDecimal, null, false, Occurrence.RequireUnique, out string m), m);
            Assert.True(c.AddLabelFieldSimple("Total", "Total", FieldType.Amount, out m), m);
            Assert.True(c.ExtractFromText("Total 5.00", out int found, out int missing, out m), m);
            Assert.Equal((1, 0), (found, missing));
            Assert.Equal("MissingLabel", Get(c, "Po").reason);
        }

        [Fact]
        public void OcrOutput_IsExtracted_AndTheSlippedLabelIsRecorded()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Number", "Invoice No", FieldType.Integer, out string m), m);
            Assert.True(c.AddLabelField("Date", "Date", ValuePosition.SameLine, FieldType.Date, DecimalStyle.DotDecimal, null, true, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddLabelFieldSimple("Total", "Total Amount", FieldType.Amount, out m), m);
            Assert.True(c.ExtractFromText("lnvoice N0 : 88731          Date : 2O26-09-26\nTota1 Arnount ...... 1,O45.00\n", out int found, out _, out m), m);
            Assert.Equal(3, found);
            Assert.Equal("88731", Get(c, "Number").value);
            Assert.Equal("2026-09-26", Get(c, "Date").value);                                             // O repaired inside the date
            Assert.Equal("1045.00", Get(c, "Total").value);
            Assert.Equal("1,O45.00", Get(c, "Total").raw);                                                // raw keeps what OCR produced
            Assert.True(c.GetResultJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("fields")[0].GetProperty("labelSlipped").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("fields")[1].GetProperty("labelSlipped").GetBoolean());
        }

        [Fact]
        public void PatternFields_AreExtracted_WithLinesAndRawText()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddPatternField("Ref", @"Ref\s*[:#]?\s*(?<value>[A-Z]{3}-\d+)", FieldType.Code, DecimalStyle.DotDecimal, null, out string m), m);
            Assert.True(c.AddPatternField("Amount", @"(?<value>\d+,\d\d) EUR", FieldType.Amount, DecimalStyle.CommaDecimal, null, out m), m);
            Assert.True(c.AddPatternField("Nothing", @"XYZ(?<value>\d+)", FieldType.Integer, DecimalStyle.DotDecimal, null, out m), m);
            Assert.True(c.ExtractFromText("line one\r\nsee Ref# ABC-123 here\r\npay 12,50 EUR", out int found, out int missing, out m), m);
            Assert.Equal((2, 1), (found, missing));
            Assert.Equal((true, "ABC-123", "ABC-123", (string)null, 2), Get(c, "Ref"));
            Assert.Equal((true, "12.50", "12,50", (string)null, 3), Get(c, "Amount"));
            Assert.Equal("MissingLabel", Get(c, "Nothing").reason);
        }

        [Fact]
        public void APatternThatRunsTooLong_IsReportedAsPatternTimeout_AndTheRestStillWorks()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddPatternField("Slow", @"^(?<value>(a+)+)$", FieldType.Text, DecimalStyle.DotDecimal, null, out string m), m);
            Assert.True(c.AddLabelFieldSimple("Total", "Total", FieldType.Amount, out m), m);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(c.ExtractFromText(new string('a', 40) + "!\nTotal 5.00", out int found, out int missing, out m), m);
            Assert.True(sw.ElapsedMilliseconds < 10000);
            Assert.Equal((1, 1), (found, missing));
            Assert.Equal("PatternTimeout", Get(c, "Slow").reason);
            Assert.Equal("5.00", Get(c, "Total").value);
        }

        [Fact]
        public void TheCursor_ReadsEveryFieldInTemplateOrder_ThenStaysExhausted_UntilReset()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText(Email, out _, out _, out string m), m);
            var names = new List<string>();
            while (true)
            {
                Assert.True(c.TryReadNextField(out bool has, out string name, out string value, out string raw, out string reason, out int line, out m), m);
                if (!has) { Assert.Null(name); Assert.Null(value); Assert.Null(raw); Assert.Null(reason); Assert.Equal(0, line); break; }
                names.Add(name + "=" + value);
            }
            Assert.Equal(6, names.Count);
            Assert.Equal("InvoiceNumber=INV-2026-0042", names[0]);
            Assert.True(c.TryReadNextField(out bool again, out _, out _, out _, out _, out _, out m));
            Assert.False(again);
            Assert.True(c.ResetFieldCursor(out m), m);
            Assert.True(c.TryReadNextField(out bool first, out string firstName, out _, out _, out _, out _, out m));
            Assert.True(first);
            Assert.Equal("InvoiceNumber", firstName);
        }

        // ------------------------------------------------------------------ failures and lifecycle

        [Fact]
        public void ExtractFromText_RefusesNullText_AnEmptyTemplate_AndTextOverTheLimit()
        {
            using var empty = new TextExtractUtils();
            Assert.False(empty.ExtractFromText("x", out _, out _, out string m)); Assert.Contains("no fields", m);
            using var c = Invoice();
            Assert.False(c.ExtractFromText(null, out _, out _, out m)); Assert.Contains("text is required", m);
            Assert.True(c.ConfigureLimits(10, out m), m);
            Assert.True(c.ExtractFromText("0123456789", out _, out int missing, out m), m);                  // at the limit
            Assert.Equal(6, missing);
            Assert.False(c.ExtractFromText("0123456789A", out int found, out missing, out m));
            Assert.Equal((0, 0), (found, missing));
            Assert.Contains("longer than 10 characters", m);
            Assert.DoesNotContain("0123456789", m);
        }

        [Fact]
        public void EmptyText_IsAnOrdinaryExtraction_WithEveryFieldMissing()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText("", out int found, out int missing, out string m), m);
            Assert.Equal((0, 6), (found, missing));
        }

        [Fact]
        public void BeforeAnExtraction_EveryReaderFails_WithAnActionableMessage()
        {
            using var c = Invoice();
            Assert.False(c.GetField("Total", out _, out _, out _, out _, out _, out string m)); Assert.Contains("run ExtractFromText first", m);
            Assert.False(c.GetResultJson(out string json, out m)); Assert.Null(json);
            Assert.False(c.ResetFieldCursor(out m));
            Assert.False(c.TryReadNextField(out _, out _, out _, out _, out _, out _, out m));
            Assert.True(c.ClearResults(out m)); Assert.Null(m);
            Assert.True(c.ClearResults(out m));                                                           // idempotent
        }

        [Fact]
        public void AnUnknownFieldName_FailsWithoutTouchingTheResults()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText(Email, out _, out _, out string m), m);
            Assert.False(c.GetField("Nope", out bool found, out string value, out _, out _, out int line, out m));
            Assert.Equal((false, (string)null, 0), (found, value, line));
            Assert.Contains("no field with that name", m);
            Assert.True(Get(c, "Total").found);
        }

        [Theory]
        [InlineData("AddLabelFieldSimple")]
        [InlineData("AddLabelField")]
        [InlineData("AddPatternField")]
        [InlineData("ClearTemplate")]
        [InlineData("LoadTemplateJson")]
        [InlineData("ConfigureLimits")]
        [InlineData("ClearResults")]
        [InlineData("FailedExtraction")]
        public void EveryAcceptedTemplateChange_AndClearResults_AndAFailedExtraction_DiscardTheResults(string action)
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText(Email, out _, out _, out string m), m);
            Assert.True(c.TryReadNextField(out _, out _, out _, out _, out _, out _, out m));
            bool ok = action switch
            {
                "AddLabelFieldSimple" => c.AddLabelFieldSimple("New", "New", FieldType.Text, out m),
                "AddLabelField" => c.AddLabelField("New", "New", ValuePosition.NextLine, FieldType.Text, DecimalStyle.DotDecimal, null, false, Occurrence.First, out m),
                "AddPatternField" => c.AddPatternField("New", "(?<value>x)", FieldType.Text, DecimalStyle.DotDecimal, null, out m),
                "ClearTemplate" => c.ClearTemplate(out m),
                "LoadTemplateJson" => c.LoadTemplateJson("{\"schemaVersion\":1}", out m),
                "ConfigureLimits" => c.ConfigureLimits(5000, out m),
                "ClearResults" => c.ClearResults(out m),
                _ => !c.ExtractFromText(null, out _, out _, out m)
            };
            Assert.True(ok, m);
            Assert.False(c.GetField("Total", out _, out _, out _, out _, out _, out m));
            Assert.Contains("no results", m);
        }

        [Fact]
        public void ARefusedTemplateChange_AndTheReadMethods_KeepTheResults()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText(Email, out _, out _, out string m), m);
            Assert.False(c.AddLabelFieldSimple("Total", "Dup", FieldType.Text, out _));
            Assert.False(c.LoadTemplateJson("{", out _));
            Assert.False(c.ConfigureLimits(0, out _));
            Assert.True(c.GetTemplateJson(out _, out m), m);
            Assert.True(c.ValidateTemplateJson("{\"schemaVersion\":1}", out _, out _, out m), m);
            Assert.True(Get(c, "Total").found);
        }

        [Fact]
        public void ANewExtraction_ReplacesTheResults_AndResetsTheCursor()
        {
            using var c = Invoice();
            Assert.True(c.ExtractFromText(Email, out _, out _, out string m), m);
            Assert.True(c.TryReadNextField(out _, out _, out _, out _, out _, out _, out m));
            Assert.True(c.ExtractFromText("Total: 1,00", out int found, out _, out m), m);
            Assert.Equal(1, found);
            Assert.Equal("1.00", Get(c, "Total").value);
            Assert.True(c.TryReadNextField(out _, out string name, out _, out _, out _, out _, out m));
            Assert.Equal("InvoiceNumber", name);
        }

        [Fact]
        public void NoMessage_EverContainsTheText()
        {
            const string secret = "SECRET-7731-XYZ";
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("A", "Amount", FieldType.Amount, out string m), m);
            Assert.True(c.ExtractFromText("Amount: " + secret, out _, out _, out m), m);
            var texts = new List<string> { m };
            c.GetField("A", out _, out _, out _, out _, out _, out string gm); texts.Add(gm);
            c.GetResultJson(out string json, out _);
            using JsonDocument doc = JsonDocument.Parse(json);
            texts.Add(doc.RootElement.GetProperty("fields")[0].GetProperty("explanation").GetString());
            Assert.True(c.ConfigureLimits(5, out m)); c.ExtractFromText(secret, out _, out _, out m); texts.Add(m);
            Assert.All(texts.Where(t => t != null), t => Assert.DoesNotContain("SECRET", t));
        }

        [Fact]
        public void TheMachineCulture_ChangesNoResult()
        {
            string Run()
            {
                using var c = Invoice();
                Assert.True(c.ExtractFromText(Email, out _, out _, out string m), m);
                Assert.True(c.GetResultJson(out string json, out m), m);
                return json;
            }
            string invariant = Run();
            foreach (string culture in new[] { "tr-TR", "de-DE", "ar-SA" })
            {
                string local = null;
                CultureScope.Run(culture, () => local = Run());
                Assert.Equal(invariant, local ?? invariant);
            }
        }

        [Fact]
        public void ConcurrentExtractionsAndReads_AreSerialized()
        {
            using var c = Invoice();
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var writer = System.Threading.Tasks.Task.Run(() => { for (int i = 0; i < 200; i++) if (!c.ExtractFromText(Email, out int f, out _, out string m) || f != 6) errors.Enqueue(m ?? "count"); });
            var reader = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                    if (c.GetResultJson(out string json, out _))
                    {
                        using JsonDocument doc = JsonDocument.Parse(json);
                        if (doc.RootElement.GetProperty("fields").GetArrayLength() != 6) errors.Enqueue("torn result");
                    }
            });
            System.Threading.Tasks.Task.WaitAll(writer, reader);
            Assert.Empty(errors);
        }

        [Fact]
        public void LineOf_CountsBreaksLikeTheLineSplitter()
        {
            string text = "a\r\nb\rc\nd";
            Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { 0, 3, 5, 7 }.Select(i => Extraction.LineOf(text, i)));    // a, b, c, d: CRLF counts once
        }
    }
}
