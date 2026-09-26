using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace ReconciliationAutomation
{
    /// <summary>The DataTable limits (Release 2). They bound one table before, and while, its values are read.</summary>
    internal sealed class TableLimits
    {
        internal const int DefaultColumns = 100;
        internal const int MaxColumns = 1000;
        internal const int DefaultCells = 2000000;
        internal const int MaxCells = 10000000;
        internal const int DefaultValueCharacters = 4096;
        internal const int MaxValueCharacters = 1000000;

        internal int MaximumColumns = DefaultColumns;
        internal int MaximumCells = DefaultCells;
        internal int MaximumValueCharacters = DefaultValueCharacters;

        /// <summary>The first problem with these limits, or null when all three are acceptable.</summary>
        internal string FirstProblem() =>
            ReconciliationLimits.Check("maximumColumns", MaximumColumns, MaxColumns)
            ?? ReconciliationLimits.Check("maximumCells", MaximumCells, MaxCells)
            ?? ReconciliationLimits.Check("maximumValueCharacters", MaximumValueCharacters, MaxValueCharacters);
    }

    /// <summary>
    /// One DataTable read into memory as field values. The columns the definition references are read once, up front, under the limits, so a
    /// table that is too large fails before anything is compared, and later changes to the caller's table cannot reach a run in progress.
    /// The table itself is never modified.
    /// </summary>
    internal sealed class DataTableInput : IRowSource
    {
        private readonly FieldValue[][] rows;                       // [row][slot]
        private readonly Dictionary<string, int> slotByColumn;

        private DataTableInput(FieldValue[][] rows, Dictionary<string, int> slotByColumn)
        {
            this.rows = rows;
            this.slotByColumn = slotByColumn;
        }

        public int RowCount => rows.Length;

        public IRowReader RowAt(int index) => new DataTableRowReader(rows[index], slotByColumn);

        /// <summary>
        /// Reads <paramref name="table"/>. The checks run in a fixed order and before any value is interpreted: rows, columns, cells, then per value
        /// and per table while reading. A failure names the side and the limit, never a value.
        /// </summary>
        internal static bool TryRead(DataTable table, string side, IEnumerable<string[]> pointers, ReconciliationLimits limits, TableLimits tableLimits, out DataTableInput input, out string failure)
        {
            input = null;
            failure = null;
            if (table == null) { failure = side + " table is null."; return false; }

            int rowCount = table.Rows.Count;
            if (rowCount > limits.MaximumRowsPerSide) { failure = side + " table has more than " + limits.MaximumRowsPerSide + " rows (the limit is set with ConfigureLimits)."; return false; }

            int columnCount = table.Columns.Count;
            if (columnCount > tableLimits.MaximumColumns) { failure = side + " table has more than " + tableLimits.MaximumColumns + " columns (the limit is set with ConfigureTableLimits)."; return false; }

            if ((long)rowCount * columnCount > tableLimits.MaximumCells) { failure = side + " table has more than " + tableLimits.MaximumCells + " cells (rows x columns; the limit is set with ConfigureTableLimits)."; return false; }

            var columnByName = new Dictionary<string, DataColumn>(StringComparer.Ordinal);
            foreach (DataColumn column in table.Columns) columnByName[column.ColumnName] = column;

            // Only the columns the definition references are read, each once.
            var slotByColumn = new Dictionary<string, int>(StringComparer.Ordinal);
            var used = new List<DataColumn>();
            foreach (string[] segments in pointers)
            {
                string name = segments[0];
                if (slotByColumn.ContainsKey(name)) continue;
                if (!columnByName.TryGetValue(name, out DataColumn found)) continue;   // no such column: every row reads it as Missing, exactly like a missing JSON property
                slotByColumn[name] = used.Count;
                used.Add(found);
            }

            var result = new List<FieldValue[]>(rowCount);
            long characters = 0;
            long characterLimit = limits.MaximumInputCharactersPerSide;
            foreach (DataRow row in table.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;   // a deleted row is no longer part of the table's data (and cannot be read)
                var values = new FieldValue[used.Count];
                for (int slot = 0; slot < values.Length; slot++)
                {
                    object raw = row[used[slot]];
                    if (raw is string text)
                    {
                        if (text.Length > tableLimits.MaximumValueCharacters) { failure = side + " table row " + result.Count + " column '" + used[slot].ColumnName + "' holds a text value longer than " + tableLimits.MaximumValueCharacters + " characters (the limit is set with ConfigureTableLimits)."; return false; }
                        characters += text.Length;
                        if (characters > characterLimit) { failure = side + " table holds more than " + characterLimit + " characters of text in the referenced columns (the limit is set with ConfigureLimits)."; return false; }
                    }
                    values[slot] = Map(raw);
                }
                result.Add(values);
            }
            input = new DataTableInput(result.ToArray(), slotByColumn);
            return true;
        }

        /// <summary>Maps one cell: <c>DBNull</c> is null; text is text; integral types are integers; decimal, double and float are number text; bool is Boolean; anything else is unsupported.</summary>
        internal static FieldValue Map(object value)
        {
            switch (value)
            {
                case null:
                case DBNull _:
                    return FieldValue.Null;
                case string text:
                    return TextCheck.HasUnpairedSurrogate(text) ? FieldValue.UnsupportedBecause("text that is not valid") : new FieldValue(FieldKind.String, text);
                case bool flag:
                    return new FieldValue(FieldKind.Boolean, flag ? "true" : "false");
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                    return new FieldValue(FieldKind.Integer, ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                case decimal number:
                    return Number(number.ToString(CultureInfo.InvariantCulture));
                case double number:
                    return double.IsFinite(number) ? Number(number.ToString("R", CultureInfo.InvariantCulture)) : FieldValue.UnsupportedBecause("a number that is not finite");
                case float number:
                    return float.IsFinite(number) ? Number(number.ToString("R", CultureInfo.InvariantCulture)) : FieldValue.UnsupportedBecause("a number that is not finite");
                default:
                    return FieldValue.UnsupportedBecause("a value of an unsupported column type");
            }
        }

        private static FieldValue Number(string token) =>
            new FieldValue(JsonRowReader.IsIntegerToken(token) ? FieldKind.Integer : FieldKind.Number, token);
    }

    /// <summary>Reads fields from one already-read table row. A pointer names exactly one column.</summary>
    internal sealed class DataTableRowReader : IRowReader
    {
        private readonly FieldValue[] values;
        private readonly Dictionary<string, int> slotByColumn;

        internal DataTableRowReader(FieldValue[] values, Dictionary<string, int> slotByColumn)
        {
            this.values = values;
            this.slotByColumn = slotByColumn;
        }

        public bool IsObject => true;

        public FieldValue Read(string[] pointerSegments) =>
            pointerSegments.Length == 1 && slotByColumn.TryGetValue(pointerSegments[0], out int slot) ? values[slot] : FieldValue.Missing;
    }
}
