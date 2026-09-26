using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// Work in progress (see the design plan). The definition operations (setup, JSON load/validate/export, limits) are
    /// implemented; reconciliation and result reading still report that they are not implemented yet.
    /// </remarks>
    [Description("Reconciles two datasets by business key and reports matches, differences, missing and duplicate records through scalar ports. Under construction: the definition operations work; running a reconciliation and reading its results are not implemented yet. Never throws.")]
    public sealed class ReconciliationUtils : Component
    {
        private readonly object syncRoot = new object();
        private bool disposed;
        private ReconciliationDefinition definition = new ReconciliationDefinition();

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
            try { return ChangeDefinition(nameof(ClearDefinition), d => { d.Keys = new List<KeyMappingDef>(); d.Comparisons = new List<ComparisonDef>(); d.Limits = new ReconciliationLimits(); return null; }, out message); }
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

        /// <summary>Reconciles two JSON arrays of objects. True means the run completed, even with mismatches; exceptionCount is 0 when everything matched.</summary>
        [Category("Reconciliation - Run")]
        [Description("Reconciles two JSON arrays of objects. True means the run completed, even with mismatches; exceptionCount is 0 when everything matched. Never throws.")]
        public bool ReconcileJson(string leftJson, string rightJson, out int exceptionCount, out string message)
        {
            message = null;
            exceptionCount = 0;
            try { return NotYetImplemented(nameof(ReconcileJson), out message); }
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
            try { return NotYetImplemented(nameof(GetSummary), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetSummary), ex); return false; }
        }

        /// <summary>Returns every count of the last completed run as JSON.</summary>
        [Category("Reconciliation - Results")]
        [Description("Returns every count of the last completed run as JSON. Never throws.")]
        public bool GetSummaryJson(out string summaryJson, out string message)
        {
            message = null;
            summaryJson = null;
            try { return NotYetImplemented(nameof(GetSummaryJson), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetSummaryJson), ex); return false; }
        }

        /// <summary>Restarts exception and difference reading from the first exception.</summary>
        [Category("Reconciliation - Results")]
        [Description("Restarts exception and difference reading from the first exception. Never throws.")]
        public bool ResetResultCursor(out string message)
        {
            message = null;
            try { return NotYetImplemented(nameof(ResetResultCursor), out message); }
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
            try { return NotYetImplemented(nameof(TryReadNextException), out message); }
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
            try { return NotYetImplemented(nameof(TryReadNextDifference), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(TryReadNextDifference), ex); return false; }
        }

        /// <summary>Returns the full detail of one result, including every member of a duplicate-key group.</summary>
        [Category("Reconciliation - Results")]
        [Description("Returns the full detail of one result, including every member of a duplicate-key group. Never throws.")]
        public bool GetResultJson(string resultId, out string resultJson, out string message)
        {
            message = null;
            resultJson = null;
            try { return NotYetImplemented(nameof(GetResultJson), out message); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(GetResultJson), ex); return false; }
        }

        /// <summary>Discards the last run's results. Succeeds even when there are none.</summary>
        [Category("Reconciliation - Results")]
        [Description("Discards the last run's results. Succeeds even when there are none. Never throws.")]
        public bool ClearResults(out string message)
        {
            message = null;
            try { return NotYetImplemented(nameof(ClearResults), out message); }
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
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(nameof(LoadDefinitionJson)); return false; }
            }
            if (!CheckDefinitionText(nameof(LoadDefinitionJson), definitionJson, out message)) return false;

            var findings = new DefinitionFindings();
            ReconciliationDefinition parsed = DefinitionParser.Parse(definitionJson, findings);
            if (parsed == null)
            {
                string more = findings.Total > 1 ? " (" + (findings.Total - 1) + " more problem(s); ValidateDefinitionJson lists them all)" : string.Empty;
                message = nameof(LoadDefinitionJson) + " failed: " + findings.Reported[0].Format() + more;
                return false;
            }
            lock (syncRoot)
            {
                if (disposed) { message = DisposedMessage(nameof(LoadDefinitionJson)); return false; }
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
            }
            if (!CheckDefinitionText(nameof(ValidateDefinitionJson), definitionJson, out message)) return false;

            var findings = new DefinitionFindings();
            DefinitionParser.Parse(definitionJson, findings);
            errorCount = findings.Total;
            reportJson = WriteReport(findings);
            return true; // validation ran; whether the definition is valid is in errorCount and the report
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

        /// <summary>Discards any results and cursors. There are none yet (reconciliation arrives in a later work package); every setup change already calls this.</summary>
        private void InvalidateResults() { }

        private static string DisposedMessage(string operation) => operation + " failed: the component has been disposed.";

        private bool NotYetImplemented(string operation, out string message)
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    message = DisposedMessage(operation);
                    return false;
                }
            }
            message = operation + " is not implemented yet.";
            return false;
        }

        /// <summary>Releases retained inputs and results. Idempotent.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (syncRoot) { disposed = true; }
            }
            base.Dispose(disposing);
        }
    }
}
