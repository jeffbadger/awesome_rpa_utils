using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TextExtractAutomation.Tests
{
    public sealed class TableTests
    {
        private const string Invoice =
            "Invoice Number: INV-7\n" +
            "\n" +
            "Item        Description              Qty      Unit Price       Amount\n" +
            "----------  -----------------------  ---  -------------  -----------\n" +
            "A-100       Blue widget              2           12.50        25.00\n" +
            "B-200       Red widget, large        10           1.25        12.50\n" +
            "C-300       Service fee              1           99.00        99.00\n" +
            "Total: 136.50\n";

        private static TextExtractUtils Lines()
        {
            var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Number", "Invoice Number", FieldType.Code, out string m), m);
            Assert.True(c.AddLabelFieldSimple("Total", "Total", FieldType.Amount, out m), m);
            Assert.True(c.AddTableColumn("Lines", "Item", FieldType.Code, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.AddTableColumn("Lines", "Description", FieldType.Text, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.AddTableColumn("Lines", "Qty", FieldType.Integer, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.AddTableColumn("Lines", "Unit Price", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.AddTableColumn("Lines", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            return c;
        }

        private static List<string> Rows(TextExtractUtils c, string table, params string[] columns)
        {
            var rows = new List<string>();
            while (true)
            {
                Assert.True(c.TryReadNextRow(table, out bool has, out int number, out string json, out string m), m);
                if (!has) { Assert.Equal((0, (string)null), (number, json)); return rows; }
                using (JsonDocument.Parse(json)) { }
                rows.Add(number + ":" + string.Join("|", columns.Select(col =>
                {
                    Assert.True(c.GetRowValue(col, out bool found, out string value, out _, out string reason, out string gm), gm);
                    return found ? value : "(" + reason + ")";
                })));
            }
        }

        [Fact]
        public void AnInvoiceTable_IsReadRowByRow_WithTypedCells_AndTheFieldsStillWork()
        {
            using var c = Lines();
            Assert.True(c.ExtractFromText(Invoice, out int found, out int missing, out string m), m);
            Assert.Equal((2, 0), (found, missing));                                           // the fields are unaffected by the table
            Assert.Equal(new[]
            {
                "1:A-100|Blue widget|2|12.50|25.00",
                "2:B-200|Red widget, large|10|1.25|12.50",
                "3:C-300|Service fee|1|99.00|99.00"
            }, Rows(c, "Lines", "Item", "Description", "Qty", "Unit Price", "Amount"));
            Assert.True(c.GetField("Total", out bool totalFound, out string total, out _, out _, out _, out m), m);
            Assert.Equal((true, "136.50"), (totalFound, total));                             // the Total line ended the table and is still a field
        }

        [Fact]
        public void TheRowJson_AndGetRowValue_CarryValueRawAndReason()
        {
            using var c = Lines();
            Assert.True(c.ExtractFromText(Invoice.Replace("        12.50        25.00", "        12.5O        n/a  "), out _, out _, out string m), m);
            Assert.True(c.TryReadNextRow("LINES", out bool has, out int number, out string json, out m), m);          // table names ignore case
            Assert.True(has);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("rowNumber").GetInt32());
            Assert.Equal(5, doc.RootElement.GetProperty("lineNumber").GetInt32());
            Assert.Equal(new[] { "Item", "Description", "Qty", "Unit Price", "Amount" }, doc.RootElement.GetProperty("cells").EnumerateArray().Select(x => x.GetProperty("column").GetString()));
            Assert.True(c.GetRowValue("unit price", out bool found, out string value, out string raw, out string reason, out m), m);
            Assert.Equal((true, "12.50", "12.5O", (string)null), (found, value, raw, reason));                          // OCR repaired, raw as written
            Assert.True(c.GetRowValue("Amount", out found, out value, out raw, out reason, out m), m);
            Assert.Equal((false, (string)null, "n/a", "InvalidValue"), (found, value, raw, reason));
        }

        [Fact]
        public void ACellWithNothingUnderItsColumn_IsMissingValue_AndOutOfLineValuesStillLandInTheirColumn()
        {
            using var c = new TextExtractUtils();
            foreach (string h in new[] { "Code", "Name", "Balance" })
                Assert.True(c.AddTableColumn("T", h, h == "Balance" ? FieldType.Amount : FieldType.Text, DecimalStyle.DotDecimal, "", out string m), m);
            const string text =
                "Code    Name            Balance\n" +
                "X1      Alpha           1,200.00\n" +       // right-aligned under Balance
                "X2                         5.00\n" +        // no name
                "  X3    Gamma        77.00\n";              // a little out of line
            Assert.True(c.ExtractFromText(text, out _, out _, out string msg), msg);
            Assert.Equal(new[] { "1:X1|Alpha|1200.00", "2:X2|(MissingValue)|5.00", "3:X3|Gamma|77.00" }, Rows(c, "T", "Code", "Name", "Balance"));
        }

        [Fact]
        public void ATableEndsAtABlankLine_ALabelLine_OrTheEnd_AndARepeatedHeaderContinuesIt()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Sub", "Page Total", FieldType.Amount, out string m), m);
            Assert.True(c.AddTableColumn("T", "Ref", FieldType.Code, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.AddTableColumn("T", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            const string text =
                "Ref      Amount\n" +
                "=======  ======\n" +
                "R1       1.00\n" +
                "R2       2.00\n" +
                "Page Total: 3.00\n" +                        // a field's label ends the table
                "not a row\n" +
                "Ref      Amount\n" +                         // page 2: the same header continues the table
                "R3       3.00\n" +
                "\n" +                                        // a blank line ends it
                "R4       4.00\n";
            Assert.True(c.ExtractFromText(text, out _, out _, out m), m);
            Assert.Equal(new[] { "1:R1|1.00", "2:R2|2.00", "3:R3|3.00" }, Rows(c, "T", "Ref", "Amount"));
            Assert.True(c.GetResultJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement t = doc.RootElement.GetProperty("tables")[0];
            Assert.Equal((true, 1, 3), (t.GetProperty("found").GetBoolean(), t.GetProperty("headerLine").GetInt32(), t.GetProperty("rowCount").GetInt32()));
        }

        [Fact]
        public void AHeaderLineRightAfterTheRows_IsNotARow()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddTableColumn("T", "Ref", FieldType.Code, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(c.AddTableColumn("T", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.ExtractFromText("Ref      Amount\nR1       1.00\nRef      Amount\nR2       2.00\n", out _, out _, out m), m);
            Assert.Equal(new[] { "1:R1|1.00", "2:R2|2.00" }, Rows(c, "T", "Ref", "Amount"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnExactHeader_TakesItsPlaceBeforeAnOcrLookAlike_WhateverTheColumnOrder(bool reversed)
        {
            using var c = new TextExtractUtils();
            foreach (string h in reversed ? new[] { "RetuM", "Return" } : new[] { "Return", "RetuM" })
                Assert.True(c.AddTableColumn("T", h, FieldType.Integer, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(c.ExtractFromText("RetuM    Return\n    1         2\n", out _, out _, out string msg), msg);
            Assert.Equal(new[] { "1:1|2" }, Rows(c, "T", "RetuM", "Return"));
        }

        [Fact]
        public void AHeaderInsideAnotherHeader_TakesItsOwnPlace_WhateverTheColumnOrder()
        {
            using (var reversed = new TextExtractUtils())
            {
                Assert.True(reversed.AddTableColumn("T", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out string rm), rm);
                Assert.True(reversed.AddTableColumn("T", "Amount Due", FieldType.Amount, DecimalStyle.DotDecimal, "", out rm), rm);
                Assert.True(reversed.ExtractFromText("Amount Due    Amount\n     10.00     20.00\n", out _, out _, out rm), rm);
                Assert.Equal(new[] { "1:20.00|10.00" }, Rows(reversed, "T", "Amount", "Amount Due"));
                Assert.True(reversed.GetResultJson(out string json, out rm), rm);
                Assert.Contains("\"cells\":[{\"column\":\"Amount\",\"found\":true,\"value\":\"20.00\"", json);      // cells stay in template column order
            }

            using var c = new TextExtractUtils();
            Assert.True(c.AddTableColumn("T", "Amount Due", FieldType.Amount, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(c.AddTableColumn("T", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.ExtractFromText("Amount Due    Amount\n     10.00     20.00\n", out _, out _, out m), m);
            Assert.Equal(new[] { "1:10.00|20.00" }, Rows(c, "T", "Amount Due", "Amount"));
            Assert.True(c.ExtractFromText("Amount Due\n     10.00\n", out _, out _, out m), m);                 // "Amount" inside "Amount Due" is not a second header
            Assert.Empty(Rows(c, "T", "Amount Due"));
        }

        [Fact]
        public void ACellUnderItsHeader_WinsOverAStrayMarkBeforeIt_AndIsNeverTakenByAnEmptyColumnBeforeIt()
        {
            using var c = new TextExtractUtils();
            foreach (string h in new[] { "Id", "Description", "Qty" })
                Assert.True(c.AddTableColumn("T", h, h == "Qty" ? FieldType.Integer : FieldType.Text, DecimalStyle.DotDecimal, "", out string m), m);
            const string text =
                "Id          Description        Qty\n" +
                "1   x       Blue widget        5\n" +          // a stray mark between Id and Description
                "          Red widget           6\n" +          // no Id; the description starts a little left of its header
                "3           Green  wid\n" +                    // a second piece inside Description's header is not moved into the empty Qty
                "4           Green  widget\n";                  // nor one that starts inside it and runs past its end
            Assert.True(c.ExtractFromText(text, out _, out _, out string msg), msg);
            Assert.Equal(new[] { "1:1|Blue widget|5", "2:(MissingValue)|Red widget|6", "3:3|Green|(MissingValue)", "4:4|Green|(MissingValue)" }, Rows(c, "T", "Id", "Description", "Qty"));
        }

        [Fact]
        public void APageFooter_IsARow_UnlessAFieldLabelEndsIt()
        {
            const string text =
                "Ref      Amount\n" +
                "R1       1.00\n" +
                "Page 1 of 2\n" +
                "Ref      Amount\n" +
                "R2       2.00\n";
            using var plain = new TextExtractUtils();
            Assert.True(plain.AddTableColumn("T", "Ref", FieldType.Code, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(plain.AddTableColumn("T", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(plain.ExtractFromText(text, out _, out _, out m), m);
            Assert.Equal(new[] { "1:R1|1.00", "2:Page|(MissingValue)", "3:R2|2.00" }, Rows(plain, "T", "Ref", "Amount"));   // no footer detection: the footer is a row (Ref is a Code, so it reads the first word)

            using var withFooterField = new TextExtractUtils();
            Assert.True(withFooterField.AddLabelField("Page", "Page", ValuePosition.SameLine, FieldType.Text, DecimalStyle.DotDecimal, "", false, Occurrence.First, out m), m);
            Assert.True(withFooterField.AddTableColumn("T", "Ref", FieldType.Code, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(withFooterField.AddTableColumn("T", "Amount", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(withFooterField.ExtractFromText(text, out _, out _, out m), m);
            Assert.Equal(new[] { "1:R1|1.00", "2:R2|2.00" }, Rows(withFooterField, "T", "Ref", "Amount"));
        }

        [Fact]
        public void HeadersAreMatchedLikeLabels_CaseSpacingAndOcrSlips()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddTableColumn("T", "Unit Price", FieldType.Amount, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(c.AddTableColumn("T", "Quantity", FieldType.Integer, DecimalStyle.DotDecimal, "", out m), m);
            Assert.True(c.ExtractFromText("UNIT   PRlCE     Quantlty\n  4.00             3\n", out _, out _, out m), m);
            Assert.Equal(new[] { "1:4.00|3" }, Rows(c, "T", "Unit Price", "Quantity"));
        }

        [Fact]
        public void AMissingHeader_GivesNoRows_AndTheJsonSaysWhy()
        {
            using var c = Lines();
            Assert.True(c.ExtractFromText("Invoice Number: INV-7\nItem   Qty\nA 1\n", out _, out _, out string m), m);   // not every header is on the line
            Assert.Empty(Rows(c, "Lines", "Item"));
            Assert.True(c.GetResultJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement t = doc.RootElement.GetProperty("tables")[0];
            Assert.False(t.GetProperty("found").GetBoolean());
            Assert.Equal("MissingLabel", t.GetProperty("reason").GetString());
            Assert.Equal(0, t.GetProperty("headerLine").GetInt32());
        }

        [Fact]
        public void MoreThanTenThousandRows_IsTooManyRows_WithNoRows()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddTableColumn("T", "N", FieldType.Integer, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(c.AddTableColumn("Other", "Never there", FieldType.Text, DecimalStyle.DotDecimal, "", out m), m);    // keeps the reader going past the limit
            string Text(int rows) => "N\n" + string.Concat(Enumerable.Range(0, rows).Select(i => i + "\n"));
            Assert.True(c.ExtractFromText(Text(10000), out _, out _, out m), m);
            Assert.True(c.GetResultJson(out string json, out m), m);
            Assert.Contains("\"rowCount\":10000", json);
            Assert.True(c.ExtractFromText(Text(10005), out _, out _, out m), m);                // rows after the limit do not start a new count
            Assert.True(c.GetResultJson(out json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement t = doc.RootElement.GetProperty("tables")[0];
            Assert.Equal(("TooManyRows", 0), (t.GetProperty("reason").GetString(), t.GetProperty("rowCount").GetInt32()));
            Assert.Empty(Rows(c, "T", "N"));
        }

        [Fact]
        public void TheRowCursor_IsPerTable_StaysExhausted_AndIsResetByANewExtraction()
        {
            using var c = Lines();
            Assert.True(c.AddTableColumn("Other", "Qty", FieldType.Integer, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(c.ExtractFromText(Invoice, out _, out _, out m), m);
            Assert.True(c.TryReadNextRow("Lines", out _, out int first, out _, out m), m);
            Assert.True(c.TryReadNextRow("Other", out _, out int otherFirst, out _, out m), m);
            Assert.Equal((1, 1), (first, otherFirst));                                      // each table has its own cursor
            Assert.Equal(2, Rows(c, "Lines", "Qty").Count);                                 // rows 2 and 3 remain
            Assert.True(c.TryReadNextRow("Lines", out bool again, out _, out _, out m));
            Assert.False(again);
            Assert.False(c.GetRowValue("Qty", out _, out _, out _, out _, out m));          // no current row once exhausted
            Assert.Contains("no current row", m);
            Assert.True(c.ExtractFromText(Invoice, out _, out _, out m), m);
            Assert.Equal(3, Rows(c, "Lines", "Qty").Count);
        }

        [Fact]
        public void TableReaders_FailCleanly()
        {
            using var c = Lines();
            Assert.False(c.TryReadNextRow("Lines", out _, out _, out _, out string m)); Assert.Contains("run ExtractFromText first", m);
            Assert.True(c.ExtractFromText(Invoice, out _, out _, out m), m);
            Assert.False(c.TryReadNextRow("Nope", out bool has, out int number, out string json, out m));
            Assert.Equal((false, 0, (string)null), (has, number, json));
            Assert.Contains("no table with that name", m);
            Assert.False(c.GetRowValue("Qty", out _, out _, out _, out _, out m)); Assert.Contains("no current row", m);
            Assert.True(c.TryReadNextRow("Lines", out _, out _, out _, out m), m);
            Assert.False(c.GetRowValue("Nope", out bool found, out string value, out _, out _, out m));
            Assert.Equal((false, (string)null), (found, value));
            Assert.Contains("no column with that header", m);
        }

        [Fact]
        public void TableOnlyTemplates_Work_AndTablesDoNotChangeTheFieldCounts()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddTableColumn("T", "N", FieldType.Integer, DecimalStyle.DotDecimal, "", out string m), m);
            Assert.True(c.ExtractFromText("N\n1\n2\n", out int found, out int missing, out m), m);
            Assert.Equal((0, 0), (found, missing));
            Assert.Equal(new[] { "1:1", "2:2" }, Rows(c, "T", "N"));
        }

        // ------------------------------------------------------------------ template

        [Fact]
        public void TableColumns_AreValidated_AndTheTableNameSharesTheFieldNamespace()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Total", "Total", FieldType.Amount, out string m), m);
            Assert.False(c.AddTableColumn("total", "Qty", FieldType.Integer, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("DuplicateName", m);
            Assert.True(c.AddTableColumn("Lines", "Qty", FieldType.Integer, DecimalStyle.DotDecimal, "", out m), m);
            Assert.False(c.AddLabelFieldSimple("LINES", "L", FieldType.Text, out m)); Assert.Contains("DuplicateName", m);
            Assert.False(c.AddTableColumn("Lines", "qty", FieldType.Integer, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("already has a column", m);
            Assert.True(c.AddTableColumn("Lines", "Unit Price", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            Assert.False(c.AddTableColumn("Lines", "UNIT   price", FieldType.Amount, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("ignoring case and spacing", m);   // matched the same way
            Assert.False(c.AddTableColumn("lines", "Price", FieldType.Amount, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("same spelling", m);
            Assert.False(c.AddTableColumn("Lines", "A|B", FieldType.Text, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("InvalidLabel", m);
            Assert.False(c.AddTableColumn("Lines", "", FieldType.Text, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("InvalidLabel", m);
            Assert.False(c.AddTableColumn("Lines", "When", FieldType.Date, DecimalStyle.DotDecimal, "yyyy", out m)); Assert.Contains("InvalidFormat", m);
            Assert.False(c.AddTableColumn("Lines", "Price", (FieldType)42, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("UnknownEnumValue", m);
            Assert.False(c.AddTableColumn("", "Price", FieldType.Text, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("InvalidName", m);
            for (int i = 2; i < 50; i++) Assert.True(c.AddTableColumn("Lines", "C" + i, FieldType.Text, DecimalStyle.DotDecimal, "", out m), m);
            Assert.False(c.AddTableColumn("Lines", "C50", FieldType.Text, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("TooManyColumns", m);
            for (int t = 1; t < 20; t++) Assert.True(c.AddTableColumn("T" + t, "H", FieldType.Text, DecimalStyle.DotDecimal, "", out m), m);
            Assert.False(c.AddTableColumn("T20", "H", FieldType.Text, DecimalStyle.DotDecimal, "", out m)); Assert.Contains("TooManyTables", m);
        }

        [Fact]
        public void TablesRoundTripThroughJson_AndATemplateWithoutTablesKeepsItsPhaseOneText()
        {
            using var built = Lines();
            Assert.True(built.AddTableColumn("Lines", "Shipped", FieldType.Date, DecimalStyle.DotDecimal, "dd/MM/yyyy", out string m), m);
            Assert.True(built.GetTemplateJson(out string json, out m), m);
            Assert.Contains("\"tables\":[{\"name\":\"Lines\",\"columns\":[{\"header\":\"Item\",\"type\":\"Code\"}", json);
            Assert.Contains("{\"header\":\"Unit Price\",\"type\":\"Amount\",\"decimalStyle\":\"DotDecimal\"}", json);
            Assert.Contains("{\"header\":\"Shipped\",\"type\":\"Date\",\"dateFormats\":[\"dd/MM/yyyy\"]}", json);
            using var loaded = new TextExtractUtils();
            Assert.True(loaded.LoadTemplateJson(json, out m), m);
            Assert.True(loaded.GetTemplateJson(out string again, out m), m);
            Assert.Equal(json, again);

            using var plain = new TextExtractUtils();
            Assert.True(plain.AddLabelFieldSimple("A", "A", FieldType.Text, out m), m);
            Assert.True(plain.GetTemplateJson(out string phaseOne, out m), m);
            Assert.DoesNotContain("tables", phaseOne);
            Assert.True(plain.ExtractFromText("A 1", out _, out _, out m), m);
            Assert.True(plain.GetResultJson(out string result, out m), m);
            Assert.DoesNotContain("tables", result);
            Assert.True(plain.ClearTemplate(out m), m);
            Assert.True(built.ClearTemplate(out m), m);
            Assert.True(built.GetTemplateJson(out string cleared, out m), m);
            Assert.DoesNotContain("tables", cleared);
        }

        [Theory]
        [InlineData("{\"schemaVersion\":1,\"tables\":{}}", "tables", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"tables\":[{\"name\":\"T\"}]}", "tables[0].columns", "MissingProperty")]
        [InlineData("{\"schemaVersion\":1,\"tables\":[{\"name\":\"T\",\"columns\":[]}]}", "tables[0].columns", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"tables\":[{\"name\":\"T\",\"columns\":[{\"type\":\"Text\"}]}]}", "tables[0].columns[0].header", "MissingProperty")]
        [InlineData("{\"schemaVersion\":1,\"tables\":[{\"name\":\"T\",\"columns\":[{\"header\":\"A\"}]}]}", "tables[0].columns[0].type", "MissingProperty")]
        [InlineData("{\"schemaVersion\":1,\"tables\":[{\"name\":\"T\",\"columns\":[{\"header\":\"A\",\"type\":\"Text\",\"decimalStyle\":\"DotDecimal\"}]}]}", "tables[0].columns[0].decimalStyle", "UnknownProperty")]
        [InlineData("{\"schemaVersion\":1,\"tables\":[{\"name\":\"T\",\"columns\":[{\"header\":\"A\",\"type\":\"Text\"},{\"header\":\"a\",\"type\":\"Text\"}]}]}", "tables[0].columns[1].header", "DuplicateName")]
        [InlineData("{\"schemaVersion\":1,\"tables\":[{\"name\":\"T\",\"rows\":5,\"columns\":[{\"header\":\"A\",\"type\":\"Text\"}]}]}", "tables[0].rows", "UnknownProperty")]
        [InlineData("{\"schemaVersion\":1,\"fields\":[{\"name\":\"T\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\"}],\"tables\":[{\"name\":\"t\",\"columns\":[{\"header\":\"A\",\"type\":\"Text\"}]}]}", "tables[0].name", "DuplicateName")]
        public void BadTableJson_IsReportedAtItsPath(string json, string path, string code)
        {
            using var c = new TextExtractUtils();
            Assert.True(c.ValidateTemplateJson(json, out int errors, out string report, out string m), m);
            Assert.True(errors > 0);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.Contains(doc.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetProperty("path").GetString() == path && e.GetProperty("code").GetString() == code);
        }

        [Fact]
        public void AddingAColumn_DiscardsResults_LikeAnyTemplateChange()
        {
            using var c = Lines();
            Assert.True(c.ExtractFromText(Invoice, out _, out _, out string m), m);
            Assert.True(c.AddTableColumn("Lines", "Tax", FieldType.Amount, DecimalStyle.DotDecimal, "", out m), m);
            Assert.False(c.TryReadNextRow("Lines", out _, out _, out _, out m));
            Assert.Contains("no results", m);
        }

        [Fact]
        public void Cells_AreSplitAtTwoOrMoreSpaces()
        {
            Assert.Equal(new[] { (0, 5), (7, 17), (20, 22) }, TableReader.Cells("A-100  Red widget   10").ToArray());
        }
    }
}
