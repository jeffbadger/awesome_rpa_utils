using System.Collections.Generic;

namespace ReconciliationAutomation
{
    /// <summary>What a result describes. Each kind is one unit: a pair, a record, a key group or a bad row.</summary>
    internal enum ResultKind
    {
        /// <summary>One unique left/right pair; every configured comparison agrees (or there are none).</summary>
        Matched,
        /// <summary>One unique pair; at least one comparison disagrees and none is invalid.</summary>
        Different,
        /// <summary>One unique pair; at least one comparison is invalid (unequal ones are kept too).</summary>
        InvalidComparison,
        /// <summary>One valid-key left record with no right counterpart.</summary>
        OnlyLeft,
        /// <summary>One valid-key right record with no left counterpart.</summary>
        OnlyRight,
        /// <summary>One ambiguous key group, with every member on both sides.</summary>
        DuplicateKey,
        /// <summary>One row that could not enter the key index (not an object, or its key is missing or unusable).</summary>
        InvalidRecord
    }

    /// <summary>One source row that belongs to a result.</summary>
    internal sealed class ResultMember
    {
        internal ResultMember(string side, int row, string[] originalKey)
        {
            Side = side;
            Row = row;
            OriginalKey = originalKey;
        }

        /// <summary><c>Left</c> or <c>Right</c>.</summary>
        internal string Side { get; }

        /// <summary>The zero-based row index in its input array.</summary>
        internal int Row { get; }

        /// <summary>The key parts as found in this row (before trimming); null for an invalid row, which has no usable key.</summary>
        internal string[] OriginalKey { get; }
    }

    /// <summary>One result of a run. Immutable once the run has published its snapshot.</summary>
    internal sealed class ReconciliationResult
    {
        /// <summary>Local reference within one snapshot: <c>r000001</c>, <c>r000002</c>, ... in emission order. Not a persistent identity.</summary>
        internal string Id { get; set; }
        internal ResultKind Kind { get; set; }

        /// <summary>The normalized key parts (what was matched on); null for an invalid row.</summary>
        internal string[] NormalizedKey { get; set; }

        /// <summary>Every source row involved, left members before right members, each side in row order.</summary>
        internal IReadOnlyList<ResultMember> Members { get; set; }

        /// <summary>A stable code (for an invalid row, <c>NonObjectRow</c>/<c>MissingKey</c>/<c>InvalidKeyType</c>/<c>EmptyKey</c>; for a duplicate group <c>DuplicateNormalizedKey</c>; for a pair the code of its first problem or difference); null when there is none.</summary>
        internal string ReasonCode { get; set; }

        internal string Explanation { get; set; }

        /// <summary>The comparisons that did not agree, in rule order: unequal ones and invalid ones. Empty for every other kind.</summary>
        internal IReadOnlyList<ComparisonOutcome> Differences { get; set; }

        /// <summary>Invalid rows only: the key mapping and the pointer (on that row's side) that failed.</summary>
        internal string MappingName { get; set; }
        internal string Pointer { get; set; }

        /// <summary>The single row index on a side, or -1 when that side has none or several (a group).</summary>
        internal int LeftRowIndex => SingleRow("Left");
        internal int RightRowIndex => SingleRow("Right");

        private int SingleRow(string side)
        {
            int found = -1;
            foreach (ResultMember member in Members)
            {
                if (member.Side != side) continue;
                if (found != -1) return -1;
                found = member.Row;
            }
            return found;
        }
    }

    /// <summary>The counts of a run. See the accounting invariants in the design plan.</summary>
    internal sealed class ReconciliationSummary
    {
        internal int LeftRowCount, RightRowCount;
        internal int MatchedPairCount, DifferentPairCount, InvalidPairCount;
        internal int OnlyLeftCount, OnlyRightCount;
        internal int InvalidLeftRowCount, InvalidRightRowCount;
        internal int AmbiguousKeyCount, AmbiguousLeftRowCount, AmbiguousRightRowCount;
        internal int ResultCount, ExceptionCount, DifferenceCount;

        internal bool AllMatched => ExceptionCount == 0;
        internal bool BothInputsEmpty => LeftRowCount == 0 && RightRowCount == 0;

        /// <summary>Checks the accounting equations; returns the first one that does not hold, or null when all do.</summary>
        internal string CheckInvariants()
        {
            if (LeftRowCount != MatchedPairCount + DifferentPairCount + InvalidPairCount + OnlyLeftCount + InvalidLeftRowCount + AmbiguousLeftRowCount)
                return "left rows are not each accounted for exactly once";
            if (RightRowCount != MatchedPairCount + DifferentPairCount + InvalidPairCount + OnlyRightCount + InvalidRightRowCount + AmbiguousRightRowCount)
                return "right rows are not each accounted for exactly once";
            if (ResultCount != MatchedPairCount + DifferentPairCount + InvalidPairCount + OnlyLeftCount + OnlyRightCount + InvalidLeftRowCount + InvalidRightRowCount + AmbiguousKeyCount)
                return "the result count does not add up";
            if (ExceptionCount != ResultCount - MatchedPairCount)
                return "the exception count does not add up";
            return null;
        }
    }

    /// <summary>The published outcome of a completed run: every result and the summary. Built privately and swapped in whole, so it is never seen half-built.</summary>
    internal sealed class ReconciliationSnapshot
    {
        internal ReconciliationSnapshot(IReadOnlyList<ReconciliationResult> results, ReconciliationSummary summary)
        {
            Results = results;
            Summary = summary;
        }

        internal IReadOnlyList<ReconciliationResult> Results { get; }
        internal ReconciliationSummary Summary { get; }
    }
}
