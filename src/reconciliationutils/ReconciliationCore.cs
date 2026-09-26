using System;
using System.Collections.Generic;
using System.Linq;

namespace ReconciliationAutomation
{
    /// <summary>
    /// Matching and classification. Both inputs are indexed by structured key first; then every row is classified exactly once, in a fixed
    /// order, and every unique pair is compared under every rule. Never a Cartesian join and never a scan of the other side: the work is
    /// O(rows + comparisons of unique pairs).
    /// </summary>
    internal static class ReconciliationCore
    {
        private sealed class RowInfo
        {
            internal bool NonObject;
            internal KeyExtraction Key;          // null for a non-object row
            internal bool IsValid => !NonObject && Key.IsValid;
        }

        /// <summary>
        /// Runs a reconciliation. On success <paramref name="snapshot"/> is complete; on failure it is null and <paramref name="failure"/> says why
        /// (a limit that was exceeded, or an internal check that did not hold), without quoting any data. Nothing is partly published.
        /// </summary>
        internal static bool TryRun(ReconciliationDefinition definition, IRowSource left, IRowSource right, out ReconciliationSnapshot snapshot, out string failure)
        {
            snapshot = null;
            failure = null;

            IReadOnlyList<KeyMappingDef> keys = definition.Keys;
            IEqualityComparer<string[]> comparer = ReconciliationKey.ComparerFor(keys);

            RowInfo[] leftRows = Extract(left, keys, true);
            RowInfo[] rightRows = Extract(right, keys, false);
            Dictionary<string[], List<int>> leftIndex = Index(leftRows, comparer);
            Dictionary<string[], List<int>> rightIndex = Index(rightRows, comparer);

            var summary = new ReconciliationSummary { LeftRowCount = leftRows.Length, RightRowCount = rightRows.Length };
            var results = new List<ReconciliationResult>();
            var emitted = new HashSet<string[]>(comparer);           // keys whose group already has its result
            int differenceCount = 0;
            int maximumResults = definition.Limits.MaximumResults;
            int maximumDifferences = definition.Limits.MaximumDifferenceDetails;
            string failed = null;                                    // (a local function cannot capture the out parameter)

            bool Add(ReconciliationResult result)
            {
                if (results.Count >= maximumResults)
                {
                    failed = "the run would produce more than " + maximumResults + " results (the limit is set with ConfigureLimits)";
                    return false;
                }
                differenceCount += result.Differences.Count;
                if (differenceCount > maximumDifferences)
                {
                    failed = "the run would produce more than " + maximumDifferences + " field differences (the limit is set with ConfigureLimits)";
                    return false;
                }
                result.Id = "r" + (results.Count + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
                results.Add(result);
                return true;
            }

            // Left rows in source order: each emits an invalid row, or the result of its key group at the group's first left member.
            for (int i = 0; i < leftRows.Length; i++)
            {
                RowInfo row = leftRows[i];
                ReconciliationResult result;
                if (!row.IsValid) result = InvalidRecord("Left", i, row, keys, summary);
                else
                {
                    string[] key = row.Key.Normalized;
                    if (!emitted.Add(key)) continue;                   // its group was already reported at an earlier left member
                    List<int> leftMembers = leftIndex[key];
                    rightIndex.TryGetValue(key, out List<int> rightMembers);
                    result = Group(definition, key, leftMembers, rightMembers, leftRows, rightRows, left, right, summary);
                }
                if (!Add(result)) { failure = failed; return false; }
            }

            // Right rows in source order: whatever is still unreported (invalid rows, right-only records, groups of right rows alone).
            for (int j = 0; j < rightRows.Length; j++)
            {
                RowInfo row = rightRows[j];
                ReconciliationResult result;
                if (!row.IsValid) result = InvalidRecord("Right", j, row, keys, summary);
                else
                {
                    string[] key = row.Key.Normalized;
                    if (!emitted.Add(key)) continue;                   // matched, or reported with its left counterpart(s)
                    result = Group(definition, key, null, rightIndex[key], leftRows, rightRows, left, right, summary);
                }
                if (!Add(result)) { failure = failed; return false; }
            }

            summary.ResultCount = results.Count;
            summary.ExceptionCount = results.Count - summary.MatchedPairCount;
            summary.DifferenceCount = differenceCount;

            string broken = summary.CheckInvariants();
            if (broken != null)
            {
                failure = "an internal consistency check failed (" + broken + "); no results were published";
                return false;
            }
            snapshot = new ReconciliationSnapshot(results, summary);
            return true;
        }

        // ------------------------------------------------------------------ indexing

        private static RowInfo[] Extract(IRowSource input, IReadOnlyList<KeyMappingDef> keys, bool leftSide)
        {
            var rows = new RowInfo[input.RowCount];
            for (int i = 0; i < rows.Length; i++)
            {
                IRowReader reader = input.RowAt(i);
                rows[i] = reader.IsObject
                    ? new RowInfo { Key = ReconciliationKey.Extract(reader, keys, leftSide) }
                    : new RowInfo { NonObject = true };
            }
            return rows;
        }

        private static Dictionary<string[], List<int>> Index(RowInfo[] rows, IEqualityComparer<string[]> comparer)
        {
            var index = new Dictionary<string[], List<int>>(comparer);
            for (int i = 0; i < rows.Length; i++)
            {
                if (!rows[i].IsValid) continue;                       // an invalid row never enters the key index
                string[] key = rows[i].Key.Normalized;
                if (!index.TryGetValue(key, out List<int> list)) index[key] = list = new List<int>();
                list.Add(i);
            }
            return index;
        }

        // ------------------------------------------------------------------ classification

        private static ReconciliationResult InvalidRecord(string side, int row, RowInfo info, IReadOnlyList<KeyMappingDef> keys, ReconciliationSummary summary)
        {
            if (side == "Left") summary.InvalidLeftRowCount++; else summary.InvalidRightRowCount++;
            var result = new ReconciliationResult
            {
                Kind = ResultKind.InvalidRecord,
                Members = new[] { new ResultMember(side, row, null) },
                Differences = Array.Empty<ComparisonOutcome>()
            };
            if (info.NonObject)
            {
                result.ReasonCode = "NonObjectRow";
                result.Explanation = "The " + side.ToLowerInvariant() + " row is not a JSON object, so it has no key.";
                return result;
            }
            result.ReasonCode = info.Key.ReasonCode;
            result.MappingName = info.Key.MappingName;
            result.Pointer = info.Key.Pointer;
            result.Explanation = "Key part '" + info.Key.MappingName + "' (" + info.Key.Pointer + ") " + Describe(info.Key.ReasonCode) + ".";
            return result;
        }

        private static string Describe(string code)
        {
            switch (code)
            {
                case "MissingKey": return "is missing or null";
                case "InvalidKeyType": return "is not a string or an integer";
                case "EmptyKey": return "is empty";
                default: return "cannot be used";
            }
        }

        /// <summary>The result for one key: an ambiguous group, a unique pair (compared), or a lone record.</summary>
        private static ReconciliationResult Group(
            ReconciliationDefinition definition, string[] key, List<int> leftMembers, List<int> rightMembers,
            RowInfo[] leftRows, RowInfo[] rightRows, IRowSource left, IRowSource right, ReconciliationSummary summary)
        {
            int leftCount = leftMembers?.Count ?? 0;
            int rightCount = rightMembers?.Count ?? 0;

            var members = new List<ResultMember>(leftCount + rightCount);
            if (leftMembers != null) foreach (int i in leftMembers) members.Add(new ResultMember("Left", i, leftRows[i].Key.Original));
            if (rightMembers != null) foreach (int j in rightMembers) members.Add(new ResultMember("Right", j, rightRows[j].Key.Original));

            // More than one row on either side: ambiguous. No first row is chosen, no rows are zipped, no pairs are formed, and a unique
            // row on the other side belongs to the group; it is not also reported as missing or matched.
            if (leftCount > 1 || rightCount > 1)
            {
                summary.AmbiguousKeyCount++;
                summary.AmbiguousLeftRowCount += leftCount;
                summary.AmbiguousRightRowCount += rightCount;
                return new ReconciliationResult
                {
                    Kind = ResultKind.DuplicateKey,
                    NormalizedKey = key,
                    Members = members,
                    ReasonCode = "DuplicateNormalizedKey",
                    Explanation = "The key matches " + leftCount + " left and " + rightCount + " right rows, so no pair can be chosen.",
                    Differences = Array.Empty<ComparisonOutcome>()
                };
            }

            if (rightCount == 0)
            {
                summary.OnlyLeftCount++;
                return Lone(ResultKind.OnlyLeft, key, members, "No right record has this key.");
            }
            if (leftCount == 0)
            {
                summary.OnlyRightCount++;
                return Lone(ResultKind.OnlyRight, key, members, "No left record has this key.");
            }

            return Pair(definition, key, members, left.RowAt(leftMembers[0]), right.RowAt(rightMembers[0]), summary);
        }

        private static ReconciliationResult Lone(ResultKind kind, string[] key, List<ResultMember> members, string explanation) => new ReconciliationResult
        {
            Kind = kind,
            NormalizedKey = key,
            Members = members,
            Explanation = explanation,
            Differences = Array.Empty<ComparisonOutcome>()
        };

        /// <summary>Compares one unique pair under every rule, in configuration order (never stopping at the first mismatch).</summary>
        private static ReconciliationResult Pair(ReconciliationDefinition definition, string[] key, List<ResultMember> members, IRowReader leftRow, IRowReader rightRow, ReconciliationSummary summary)
        {
            var problems = new List<ComparisonOutcome>();
            bool anyInvalid = false;
            foreach (ComparisonDef rule in definition.Comparisons)
            {
                ComparisonOutcome outcome = ComparisonCore.Evaluate(rule, leftRow, rightRow);
                if (outcome.State == ComparisonState.Equal) continue;
                problems.Add(outcome);
                if (outcome.State == ComparisonState.Invalid) anyInvalid = true;
            }

            var result = new ReconciliationResult { NormalizedKey = key, Members = members, Differences = problems };
            if (problems.Count == 0)
            {
                summary.MatchedPairCount++;
                result.Kind = ResultKind.Matched;
                return result;
            }

            // An invalid comparison outranks a difference, but the unequal fields are kept alongside it.
            ComparisonOutcome first = anyInvalid ? problems.First(p => p.State == ComparisonState.Invalid) : problems[0];
            result.ReasonCode = first.ReasonCode;
            if (anyInvalid)
            {
                summary.InvalidPairCount++;
                result.Kind = ResultKind.InvalidComparison;
                result.Explanation = problems.Count(p => p.State == ComparisonState.Invalid) + " of " + problems.Count + " field problem(s) could not be compared: " + string.Join(", ", problems.Select(p => p.RuleName)) + ".";
            }
            else
            {
                summary.DifferentPairCount++;
                result.Kind = ResultKind.Different;
                result.Explanation = problems.Count + " field(s) differ: " + string.Join(", ", problems.Select(p => p.RuleName)) + ".";
            }
            return result;
        }
    }
}
