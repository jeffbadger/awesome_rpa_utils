using System;
using System.Collections.Generic;
using System.Linq;

namespace Exerciser
{
    /// <summary>Console input helpers, one per parameter shape used across the component menus.</summary>
    internal static class Prompt
    {
        internal static string String(string label, string defaultValue)
        {
            Console.Write(defaultValue != null ? $"{label} [{defaultValue}]: " : $"{label}: ");
            string input = Console.ReadLine();
            return string.IsNullOrEmpty(input) ? defaultValue : input;
        }

        internal static int Int(string label, int defaultValue)
        {
            while (true)
            {
                Console.Write($"{label} [{defaultValue}]: ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    return defaultValue;
                }
                if (int.TryParse(input, out int value))
                {
                    return value;
                }
                Console.WriteLine("  Not a valid integer, try again.");
            }
        }

        internal static bool Bool(string label, bool defaultValue)
        {
            while (true)
            {
                Console.Write($"{label} (y/n) [{(defaultValue ? "y" : "n")}]: ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    return defaultValue;
                }
                if (string.Equals(input, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (string.Equals(input, "n", StringComparison.OrdinalIgnoreCase) || string.Equals(input, "no", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                Console.WriteLine("  Enter y or n.");
            }
        }

        internal static T Enum<T>(string label) where T : struct, System.Enum
        {
            string[] names = System.Enum.GetNames(typeof(T));
            Console.WriteLine($"{label}:");
            for (int i = 0; i < names.Length; i++)
            {
                Console.WriteLine($"  [{i}] {names[i]}");
            }
            while (true)
            {
                Console.Write("> ");
                string input = Console.ReadLine();
                if (int.TryParse(input, out int index) && index >= 0 && index < names.Length)
                {
                    return (T)System.Enum.Parse(typeof(T), names[index]);
                }
                // Enum.TryParse accepts any numeric string as the underlying value,
                // even one no member defines (e.g. "99") - IsDefined rejects that,
                // so an out-of-range number reprompts instead of returning garbage.
                if (System.Enum.TryParse(input, true, out T parsed) && System.Enum.IsDefined(typeof(T), parsed))
                {
                    return parsed;
                }
                Console.WriteLine("  Enter a number from the list, or a valid name.");
            }
        }

        // Prints the example JSON inline, defaulting to it on a bare Enter, so
        // JSON-taking methods are fast to exercise repeatedly.
        internal static string Json(string label, string exampleJson)
        {
            Console.WriteLine($"{label} (JSON). Example:");
            Console.WriteLine($"  {exampleJson}");
            Console.Write("Press Enter to use the example, or type your own: ");
            string input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? exampleJson : input;
        }

        internal static List<string> StringList(string label)
        {
            Console.Write($"{label} (comma or semicolon separated): ");
            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }
            return input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
