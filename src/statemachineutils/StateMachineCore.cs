using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StateMachineAutomation
{
    internal sealed class StateDef
    {
        public string Name;
        public bool Final;
    }

    internal sealed class GuardDef
    {
        public string Key;
        public string Op;
        public string Value;

        internal string Describe() => Op == "exists" || Op == "notExists" ? Key + " " + Op : Key + " " + Op + " " + Value;
    }

    internal sealed class TransitionDef
    {
        public string From;
        public string Trigger;
        public string To;
        public List<GuardDef> Guards = new List<GuardDef>();
    }

    /// <summary>The outcome of parsing/validating a definition. Errors make it unusable; warnings do not.</summary>
    internal sealed class DefinitionReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public MachineDefinition Definition;
        public bool Valid => Errors.Count == 0;
    }

    /// <summary>A history record. Lowercase property names are deliberate: this serializes directly.</summary>
    internal sealed class HistoryEntry
    {
        public long seq { get; set; }
        public string utc { get; set; }
        public string kind { get; set; }
        public string trigger { get; set; }
        public string from { get; set; }
        public string to { get; set; }
        public string reason { get; set; }
        public string detail { get; set; }
    }

    /// <summary>The persisted form of a machine's run state. Lowercase property names are deliberate.</summary>
    internal sealed class PersistedState
    {
        public int schemaVersion { get; set; }
        public string machineName { get; set; }
        public string definitionHash { get; set; }
        public bool started { get; set; }
        public string currentState { get; set; }
        public string enteredUtc { get; set; }
        public long sequence { get; set; }
        public Dictionary<string, string> context { get; set; }
        public List<HistoryEntry> history { get; set; }
    }

    internal sealed class DefinitionDto
    {
        public string name { get; set; }
        public string initial { get; set; }
        public List<StateDto> states { get; set; }
        public List<TransitionDto> transitions { get; set; }
    }

    internal sealed class StateDto
    {
        public string name { get; set; }
        public bool final { get; set; }
    }

    internal sealed class TransitionDto
    {
        public string from { get; set; }
        public string trigger { get; set; }
        public string to { get; set; }
        public List<GuardDto> guards { get; set; }
    }

    internal sealed class GuardDto
    {
        public string key { get; set; }
        public string op { get; set; }
        public string value { get; set; }
    }

    internal sealed class MachineDefinition
    {
        internal const int MaxStates = 500;
        internal const int MaxTransitions = 5000;
        internal const int MaxGuardsPerTransition = 20;
        internal const int MaxNameLength = 128;
        internal const int MaxGuardValueLength = 1024;
        internal const string Wildcard = "*";

        internal static readonly string[] KnownOps = { "equals", "notEquals", "exists", "notExists", "in", "notIn", "greaterThan", "lessThan" };

        public string Name = string.Empty;
        public string Initial;
        public readonly List<StateDef> States = new List<StateDef>();
        public readonly List<TransitionDef> Transitions = new List<TransitionDef>();

        internal StateDef FindState(string name) =>
            name == null ? null : States.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        internal static string NormalizeOp(string op) =>
            KnownOps.FirstOrDefault(k => string.Equals(k, op, StringComparison.OrdinalIgnoreCase)) ?? op;

        internal static bool IsValidName(string value, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(value)) { problem = "is empty"; return false; }
            if (value.Trim().Length > MaxNameLength) { problem = "is longer than " + MaxNameLength + " characters"; return false; }
            return true;
        }

        /// <summary>Applies every cross-cutting rule. Structural (type) problems are reported earlier, by the parser.</summary>
        internal void Validate(DefinitionReport report)
        {
            if (Name != null && Name.Length > MaxNameLength) report.Errors.Add("The machine name is longer than " + MaxNameLength + " characters.");

            if (States.Count == 0) report.Errors.Add("At least one state is required.");
            if (States.Count > MaxStates) report.Errors.Add("A machine may have at most " + MaxStates + " states (found " + States.Count + ").");
            if (Transitions.Count > MaxTransitions) report.Errors.Add("A machine may have at most " + MaxTransitions + " transitions (found " + Transitions.Count + ").");

            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (StateDef state in States)
            {
                if (!IsValidName(state.Name, out string problem)) { report.Errors.Add("A state name " + problem + "."); continue; }
                if (state.Name == Wildcard) { report.Errors.Add("'*' is reserved for transitions and cannot be a state name."); continue; }
                if (seen.TryGetValue(state.Name, out string existing))
                {
                    report.Errors.Add(string.Equals(existing, state.Name, StringComparison.Ordinal)
                        ? "Duplicate state '" + state.Name + "'."
                        : "States '" + existing + "' and '" + state.Name + "' differ only by case; state names are case-insensitive.");
                    continue;
                }
                seen[state.Name] = state.Name;
            }

            StateDef initial = FindState(Initial);
            if (string.IsNullOrWhiteSpace(Initial)) report.Errors.Add("An initial state is required.");
            else if (initial == null) report.Errors.Add("The initial state '" + Initial + "' is not a declared state.");
            else if (initial.Final) report.Errors.Add("The initial state '" + Initial + "' cannot be a final state.");

            for (int i = 0; i < Transitions.Count; i++)
                ValidateTransition(Transitions[i], "Transition #" + (i + 1), report);

            if (report.Errors.Count == 0) AddWarnings(report);
        }

        /// <summary>Validates one transition against the declared states. Shared by whole-definition validation and the incremental AddTransition API.</summary>
        internal void ValidateTransition(TransitionDef t, string label, DefinitionReport report)
        {
            if (t.From != Wildcard)
            {
                StateDef from = FindState(t.From);
                if (from == null) report.Errors.Add(label + " starts from '" + t.From + "', which is not a declared state.");
                else if (from.Final) report.Errors.Add(label + " starts from final state '" + from.Name + "'; a final state cannot have outgoing transitions.");
            }

            if (!IsValidName(t.Trigger, out string triggerProblem)) report.Errors.Add(label + " has a trigger that " + triggerProblem + ".");
            else if (t.Trigger.Trim() == Wildcard) report.Errors.Add(label + " uses '*' as a trigger; '*' is only valid as a 'from' state.");

            if (t.To == Wildcard || FindState(t.To) == null) report.Errors.Add(label + " goes to '" + t.To + "', which is not a declared state.");

            if (t.Guards.Count > MaxGuardsPerTransition) report.Errors.Add(label + " has more than " + MaxGuardsPerTransition + " guards.");
            foreach (GuardDef g in t.Guards)
            {
                if (!IsValidName(g.Key, out string keyProblem)) { report.Errors.Add(label + " has a guard whose key " + keyProblem + "."); continue; }
                if (!KnownOps.Contains(g.Op)) { report.Errors.Add(label + " guard '" + g.Key + "' uses unknown operator '" + g.Op + "' (expected one of: " + string.Join(", ", KnownOps) + ")."); continue; }
                if (g.Op == "exists" || g.Op == "notExists") continue;
                if (g.Value == null) { report.Errors.Add(label + " guard '" + g.Key + " " + g.Op + "' requires a value."); continue; }
                if (g.Value.Length > MaxGuardValueLength) report.Errors.Add(label + " guard '" + g.Key + "' has a value longer than " + MaxGuardValueLength + " characters.");
                if ((g.Op == "greaterThan" || g.Op == "lessThan") && !StateMachineCore.TryParseNumber(g.Value, out _))
                    report.Errors.Add(label + " guard '" + g.Key + " " + g.Op + "' needs a numeric value but got '" + g.Value + "'.");
            }
        }

        private void AddWarnings(DefinitionReport report)
        {
            // Reachability: a wildcard transition is reachable from every reachable non-final state.
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            reachable.Add(FindState(Initial).Name);
            queue.Enqueue(FindState(Initial).Name);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (FindState(current).Final) continue;
                foreach (TransitionDef t in Transitions.Where(t => t.From == Wildcard || string.Equals(t.From, current, StringComparison.OrdinalIgnoreCase)))
                {
                    string target = FindState(t.To).Name;
                    if (reachable.Add(target)) queue.Enqueue(target);
                }
            }
            foreach (StateDef s in States.Where(s => !reachable.Contains(s.Name)))
                report.Warnings.Add("State '" + s.Name + "' is unreachable from the initial state.");

            foreach (StateDef s in States.Where(s => !s.Final && reachable.Contains(s.Name)))
                if (!Transitions.Any(t => t.From == Wildcard || string.Equals(t.From, s.Name, StringComparison.OrdinalIgnoreCase)))
                    report.Warnings.Add("State '" + s.Name + "' is not final but has no outgoing transitions; a machine reaching it can never leave.");

            // Shadowing: an earlier unguarded transition for the same (from, trigger) - or a wildcard for
            // the same trigger - always wins, so later ones can never fire.
            var unguarded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Transitions.Count; i++)
            {
                TransitionDef t = Transitions[i];
                string key = t.From + "|" + t.Trigger.Trim();
                if (unguarded.Contains(key) || unguarded.Contains(Wildcard + "|" + t.Trigger.Trim()))
                    report.Warnings.Add("Transition #" + (i + 1) + " (from '" + t.From + "' on '" + t.Trigger.Trim() + "') can never fire: an earlier unguarded transition already handles that state and trigger.");
                if (t.Guards.Count == 0) unguarded.Add(key);
            }
        }

        internal string ToJson(bool indented)
        {
            var dto = new DefinitionDto
            {
                name = Name ?? string.Empty,
                initial = Initial,
                states = States.Select(s => new StateDto { name = s.Name, final = s.Final }).ToList(),
                transitions = Transitions.Select(t => new TransitionDto
                {
                    from = t.From,
                    trigger = t.Trigger,
                    to = t.To,
                    guards = t.Guards.Count == 0 ? null : t.Guards.Select(g => new GuardDto { key = g.Key, op = g.Op, value = g.Value }).ToList()
                }).ToList()
            };
            return JsonSerializer.Serialize(dto, indented ? StateMachineCore.IndentedOptions : StateMachineCore.CompactOptions);
        }

        internal string ComputeHash()
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(ToJson(false))).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }

    internal static class StateMachineCore
    {
        internal const int MaxContextEntries = 1000;
        internal const int MaxContextValueLength = 4096;
        internal const int MaxMachineNameLength = 64;
        internal const int MaxDefinitionChars = 2000000;

        internal static readonly JsonSerializerOptions CompactOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        internal static readonly JsonSerializerOptions IndentedOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        internal static bool TryParseNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        // ---------------------------------------------------------------- parsing

        private static readonly string[] RootProperties = { "name", "initial", "states", "transitions" };
        private static readonly string[] StateProperties = { "name", "final" };
        private static readonly string[] TransitionProperties = { "from", "trigger", "to", "guards" };
        private static readonly string[] GuardProperties = { "key", "op", "value" };

        private static bool TryProp(JsonElement obj, string name, out JsonElement value)
        {
            foreach (JsonProperty p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
            value = default;
            return false;
        }

        /// <summary>Flags misspelled and repeated properties: silently ignoring the first or the last of two 'initial' values would be a plausible-looking wrong machine.</summary>
        private static void RejectUnknown(JsonElement obj, string[] allowed, string where, DefinitionReport report)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty p in obj.EnumerateObject())
            {
                if (!allowed.Any(a => string.Equals(a, p.Name, StringComparison.OrdinalIgnoreCase)))
                    report.Errors.Add("Unknown property '" + p.Name + "' in " + where + " (expected: " + string.Join(", ", allowed) + ").");
                else if (!seen.Add(p.Name))
                    report.Errors.Add("Property '" + p.Name + "' appears more than once in " + where + ".");
            }
        }

        private static string ReadString(JsonElement obj, string name, string where, DefinitionReport report, bool required)
        {
            if (!TryProp(obj, name, out JsonElement v) || v.ValueKind == JsonValueKind.Null)
            {
                if (required) report.Errors.Add(where + " is missing '" + name + "'.");
                return null;
            }
            if (v.ValueKind != JsonValueKind.String)
            {
                report.Errors.Add(where + " property '" + name + "' must be a string.");
                return null;
            }
            return v.GetString();
        }

        /// <summary>Parses and fully validates a JSON definition. Never throws.</summary>
        internal static DefinitionReport ParseDefinition(string json)
        {
            var report = new DefinitionReport();
            if (string.IsNullOrWhiteSpace(json)) { report.Errors.Add("definitionJson is required."); return report; }
            if (json.Length > MaxDefinitionChars) { report.Errors.Add("definitionJson is longer than " + MaxDefinitionChars + " characters."); return report; }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (JsonException ex) { report.Errors.Add("definitionJson is not valid JSON: " + ex.Message); return report; }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { report.Errors.Add("The definition must be a JSON object."); return report; }
                RejectUnknown(root, RootProperties, "the definition", report);

                var def = new MachineDefinition();
                def.Name = ReadString(root, "name", "The definition", report, false)?.Trim() ?? string.Empty;
                def.Initial = ReadString(root, "initial", "The definition", report, true)?.Trim();

                if (!TryProp(root, "states", out JsonElement states) || states.ValueKind != JsonValueKind.Array)
                    report.Errors.Add("The definition needs a 'states' array.");
                else
                    ParseStates(states, def, report);

                if (TryProp(root, "transitions", out JsonElement transitions))
                {
                    if (transitions.ValueKind != JsonValueKind.Array) report.Errors.Add("'transitions' must be an array.");
                    else ParseTransitions(transitions, def, report);
                }

                // Semantic rules run only once the structure is sound, so one typo doesn't bury the
                // reader in follow-on errors.
                if (report.Errors.Count == 0) def.Validate(report);
                report.Definition = def;
            }
            return report;
        }

        private static void ParseStates(JsonElement states, MachineDefinition def, DefinitionReport report)
        {
            int index = 0;
            foreach (JsonElement s in states.EnumerateArray())
            {
                index++;
                string where = "State #" + index;
                if (s.ValueKind == JsonValueKind.String) { def.States.Add(new StateDef { Name = s.GetString()?.Trim() }); continue; }
                if (s.ValueKind != JsonValueKind.Object) { report.Errors.Add(where + " must be a string or an object."); continue; }

                RejectUnknown(s, StateProperties, where, report);
                string name = ReadString(s, "name", where, report, true)?.Trim();
                bool isFinal = false;
                if (TryProp(s, "final", out JsonElement f))
                {
                    if (f.ValueKind == JsonValueKind.True) isFinal = true;
                    else if (f.ValueKind != JsonValueKind.False && f.ValueKind != JsonValueKind.Null) report.Errors.Add(where + " property 'final' must be true or false.");
                }
                def.States.Add(new StateDef { Name = name, Final = isFinal });
            }
        }

        private static void ParseTransitions(JsonElement transitions, MachineDefinition def, DefinitionReport report)
        {
            int index = 0;
            foreach (JsonElement t in transitions.EnumerateArray())
            {
                index++;
                string where = "Transition #" + index;
                if (t.ValueKind != JsonValueKind.Object) { report.Errors.Add(where + " must be an object."); continue; }

                RejectUnknown(t, TransitionProperties, where, report);
                var transition = new TransitionDef
                {
                    From = ReadString(t, "from", where, report, true)?.Trim(),
                    Trigger = ReadString(t, "trigger", where, report, true)?.Trim(),
                    To = ReadString(t, "to", where, report, true)?.Trim()
                };

                if (TryProp(t, "guards", out JsonElement guards) && guards.ValueKind != JsonValueKind.Null)
                {
                    if (guards.ValueKind != JsonValueKind.Array) report.Errors.Add(where + " property 'guards' must be an array.");
                    else
                    {
                        int g = 0;
                        foreach (JsonElement ge in guards.EnumerateArray())
                        {
                            g++;
                            string gWhere = where + " guard #" + g;
                            if (ge.ValueKind != JsonValueKind.Object) { report.Errors.Add(gWhere + " must be an object."); continue; }
                            RejectUnknown(ge, GuardProperties, gWhere, report);
                            transition.Guards.Add(new GuardDef
                            {
                                Key = ReadString(ge, "key", gWhere, report, true)?.Trim(),
                                Op = MachineDefinition.NormalizeOp(ReadString(ge, "op", gWhere, report, true)?.Trim()),
                                Value = ReadGuardValue(ge, gWhere, report)
                            });
                        }
                    }
                }
                def.Transitions.Add(transition);
            }
        }

        private static string ReadGuardValue(JsonElement guard, string where, DefinitionReport report)
        {
            if (!TryProp(guard, "value", out JsonElement v) || v.ValueKind == JsonValueKind.Null) return null;
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number: return v.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default:
                    report.Errors.Add(where + " property 'value' must be a string, number, or boolean.");
                    return null;
            }
        }

        // ---------------------------------------------------------------- guard evaluation

        /// <summary>
        /// Evaluates one guard against the context. A missing key fails every comparison (fail closed),
        /// and passes only <c>notExists</c>. <c>equals</c>/<c>in</c> are case-insensitive text comparisons;
        /// <c>greaterThan</c>/<c>lessThan</c> are invariant-culture numeric.
        /// </summary>
        internal static bool EvaluateGuard(GuardDef guard, IReadOnlyDictionary<string, string> context)
        {
            bool has = context.TryGetValue(guard.Key, out string actual);
            switch (guard.Op)
            {
                case "exists": return has;
                case "notExists": return !has;
            }
            if (!has) return false;
            switch (guard.Op)
            {
                case "equals": return string.Equals(actual, guard.Value, StringComparison.OrdinalIgnoreCase);
                case "notEquals": return !string.Equals(actual, guard.Value, StringComparison.OrdinalIgnoreCase);
                case "in": return SplitList(guard.Value).Any(v => string.Equals(v, actual, StringComparison.OrdinalIgnoreCase));
                case "notIn": return !SplitList(guard.Value).Any(v => string.Equals(v, actual, StringComparison.OrdinalIgnoreCase));
                case "greaterThan":
                    return TryParseNumber(actual, out double a1) && TryParseNumber(guard.Value, out double b1) && a1 > b1;
                case "lessThan":
                    return TryParseNumber(actual, out double a2) && TryParseNumber(guard.Value, out double b2) && a2 < b2;
                default: return false;
            }
        }

        private static IEnumerable<string> SplitList(string value) =>
            (value ?? string.Empty).Split(',').Select(v => v.Trim());

        /// <summary>
        /// Finds the first transition (in declared order) for <paramref name="trigger"/> from
        /// <paramref name="state"/> whose guards all pass. Returns <c>null</c> with a rejection reason
        /// (<c>NoTransition</c> or <c>GuardFailed</c>) when none does.
        /// </summary>
        internal static TransitionDef FindTransition(MachineDefinition def, string state, string trigger, IReadOnlyDictionary<string, string> context, out string reason, out string detail)
        {
            reason = null;
            detail = null;
            var candidates = def.Transitions
                .Where(t => (t.From == MachineDefinition.Wildcard || string.Equals(t.From, state, StringComparison.OrdinalIgnoreCase))
                            && string.Equals(t.Trigger, trigger, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
            {
                reason = "NoTransition";
                detail = "State '" + state + "' has no transition for trigger '" + trigger + "'.";
                return null;
            }

            var failures = new List<string>();
            foreach (TransitionDef t in candidates)
            {
                GuardDef failed = t.Guards.FirstOrDefault(g => !EvaluateGuard(g, context));
                if (failed == null) return t;
                failures.Add(failed.Describe());
            }
            reason = "GuardFailed";
            detail = "Trigger '" + trigger + "' from state '" + state + "' was declined; guard failed: " + string.Join("; ", failures.Take(3)) + (failures.Count > 3 ? "; ..." : string.Empty) + ".";
            return null;
        }

        internal static List<string> AvailableTriggers(MachineDefinition def, string state, IReadOnlyDictionary<string, string> context)
        {
            var result = new List<string>();
            foreach (string trigger in def.Transitions
                .Where(t => t.From == MachineDefinition.Wildcard || string.Equals(t.From, state, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Trigger).Distinct(StringComparer.OrdinalIgnoreCase))
                if (FindTransition(def, state, trigger, context, out _, out _) != null) result.Add(trigger);
            return result;
        }

        // ---------------------------------------------------------------- persistence helpers

        internal static string BasePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwesomeRpaUtils", "StateMachines");

        internal static bool TryResolveMachineFolder(string machineName, out string folder, out string message)
        {
            folder = null;
            message = null;
            if (string.IsNullOrWhiteSpace(machineName)) { message = "machineName is required."; return false; }
            string name = machineName.Trim();
            if (name.Length > MaxMachineNameLength) { message = "machineName must be at most " + MaxMachineNameLength + " characters."; return false; }
            if (name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                message = "machineName must be a plain name without path separators or characters that are invalid in a file name.";
                return false;
            }
            folder = Path.Combine(BasePath, name);
            return true;
        }

        internal static void WriteAtomic(string path, string json)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { /* best-effort cleanup; the original failure is what matters */ }
                throw;
            }
        }
    }
}
