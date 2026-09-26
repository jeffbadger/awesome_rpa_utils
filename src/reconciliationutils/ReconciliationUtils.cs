using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ReconciliationAutomation
{
    /// <summary>
    /// Reconciles two datasets by business key and explains every disagreement, exposing exceptions through
    /// scalar ports. One component instance serves one automation flow.
    /// </summary>
    /// <remarks>
    /// Build a definition with the <c>Add...</c> methods or load it as JSON, run <c>ReconcileJson</c> on two JSON arrays, then read
    /// the counts, exceptions and field differences. See the component README and its Documentation folder for worked examples.
    /// </remarks>
    [Description("Reconciles two datasets by business key and reports matches, differences, missing and duplicate records through scalar ports. Define keys and comparisons (or load a JSON definition), run ReconcileJson on two JSON arrays, then read counts, exceptions and field differences through scalar ports. Never throws.")]
    public sealed class ReconciliationUtils : Component
    {
        private readonly object syncRoot = new object();
        private bool disposed;
        private ReconciliationDefinition definition = new ReconciliationDefinition();

        // The published outcome of the last completed run, or null. Replaced whole, never edited, so a reader can never see it half-built.
        private TableLimits tableLimits = new TableLimits();      // replaced whole, never edited in place
        private ReconciliationSnapshot results;
        private int exceptionPosition;                 // next index in results to examine
        private ReconciliationResult currentException;   // the exception most recently read; its differences feed the inner cursor
        private int differencePosition;

        /// <summary>Test seam: runs inside <see cref="LoadDefinitionJson"/> after the definition is parsed and before it is committed, while the instance lock is held.</summary>
        internal Action DuringLoad;

        /// <summary>Empty constructor required so Pega Robot Studio can create the component.</summary>
        public ReconciliationUtils() { }

        /// <summary>Standard designer constructor; attaches the component to a container.</summary>
        public ReconciliationUtils(IContainer container) { container?.Add(this); }

        /// <summary>Restores the default definition (no keys, no comparisons, default limits) and clears any results.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Restores the default definition (no keys, no comparisons, default limits) and clears any results. Never throws.")]
        public bool ClearDefinition(out string message)
        {
            message = null;
            try
            {
                // The change runs under the instance lock, so the DataTable limits are reset in the same critical section as the definition: a concurrent
                // ConfigureTableLimits lands entirely before or entirely after this call, never between its two halves.
                return ChangeDefinition(nameof(ClearDefinition), d =>
                {
                    d.Keys = new List<KeyMappingDef>();
                    d.Comparisons = new List<ComparisonDef>();
                    d.Limits = new ReconciliationLimits();
                    tableLimits = new TableLimits();
                    return null;
                }, out message);
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ClearDefinition), ex); return false; }
        }

        /// <summary>Adds a business-key part matched exactly (no trimming, case-sensitive). The pointers are restricted JSON Pointers such as /invoiceNumber.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds a business-key part matched exactly (no trimming, case-sensitive). The pointers are restricted JSON Pointers such as /invoiceNumber. Never throws.")]
        public bool AddKeyMappingSimple(string name, string leftPointer, string rightPointer, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddKeyMappingSimple), d => d.TryAddKey(name, leftPointer, rightPointer, false, false), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddKeyMappingSimple), ex); return false; }
        }

        /// <summary>Adds a business-key part with a choice of trimming and case-insensitive matching. The pointers are restricted JSON Pointers such as /invoiceNumber.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds a business-key part with a choice of trimming and case-insensitive matching. The pointers are restricted JSON Pointers such as /invoiceNumber. Never throws.")]
        public bool AddKeyMapping(string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddKeyMapping), d => d.TryAddKey(name, leftPointer, rightPointer, trim, ignoreCase), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddKeyMapping), ex); return false; }
        }

        /// <summary>Adds an exact text comparison (no trimming, case-sensitive, a value is required on both sides).</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds an exact text comparison (no trimming, case-sensitive, a value is required on both sides). Never throws.")]
        public bool AddTextComparisonSimple(string name, string leftPointer, string rightPointer, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddTextComparisonSimple), d => d.TryAddText(name, leftPointer, rightPointer, false, false, ComparisonNullPolicy.RequireValue), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddTextComparisonSimple), ex); return false; }
        }

        /// <summary>Adds a text comparison with a choice of trimming, case-insensitive comparison and null policy.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds a text comparison with a choice of trimming, case-insensitive comparison and null policy. Never throws.")]
        public bool AddTextComparison(string name, string leftPointer, string rightPointer, bool trim, bool ignoreCase, ComparisonNullPolicy nullPolicy, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddTextComparison), d => d.TryAddText(name, leftPointer, rightPointer, trim, ignoreCase, nullPolicy), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddTextComparison), ex); return false; }
        }

        /// <summary>Adds an exact decimal comparison (tolerance 0, a value is required on both sides).</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds an exact decimal comparison (tolerance 0, a value is required on both sides). Never throws.")]
        public bool AddDecimalComparisonSimple(string name, string leftPointer, string rightPointer, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddDecimalComparisonSimple), d => d.TryAddDecimal(name, leftPointer, rightPointer, "0", ComparisonNullPolicy.RequireValue), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddDecimalComparisonSimple), ex); return false; }
        }

        /// <summary>Adds a decimal comparison with an absolute tolerance given as invariant decimal text (for example 0.01) and a null policy.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds a decimal comparison with an absolute tolerance given as invariant decimal text (for example 0.01) and a null policy. Never throws.")]
        public bool AddDecimalComparison(string name, string leftPointer, string rightPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddDecimalComparison), d => d.TryAddDecimal(name, leftPointer, rightPointer, absoluteTolerance, nullPolicy), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddDecimalComparison), ex); return false; }
        }

        /// <summary>Adds a Boolean comparison: both sides must be JSON true or false (not yes, 0 or 1), and a value is required on both sides.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds a Boolean comparison: both sides must be JSON true or false (not yes, 0 or 1), and a value is required on both sides. Never throws.")]
        public bool AddBooleanComparisonSimple(string name, string leftPointer, string rightPointer, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddBooleanComparisonSimple), d => d.TryAddBoolean(name, leftPointer, rightPointer, ComparisonNullPolicy.RequireValue), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddBooleanComparisonSimple), ex); return false; }
        }

        /// <summary>Adds a Boolean comparison (JSON true or false only) with a choice of null policy.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds a Boolean comparison (JSON true or false only) with a choice of null policy. Never throws.")]
        public bool AddBooleanComparison(string name, string leftPointer, string rightPointer, ComparisonNullPolicy nullPolicy, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddBooleanComparison), d => d.TryAddBoolean(name, leftPointer, rightPointer, nullPolicy), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddBooleanComparison), ex); return false; }
        }

        /// <summary>Adds an exact money comparison (tolerance 0, a value is required on both sides). Each side needs a three-letter currency; different currencies never compare amounts.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds an exact money comparison (tolerance 0, a value is required on both sides). Each side needs a three-letter currency; different currencies never compare amounts. Never throws.")]
        public bool AddMoneyComparisonSimple(string name, string leftPointer, string rightPointer, string leftCurrencyPointer, string rightCurrencyPointer, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddMoneyComparisonSimple), d => d.TryAddMoney(name, leftPointer, rightPointer, leftCurrencyPointer, rightCurrencyPointer, "0", ComparisonNullPolicy.RequireValue), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddMoneyComparisonSimple), ex); return false; }
        }

        /// <summary>Adds a money comparison with an absolute tolerance (invariant decimal text) and a null policy. Each side needs a three-letter currency; different currencies are a CurrencyMismatch and the amounts are not compared.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Adds a money comparison with an absolute tolerance (invariant decimal text) and a null policy. Each side needs a three-letter currency; different currencies are a CurrencyMismatch and the amounts are not compared. Never throws.")]
        public bool AddMoneyComparison(string name, string leftPointer, string rightPointer, string leftCurrencyPointer, string rightCurrencyPointer, string absoluteTolerance, ComparisonNullPolicy nullPolicy, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(AddMoneyComparison), d => d.TryAddMoney(name, leftPointer, rightPointer, leftCurrencyPointer, rightCurrencyPointer, absoluteTolerance, nullPolicy), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(AddMoneyComparison), ex); return false; }
        }

        /// <summary>Replaces the whole definition from JSON. An invalid definition is rejected whole and the previous one stays in force.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Replaces the whole definition from JSON. An invalid definition is rejected whole and the previous one stays in force. Never throws.")]
        public bool LoadDefinitionJson(string definitionJson, out string message)
        {
            message = null;
            try { return LoadDefinition(definitionJson, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(LoadDefinitionJson), ex); return false; }
        }

        /// <summary>Returns the current definition, including limits, as canonical JSON.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Returns the current definition, including limits, as canonical JSON. Never throws.")]
        public bool GetDefinitionJson(out string definitionJson, out string message)
        {
            message = null;
            definitionJson = null;
            try { return GetDefinition(out definitionJson, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetDefinitionJson), ex); return false; }
        }

        /// <summary>Validates a JSON definition without loading it. Returns True when validation ran; errorCount is 0 for a valid definition and reportJson lists the findings.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Validates a JSON definition without loading it. Returns True when validation ran; errorCount is 0 for a valid definition and reportJson lists the findings. Never throws.")]
        public bool ValidateDefinitionJson(string definitionJson, out int errorCount, out string reportJson, out string message)
        {
            message = null;
            errorCount = 0;
            reportJson = null;
            try { return ValidateDefinition(definitionJson, out errorCount, out reportJson, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ValidateDefinitionJson), ex); return false; }
        }

        /// <summary>Sets the resource limits: rows per side, input characters per side, result records and difference details. A run that exceeds a limit fails whole.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Sets the resource limits: rows per side, input characters per side, result records and difference details. A run that exceeds a limit fails whole. Never throws.")]
        public bool ConfigureLimits(int maximumRowsPerSide, int maximumInputCharactersPerSide, int maximumResults, int maximumDifferenceDetails, out string message)
        {
            message = null;
            try { return ChangeDefinition(nameof(ConfigureLimits), d => ApplyLimits(d, maximumRowsPerSide, maximumInputCharactersPerSide, maximumResults, maximumDifferenceDetails), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ConfigureLimits), ex); return false; }
        }

        /// <summary>Sets the DataTable limits used by ReconcileDataTables: columns per table, cells (rows x columns) per table and characters in one text value.</summary>
        [Category("Reconciliation - Definition")]
        [Description("Sets the DataTable limits used by ReconcileDataTables: columns per table, cells (rows x columns) per table and characters in one text value. Defaults 100, 2,000,000 and 4,096. Never throws.")]
        public bool ConfigureTableLimits(int maximumColumns, int maximumCells, int maximumValueCharacters, out string message)
        {
            message = null;
            try
            {
                var limits = new TableLimits { MaximumColumns = maximumColumns, MaximumCells = maximumCells, MaximumValueCharacters = maximumValueCharacters };
                string problem = limits.FirstProblem();
                lock (syncRoot)
                {
                    if (disposed) { message = DisposedMessage(nameof(ConfigureTableLimits)); return false; }
                    if (problem != null) { message = nameof(ConfigureTableLimits) + " failed: " + problem + "."; return false; }
                    tableLimits = limits;
                    InvalidateResults();
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ConfigureTableLimits), ex); return false; }
        }

        /// <summary>Reconciles two DataTables. A pointer names one column (for example /Amount). True means the run completed, even with mismatches; the tables are read, never modified.</summary>
        [Category("Reconciliation - Run")]
        [Description("Reconciles two DataTables (for example loaded from Excel, CSV or a database). A pointer names one column, such as /Amount. True means the run completed, even with mismatches; exceptionCount is 0 when everything matched. The tables are read, never modified. Never throws.")]
        public bool ReconcileDataTables(DataTable leftTable, DataTable rightTable, out int exceptionCount, out string message)
        {
            message = null;
            exceptionCount = 0;
            try { return RunTableReconciliation(leftTable, rightTable, out exceptionCount, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ReconcileDataTables), ex); return false; }
        }

        /// <summary>Reconciles two JSON arrays of objects. True means the run completed, even with mismatches; exceptionCount is 0 when everything matched.</summary>
        [Category("Reconciliation - Run")]
        [Description("Reconciles two JSON arrays of objects. True means the run completed, even with mismatches; exceptionCount is 0 when everything matched. Never throws.")]
        public bool ReconcileJson(string leftJson, string rightJson, out int exceptionCount, out string message)
        {
            message = null;
            exceptionCount = 0;
            try { return RunReconciliation(leftJson, rightJson, out exceptionCount, out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ReconcileJson), ex); return false; }
        }

        /// <summary>Returns the headline counts of the last completed run.</summary>
        [Category("Reconciliation - Results")]
        [Description("Returns the headline counts of the last completed run. Never throws.")]
        public bool GetSummary(out int leftRowCount, out int rightRowCount, out int matchedPairCount, out int exceptionCount, out string message)
        {
            message = null;
            leftRowCount = 0;
            rightRowCount = 0;
            matchedPairCount = 0;
            exceptionCount = 0;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(GetSummary), out ReconciliationSnapshot run, out message)) return false;
                    ReconciliationSummary x = run.Summary;
                    leftRowCount = x.LeftRowCount;
                    rightRowCount = x.RightRowCount;
                    matchedPairCount = x.MatchedPairCount;
                    exceptionCount = x.ExceptionCount;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetSummary), ex); return false; }
        }

        /// <summary>Returns every count of the last completed run as JSON.</summary>
        [Category("Reconciliation - Results")]
        [Description("Returns every count of the last completed run as JSON. Never throws.")]
        public bool GetSummaryJson(out string summaryJson, out string message)
        {
            message = null;
            summaryJson = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(GetSummaryJson), out ReconciliationSnapshot run, out message)) return false;
                    summaryJson = ResultJson.Summary(run.Summary);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetSummaryJson), ex); return false; }
        }

        /// <summary>Restarts exception and difference reading from the first exception.</summary>
        [Category("Reconciliation - Results")]
        [Description("Restarts exception and difference reading from the first exception. Never throws.")]
        public bool ResetResultCursor(out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(ResetResultCursor), out _, out message)) return false;
                    ResetCursors();
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ResetResultCursor), ex); return false; }
        }

        /// <summary>Reads the next exception of the last run. hasItem is False when there are no more. kind is a stable code such as Different or OnlyLeft; a row index is -1 when absent or ambiguous.</summary>
        [Category("Reconciliation - Results")]
        [Description("Reads the next exception of the last run. hasItem is False when there are no more. kind is a stable code such as Different or OnlyLeft; a row index is -1 when absent or ambiguous. Never throws.")]
        public bool TryReadNextException(out bool hasItem, out string resultId, out string kind, out string keyJson, out int leftRowIndex, out int rightRowIndex, out string reason, out int differenceCount, out string message)
        {
            message = null;
            hasItem = false;
            resultId = null;
            kind = null;
            keyJson = null;
            leftRowIndex = -1;
            rightRowIndex = -1;
            reason = null;
            differenceCount = 0;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(TryReadNextException), out ReconciliationSnapshot run, out message)) return false;
                    while (exceptionPosition < run.Results.Count)
                    {
                        ReconciliationResult r = run.Results[exceptionPosition++];
                        if (r.Kind == ResultKind.Matched) continue;      // only exceptions are read; matched pairs are counted in the summary
                        currentException = r;
                        differencePosition = 0;                          // reading an exception restarts the difference cursor
                        hasItem = true;
                        resultId = r.Id;
                        kind = r.Kind.ToString();
                        keyJson = r.NormalizedKey == null ? null : ReconciliationKey.ToDisplayJson(r.NormalizedKey);
                        leftRowIndex = r.LeftRowIndex;
                        rightRowIndex = r.RightRowIndex;
                        reason = r.ReasonCode;
                        differenceCount = r.Differences.Count;
                        return true;
                    }
                    currentException = null;                             // exhausted: stays exhausted until a reset or a new run
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(TryReadNextException), ex); return false; }
        }

        /// <summary>Reads the next field difference of the exception most recently read. hasItem is False when there are no more. A missing value is null; a JSON null is the text null.</summary>
        [Category("Reconciliation - Results")]
        [Description("Reads the next field difference of the exception most recently read. hasItem is False when there are no more. A missing value is null; a JSON null is the text null. Never throws.")]
        public bool TryReadNextDifference(out bool hasItem, out string ruleName, out string reasonCode, out string leftValueJson, out string rightValueJson, out string explanation, out string message)
        {
            message = null;
            hasItem = false;
            ruleName = null;
            reasonCode = null;
            leftValueJson = null;
            rightValueJson = null;
            explanation = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(TryReadNextDifference), out _, out message)) return false;
                    if (currentException == null || differencePosition >= currentException.Differences.Count) return true;
                    ComparisonOutcome d = currentException.Differences[differencePosition++];
                    hasItem = true;
                    ruleName = d.RuleName;
                    reasonCode = d.ReasonCode;
                    leftValueJson = d.LeftValueJson;
                    rightValueJson = d.RightValueJson;
                    explanation = d.Explanation;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(TryReadNextDifference), ex); return false; }
        }

        /// <summary>Returns the full detail of one result, including every member of a duplicate-key group.</summary>
        [Category("Reconciliation - Results")]
        [Description("Returns the full detail of one result, including every member of a duplicate-key group. Never throws.")]
        public bool GetResultJson(string resultId, out string resultJson, out string message)
        {
            message = null;
            resultJson = null;
            try
            {
                lock (syncRoot)
                {
                    if (!Available(nameof(GetResultJson), out ReconciliationSnapshot run, out message)) return false;
                    ReconciliationResult found = FindResult(run, resultId);
                    if (found == null)
                    {
                        message = nameof(GetResultJson) + " failed: there is no result with that ID in the current results; IDs look like r000001 and come from TryReadNextException.";
                        return false;
                    }
                    resultJson = ResultJson.Result(found);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetResultJson), ex); return false; }
        }

        /// <summary>Discards the last run's results. Succeeds even when there are none.</summary>
        [Category("Reconciliation - Results")]
        [Description("Discards the last run's results. Succeeds even when there are none. Never throws.")]
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


        // ------------------------------------------------------------------ definition

        /// <summary>
        /// Applies one change to a copy of the definition and swaps it in only if the change was accepted, so a rejected change
        /// leaves the current definition (and everything derived from it) untouched.
        /// </summary>
        private bool ChangeDefinition(string operation, Func<ReconciliationDefinition, Finding> change, out string message)
        {
            message = null;
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(operation); return false; }
                ReconciliationDefinition copy = definition.Clone();
                Finding problem = change(copy);
                if (problem != null) { message = operation + " failed: " + problem.Format(); return false; }
                definition = copy;
                InvalidateResults();
                return true;
            }
        }

        private static Finding ApplyLimits(ReconciliationDefinition target, int rows, int characters, int results, int differences)
        {
            var limits = new ReconciliationLimits
            {
                MaximumRowsPerSide = rows,
                MaximumInputCharactersPerSide = characters,
                MaximumResults = results,
                MaximumDifferenceDetails = differences
            };
            string problem = limits.FirstProblem();
            if (problem != null) return new Finding("limits", "InvalidLimit", problem);
            target.Limits = limits;
            return null;
        }

        private bool LoadDefinition(string definitionJson, out string message)
        {
            message = null;
            // The whole operation runs under the instance lock, including the parse: another setup call cannot commit between
            // the check and the swap, so operations are strictly serialized and no change can be silently overwritten. The
            // parse is bounded (256,000 characters) and fast, so holding the lock through it costs a few milliseconds.
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(nameof(LoadDefinitionJson)); return false; }
                if (!CheckDefinitionText(nameof(LoadDefinitionJson), definitionJson, out message)) return false;

                var findings = new DefinitionFindings();
                ReconciliationDefinition parsed = DefinitionParser.Parse(definitionJson, findings);
                DuringLoad?.Invoke();
                if (parsed == null)
                {
                    string more = findings.Total > 1 ? " (" + (findings.Total - 1) + " more problem(s); ValidateDefinitionJson lists them all)" : string.Empty;
                    message = nameof(LoadDefinitionJson) + " failed: " + findings.Reported[0].Format() + more;
                    return false;
                }
                definition = parsed;
                InvalidateResults();
                return true;
            }
        }

        private bool GetDefinition(out string definitionJson, out string message)
        {
            definitionJson = null;
            message = null;
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(nameof(GetDefinitionJson)); return false; }
                definitionJson = definition.ToCanonicalJson();
                return true;
            }
        }

        private bool ValidateDefinition(string definitionJson, out int errorCount, out string reportJson, out string message)
        {
            errorCount = 0;
            reportJson = null;
            message = null;
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(nameof(ValidateDefinitionJson)); return false; }
                if (!CheckDefinitionText(nameof(ValidateDefinitionJson), definitionJson, out message)) return false;

                var findings = new DefinitionFindings();
                DefinitionParser.Parse(definitionJson, findings);
                errorCount = findings.Total;
                reportJson = WriteReport(findings);
                return true; // validation ran; whether the definition is valid is in errorCount and the report
            }
        }

        private static bool CheckDefinitionText(string operation, string definitionJson, out string message)
        {
            message = null;
            if (definitionJson == null) { message = operation + " failed: definitionJson is required."; return false; }
            if (definitionJson.Length > ReconciliationDefinition.MaxDefinitionJsonCharacters)
            {
                message = operation + " failed: the definition is longer than " + ReconciliationDefinition.MaxDefinitionJsonCharacters + " characters.";
                return false;
            }
            return true;
        }

        private static string WriteReport(DefinitionFindings findings)
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

        /// <summary>Discards the results of the last run (a successful setup change makes them stale). Called with the instance lock held.</summary>
        private void InvalidateResults() { results = null; ResetCursors(); }

        private void ResetCursors() { exceptionPosition = 0; currentException = null; differencePosition = 0; }

        /// <summary>Fails with the standard disposed message or an actionable no-results message; otherwise hands back the published run. Called with the lock held.</summary>
        private bool Available(string operation, out ReconciliationSnapshot run, out string message)
        {
            run = results;
            message = null;
            if (disposed) { message = DisposedMessage(operation); return false; }
            if (run == null) { message = operation + " failed: there are no results; run ReconcileJson or ReconcileDataTables first (a failed run, a setup change or ClearResults discards them)."; return false; }
            return true;
        }

        /// <summary>Result IDs are <c>r</c> plus the 1-based position, so a lookup is an index check rather than a scan.</summary>
        private static ReconciliationResult FindResult(ReconciliationSnapshot run, string resultId)
        {
            if (resultId == null || resultId.Length < 2 || resultId.Length > 12 || resultId[0] != 'r') return null;
            if (!int.TryParse(resultId.Substring(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int position)) return null;
            if (position < 1 || position > run.Results.Count) return null;
            ReconciliationResult candidate = run.Results[position - 1];
            return candidate.Id == resultId ? candidate : null;
        }

        /// <summary>The last completed run, or null (test seam).</summary>
        internal ReconciliationSnapshot Snapshot { get { lock (syncRoot) return results; } }


        // ------------------------------------------------------------------ running

        /// <summary>
        /// Reconciles two JSON arrays. The whole call runs under the instance lock, so it is serialized with every setup call. Any earlier results
        /// are discarded first, so a run that fails leaves nothing behind that could be mistaken for its outcome; a new snapshot is built privately
        /// and published only when the whole run succeeded.
        /// </summary>
        private bool RunReconciliation(string leftJson, string rightJson, out int exceptionCount, out string message)
        {
            return RunWith(nameof(ReconcileJson), (ReconciliationDefinition d, out IRowSource left, out IRowSource right, out string failure) =>
            {
                right = null;
                failure = null;
                if (!JsonInput.TryParse(leftJson, "Left", d.Limits, out JsonInput leftInput, out InputFailure leftFailure)) { left = null; failure = leftFailure.Message; return false; }
                left = leftInput;
                if (!JsonInput.TryParse(rightJson, "Right", d.Limits, out JsonInput rightInput, out InputFailure rightFailure)) { failure = rightFailure.Message; return false; }
                right = rightInput;
                return true;
            }, out exceptionCount, out message);
        }

        private bool RunTableReconciliation(DataTable leftTable, DataTable rightTable, out int exceptionCount, out string message)
        {
            return RunWith(nameof(ReconcileDataTables), (ReconciliationDefinition d, out IRowSource left, out IRowSource right, out string failure) =>
            {
                left = null;
                right = null;
                failure = null;

                // In a table, a pointer names exactly one column, so /Amount or /a~1b; anything deeper cannot be a column.
                foreach (KeyMappingDef k in d.Keys)
                {
                    if (k.LeftSegments.Length != 1 || k.RightSegments.Length != 1) { failure = "key mapping '" + k.Name + "' must use pointers that each name exactly one column, such as /Amount"; return false; }
                }
                foreach (ComparisonDef c in d.Comparisons)
                {
                    if (c.LeftSegments.Length != 1 || c.RightSegments.Length != 1
                        || (c.Kind == RuleKind.Money && (c.LeftCurrencySegments.Length != 1 || c.RightCurrencySegments.Length != 1))) { failure = "comparison '" + c.Name + "' must use pointers that each name exactly one column, such as /Amount"; return false; }
                }

                if (!DataTableInput.TryRead(leftTable, "Left", ColumnPointers(d, true), d.Limits, tableLimits, out DataTableInput leftInput, out failure)) return false;
                if (!DataTableInput.TryRead(rightTable, "Right", ColumnPointers(d, false), d.Limits, tableLimits, out DataTableInput rightInput, out failure)) return false;
                left = leftInput;
                right = rightInput;
                return true;
            }, out exceptionCount, out message);
        }

        private static IEnumerable<string[]> ColumnPointers(ReconciliationDefinition d, bool leftSide)
        {
            foreach (KeyMappingDef k in d.Keys) yield return leftSide ? k.LeftSegments : k.RightSegments;
            foreach (ComparisonDef c in d.Comparisons)
            {
                yield return leftSide ? c.LeftSegments : c.RightSegments;
                if (c.Kind == RuleKind.Money) yield return leftSide ? c.LeftCurrencySegments : c.RightCurrencySegments;
            }
        }

        private delegate bool InputLoader(ReconciliationDefinition definition, out IRowSource left, out IRowSource right, out string failure);

        /// <summary>
        /// The run every entry point shares. The whole call runs under the instance lock, so it is serialized with every setup call. Any earlier results
        /// are discarded first, so a run that fails leaves nothing behind that could be mistaken for its outcome; a new snapshot is built privately
        /// and published only when the whole run succeeded.
        /// </summary>
        private bool RunWith(string operation, InputLoader load, out int exceptionCount, out string message)
        {
            exceptionCount = 0;
            message = null;
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(operation); return false; }
                InvalidateResults();                                // stale results must not survive an attempted run, whatever its outcome

                if (definition.Keys.Count == 0)
                {
                    message = operation + " failed: the definition has no key mapping; add one with AddKeyMapping or AddKeyMappingSimple, or load a definition with keys.";
                    return false;
                }

                ReconciliationDefinition snapshotOfDefinition = definition;   // definitions are replaced whole, never edited in place
                IRowSource left = null, right = null;
                try
                {
                    if (!load(snapshotOfDefinition, out left, out right, out string loadFailure)) { message = operation + " failed: " + loadFailure; return false; }

                    if (!ReconciliationCore.TryRun(snapshotOfDefinition, left, right, out ReconciliationSnapshot snapshot, out string failure))
                    {
                        message = operation + " failed: " + failure + ".";
                        return false;
                    }
                    results = snapshot;                     // the cursors were reset when the run began, under the same lock
                    exceptionCount = snapshot.Summary.ExceptionCount;
                    return true;
                }
                finally
                {
                    (left as IDisposable)?.Dispose();       // parsed JSON is released as soon as the snapshot exists; it holds only what it needs
                    (right as IDisposable)?.Dispose();
                }
            }
        }

        private static string DisposedMessage(string operation) => operation + " failed: the component has been disposed.";

        /// <summary>Releases retained inputs and results. Idempotent.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (syncRoot) { disposed = true; results = null; ResetCursors(); }
            }
            base.Dispose(disposing);
        }
    }
}
