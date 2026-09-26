using System;
using System.Collections.Generic;
using System.Text;

namespace ReconciliationAutomation
{
    /// <summary>The outcome of extracting a row's business key.</summary>
    internal sealed class KeyExtraction
    {
        private KeyExtraction() { }

        internal bool IsValid { get; private set; }

        /// <summary>The key parts exactly as found (a number key is its integer token). Valid keys only.</summary>
        internal string[] Original { get; private set; }

        /// <summary>The key parts after each mapping's trimming. Valid keys only.</summary>
        internal string[] Normalized { get; private set; }

        /// <summary>A stable code (<c>MissingKey</c>, <c>InvalidKeyType</c>, <c>EmptyKey</c>). Invalid keys only.</summary>
        internal string ReasonCode { get; private set; }

        /// <summary>The mapping that failed. Invalid keys only.</summary>
        internal string MappingName { get; private set; }

        /// <summary>The pointer (for this side) that failed. Invalid keys only.</summary>
        internal string Pointer { get; private set; }

        internal static KeyExtraction Valid(string[] original, string[] normalized) =>
            new KeyExtraction { IsValid = true, Original = original, Normalized = normalized };

        internal static KeyExtraction Invalid(string reasonCode, string mappingName, string pointer) =>
            new KeyExtraction { IsValid = false, ReasonCode = reasonCode, MappingName = mappingName, Pointer = pointer };
    }

    /// <summary>
    /// Builds structured composite keys. A key is an ordered tuple of normalized strings, never a delimiter-joined string, so
    /// content that merely looks like a delimiter cannot create a collision. Equality and hashing use the same per-part rule.
    /// </summary>
    internal static class ReconciliationKey
    {
        /// <summary>Extracts the key of one row. <paramref name="left"/> chooses which side's pointers to use.</summary>
        internal static KeyExtraction Extract(IRowReader row, IReadOnlyList<KeyMappingDef> mappings, bool left)
        {
            var original = new string[mappings.Count];
            var normalized = new string[mappings.Count];
            for (int i = 0; i < mappings.Count; i++)
            {
                KeyMappingDef mapping = mappings[i];
                string pointer = left ? mapping.LeftPointer : mapping.RightPointer;
                FieldValue value = row.Read(left ? mapping.LeftSegments : mapping.RightSegments);

                switch (value.Kind)
                {
                    case FieldKind.Missing:
                    case FieldKind.Null:
                        return KeyExtraction.Invalid("MissingKey", mapping.Name, pointer);
                    case FieldKind.String:
                    case FieldKind.Integer:
                        break; // a string, or an integer token used as its exact text
                    default:
                        return KeyExtraction.Invalid("InvalidKeyType", mapping.Name, pointer);
                }

                string text = mapping.Trim ? value.Text.Trim() : value.Text;
                if (text.Length == 0) return KeyExtraction.Invalid("EmptyKey", mapping.Name, pointer);
                original[i] = value.Text;
                normalized[i] = text;
            }
            return KeyExtraction.Valid(original, normalized);
        }

        /// <summary>The display form of a key: a JSON array of its normalized parts, for example <c>["A","INV-101"]</c>.</summary>
        internal static string ToDisplayJson(string[] normalized)
        {
            var builder = new StringBuilder("[");
            for (int i = 0; i < normalized.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append('"');
                string part = normalized[i];
                for (int j = 0; j < part.Length; j++)
                {
                    char c = part[j];
                    if (c == '"' || c == '\\') builder.Append('\\').Append(c);
                    else if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else if (char.IsHighSurrogate(c) && j + 1 < part.Length && char.IsLowSurrogate(part[j + 1]))
                    {
                        builder.Append(c).Append(part[j + 1]);   // a proper pair is one valid character: keep it
                        j++;
                    }
                    else if (char.IsSurrogate(c)) builder.Append("\\u").Append(((int)c).ToString("x4"));   // an unpaired half cannot be written as text
                    else builder.Append(c);
                }
                builder.Append('"');
            }
            return builder.Append(']').ToString();
        }

        /// <summary>An equality comparer for normalized keys under a definition's per-part case rules.</summary>
        internal static IEqualityComparer<string[]> ComparerFor(IReadOnlyList<KeyMappingDef> mappings) => new KeyComparer(mappings);

        private sealed class KeyComparer : IEqualityComparer<string[]>
        {
            private readonly StringComparer[] parts;

            internal KeyComparer(IReadOnlyList<KeyMappingDef> mappings)
            {
                parts = new StringComparer[mappings.Count];
                for (int i = 0; i < parts.Length; i++)
                    parts[i] = mappings[i].IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            }

            public bool Equals(string[] x, string[] y)
            {
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (!parts[i].Equals(x[i], y[i])) return false;
                return true;
            }

            public int GetHashCode(string[] key)
            {
                unchecked
                {
                    int hash = 17;
                    for (int i = 0; i < key.Length; i++) hash = hash * 31 + parts[i].GetHashCode(key[i]);
                    return hash;
                }
            }
        }
    }
}
