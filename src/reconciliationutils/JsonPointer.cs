using System.Text;

namespace ReconciliationAutomation
{
    /// <summary>
    /// The restricted JSON Pointer syntax used to select a field: a leading <c>/</c> and object-property segments, with
    /// <c>~0</c> for <c>~</c> and <c>~1</c> for <c>/</c>. The empty (whole-row) pointer, invalid escapes and array
    /// traversal are unsupported. Empty segments are valid (<c>/</c> selects the property whose name is empty).
    /// </summary>
    internal static class JsonPointer
    {
        internal const int MaxLength = 1024;

        /// <summary>Parses a pointer; on failure <paramref name="error"/> says why (it never echoes anything but the pointer itself).</summary>
        internal static bool TryParse(string pointer, out string[] segments, out string error)
        {
            segments = null;
            error = null;
            if (string.IsNullOrEmpty(pointer))
            {
                error = "a pointer is required (for example /invoiceNumber); the empty pointer would select the whole row, which is not supported";
                return false;
            }
            if (pointer.Length > MaxLength)
            {
                error = "the pointer is longer than " + MaxLength + " characters";
                return false;
            }
            if (pointer[0] != '/')
            {
                error = "the pointer must start with '/' (for example /invoiceNumber)";
                return false;
            }

            string[] raw = pointer.Substring(1).Split('/');
            var result = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                if (!TryUnescape(raw[i], out result[i]))
                {
                    error = "segment " + (i + 1) + " has an invalid escape: '~' must be followed by '0' (for '~') or '1' (for '/')";
                    return false;
                }
            }
            segments = result;
            return true;
        }

        private static bool TryUnescape(string segment, out string unescaped)
        {
            if (segment.IndexOf('~') < 0)
            {
                unescaped = segment;
                return true;
            }
            var builder = new StringBuilder(segment.Length);
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (c != '~')
                {
                    builder.Append(c);
                    continue;
                }
                if (i + 1 >= segment.Length || (segment[i + 1] != '0' && segment[i + 1] != '1'))
                {
                    unescaped = null;
                    return false;
                }
                builder.Append(segment[i + 1] == '0' ? '~' : '/');
                i++;
            }
            unescaped = builder.ToString();
            return true;
        }
    }
}
