using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>
    /// The behaviors the Excel worksheet guide (Documentation/ExcelWorksheets.md) promises, on DataTables shaped like an Excel export: header-named columns,
    /// numeric cells, blank cells as DBNull, a totals row, text numbers and dates. (The DataTables here are built by hand: the export itself needs Robot Studio.)
    /// </summary>
    public sealed class ExcelWorksheetGuideTests
    {
        private static DataTable Sheet(params (string name, Type type)[] columns)
        {
            var t = new DataTable();
            foreach (var c in columns) t.Columns.Add(c.name, c.type);
            return t;
        }

        private static ReconciliationUtils Component()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Invoice", "/Invoice Number", "/Invoice No", out string m), m);      // header text with spaces is fine in a pointer
            Assert.True(c.AddDecimalComparison("Amount", "/Amount", "/Paid", "0.01", ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddTextComparison("Status", "/Status", "/Status", true, true, ComparisonNullPolicy.AllowBothNull, out m), m);
            return c;
        }

        private static DataTable Erp(params object[][] rows) { DataTable t = Sheet(("Invoice Number", typeof(string)), ("Amount", typeof(double)), ("Status", typeof(string))); foreach (object[] r in rows) t.Rows.Add(r); return t; }
        private static DataTable Bank(params object[][] rows) { DataTable t = Sheet(("Invoice No", typeof(string)), ("Paid", typeof(double)), ("Status", typeof(string))); foreach (object[] r in rows) t.Rows.Add(r); return t; }

        private static List<string> Exceptions(ReconciliationUtils c)
        {
            var list = new List<string>();
            while (c.TryReadNextException(out bool has, out _, out string kind, out string key, out int l, out int r, out string reason, out _, out _) && has)
                list.Add(kind + " " + (reason ?? "-") + " " + key + " " + l + "/" + r);
            return list;
        }

        [Fact]
        public void TheGuideFlow_TwoWorksheets_HeaderNamedColumns_DifferentNamesPerSide()
        {
            using var c = Component();
            DataTable erp = Erp(new object[] { "INV-1", 100.0, "Open" }, new object[] { "INV-2", 250.5, "Open" }, new object[] { "INV-3", 75.0, "Open" });
            DataTable bank = Bank(new object[] { "INV-1", 100.0, "open" }, new object[] { "INV-2", 250.0, "Open" }, new object[] { "INV-4", 10.0, "Open" });
            Assert.True(c.ReconcileDataTables(erp, bank, out int count, out string m), m);
            Assert.Equal(3, count);
            Assert.Equal(new[] { "Different DecimalMismatch [\"INV-2\"] 1/1", "OnlyLeft - [\"INV-3\"] 2/-1", "OnlyRight - [\"INV-4\"] -1/2" }, Exceptions(c));
            Assert.True(c.GetSummary(out _, out _, out int matched, out _, out _)); Assert.Equal(1, matched);
        }

        [Fact]
        public void ATotalsRow_LeftInTheExport_ShowsUpAsAnException_AndAFilteredExportRemovesIt()
        {
            using var c = Component();
            DataTable erp = Erp(new object[] { "INV-1", 100.0, "Open" }, new object[] { "INV-2", 50.0, "Open" }, new object[] { DBNull.Value, 150.0, DBNull.Value });   // Total row: no invoice number
            DataTable bank = Bank(new object[] { "INV-1", 100.0, "Open" }, new object[] { "INV-2", 50.0, "Open" });
            Assert.True(c.ReconcileDataTables(erp, bank, out int count, out string m), m);
            Assert.Equal(1, count);
            Assert.Equal(new[] { "InvalidRecord MissingKey  2/-1" }, Exceptions(c));
            Assert.True(c.GetSummaryJson(out string json, out m), m);
            Assert.Contains("\"invalidLeftRowCount\":1", json);                                  // counted as an invalid record, not as a missing invoice

            erp.Rows.RemoveAt(2);                                                                  // what a filtered export or a bounded range gives you
            Assert.True(c.ReconcileDataTables(erp, bank, out count, out m), m);
            Assert.Equal(0, count);

            // a totals row that carries a label in the key column is an ordinary unmatched record instead
            DataTable labelled = Erp(new object[] { "INV-1", 100.0, "Open" }, new object[] { "INV-2", 50.0, "Open" }, new object[] { "Total", 150.0, DBNull.Value });
            Assert.True(c.ReconcileDataTables(labelled, bank, out count, out m), m);
            Assert.Equal(new[] { "OnlyLeft - [\"Total\"] 2/-1" }, Exceptions(c));
        }

        [Fact]
        public void BlankCells_AreDBNull_ANullPolicyDecidesWhatTheyMean()
        {
            using var c = Component();
            DataTable erp = Erp(new object[] { "INV-1", 100.0, DBNull.Value }, new object[] { "INV-2", DBNull.Value, "Open" });
            DataTable bank = Bank(new object[] { "INV-1", 100.0, DBNull.Value }, new object[] { "INV-2", DBNull.Value, "Open" });
            Assert.True(c.ReconcileDataTables(erp, bank, out int count, out string m), m);
            Assert.Equal(1, count);                                                                // Status may be blank on both sides (AllowBothNull); a blank Amount is not allowed (RequireValue)
            Assert.Equal(new[] { "InvalidComparison NullNotAllowed [\"INV-2\"] 1/1" }, Exceptions(c));
        }

        [Fact]
        public void NumbersStoredAsText_ReadAsDecimalsWhenPlain_AndNotWhenTheyHaveSeparatorsOrSymbols()
        {
            using var c = Component();
            DataTable erp = Sheet(("Invoice Number", typeof(string)), ("Amount", typeof(string)), ("Status", typeof(string)));
            erp.Rows.Add("A", "1234.50", "x"); erp.Rows.Add("B", "1,234.50", "x"); erp.Rows.Add("C", "$10.00", "x"); erp.Rows.Add("D", " 5", "x");
            DataTable bank = Bank(new object[] { "A", 1234.5, "x" }, new object[] { "B", 1234.5, "x" }, new object[] { "C", 10.0, "x" }, new object[] { "D", 5.0, "x" });
            Assert.True(c.ReconcileDataTables(erp, bank, out int count, out string m), m);
            Assert.Equal(3, count);                                                                // only "1234.50" (plain text) compares; separators, symbols and spaces are InvalidDecimal
            Assert.Equal(new[] { "B", "C", "D" }, Exceptions(c).Select(e => e.Split(' ')[2].Trim('[', ']', '"')));
        }

        [Fact]
        public void ANumericKeyColumn_MatchesTheSameNumberStoredAsText()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Account", "/Account", "/Account", out string m), m);
            DataTable erp = Sheet(("Account", typeof(double))); erp.Rows.Add(10012345.0); erp.Rows.Add(7.0);
            DataTable bank = Sheet(("Account", typeof(string))); bank.Rows.Add("10012345"); bank.Rows.Add("7");
            Assert.True(c.ReconcileDataTables(erp, bank, out int count, out m), m);
            Assert.Equal(0, count);                                                                // a number and its text are the same key; a leading zero in text is not ("007" is not 7)
            bank.Rows[1]["Account"] = "007";
            Assert.True(c.ReconcileDataTables(erp, bank, out count, out m), m);
            Assert.Equal(2, count);
        }

        [Fact]
        public void DatesAreUnsupportedCells_ADateRuleNeedsTextInAnExplicitFormat()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Id", out string m), m);
            Assert.True(c.AddCalendarDateComparisonSimple("Due", "/Due", "/Due", "yyyy-MM-dd", "yyyy-MM-dd", out m), m);
            DataTable typed = Sheet(("Id", typeof(string)), ("Due", typeof(DateTime))); typed.Rows.Add("a", new DateTime(2026, 9, 30));
            DataTable text = Sheet(("Id", typeof(string)), ("Due", typeof(string))); text.Rows.Add("a", "2026-09-30");
            Assert.True(c.ReconcileDataTables(typed, text, out int count, out m), m);
            Assert.Equal(1, count);
            Assert.Equal(new[] { "InvalidComparison InvalidType [\"a\"] 0/0" }, Exceptions(c));
            Assert.True(c.ReconcileDataTables(text, text, out count, out m), m);                  // text dates work
            Assert.Equal(0, count);
        }

        [Fact]
        public void AHeaderThatDoesNotMatchExactly_IsAMissingColumn_NotAFailure_SoCheckTheCounts()
        {
            using var c = Component();
            DataTable erp = Erp(new object[] { "INV-1", 100.0, "Open" });
            erp.Columns["Invoice Number"].ColumnName = "Invoice number";                           // one letter of case
            DataTable bank = Bank(new object[] { "INV-1", 100.0, "Open" });
            Assert.True(c.ReconcileDataTables(erp, bank, out int count, out string m), m);
            Assert.Null(m);
            Assert.Equal(2, count);                                                                // the left row has no readable key, and the right row therefore has no partner
            Assert.True(c.GetSummaryJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("invalidLeftRowCount").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("onlyRightCount").GetInt32());
            Assert.False(doc.RootElement.GetProperty("allMatched").GetBoolean());
        }

        [Fact]
        public void TheWorksheetsAreOnlyRead_AndDuplicateInvoiceRowsAreReportedNotGuessed()
        {
            using var c = Component();
            DataTable erp = Erp(new object[] { "INV-1", 10.0, "Open" }, new object[] { "INV-1", 20.0, "Open" });
            DataTable bank = Bank(new object[] { "INV-1", 30.0, "Open" });
            string Snapshot(DataTable t) => string.Join("|", t.Rows.Cast<DataRow>().Select(r => string.Join(",", r.ItemArray)));
            string before = Snapshot(erp) + Snapshot(bank);
            Assert.True(c.ReconcileDataTables(erp, bank, out int count, out string m), m);
            Assert.Equal(1, count);
            Assert.Equal(new[] { "DuplicateKey DuplicateNormalizedKey [\"INV-1\"] -1/0" }, Exceptions(c));   // a group: several left rows, one right row
            Assert.Equal(before, Snapshot(erp) + Snapshot(bank));
        }
    }
}
