using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class ReconcileDataTableTests
    {
        private static DataTable Table(params (string name, Type type)[] columns)
        {
            var t = new DataTable();
            foreach (var c in columns) t.Columns.Add(c.name, c.type);
            return t;
        }

        private static DataTable Invoices(params object[][] rows)
        {
            DataTable t = Table(("Invoice", typeof(string)), ("Amount", typeof(decimal)), ("Status", typeof(string)));
            foreach (object[] r in rows) t.Rows.Add(r);
            return t;
        }

        private static ReconciliationUtils Component()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Invoice", "/Invoice", "/Invoice", out string m), m);
            Assert.True(c.AddDecimalComparisonSimple("Amount", "/Amount", "/Amount", out m), m);
            Assert.True(c.AddTextComparison("Status", "/Status", "/Status", true, true, ComparisonNullPolicy.RequireValue, out m), m);
            return c;
        }

        private static List<string> Exceptions(ReconciliationUtils c)
        {
            var list = new List<string>();
            while (true)
            {
                Assert.True(c.TryReadNextException(out bool has, out _, out string kind, out string key, out int l, out int r, out _, out int d, out string m), m);
                if (!has) return list;
                list.Add(kind + " " + key + " " + l + "/" + r + " d" + d);
            }
        }

        [Fact]
        public void TwoTables_ReconcileWithTheSameResultsAsTheEquivalentJson()
        {
            using var c = Component();
            DataTable left = Invoices(new object[] { "INV-100", 10.00m, "Open" }, new object[] { "INV-101", 20.00m, "Open" }, new object[] { "INV-102", 5m, "Open" });
            DataTable right = Invoices(new object[] { "INV-100", 10.00m, "open" }, new object[] { "INV-101", 21.50m, "Open" }, new object[] { "INV-103", 9m, "Open" });
            Assert.True(c.ReconcileDataTables(left, right, out int count, out string m), m);
            Assert.Equal(3, count);
            Assert.True(c.GetSummary(out int l, out int r, out int matched, out int exc, out m), m);
            Assert.Equal((3, 3, 1, 3), (l, r, matched, exc));
            List<string> fromTables = Exceptions(c);
            Assert.Equal(new[] { "Different [\"INV-101\"] 1/1 d1", "OnlyLeft [\"INV-102\"] 2/-1 d0", "OnlyRight [\"INV-103\"] -1/2 d0" }, fromTables);

            using var json = Component();
            Assert.True(json.ReconcileJson("[{\"Invoice\":\"INV-100\",\"Amount\":10.00,\"Status\":\"Open\"},{\"Invoice\":\"INV-101\",\"Amount\":20.00,\"Status\":\"Open\"},{\"Invoice\":\"INV-102\",\"Amount\":5,\"Status\":\"Open\"}]",
                "[{\"Invoice\":\"INV-100\",\"Amount\":10.00,\"Status\":\"open\"},{\"Invoice\":\"INV-101\",\"Amount\":21.50,\"Status\":\"Open\"},{\"Invoice\":\"INV-103\",\"Amount\":9,\"Status\":\"Open\"}]", out _, out m), m);
            Assert.Equal(Exceptions(json), fromTables);
            Assert.True(c.GetSummaryJson(out string a, out _)); Assert.True(json.GetSummaryJson(out string b, out _));
            Assert.Equal(b, a);
        }

        [Fact]
        public void ColumnNamesCanDifferPerSide_AndAPointerEscapeNamesAColumnWithASlash()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Ref/No", out string m), m);      // two segments on the right: not a column
            Assert.False(c.ReconcileDataTables(Table(("Id", typeof(string))), Table(("Ref/No", typeof(string))), out _, out m));
            Assert.Contains("exactly one column", m);

            Assert.True(c.ClearDefinition(out m));
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Ref~1No", out m), m);          // ~1 is a slash inside one name
            DataTable left = Table(("Id", typeof(string))); left.Rows.Add("a");
            DataTable right = Table(("Ref/No", typeof(string))); right.Rows.Add("a");
            Assert.True(c.ReconcileDataTables(left, right, out int count, out m), m);
            Assert.Equal(0, count);
        }

        [Fact]
        public void AMissingCompareColumn_IsAMissingFieldOnEveryPair_LikeAMissingJsonProperty()
        {
            using var c = Component();
            DataTable left = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" });
            DataTable noStatus = Table(("Invoice", typeof(string)), ("Amount", typeof(decimal)));
            noStatus.Rows.Add("A", 1m); noStatus.Rows.Add("B", 2m);
            Assert.True(c.ReconcileDataTables(left, noStatus, out int count, out string m), m);
            Assert.Null(m);
            Assert.Equal(2, count);                                                       // both pairs: the Status rule cannot be evaluated on the right
            Assert.True(c.TryReadNextException(out _, out _, out string kind, out _, out _, out _, out string reason, out int differences, out _));
            Assert.Equal(("InvalidComparison", "MissingField", 1), (kind, reason, differences));
            Assert.True(c.TryReadNextDifference(out _, out string rule, out _, out string leftValue, out string rightValue, out _, out _));
            Assert.Equal("Status", rule);
            Assert.Equal("\"x\"", leftValue);
            Assert.Null(rightValue);                                                      // missing, not null
        }

        [Fact]
        public void AMissingKeyColumn_MakesEveryRowOnThatSideAnInvalidRecord_NotOnlyLeftOrOnlyRight()
        {
            using var c = Component();
            DataTable good = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" });
            DataTable noKey = Table(("Amount", typeof(decimal)), ("Status", typeof(string)));
            noKey.Rows.Add(1m, "x"); noKey.Rows.Add(2m, "x"); noKey.Rows.Add(3m, "x");
            Assert.True(c.ReconcileDataTables(good, noKey, out int count, out string m), m);
            Assert.Equal(5, count);                                                       // 3 unkeyable right rows + 2 left rows with no right counterpart
            Assert.True(c.GetSummary(out _, out _, out int matched, out _, out _));
            Assert.Equal(0, matched);
            Assert.True(c.GetSummaryJson(out string json, out m));
            Assert.Contains("\"invalidRightRowCount\":3", json);
            Assert.Contains("\"invalidLeftRowCount\":0", json);
            Assert.Contains("\"onlyLeftCount\":2", json);
            Assert.Contains("\"onlyRightCount\":0", json);                             // an unkeyable row is never claimed to be "only right"
            var kinds = new List<string>();
            while (c.TryReadNextException(out bool has, out _, out string kind, out _, out _, out _, out string reason, out _, out _) && has) kinds.Add(kind + ":" + reason);
            Assert.Equal(new[] { "OnlyLeft:", "OnlyLeft:", "InvalidRecord:MissingKey", "InvalidRecord:MissingKey", "InvalidRecord:MissingKey" }, kinds);

            // column names are case-sensitive: a column called "invoice" is not the key column "Invoice"
            DataTable lower = Table(("invoice", typeof(string))); lower.Rows.Add("A");
            Assert.True(c.ReconcileDataTables(lower, good, out count, out m), m);
            Assert.True(c.GetSummaryJson(out json, out m));
            Assert.Contains("\"invalidLeftRowCount\":1", json);
            Assert.Contains("\"onlyRightCount\":2", json);
        }

        [Fact]
        public void NullTables_DisposedComponent_AndNoKey_AreOrdinaryFailures()
        {
            using var c = Component();
            Assert.False(c.ReconcileDataTables(null, Invoices(), out _, out string m)); Assert.Contains("Left table is null", m);
            Assert.False(c.ReconcileDataTables(Invoices(), null, out _, out m)); Assert.Contains("Right table is null", m);
            using var empty = new ReconciliationUtils();
            Assert.False(empty.ReconcileDataTables(Invoices(), Invoices(), out _, out m)); Assert.Contains("no key mapping", m);
            var gone = Component(); gone.Dispose();
            Assert.False(gone.ReconcileDataTables(Invoices(), Invoices(), out _, out m)); Assert.Contains("disposed", m);
        }

        [Fact]
        public void EmptyTables_ReconcileToAnEmptyRun()
        {
            using var c = Component();
            Assert.True(c.ReconcileDataTables(Invoices(), Invoices(), out int count, out string m), m);
            Assert.Equal(0, count);
            Assert.True(c.GetSummaryJson(out string json, out m));
            Assert.Contains("\"bothInputsEmpty\":true", json);
        }

        // ---------------------------------------------------------------- value mapping

        private static (FieldKind kind, string text) Map(object value) { FieldValue f = DataTableInput.Map(value); return (f.Kind, f.Text); }

        [Fact]
        public void ValueMapping_FollowsThePlan()
        {
            Assert.Equal((FieldKind.Null, null), Map(DBNull.Value));
            Assert.Equal((FieldKind.String, "abc"), Map("abc"));
            Assert.Equal((FieldKind.String, ""), Map(""));
            Assert.Equal((FieldKind.Boolean, "true"), Map(true));
            Assert.Equal((FieldKind.Boolean, "false"), Map(false));
            foreach (object v in new object[] { (sbyte)-5, (byte)5, (short)-300, (ushort)300, -70000, 70000u, -5000000000L, 18446744073709551615UL })
                Assert.Equal(FieldKind.Integer, Map(v).kind);
            Assert.Equal((FieldKind.Integer, "18446744073709551615"), Map(18446744073709551615UL));
            Assert.Equal((FieldKind.Integer, "-5"), Map((sbyte)-5));
            Assert.Equal((FieldKind.Number, "10.00"), Map(10.00m));               // scale is kept: a decimal is exactly what it says
            Assert.Equal((FieldKind.Integer, "10"), Map(10m));
            Assert.Equal((FieldKind.Number, "0.1"), Map(0.1));                    // shortest text that reads back as the same double
            Assert.Equal((FieldKind.Number, "0.1"), Map(0.1f));                   // and the same for a float (not 0.10000000149...)
            Assert.Equal((FieldKind.Integer, "3"), Map(3.0));
            Assert.Equal((FieldKind.Number, "1E-05"), Map(0.00001));
        }

        [Fact]
        public void ValueMapping_RefusesWhatItCannotRepresent_AndWhatIsNotSupported()
        {
            foreach (object v in new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, float.NaN, float.PositiveInfinity })
                Assert.Equal(FieldKind.Unsupported, Map(v).kind);
            foreach (object v in new object[] { DateTime.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), TimeSpan.FromSeconds(1), 'c', new byte[] { 1 }, new object() })
                Assert.Equal(FieldKind.Unsupported, Map(v).kind);
            Assert.Equal(FieldKind.Unsupported, Map("bad \ud800 text").kind);       // a lone surrogate, as in JSON input
            Assert.DoesNotContain("bad", Map("bad \ud800 text").text);              // the description never quotes the value
        }

        [Fact]
        public void EachColumnType_FlowsThroughTheRules()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Id", out string m), m);
            Assert.True(c.AddDecimalComparison("Amount", "/Amount", "/Amount", "0", ComparisonNullPolicy.AllowBothNull, out m), m);
            (Type, object, object, string)[] cases =
            {
                (typeof(int), 5, 5, null), (typeof(long), 5L, 6L, "DecimalMismatch"), (typeof(decimal), 1.5m, 1.50m, null), (typeof(double), 0.1, 0.1, null),
                (typeof(float), 0.25f, 0.5f, "DecimalMismatch"), (typeof(string), "12.5", "12.50", null), (typeof(byte), (byte)7, (byte)7, null), (typeof(bool), true, true, "InvalidType"),
                (typeof(DateTime), DateTime.UnixEpoch, DateTime.UnixEpoch, "InvalidType"), (typeof(string), "abc", "abc", "InvalidDecimal")
            };
            foreach ((Type type, object l, object r, string reason) in cases)
            {
                DataTable left = Table(("Id", typeof(string)), ("Amount", type)); left.Rows.Add("k", l);
                DataTable right = Table(("Id", typeof(string)), ("Amount", type)); right.Rows.Add("k", r);
                Assert.True(c.ReconcileDataTables(left, right, out int count, out m), type.Name + ": " + m);
                Assert.Equal(reason == null ? 0 : 1, count);
                if (reason != null)
                {
                    Assert.True(c.TryReadNextException(out _, out _, out _, out _, out _, out _, out string code, out _, out _));
                    Assert.Equal(reason, code);
                }
            }
        }

        [Fact]
        public void DBNull_IsNull_UnderTheNullPolicy()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Id", out string m), m);
            Assert.True(c.AddTextComparison("Note", "/Note", "/Note", false, false, ComparisonNullPolicy.AllowBothNull, out m), m);
            DataTable left = Table(("Id", typeof(string)), ("Note", typeof(string))); left.Rows.Add("a", DBNull.Value); left.Rows.Add("b", DBNull.Value); left.Rows.Add("c", "");
            DataTable right = Table(("Id", typeof(string)), ("Note", typeof(string))); right.Rows.Add("a", DBNull.Value); right.Rows.Add("b", "x"); right.Rows.Add("c", DBNull.Value);
            Assert.True(c.ReconcileDataTables(left, right, out int count, out m), m);
            Assert.Equal(2, count);                                                   // a matches (both null); b (null vs value) and c (empty vs null) differ
            Assert.Equal(new[] { "Different [\"b\"] 1/1 d1", "Different [\"c\"] 2/2 d1" }, Exceptions(c));
            Assert.True(c.ClearResults(out m));

            Assert.True(c.AddTextComparison("Note2", "/Note", "/Note", false, false, ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.ReconcileDataTables(left, right, out count, out m), m);
            Assert.True(count >= 1);
            Assert.True(c.TryReadNextException(out _, out _, out string kind, out _, out _, out _, out _, out _, out _));
            Assert.Equal("InvalidComparison", kind);                                  // RequireValue: a null is invalid, not different
        }

        [Fact]
        public void IntegerKeys_MatchTheirTextForm_LikeJsonIntegers()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/Id", "/Id", out string m), m);
            DataTable left = Table(("Id", typeof(int))); left.Rows.Add(123);
            DataTable right = Table(("Id", typeof(string))); right.Rows.Add("123");
            Assert.True(c.ReconcileDataTables(left, right, out int count, out m), m);
            Assert.Equal(0, count);                                                   // 123 and "123" are the same key
            DataTable fractional = Table(("Id", typeof(double))); fractional.Rows.Add(1.5);
            Assert.True(c.ReconcileDataTables(fractional, right, out count, out m), m);
            Assert.True(c.TryReadNextException(out _, out _, out string kind, out _, out _, out _, out string reason, out _, out _));
            Assert.Equal(("InvalidRecord", "InvalidKeyType"), (kind, reason));
        }

        [Fact]
        public void DeletedRows_AreNotPartOfTheData_AndTheTablesAreNeverModified()
        {
            using var c = Component();
            DataTable left = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" }, new object[] { "C", 3m, "x" });
            left.AcceptChanges();
            left.Rows[1].Delete();
            DataTable right = Invoices(new object[] { "A", 1m, "x" }, new object[] { "C", 3m, "x" });
            string Snapshot(DataTable t) => string.Join("|", t.Rows.Cast<DataRow>().Select(r => r.RowState + ":" + (r.RowState == DataRowState.Deleted ? "-" : string.Join(",", r.ItemArray))));
            string beforeLeft = Snapshot(left), beforeRight = Snapshot(right);
            Assert.True(c.ReconcileDataTables(left, right, out int count, out string m), m);
            Assert.Equal(0, count);                                                    // B was deleted, so it is not "only left"
            Assert.Equal(beforeLeft, Snapshot(left));
            Assert.Equal(beforeRight, Snapshot(right));
            Assert.Equal(DataRowState.Deleted, left.Rows[1].RowState);
        }

        // ---------------------------------------------------------------- limits

        [Fact]
        public void TableLimits_AreValidated_AndTheDefaultsAndMaximumsAreThePlans()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.ConfigureTableLimits(1000, 10000000, 1000000, out string m), m);
            Assert.False(c.ConfigureTableLimits(1001, 10000000, 1000000, out m)); Assert.Contains("maximumColumns", m);
            Assert.False(c.ConfigureTableLimits(1000, 10000001, 1000000, out m)); Assert.Contains("maximumCells", m);
            Assert.False(c.ConfigureTableLimits(1000, 10000000, 1000001, out m)); Assert.Contains("maximumValueCharacters", m);
            Assert.False(c.ConfigureTableLimits(0, 1, 1, out _)); Assert.False(c.ConfigureTableLimits(1, 0, 1, out _)); Assert.False(c.ConfigureTableLimits(1, 1, 0, out _));
            Assert.False(c.ConfigureTableLimits(-1, -1, -1, out _));
            var defaults = new TableLimits();
            Assert.Equal((100, 2000000, 4096), (defaults.MaximumColumns, defaults.MaximumCells, defaults.MaximumValueCharacters));
        }

        [Fact]
        public void ColumnLimit_IsCheckedAtAndBeyondTheBoundary_BeforeAnyValueIsRead()
        {
            using var c = Component();
            Assert.True(c.ConfigureTableLimits(3, 1000, 100, out string m), m);
            DataTable atLimit = Invoices();                                            // exactly 3 columns
            Assert.True(c.ReconcileDataTables(atLimit, atLimit, out _, out m), m);
            DataTable over = Invoices(); over.Columns.Add("Extra", typeof(string));   // 4 columns; the extra one is not even referenced
            Assert.False(c.ReconcileDataTables(atLimit, over, out _, out m));
            Assert.Contains("Right table has more than 3 columns", m);
            Assert.Contains("ConfigureTableLimits", m);
        }

        [Fact]
        public void CellLimit_ManyColumnsFewRows_AndFewColumnsManyRows_AreBothStoppedWithoutOverflow()
        {
            using var c = Component();
            Assert.True(c.ConfigureLimits(250000, 32000000, 500000, 500000, out string m), m);
            Assert.True(c.ConfigureTableLimits(1000, 6, 100, out m), m);
            DataTable two = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" });         // 2 rows x 3 columns = 6 cells: at the limit
            Assert.True(c.ReconcileDataTables(two, two, out _, out m), m);
            DataTable three = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" }, new object[] { "C", 3m, "x" });   // 9 cells
            Assert.False(c.ReconcileDataTables(three, two, out _, out m)); Assert.Contains("Left table has more than 6 cells", m);
            Assert.False(c.ReconcileDataTables(two, three, out _, out m)); Assert.Contains("Right table has more than 6 cells", m);

            Assert.True(c.ConfigureTableLimits(1000, 10000000, 100, out m), m);
            DataTable wide = Invoices(new object[] { "A", 1m, "x" });                                        // one row, 1,000 columns: 1,000 cells
            for (int i = 0; i < 997; i++) wide.Columns.Add("c" + i, typeof(string));
            Assert.True(c.ReconcileDataTables(wide, wide, out _, out m), m);

            // many rows AND many columns together: 20,000 x 1,000 = 20,000,000 cells, beyond the 10,000,000 maximum (the product is computed as a long; with the row and column maximums it cannot overflow an int)
            Assert.True(c.ConfigureTableLimits(1000, 10000000, 100, out m), m);
            DataTable tall = Invoices(); for (int i = 0; i < 997; i++) tall.Columns.Add("c" + i, typeof(string));
            tall.BeginLoadData(); for (int i = 0; i < 20000; i++) tall.LoadDataRow(new object[] { "k" + i, 1m, "x" }, true); tall.EndLoadData();
            Assert.False(c.ReconcileDataTables(tall, wide, out _, out m)); Assert.Contains("cells", m);       // 20,000 x 1,000 = 20,000,000 > 10,000,000
        }

        [Fact]
        public void RowLimit_UsesConfigureLimits_AtAndBeyondTheBoundary()
        {
            using var c = Component();
            Assert.True(c.ConfigureLimits(2, 1000000, 1000, 1000, out string m), m);
            DataTable two = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" });
            DataTable three = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" }, new object[] { "C", 3m, "x" });
            Assert.True(c.ReconcileDataTables(two, two, out _, out m), m);
            Assert.False(c.ReconcileDataTables(three, two, out _, out m)); Assert.Contains("Left table has more than 2 rows", m);
            Assert.False(c.ReconcileDataTables(two, three, out _, out m)); Assert.Contains("Right table has more than 2 rows", m);
        }

        [Fact]
        public void ValueLimit_FewRowsHugeStrings_IsCheckedAtAndBeyondTheBoundary_WithoutQuotingTheValue()
        {
            using var c = Component();
            Assert.True(c.ConfigureTableLimits(100, 1000, 10, out string m), m);
            DataTable ok = Invoices(new object[] { "0123456789", 1m, "x" });             // a 10-character key: at the limit
            Assert.True(c.ReconcileDataTables(ok, ok, out _, out m), m);
            DataTable big = Invoices(new object[] { "0123456789A", 1m, "SECRETSECRETSECRET" });
            Assert.False(c.ReconcileDataTables(ok, big, out _, out m));
            Assert.Contains("Right table row 0 column 'Invoice' holds a text value longer than 10 characters", m);
            Assert.DoesNotContain("0123456789A", m);
            DataTable unreferenced = Invoices(); unreferenced.Columns.Add("Notes", typeof(string)); unreferenced.Rows.Add("K", 1m, "x", new string('z', 5000));
            Assert.True(c.ReconcileDataTables(unreferenced, unreferenced, out _, out m), m);   // only referenced columns are read, so an unreferenced huge value is fine
        }

        [Fact]
        public void TotalTextLimit_UsesMaximumInputCharactersPerSide()
        {
            using var c = Component();
            Assert.True(c.ConfigureLimits(1000, 30, 1000, 1000, out string m), m);
            Assert.True(c.ConfigureTableLimits(100, 1000, 100, out m), m);
            // each row: key (1) + status (9) = 10 characters of text in referenced columns; 3 rows = 30: at the limit
            DataTable thirty = Invoices(new object[] { "a", 1m, "123456789" }, new object[] { "b", 1m, "123456789" }, new object[] { "c", 1m, "123456789" });
            Assert.True(c.ReconcileDataTables(thirty, thirty, out _, out m), m);
            DataTable more = Invoices(new object[] { "a", 1m, "123456789" }, new object[] { "b", 1m, "123456789" }, new object[] { "c", 1m, "1234567890" });
            Assert.False(c.ReconcileDataTables(thirty, more, out _, out m));
            Assert.Contains("Right table holds more than 30 characters of text", m);
        }

        [Fact]
        public void ATableLimitChangeAndClearDefinition_DiscardResults_AndClearDefinitionRestoresTheDefaults()
        {
            using var c = Component();
            Assert.True(c.ReconcileDataTables(Invoices(), Invoices(), out _, out string m), m);
            Assert.True(c.ConfigureTableLimits(2, 10, 5, out m), m);
            Assert.False(c.GetSummary(out _, out _, out _, out _, out _));           // a setup change discards results
            Assert.False(c.ConfigureTableLimits(0, 1, 1, out _));                   // rejected changes change nothing
            Assert.False(c.ReconcileDataTables(Invoices(), Invoices(), out _, out m)); Assert.Contains("more than 2 columns", m);

            Assert.True(c.ClearDefinition(out m), m);
            Assert.True(c.AddKeyMappingSimple("Invoice", "/Invoice", "/Invoice", out m), m);
            Assert.True(c.ReconcileDataTables(Invoices(), Invoices(), out _, out m), m);   // back to 100 columns
            Assert.True(c.ConfigureTableLimits(2, 10, 5, out m), m);
            Assert.True(c.LoadDefinitionJson("{\"schemaVersion\":1,\"keys\":[{\"name\":\"K\",\"leftPointer\":\"/Invoice\",\"rightPointer\":\"/Invoice\"}]}", out m), m);
            Assert.False(c.ReconcileDataTables(Invoices(), Invoices(), out _, out m)); Assert.Contains("more than 2 columns", m);   // loading a definition keeps the table limits
        }

        [Fact]
        public void ARunReadsASnapshot_SoChangingTheTablesLaterNeverChangesPublishedResults()
        {
            using var c = Component();
            DataTable left = Invoices(new object[] { "A", 1m, "x" });
            DataTable right = Invoices(new object[] { "A", 2m, "x" });
            Assert.True(c.ReconcileDataTables(left, right, out int count, out string m), m);
            Assert.Equal(1, count);
            right.Rows[0]["Amount"] = 1m; left.Rows.Clear();
            Assert.True(c.GetResultJson("r000001", out string json, out m), m);
            Assert.Contains("\"Different\"", json);
            Assert.Contains("\"1\"", json.Replace("1.0", "1"));
        }

        [Fact]
        public void ConcurrentRunsAndSetupCalls_AreSerialized()
        {
            using var c = Component();
            DataTable left = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 2m, "x" });
            DataTable right = Invoices(new object[] { "A", 1m, "x" }, new object[] { "B", 3m, "x" });
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var run = System.Threading.Tasks.Task.Run(() => { for (int i = 0; i < 200; i++) if (!c.ReconcileDataTables(left, right, out int n, out string m) || n != 1) errors.Enqueue(m ?? "wrong count"); });
            var setup = System.Threading.Tasks.Task.Run(() => { for (int i = 0; i < 200; i++) c.ConfigureTableLimits(100, 2000000, 4096, out _); });
            System.Threading.Tasks.Task.WaitAll(run, setup);
            Assert.Empty(errors);
        }
    }
}
