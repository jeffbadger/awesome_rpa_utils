using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using EventAutomation.Native;

namespace EventAutomation
{
    /// <summary>
    /// A window-event filter. All fields are optional — an unset field is a
    /// wildcard. Build one fluently with <see cref="Create"/>, or parse the
    /// compact JSON form that crosses the Pega boundary (see <see cref="FromJson"/>).
    /// </summary>
    public class EventFilter
    {
        private string _process;
        private readonly HashSet<string> _anyOfProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _class;
        private string _titleContains;
        private Regex _titleRegex;
        // Set when TitleMatches was given a pattern that failed to compile: the
        // filter then fails closed (Matches returns false for everything) rather
        // than silently degrading the invalid regex into a wildcard.
        private bool _hasRegexError;
        private bool _hasButtonChildren;
        private bool _excludeSelf;

        /// <summary>Starts a fluent filter builder.</summary>
        public static EventFilter Create() => new EventFilter();

        /// <summary>Matches a single process name (case-insensitive).</summary>
        public EventFilter Process(string name) { _process = name; return this; }

        /// <summary>Matches any of the given process names (case-insensitive).</summary>
        public EventFilter AnyOfProcesses(params string[] names)
        {
            if (names != null)
                foreach (var n in names)
                    if (!string.IsNullOrWhiteSpace(n))
                        _anyOfProcesses.Add(n);
            return this;
        }

        /// <summary>Matches a window class name (case-insensitive).</summary>
        public EventFilter Class(string name) { _class = name; return this; }

        /// <summary>Requires the window title to contain this text (case-insensitive).</summary>
        public EventFilter TitleContains(string text) { _titleContains = text; return this; }

        /// <summary>
        /// Requires the window title to match this regex (IgnoreCase | Compiled,
        /// with a 250 ms match timeout). A pattern that fails to compile makes the
        /// filter fail closed — <see cref="Matches"/> returns False for everything
        /// — instead of silently behaving as a wildcard.
        /// </summary>
        public EventFilter TitleMatches(string pattern)
        {
            _titleRegex = CompileRegex(pattern, out string error);
            _hasRegexError = error != null;
            return this;
        }

        /// <summary>Requires (or excludes) a Button child window (dialog heuristic).</summary>
        public EventFilter HasButtonChildren(bool value) { _hasButtonChildren = value; return this; }

        /// <summary>Skips events whose process id equals the engine host process.</summary>
        public EventFilter ExcludeSelf(bool value) { _excludeSelf = value; return this; }

        /// <summary>
        /// Parses the compact JSON filter form, e.g.
        /// <c>{"process":"notepad","class":"#32770","titleContains":"Save"}</c>.
        /// Unknown keys are ignored. Returns <c>null</c> on malformed JSON (the
        /// caller decides whether that means "match-all" or "reject").
        /// </summary>
        public static EventFilter FromJson(string json)
        {
            return TryFromJson(json, out var filter, out _) ? filter : null;
        }

        /// <summary>
        /// Parses the compact JSON filter form and reports why it was rejected.
        /// Returns True on success. Fails with an error when the JSON is
        /// malformed/not an object, or when the <c>titleMatches</c> regex does
        /// not compile — nothing is silently degraded to a wildcard.
        /// Null/empty <paramref name="json"/> is a match-all filter (no error).
        /// </summary>
        public static bool TryFromJson(string json, out EventFilter filter, out string error)
        {
            filter = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
                return true; // null/empty = match-all; caller combines with Create()
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = "Filter JSON must be an object.";
                    return false;
                }
                filter = new EventFilter();
                foreach (var prop in root.EnumerateObject())
                {
                    switch (prop.Name)
                    {
                        case "process":
                            filter._process = prop.Value.GetString();
                            break;
                        case "processes":
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                                foreach (var item in prop.Value.EnumerateArray())
                                {
                                    var s = item.GetString();
                                    if (!string.IsNullOrWhiteSpace(s))
                                        filter._anyOfProcesses.Add(s);
                                }
                            break;
                        case "class":
                            filter._class = prop.Value.GetString();
                            break;
                        case "titleContains":
                            filter._titleContains = prop.Value.GetString();
                            break;
                        case "titleMatches":
                            filter._titleRegex = CompileRegex(prop.Value.GetString(), out string regexError);
                            if (regexError != null)
                            {
                                error = regexError;
                                filter = null;
                                return false;
                            }
                            break;
                        case "hasButtonChildren":
                            filter._hasButtonChildren = prop.Value.ValueKind == JsonValueKind.True;
                            break;
                        case "excludeSelf":
                            filter._excludeSelf = prop.Value.ValueKind == JsonValueKind.True;
                            break;
                        // Unknown keys are intentionally ignored.
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                filter = null;
                error = "Malformed filter JSON: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Evaluates this filter against an event. <paramref name="hostPid"/> is
        /// the engine host process id, used only when <see cref="ExcludeSelf"/> is set.
        /// </summary>
        public bool Matches(EventData e, uint hostPid)
        {
            if (e == null)
                return false;
            if (_hasRegexError)
                return false; // fail closed: an uncompilable TitleMatches matches nothing
            string procName = NormalizeProcessName(e.ProcessName);
            if (_process != null && !string.Equals(procName, _process, StringComparison.OrdinalIgnoreCase))
                return false;
            if (_anyOfProcesses.Count > 0 && !_anyOfProcesses.Contains(procName))
                return false;
            if (_class != null && !string.Equals(e.ClassName, _class, StringComparison.OrdinalIgnoreCase))
                return false;
            if (_titleContains != null && (e.Title == null || e.Title.IndexOf(_titleContains, StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (_titleRegex != null && (e.Title == null || !_titleRegex.IsMatch(e.Title)))
                return false;
            if (_hasButtonChildren && !HasButtonChild(e.Hwnd))
                return false;
            if (_excludeSelf && e.ProcessId == hostPid)
                return false;
            return true;
        }

        /// <summary>Strips a trailing ".exe" so "notepad" matches "notepad.exe".</summary>
        private static string NormalizeProcessName(string name)
        {
            if (name == null)
                return null;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 4);
            return name;
        }

        /// <summary>
        /// Compiles with IgnoreCase|Compiled and a 250 ms match timeout so a
        /// catastrophic-backtracking pattern cannot stall a caller indefinitely.
        /// Returns null and sets <paramref name="error"/> for an invalid pattern.
        /// </summary>
        private static Regex CompileRegex(string pattern, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(pattern))
                return null;
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
            }
            catch (Exception ex)
            {
                error = "Invalid regular expression '" + pattern + "': " + ex.Message;
                return null;
            }
        }

        // Enumerating child windows is not free, and for a busy window (hundreds
        // of controls) it happens on the WinEvent hook thread. Cache the result
        // per hwnd for a short window so repeated events for the same dialog do
        // not re-enumerate; a hung window can at worst stall the pump once per
        // cache entry, not once per event.
        private static readonly ConcurrentDictionary<uint, (long Ticks, bool Found)> ButtonChildCache =
            new ConcurrentDictionary<uint, (long, bool)>();
        private static readonly TimeSpan ButtonChildCacheTtl = TimeSpan.FromMilliseconds(500);

        private static bool HasButtonChild(uint hwnd)
        {
            if (hwnd == 0)
                return false;
            long now = Environment.TickCount64;
            if (ButtonChildCache.TryGetValue(hwnd, out var cached) && now - cached.Ticks < ButtonChildCacheTtl.TotalMilliseconds)
                return cached.Found;
            bool found = false;
            WinEventInterop.EnumChildWindows(
                new IntPtr((long)hwnd),
                (child, lParam) =>
                {
                    var sb = new System.Text.StringBuilder(64);
                    WinEventInterop.GetClassName(child, sb, sb.Capacity);
                    if (string.Equals(sb.ToString(), "Button", StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        return false; // stop enumerating
                    }
                    return true;
                },
                IntPtr.Zero);
            ButtonChildCache[hwnd] = (now, found);
            if (ButtonChildCache.Count > 1024)
                ButtonChildCache.Clear(); // crude bound; hwnds are cheap to recompute
            return found;
        }
    }
}
