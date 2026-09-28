using System;
using System.Collections;
using System.Collections.Generic;

namespace Exerciser
{
    /// <summary>
    /// Prints a never-throws call's result. Labels a null message as a "normal
    /// negative" rather than leaving it blank, so the documented distinction
    /// between "normal negative" (false, message == null) and "real failure"
    /// (false, message != null) is never missed.
    /// </summary>
    internal static class Report
    {
        internal static void Result(bool ok, string message, params (string Name, object Value)[] outs)
        {
            Console.WriteLine();
            Console.WriteLine(ok ? "Result: true" : "Result: false");
            Console.WriteLine(message == null
                ? (ok ? "Message: (none)" : "Message: (no message - normal negative)")
                : $"Message: {message}");

            foreach ((string name, object value) in outs)
            {
                Console.WriteLine($"  {name}: {Format(value)}");
            }
            Console.WriteLine();
        }

        private static string Format(object value)
        {
            if (value == null)
            {
                return "(null)";
            }
            if (value is string text)
            {
                return text;
            }
            if (value is IEnumerable enumerable)
            {
                var items = new List<string>();
                foreach (object item in enumerable)
                {
                    items.Add(item?.ToString() ?? "(null)");
                }
                return "[" + string.Join(", ", items) + "]";
            }
            return value.ToString();
        }
    }
}
