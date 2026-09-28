using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TextExtractAutomation
{
    /// <summary>
    /// Pulls labelled, typed business fields (an invoice number, a total, a due date, an IBAN) out of text such as email bodies, OCR output and
    /// terminal screens, without writing regular expressions. One component instance serves one automation flow.
    /// </summary>
    /// <remarks>
    /// Describe each field the way a person sees it (the labels in front of it, where the value sits, what type it is) with the <c>Add...</c>
    /// methods or a JSON template, run <c>ExtractFromText</c>, then read each field's normalized value or its reason code.
    /// </remarks>
    [Description("Extracts labelled, typed fields (invoice number, total, due date, IBAN) from email, OCR or screen text without writing regular expressions. Describe each field by its labels, position and type, run ExtractFromText, then read each value or its reason code. Never throws.")]
    public sealed class TextExtractUtils : Component
    {
        private readonly object syncRoot = new object();
        private bool disposed;
        private Template template = new Template();          // replaced whole, never edited in place
        private ExtractionSnapshot results;                  // the last completed extraction, or null; replaced whole, never edited
        private int cursor;                                  // TryReadNextField: the next field to read
        private readonly System.Collections.Generic.Dictionary<string, int> rowCursors = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private RowResult currentRow;                        // the row TryReadNextRow read last; GetRowValue reads its cells

        /// <summary>Empty constructor required so Pega Robot Studio can create the component.</summary>
        public TextExtractUtils() { }

        /// <summary>Standard designer constructor; attaches the component to a container.</summary>
        public TextExtractUtils(IContainer container) { container?.Add(this); }

        // ------------------------------------------------------------------ template

        /// <summary>Removes every field and table and restores the default limits. Also clears any results.</summary>
        [Category("Text Extract - Template")]
        [Description("Removes every field and table and restores the default limits. Also clears any results. Never throws.")]
        public bool ClearTemplate(out string message)
        {
            message = null;
            try { return ChangeTemplate(nameof(ClearTemplate), t => { t.Fields.Clear(); t.Tables.Clear(); t.Limits = new TemplateLimits(); return null; }, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ClearTemplate), ex); return false; }
        }

        /// <summary>Adds a required field whose value follows one of its labels on the same line. Labels are alternatives separated by |, for example Invoice No|Invoice Number.</summary>
        [Category("Text Extract - Template")]
        [Description("Adds a required field whose value follows one of its labels on the same line. Labels are alternatives separated by |, for example Invoice No|Invoice Number. The type decides what a valid value is; numbers use DotDecimal (1,234.56) and dates yyyy-MM-dd. Never throws.")]
        public bool AddLabelFieldSimple(string name, string labels, FieldType type, out string message)
        {
            message = null;
            try { return ChangeTemplate(nameof(AddLabelFieldSimple), t => t.TryAddLabelField(name, labels, ValuePosition.SameLine, type, DecimalStyle.DotDecimal, null, true, Occurrence.RequireUnique), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddLabelFieldSimple), ex); return false; }
        }

        /// <summary>Adds a field with every option: where the value sits, how numbers are written (DecimalStyle), the date formats for a Date field (separated by |), whether it is required, and what to do when its label appears more than once.</summary>
        [Category("Text Extract - Template")]
        [Description("Adds a field with every option: where the value sits relative to the label, how numbers are written (DecimalStyle, used by Integer, Decimal, Amount and Percentage fields), the date formats for a Date field (separated by |; empty for yyyy-MM-dd, and empty for every other type), whether it is required, and what to do when the label appears more than once. Never throws.")]
        public bool AddLabelField(string name, string labels, ValuePosition position, FieldType type, DecimalStyle decimalStyle, string dateFormats, bool required, Occurrence occurrence, out string message)
        {
            message = null;
            try { return ChangeTemplate(nameof(AddLabelField), t => t.TryAddLabelField(name, labels, position, type, decimalStyle, dateFormats, required, occurrence), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddLabelField), ex); return false; }
        }

        /// <summary>Adds a required field found by a regular expression with a named group called value, for text that has no stable label. The pattern times out rather than hanging.</summary>
        [Category("Text Extract - Template")]
        [Description("Adds a required field found by a regular expression with a named group called value, for text that has no stable label (an escape hatch; label fields need no pattern). DecimalStyle and date formats work as in AddLabelField. The match times out rather than hanging the robot. Never throws.")]
        public bool AddPatternField(string name, string pattern, FieldType type, DecimalStyle decimalStyle, string dateFormats, out string message)
        {
            message = null;
            try { return ChangeTemplate(nameof(AddPatternField), t => t.TryAddPatternField(name, pattern, type, decimalStyle, dateFormats), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddPatternField), ex); return false; }
        }

        /// <summary>Adds a column to a table, creating the table when it is new. The header is found on a header line like a label, and names the column for GetRowValue.</summary>
        [Category("Text Extract - Template")]
        [Description("Adds a column to a table (rows under a header line), creating the table when the name is new. The header text is found on the header line like a label and names the column for GetRowValue; the type, decimal style and date formats read each cell as they read a field. Never throws.")]
        public bool AddTableColumn(string table, string header, FieldType type, DecimalStyle decimalStyle, string dateFormats, out string message)
        {
            message = null;
            try { return ChangeTemplate(nameof(AddTableColumn), t => t.TryAddTableColumn(table, header, type, decimalStyle, dateFormats), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddTableColumn), ex); return false; }
        }

        /// <summary>Replaces the whole template from JSON. An invalid template is rejected whole and the previous one stays in force.</summary>
        [Category("Text Extract - Template")]
        [Description("Replaces the whole template from JSON. An invalid template is rejected whole and the previous one stays in force. Never throws.")]
        public bool LoadTemplateJson(string templateJson, out string message)
        {
            message = null;
            try { return LoadTemplate(templateJson, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(LoadTemplateJson), ex); return false; }
        }

        /// <summary>Returns the current template, including limits, as canonical JSON that LoadTemplateJson accepts.</summary>
        [Category("Text Extract - Template")]
        [Description("Returns the current template, including limits, as canonical JSON that LoadTemplateJson accepts. Never throws.")]
        public bool GetTemplateJson(out string templateJson, out string message)
        {
            templateJson = null;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (disposed) { message = DisposedMessage(nameof(GetTemplateJson)); return false; }
                    templateJson = template.ToCanonicalJson();
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { templateJson = null; message = NeverThrowsGuard.Failure(nameof(GetTemplateJson), ex); return false; }
        }

        /// <summary>Checks a JSON template without loading it. Returns True when the check ran; errorCount is 0 for a valid template and reportJson lists the problems.</summary>
        [Category("Text Extract - Template")]
        [Description("Checks a JSON template without loading it. Returns True when the check ran; errorCount is 0 for a valid template and reportJson lists every problem with its path. Never throws.")]
        public bool ValidateTemplateJson(string templateJson, out int errorCount, out string reportJson, out string message)
        {
            errorCount = 0;
            reportJson = null;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (disposed) { message = DisposedMessage(nameof(ValidateTemplateJson)); return false; }
                    if (!CheckTemplateText(nameof(ValidateTemplateJson), templateJson, out message)) return false;
                    var findings = new TemplateFindings();
                    TemplateParser.Parse(templateJson, findings);
                    errorCount = findings.Total;
                    reportJson = WriteReport(findings);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { errorCount = 0; reportJson = null; message = NeverThrowsGuard.Failure(nameof(ValidateTemplateJson), ex); return false; }
        }

        /// <summary>Sets the longest text ExtractFromText accepts, in characters (default 1,000,000; maximum 10,000,000).</summary>
        [Category("Text Extract - Template")]
        [Description("Sets the longest text ExtractFromText accepts, in characters (default 1,000,000; maximum 10,000,000). Longer text fails whole. Never throws.")]
        public bool ConfigureLimits(int maximumTextCharacters, out string message)
        {
            message = null;
            try
            {
                return ChangeTemplate(nameof(ConfigureLimits), t =>
                {
                    string problem = TemplateLimits.Check("maximumTextCharacters", maximumTextCharacters, TemplateLimits.MaxTextCharacters);
                    if (problem != null) return new Finding("maximumTextCharacters", "InvalidLimit", problem);
                    t.Limits = t.Limits.Clone();
                    t.Limits.MaximumTextCharacters = maximumTextCharacters;
                    return null;
                }, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ConfigureLimits), ex); return false; }
        }

        // ------------------------------------------------------------------ run

        /// <summary>Extracts every field and table of the template from the text. True means the extraction ran, even when fields were not found; foundCount and missingRequiredCount summarize it.</summary>
        [Category("Text Extract - Run")]
        [Description("Extracts every field and table of the template from the text. True means the extraction ran, even when fields were not found; foundCount and missingRequiredCount summarize the fields, GetField reads each field and TryReadNextRow each table row. Never throws.")]
        public bool ExtractFromText(string text, out int foundCount, out int missingRequiredCount, out string message)
        {
            foundCount = 0;
            missingRequiredCount = 0;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (disposed) { message = DisposedMessage(nameof(ExtractFromText)); return false; }
                    InvalidateResults();                         // earlier results never survive an attempt, whatever its outcome
                    if (text == null) { message = nameof(ExtractFromText) + " failed: text is required (an empty string is allowed)."; return false; }
                    if (template.Fields.Count == 0 && template.Tables.Count == 0) { message = nameof(ExtractFromText) + " failed: the template has no fields or tables; add one with AddLabelFieldSimple, AddLabelField or AddTableColumn, or load a template."; return false; }
                    if (text.Length > template.Limits.MaximumTextCharacters)
                    {
                        message = nameof(ExtractFromText) + " failed: the text is longer than " + template.Limits.MaximumTextCharacters + " characters (the limit is set with ConfigureLimits).";
                        return false;
                    }
                    ExtractionSnapshot snapshot = Extraction.Run(template, text);
                    results = snapshot;
                    foundCount = snapshot.FoundCount;
                    missingRequiredCount = snapshot.MissingRequiredCount;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ExtractFromText), ex); return false; }
        }

        // ------------------------------------------------------------------ results

        /// <summary>Reads one field of the last extraction: found, the normalized value, the text as it appeared, a reason code when not found, and the 1-based line (0 when none).</summary>
        [Category("Text Extract - Results")]
        [Description("Reads one field of the last extraction: found, the normalized value, the text as it appeared, a reason code when it was not found or not valid, and the 1-based line number (0 when none). Never throws.")]
        public bool GetField(string name, out bool found, out string value, out string raw, out string reason, out int lineNumber, out string message)
        {
            found = false;
            value = null;
            raw = null;
            reason = null;
            lineNumber = 0;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(GetField), out ExtractionSnapshot run, out message)) return false;
                    FieldResult r = run.Find(name);
                    if (r == null) { message = nameof(GetField) + " failed: the template has no field with that name (names are matched ignoring case)."; return false; }
                    found = r.Found;
                    value = r.Value;
                    raw = r.Raw;
                    reason = r.Reason;
                    lineNumber = r.LineNumber;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetField), ex); return false; }
        }

        /// <summary>Returns every field and table of the last extraction as JSON.</summary>
        [Category("Text Extract - Results")]
        [Description("Returns every field and table of the last extraction as JSON, with values, text as found, reasons and line numbers. Never throws.")]
        public bool GetResultJson(out string resultJson, out string message)
        {
            resultJson = null;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(GetResultJson), out ExtractionSnapshot run, out message)) return false;
                    resultJson = Extraction.ToJson(run);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetResultJson), ex); return false; }
        }

        /// <summary>Restarts TryReadNextField from the first field.</summary>
        [Category("Text Extract - Results")]
        [Description("Restarts TryReadNextField from the first field. Never throws.")]
        public bool ResetFieldCursor(out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(ResetFieldCursor), out _, out message)) return false;
                    cursor = 0;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ResetFieldCursor), ex); return false; }
        }

        /// <summary>Reads the next field of the last extraction, in template order. hasItem is False when there are no more; reason is null when the field was found.</summary>
        [Category("Text Extract - Results")]
        [Description("Reads the next field of the last extraction, in template order. hasItem is False when there are no more; reason is null when the field was found and valid. Never throws.")]
        public bool TryReadNextField(out bool hasItem, out string name, out string value, out string raw, out string reason, out int lineNumber, out string message)
        {
            hasItem = false;
            name = null;
            value = null;
            raw = null;
            reason = null;
            lineNumber = 0;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(TryReadNextField), out ExtractionSnapshot run, out message)) return false;
                    if (cursor >= run.Fields.Count) return true;              // exhausted: stays exhausted until a reset or a new extraction
                    FieldResult r = run.Fields[cursor++];
                    hasItem = true;
                    name = r.Name;
                    value = r.Value;
                    raw = r.Raw;
                    reason = r.Reason;
                    lineNumber = r.LineNumber;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(TryReadNextField), ex); return false; }
        }

        /// <summary>Reads the next row of a table from the last extraction. hasItem is False when there are no more. rowNumber is 1-based; rowJson holds every cell. GetRowValue then reads one cell of this row.</summary>
        [Category("Text Extract - Results")]
        [Description("Reads the next row of a table from the last extraction. hasItem is False when there are no more rows (or the table's header was not found). rowNumber is 1-based within the table; rowJson holds every cell; GetRowValue then reads one cell of this row. Never throws.")]
        public bool TryReadNextRow(string table, out bool hasItem, out int rowNumber, out string rowJson, out string message)
        {
            hasItem = false;
            rowNumber = 0;
            rowJson = null;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(TryReadNextRow), out ExtractionSnapshot run, out message)) return false;
                    TableResult t = run.FindTable(table);
                    if (t == null) { message = nameof(TryReadNextRow) + " failed: the template has no table with that name (names are matched ignoring case)."; return false; }
                    rowCursors.TryGetValue(t.Name, out int next);
                    if (next >= t.Rows.Count) { currentRow = null; return true; }      // exhausted: stays exhausted until a new extraction
                    RowResult row = t.Rows[next];
                    rowCursors[t.Name] = next + 1;
                    currentRow = row;
                    hasItem = true;
                    rowNumber = row.RowNumber;
                    rowJson = Extraction.RowJson(row);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { hasItem = false; rowNumber = 0; rowJson = null; message = NeverThrowsGuard.Failure(nameof(TryReadNextRow), ex); return false; }
        }

        /// <summary>Reads one cell of the row TryReadNextRow read last: found, the normalized value, the text as it appeared, and a reason when not found.</summary>
        [Category("Text Extract - Results")]
        [Description("Reads one cell (by column header, ignoring case) of the row TryReadNextRow read last: found, the normalized value, the text as it appeared, and a reason (MissingValue or InvalidValue) when it was not found. Never throws.")]
        public bool GetRowValue(string column, out bool found, out string value, out string raw, out string reason, out string message)
        {
            found = false;
            value = null;
            raw = null;
            reason = null;
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(GetRowValue), out _, out message)) return false;
                    if (currentRow == null) { message = nameof(GetRowValue) + " failed: there is no current row; call TryReadNextRow and use the row only while hasItem is True."; return false; }
                    CellResult cell = currentRow.Find(column);
                    if (cell == null) { message = nameof(GetRowValue) + " failed: the table has no column with that header (headers are matched ignoring case)."; return false; }
                    found = cell.Found;
                    value = cell.Value;
                    raw = cell.Raw;
                    reason = cell.Reason;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { found = false; value = null; raw = null; reason = null; message = NeverThrowsGuard.Failure(nameof(GetRowValue), ex); return false; }
        }

        /// <summary>Discards the last extraction's results. Succeeds even when there are none.</summary>
        [Category("Text Extract - Results")]
        [Description("Discards the last extraction's results. Succeeds even when there are none. Never throws.")]
        public bool ClearResults(out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (disposed) { message = DisposedMessage(nameof(ClearResults)); return false; }
                    InvalidateResults();
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ClearResults), ex); return false; }
        }

        // ------------------------------------------------------------------ internals

        /// <summary>The current template (test seam).</summary>
        internal Template CurrentTemplate { get { lock (syncRoot) return template; } }

        /// <summary>
        /// Applies one change to a copy of the template and swaps it in only if the change was accepted and the result can still be saved and loaded
        /// again, so a rejected change leaves the current template untouched.
        /// </summary>
        private bool ChangeTemplate(string operation, Func<Template, Finding> change, out string message)
        {
            message = null;
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(operation); return false; }
                Template copy = template.Clone();
                Finding problem = change(copy) ?? copy.CheckCanonicalSize();
                if (problem != null) { message = operation + " failed: " + problem.Format(); return false; }
                template = copy;
                InvalidateResults();                             // results belong to the template that produced them
                return true;
            }
        }

        private bool LoadTemplate(string templateJson, out string message)
        {
            message = null;
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(nameof(LoadTemplateJson)); return false; }
                if (!CheckTemplateText(nameof(LoadTemplateJson), templateJson, out message)) return false;
                var findings = new TemplateFindings();
                Template parsed = TemplateParser.Parse(templateJson, findings);
                if (parsed == null)
                {
                    string more = findings.Total > 1 ? " (" + (findings.Total - 1) + " more problem(s); ValidateTemplateJson lists them all)" : string.Empty;
                    message = nameof(LoadTemplateJson) + " failed: " + findings.Reported[0].Format() + more;
                    return false;
                }
                template = parsed;
                InvalidateResults();
                return true;
            }
        }

        private static bool CheckTemplateText(string operation, string templateJson, out string message)
        {
            message = null;
            if (templateJson == null) { message = operation + " failed: templateJson is required."; return false; }
            if (templateJson.Length > TemplateLimits.MaxTemplateJsonCharacters)
            {
                message = operation + " failed: the template is longer than " + TemplateLimits.MaxTemplateJsonCharacters + " characters.";
                return false;
            }
            return true;
        }

        private static string WriteReport(TemplateFindings findings)
        {
            using (var stream = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    w.WriteBoolean("valid", findings.Total == 0);
                    w.WriteNumber("errorCount", findings.Total);
                    w.WriteNumber("reportedCount", findings.Reported.Count);
                    w.WriteBoolean("truncated", findings.Truncated);
                    w.WriteStartArray("errors");
                    foreach (Finding f in findings.Reported)
                    {
                        w.WriteStartObject();
                        w.WriteString("path", f.Path);
                        w.WriteString("code", f.Code);
                        w.WriteString("message", f.Message);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string DisposedMessage(string operation) => operation + " failed: the component has been disposed.";

        private void InvalidateResults() { results = null; cursor = 0; rowCursors.Clear(); currentRow = null; }

        /// <summary>Fails with the disposed message or an actionable no-results message; otherwise hands back the published extraction. Called with the lock held.</summary>
        private bool Available(string operation, out ExtractionSnapshot run, out string message)
        {
            run = results;
            message = null;
            if (disposed) { message = DisposedMessage(operation); return false; }
            if (run == null) { message = operation + " failed: there are no results; run ExtractFromText first (a failed extraction, a template change or ClearResults discards them)."; return false; }
            return true;
        }

        /// <summary>Releases the template and results. Idempotent.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (syncRoot) { disposed = true; InvalidateResults(); }
            }
            base.Dispose(disposing);
        }
    }
}
