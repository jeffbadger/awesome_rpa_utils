using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ValueStoreAutomation
{
    /// <summary>Provides an instance-local, freeform key/value bag for passing loosely-typed data
    /// (screen-scrape results, business-object fields, config values) between RPA automation steps,
    /// with forgiving typed conversion and dot-notation access into JSON-shaped values. Like every
    /// component in this suite, its methods report recoverable failures as <c>False</c> with a
    /// descriptive message instead of throwing.</summary>
    [Description("Stores freeform named values with forgiving typed getters, dot-notation path access, wildcard key search, and JSON interop. Never throws.")]
    public sealed class ValueStoreUtils : Component
    {
        private readonly object syncRoot = new object();
        private Dictionary<string, object> data;
        private bool caseSensitiveKeys;
        private bool disposed;

        /// <summary>Creates an empty ValueStoreUtils component with case-insensitive keys.</summary>
        public ValueStoreUtils() { data = NewDictionary(); }

        /// <summary>Creates an empty ValueStoreUtils component and attaches it to a designer container.</summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public ValueStoreUtils(IContainer container) : this() { container?.Add(this); }

        /// <summary>Whether keys use case-sensitive comparison. Changing this rebuilds the internal
        /// map with the new comparer, preserving existing entries; entries that only differ by case
        /// can collide silently when switching to case-insensitive.</summary>
        [Category("ValueStore - Configuration")]
        [Description("Whether keys are case-sensitive. Default False. Changing this preserves existing entries under the new comparer.")]
        [DefaultValue(false)]
        public bool CaseSensitiveKeys
        {
            get { lock (syncRoot) { if (disposed) throw new ObjectDisposedException(nameof(ValueStoreUtils)); return caseSensitiveKeys; } }
            set
            {
                lock (syncRoot)
                {
                    if (disposed) throw new ObjectDisposedException(nameof(ValueStoreUtils));
                    if (value == caseSensitiveKeys) return;
                    caseSensitiveKeys = value;
                    var rebuilt = NewDictionary();
                    foreach (var pair in data) rebuilt[pair.Key] = pair.Value;
                    data = rebuilt;
                }
            }
        }

        #region Core

        /// <summary>Sets a value for a key, creating or overwriting it.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store, of any type.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Core")]
        [Description("Sets a value for a key, creating or overwriting it. Never throws.")]
        public bool SetValue(string key, object value, out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message) || !ValidKey(key, out message)) return false;
                    data[key] = value;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(SetValue), ex); return false; }
        }

        /// <summary>Gets the raw value for a key.</summary>
        /// <param name="key">The key to read. Required.</param>
        /// <param name="found"><c>True</c> if the key was present.</param>
        /// <param name="value">The stored value on success; <c>null</c> if not found or on failure.</param>
        /// <param name="message"><c>null</c> on success (including a normal not-found result); a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the lookup completed, regardless of whether the key was found.</returns>
        [Category("ValueStore - Core")]
        [Description("Gets the raw value for a key. A missing key is a normal result with found False. Never throws.")]
        public bool TryGetValue(string key, out bool found, out object value, out string message)
        {
            found = false; value = null; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message) || !ValidKey(key, out message)) return false;
                    found = data.TryGetValue(key, out value);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { found = false; value = null; message = NeverThrowsGuard.Failure(nameof(TryGetValue), ex); return false; }
        }

        /// <summary>Checks whether a key is present.</summary>
        /// <param name="key">The key to check. Required.</param>
        /// <param name="exists"><c>True</c> if the key is present.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the check completed.</returns>
        [Category("ValueStore - Core")]
        [Description("Checks whether a key is present. Never throws.")]
        public bool ContainsKey(string key, out bool exists, out string message)
        {
            exists = false; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message) || !ValidKey(key, out message)) return false; exists = data.ContainsKey(key); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { exists = false; message = NeverThrowsGuard.Failure(nameof(ContainsKey), ex); return false; }
        }

        /// <summary>Removes a key.</summary>
        /// <param name="key">The key to remove. Required.</param>
        /// <param name="removed"><c>True</c> if the key was present and removed.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the operation completed, regardless of whether the key was present.</returns>
        [Category("ValueStore - Core")]
        [Description("Removes a key. Removing a key that is not present is a normal result with removed False. Never throws.")]
        public bool Remove(string key, out bool removed, out string message)
        {
            removed = false; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message) || !ValidKey(key, out message)) return false; removed = data.Remove(key); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { removed = false; message = NeverThrowsGuard.Failure(nameof(Remove), ex); return false; }
        }

        /// <summary>Removes every key.</summary>
        /// <param name="removedCount">The number of entries removed.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the store was cleared.</returns>
        [Category("ValueStore - Core")]
        [Description("Removes every entry and returns the number removed. Never throws.")]
        public bool Clear(out int removedCount, out string message)
        {
            removedCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; removedCount = data.Count; data.Clear(); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { removedCount = 0; message = NeverThrowsGuard.Failure(nameof(Clear), ex); return false; }
        }

        /// <summary>Returns the current entry count.</summary>
        /// <param name="count">The number of entries.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the count was returned.</returns>
        [Category("ValueStore - Core")]
        [Description("Returns the current number of entries. Never throws.")]
        public bool GetCount(out int count, out string message)
        {
            count = 0; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; count = data.Count; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { count = 0; message = NeverThrowsGuard.Failure(nameof(GetCount), ex); return false; }
        }

        /// <summary>Returns every key as a JSON array.</summary>
        /// <param name="keysJson">A JSON array of key strings.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the keys were returned.</returns>
        [Category("ValueStore - Core")]
        [Description("Returns every key as a JSON array, in insertion order. Never throws.")]
        public bool GetKeysJson(out string keysJson, out string message)
        {
            keysJson = null; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; keysJson = JsonSerializer.Serialize(data.Keys); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { keysJson = null; message = NeverThrowsGuard.Failure(nameof(GetKeysJson), ex); return false; }
        }

        /// <summary>Returns every key as a string array, in insertion order.</summary>
        /// <param name="keys">The keys, in insertion order; an empty array if the store has no entries.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the keys were returned.</returns>
        [Category("ValueStore - Core")]
        [Description("Returns every key as a string array, in insertion order, for direct iteration. Never throws.")]
        public bool GetKeys(out string[] keys, out string message)
        {
            keys = null; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; keys = data.Keys.ToArray(); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { keys = null; message = NeverThrowsGuard.Failure(nameof(GetKeys), ex); return false; }
        }

        #endregion

        #region Set

        /// <summary>Sets an existing or new String entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The text to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets a String entry. Never throws.")]
        public bool SetString(string key, string value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets an Int32 entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets an Int32 entry. Never throws.")]
        public bool SetInt32(string key, int value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets an Int64 entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets an Int64 entry. Never throws.")]
        public bool SetInt64(string key, long value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets a Double entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets a Double entry. Never throws.")]
        public bool SetDouble(string key, double value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets a Decimal entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets a Decimal entry. Never throws.")]
        public bool SetDecimal(string key, decimal value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets a Boolean entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets a Boolean entry. Never throws.")]
        public bool SetBoolean(string key, bool value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets a DateTime entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets a DateTime entry. Never throws.")]
        public bool SetDateTime(string key, DateTime value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets a Guid entry.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets a Guid entry. Never throws.")]
        public bool SetGuid(string key, Guid value, out string message) => SetValue(key, value, out message);

        /// <summary>Sets an entry to null without removing the key.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was cleared.</returns>
        [Category("ValueStore - Set")]
        [Description("Sets an entry to null without removing the key. Never throws.")]
        public bool SetNull(string key, out string message) => SetValue(key, null, out message);

        /// <summary>Validates and sets an entry from a JSON fragment. Objects and arrays are stored
        /// as nested structures navigable by <see cref="GetPathString"/>/<see cref="SetPath"/>.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="valueJson">A JSON object, array, string, number, boolean, or null literal.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="valueJson"/> parsed and was stored.</returns>
        [Category("ValueStore - Set")]
        [Description("Validates and sets an entry from a JSON fragment. Objects/arrays become nested structures. Never throws.")]
        public bool SetJson(string key, string valueJson, out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message) || !ValidKey(key, out message)) return false;
                    if (valueJson == null) { message = "valueJson is required."; return false; }
                    using JsonDocument document = JsonDocument.Parse(valueJson);
                    data[key] = NormalizeJsonValue(document.RootElement);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(SetJson), ex); return false; }
        }

        #endregion

        #region Get

        /// <summary>Gets the value as a string, converting a non-string value with InvariantCulture.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unconvertible.</param>
        /// <returns>The converted string, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as a string, converting non-string values. Returns defaultValue if missing, null, or unconvertible. Never throws.")]
        public string GetString(string key, string defaultValue = null)
        {
            object v = Peek(key);
            if (v == null) return defaultValue;
            if (v is string s) return s;
            try { return Convert.ToString(v, CultureInfo.InvariantCulture); } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { return defaultValue; }
        }

        /// <summary>Gets the value as an Int32, converting from string/numeric sources.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unconvertible.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as an Int32, converting from string/numeric sources. Returns defaultValue on failure. Never throws.")]
        public int GetInt32(string key, int defaultValue = 0) => (int)ConvertOrDefault(Peek(key), typeof(int), defaultValue);

        /// <summary>Gets the value as an Int64, converting from string/numeric sources.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unconvertible.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as an Int64, converting from string/numeric sources. Returns defaultValue on failure. Never throws.")]
        public long GetInt64(string key, long defaultValue = 0L) => (long)ConvertOrDefault(Peek(key), typeof(long), defaultValue);

        /// <summary>Gets the value as a Double, converting from string/numeric sources.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unconvertible.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as a Double, converting from string/numeric sources. Returns defaultValue on failure. Never throws.")]
        public double GetDouble(string key, double defaultValue = 0d) => (double)ConvertOrDefault(Peek(key), typeof(double), defaultValue);

        /// <summary>Gets the value as a Decimal, converting from string/numeric sources.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unconvertible.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as a Decimal, converting from string/numeric sources. Returns defaultValue on failure. Never throws.")]
        public decimal GetDecimal(string key, decimal defaultValue = 0m) => (decimal)ConvertOrDefault(Peek(key), typeof(decimal), defaultValue);

        /// <summary>Gets the value as a Boolean. Accepts a native bool, true/false, 1/0, and yes/no/y/n (case-insensitive).</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unrecognized.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as a Boolean. Accepts true/false, 1/0, and yes/no/y/n. Returns defaultValue on failure. Never throws.")]
        public bool GetBoolean(string key, bool defaultValue = false) => ParseBool(Peek(key), defaultValue);

        /// <summary>Gets the value as a DateTime, with an optional exact parse format.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unparsable.</param>
        /// <param name="format">An exact format string (e.g. <c>"yyyyMMdd"</c>) to force strict parsing, or null for a general parse.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as a DateTime. Pass an exact format to force strict parsing. Returns defaultValue on failure. Never throws.")]
        public DateTime GetDateTime(string key, DateTime defaultValue = default, string format = null)
        {
            object v = Peek(key);
            if (v == null) return defaultValue;
            if (v is DateTime dt) return dt;
            try
            {
                string s = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(s)) return defaultValue;
                if (format != null && DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exact)) return exact;
                return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed) ? parsed : defaultValue;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { return defaultValue; }
        }

        /// <summary>Gets the value as a Guid, parsing from string if necessary.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="defaultValue">The value returned if the key is missing, null, or unparsable.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Get")]
        [Description("Gets the value as a Guid, parsing from string if necessary. Returns defaultValue on failure. Never throws.")]
        public Guid GetGuid(string key, Guid defaultValue = default)
        {
            object v = Peek(key);
            if (v == null) return defaultValue;
            if (v is Guid g) return g;
            try { return Guid.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out Guid parsed) ? parsed : defaultValue; }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { return defaultValue; }
        }

        #endregion

        #region Try Get

        /// <summary>Attempts to read the value as a string.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <c>null</c> otherwise.</param>
        /// <returns><c>True</c> if the key was present with a non-null value.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as a string. Returns False if missing or null. Never throws.")]
        public bool TryGetString(string key, out string value)
        {
            object raw = Peek(key);
            if (raw == null) { value = null; return false; }
            value = GetString(key);
            return true;
        }

        /// <summary>Attempts to read the value as an Int32.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <c>0</c> otherwise.</param>
        /// <returns><c>True</c> if the value was present and convertible.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as an Int32. Returns False if missing, null, or unconvertible. Never throws.")]
        public bool TryGetInt32(string key, out int value)
        {
            bool ok = TryConvertBoxed(key, typeof(int), out object boxed);
            value = ok ? (int)boxed : default;
            return ok;
        }

        /// <summary>Attempts to read the value as an Int64.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <c>0</c> otherwise.</param>
        /// <returns><c>True</c> if the value was present and convertible.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as an Int64. Returns False if missing, null, or unconvertible. Never throws.")]
        public bool TryGetInt64(string key, out long value)
        {
            bool ok = TryConvertBoxed(key, typeof(long), out object boxed);
            value = ok ? (long)boxed : default;
            return ok;
        }

        /// <summary>Attempts to read the value as a Double.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <c>0</c> otherwise.</param>
        /// <returns><c>True</c> if the value was present and convertible.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as a Double. Returns False if missing, null, or unconvertible. Never throws.")]
        public bool TryGetDouble(string key, out double value)
        {
            bool ok = TryConvertBoxed(key, typeof(double), out object boxed);
            value = ok ? (double)boxed : default;
            return ok;
        }

        /// <summary>Attempts to read the value as a Decimal.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <c>0</c> otherwise.</param>
        /// <returns><c>True</c> if the value was present and convertible.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as a Decimal. Returns False if missing, null, or unconvertible. Never throws.")]
        public bool TryGetDecimal(string key, out decimal value)
        {
            bool ok = TryConvertBoxed(key, typeof(decimal), out object boxed);
            value = ok ? (decimal)boxed : default;
            return ok;
        }

        /// <summary>Attempts to read the value as a Boolean (true/false, 1/0, yes/no/y/n).</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <c>false</c> otherwise.</param>
        /// <returns><c>True</c> if the value was present and recognized.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as a Boolean. Returns False if missing, null, or unrecognized. Never throws.")]
        public bool TryGetBoolean(string key, out bool value)
        {
            object raw = Peek(key);
            if (raw == null) { value = default; return false; }
            if (raw is bool b) { value = b; return true; }
            string s = SafeToString(raw)?.Trim();
            if (bool.TryParse(s, out value)) return true;
            if (s == "1" || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "y", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
            if (s == "0" || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "n", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
            value = default;
            return false;
        }

        /// <summary>Attempts to read the value as a DateTime.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <see cref="DateTime.MinValue"/> otherwise.</param>
        /// <returns><c>True</c> if the value was present and parsable.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as a DateTime. Returns False if missing, null, or unparsable. Never throws.")]
        public bool TryGetDateTime(string key, out DateTime value)
        {
            object raw = Peek(key);
            if (raw == null) { value = default; return false; }
            if (raw is DateTime dt) { value = dt; return true; }
            return DateTime.TryParse(SafeToString(raw), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        /// <summary>Attempts to read the value as a Guid.</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="value">The value on success; <see cref="Guid.Empty"/> otherwise.</param>
        /// <returns><c>True</c> if the value was present and parsable.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as a Guid. Returns False if missing, null, or unparsable. Never throws.")]
        public bool TryGetGuid(string key, out Guid value)
        {
            object raw = Peek(key);
            if (raw == null) { value = default; return false; }
            if (raw is Guid g) { value = g; return true; }
            return Guid.TryParse(SafeToString(raw), out value);
        }

        /// <summary>Attempts to read the value as a member of a named enum type, parsed by name (case-insensitive).</summary>
        /// <param name="key">The key to read.</param>
        /// <param name="enumTypeName">An assembly-qualified or in-scope simple type name, resolved via <see cref="Type.GetType(string)"/>. Should be a design-time-authored literal, not untrusted data.</param>
        /// <param name="found"><c>True</c> if the key was present.</param>
        /// <param name="value">The parsed enum value on success; <c>null</c> otherwise.</param>
        /// <param name="message"><c>null</c> on success (including a normal not-found result); a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the lookup and any parsing completed without an operational failure.</returns>
        [Category("ValueStore - Try Get")]
        [Description("Attempts to read the value as a member of a named enum type. Never throws.")]
        public bool TryGetEnum(string key, string enumTypeName, out bool found, out object value, out string message)
        {
            found = false; value = null; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message) || !ValidKey(key, out message)) return false;
                    Type enumType = Type.GetType(enumTypeName);
                    if (enumType == null || !enumType.IsEnum) { message = "enumTypeName '" + enumTypeName + "' did not resolve to an enum type."; return false; }
                    if (!data.TryGetValue(key, out object raw) || raw == null) return true;
                    found = true;
                    if (enumType.IsInstanceOfType(raw)) { value = raw; return true; }
                    string s = Convert.ToString(raw, CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(s)) { message = "The value for '" + key + "' could not be converted to text for enum parsing."; return false; }
                    value = Enum.Parse(enumType, s, true);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { found = false; value = null; message = NeverThrowsGuard.Failure(nameof(TryGetEnum), ex); return false; }
        }

        #endregion

        #region Helpers

        /// <summary>Reports whether a key is effectively empty (missing, null, or blank/whitespace text).</summary>
        /// <param name="key">The key to check. Required.</param>
        /// <param name="isEmpty"><c>True</c> if the key is missing, null, or blank/whitespace text.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the check completed.</returns>
        [Category("ValueStore - Helpers")]
        [Description("Reports whether a key is missing, null, or blank/whitespace text. Never throws.")]
        public bool IsEmpty(string key, out bool isEmpty, out string message)
        {
            isEmpty = true; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message) || !ValidKey(key, out message)) return false;
                    if (!data.TryGetValue(key, out object v) || v == null) { isEmpty = true; return true; }
                    isEmpty = v is string s && string.IsNullOrWhiteSpace(s);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { isEmpty = true; message = NeverThrowsGuard.Failure(nameof(IsEmpty), ex); return false; }
        }

        /// <summary>Sets a value only if the key is not already present.</summary>
        /// <param name="key">The key to set. Required.</param>
        /// <param name="value">The value to store if the key is missing.</param>
        /// <param name="added"><c>True</c> if the key was missing and the value was stored.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the operation completed, regardless of whether the key was already present.</returns>
        [Category("ValueStore - Helpers")]
        [Description("Sets a value only if the key is not already present. Never throws.")]
        public bool AddIfMissing(string key, object value, out bool added, out string message)
        {
            added = false; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message) || !ValidKey(key, out message)) return false;
                    if (data.ContainsKey(key)) return true;
                    data[key] = value;
                    added = true;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { added = false; message = NeverThrowsGuard.Failure(nameof(AddIfMissing), ex); return false; }
        }

        /// <summary>Removes a key and returns its raw value in one step.</summary>
        /// <param name="key">The key to remove. Required.</param>
        /// <param name="found"><c>True</c> if the key was present.</param>
        /// <param name="value">The removed value on success; <c>null</c> if not found.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the operation completed, regardless of whether the key was present.</returns>
        [Category("ValueStore - Helpers")]
        [Description("Removes a key and returns its raw value in one step. A missing key is a normal result with found False. Never throws.")]
        public bool RemoveAndGetValue(string key, out bool found, out object value, out string message)
        {
            found = false; value = null; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message) || !ValidKey(key, out message)) return false;
                    found = data.TryGetValue(key, out value);
                    if (found) data.Remove(key);
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { found = false; value = null; message = NeverThrowsGuard.Failure(nameof(RemoveAndGetValue), ex); return false; }
        }

        /// <summary>Merges a flat JSON object's top-level properties into this store.</summary>
        /// <param name="sourceJson">A JSON object whose top-level properties are merged in.</param>
        /// <param name="overwrite"><c>False</c> to preserve this store's existing values on key conflicts.</param>
        /// <param name="mergedCount">The number of properties merged in.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="sourceJson"/> parsed as a JSON object and was merged.</returns>
        [Category("ValueStore - Helpers")]
        [Description("Merges a flat JSON object's top-level properties into this store. The operation is atomic. Never throws.")]
        public bool Merge(string sourceJson, bool overwrite, out int mergedCount, out string message)
        {
            mergedCount = 0; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    if (sourceJson == null) { message = "sourceJson is required."; return false; }
                    using JsonDocument document = JsonDocument.Parse(sourceJson);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) { message = "sourceJson must contain a JSON object."; return false; }
                    var candidate = new Dictionary<string, object>(data, data.Comparer);
                    int count = 0;
                    foreach (JsonProperty property in document.RootElement.EnumerateObject())
                    {
                        if (overwrite || !candidate.ContainsKey(property.Name)) { candidate[property.Name] = NormalizeJsonValue(property.Value); count++; }
                    }
                    data = candidate;
                    mergedCount = count;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { mergedCount = 0; message = NeverThrowsGuard.Failure(nameof(Merge), ex); return false; }
        }

        /// <summary>Returns keys matching a simple <c>*</c> wildcard pattern as a JSON array.</summary>
        /// <param name="wildcardPattern">A pattern such as <c>"Cust_*"</c>. Null or empty matches nothing.</param>
        /// <param name="keysJson">A JSON array of matching keys.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the search completed, regardless of how many keys matched.</returns>
        [Category("ValueStore - Helpers")]
        [Description("Returns keys matching a simple '*' wildcard pattern as a JSON array. Never throws.")]
        public bool FindKeysJson(string wildcardPattern, out string keysJson, out string message)
        {
            keysJson = null; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    if (string.IsNullOrEmpty(wildcardPattern)) { keysJson = "[]"; return true; }
                    string regexPattern = "^" + Regex.Escape(wildcardPattern).Replace("\\*", ".*") + "$";
                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                    keysJson = JsonSerializer.Serialize(data.Keys.Where(key => regex.IsMatch(key)).ToList());
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { keysJson = null; message = NeverThrowsGuard.Failure(nameof(FindKeysJson), ex); return false; }
        }

        #endregion

        #region Path

        /// <summary>Dot-notation get that walks nested structures created by <see cref="SetJson"/>/<see cref="SetPath"/>.</summary>
        /// <param name="path">A dotted path, e.g. <c>"Customer.Address.City"</c>.</param>
        /// <param name="found"><c>True</c> if every segment resolved.</param>
        /// <param name="value">The resolved value on success; <c>null</c> otherwise.</param>
        /// <param name="message"><c>null</c> on success (including a normal not-found result); a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the walk completed, regardless of whether it resolved.</returns>
        [Category("ValueStore - Path")]
        [Description("Dot-notation get that walks nested structures. A path that does not resolve is a normal result with found False. Never throws.")]
        public bool TryGetPathValue(string path, out bool found, out object value, out string message)
        {
            found = false; value = null; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    if (string.IsNullOrEmpty(path)) return true;
                    object current = data;
                    foreach (string segment in path.Split('.'))
                    {
                        if (current is Dictionary<string, object> dict && dict.TryGetValue(segment, out object next)) current = next;
                        else return true;
                    }
                    found = true;
                    value = current;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { found = false; value = null; message = NeverThrowsGuard.Failure(nameof(TryGetPathValue), ex); return false; }
        }

        /// <summary>Dot-notation get returning a string.</summary>
        /// <param name="path">A dotted path.</param>
        /// <param name="defaultValue">The value returned if any segment is missing or the resolved value is unconvertible.</param>
        /// <returns>The converted string, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Path")]
        [Description("Dot-notation get returning a string. Returns defaultValue if any segment is missing. Never throws.")]
        public string GetPathString(string path, string defaultValue = null)
        {
            object raw = PeekPath(path);
            if (raw == null) return defaultValue;
            if (raw is string s) return s;
            try { return Convert.ToString(raw, CultureInfo.InvariantCulture); } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { return defaultValue; }
        }

        /// <summary>Dot-notation get returning an Int32.</summary>
        /// <param name="path">A dotted path.</param>
        /// <param name="defaultValue">The value returned if any segment is missing or the resolved value is unconvertible.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Path")]
        [Description("Dot-notation get returning an Int32. Returns defaultValue if any segment is missing. Never throws.")]
        public int GetPathInt32(string path, int defaultValue = 0) => (int)ConvertOrDefault(PeekPath(path), typeof(int), defaultValue);

        /// <summary>Dot-notation get returning a Boolean.</summary>
        /// <param name="path">A dotted path.</param>
        /// <param name="defaultValue">The value returned if any segment is missing or unrecognized.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Path")]
        [Description("Dot-notation get returning a Boolean, accepting the same formats as GetBoolean. Never throws.")]
        public bool GetPathBoolean(string path, bool defaultValue = false) => ParseBool(PeekPath(path), defaultValue);

        /// <summary>Dot-notation get returning a DateTime.</summary>
        /// <param name="path">A dotted path.</param>
        /// <param name="defaultValue">The value returned if any segment is missing or unparsable.</param>
        /// <returns>The converted value, or <paramref name="defaultValue"/>.</returns>
        [Category("ValueStore - Path")]
        [Description("Dot-notation get returning a DateTime. Returns defaultValue if any segment is missing. Never throws.")]
        public DateTime GetPathDateTime(string path, DateTime defaultValue = default)
        {
            object raw = PeekPath(path);
            if (raw == null) return defaultValue;
            if (raw is DateTime dt) return dt;
            try { return DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed) ? parsed : defaultValue; }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { return defaultValue; }
        }

        /// <summary>Dot-notation set, creating intermediate nested structures as needed.</summary>
        /// <param name="path">A dotted path, e.g. <c>"Customer.Address.City"</c>. Required.</param>
        /// <param name="value">The value to set at the final segment.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the value was set.</returns>
        [Category("ValueStore - Path")]
        [Description("Dot-notation set, creating intermediate nested structures as needed. Never throws.")]
        public bool SetPath(string path, object value, out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    if (string.IsNullOrEmpty(path)) { message = "path is required."; return false; }
                    string[] segments = path.Split('.');
                    Dictionary<string, object> current = data;
                    for (int i = 0; i < segments.Length - 1; i++)
                    {
                        if (!current.TryGetValue(segments[i], out object next) || next is not Dictionary<string, object> nested)
                        {
                            nested = NewDictionary();
                            current[segments[i]] = nested;
                        }
                        current = nested;
                    }
                    current[segments[^1]] = value;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(SetPath), ex); return false; }
        }

        #endregion

        #region Json

        /// <summary>Serializes the store to a JSON string.</summary>
        /// <param name="json">The JSON text on success; <c>null</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if serialization succeeded.</returns>
        [Category("ValueStore - Json")]
        [Description("Serializes the store to a JSON string. Never throws.")]
        public bool TryGetJson(out string json, out string message)
        {
            json = null; message = null;
            try { lock (syncRoot) { if (!RequireActive(out message)) return false; json = JsonSerializer.Serialize(data); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { json = null; message = NeverThrowsGuard.Failure(nameof(TryGetJson), ex); return false; }
        }

        /// <summary>Loads a JSON object's properties into the store.</summary>
        /// <param name="json">A JSON object. Nested objects/arrays become nested structures; numbers come back as Int64 or Double.</param>
        /// <param name="clearExisting"><c>True</c> to remove existing entries before loading.</param>
        /// <param name="loadedCount">The number of top-level properties loaded.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="json"/> parsed as a JSON object and was loaded.</returns>
        [Category("ValueStore - Json")]
        [Description("Loads a JSON object's properties into the store. The operation is atomic. Never throws.")]
        public bool TryLoadFromJson(string json, bool clearExisting, out int loadedCount, out string message)
        {
            loadedCount = 0; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    if (string.IsNullOrWhiteSpace(json)) { message = "json is required."; return false; }
                    using JsonDocument document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) { message = "json must contain a JSON object at the root."; return false; }
                    var candidate = clearExisting ? NewDictionary() : new Dictionary<string, object>(data, data.Comparer);
                    int count = 0;
                    foreach (JsonProperty property in document.RootElement.EnumerateObject()) { candidate[property.Name] = NormalizeJsonValue(property.Value); count++; }
                    data = candidate;
                    loadedCount = count;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { loadedCount = 0; message = NeverThrowsGuard.Failure(nameof(TryLoadFromJson), ex); return false; }
        }

        /// <summary>Loads a POCO's public readable properties into the store (shallow — nested object
        /// properties are stored as-is, not recursively converted). The operation is atomic: if any
        /// property getter throws, nothing is loaded.</summary>
        /// <param name="source">The object to read properties from. A null source loads nothing and succeeds with <paramref name="loadedCount"/> of 0.</param>
        /// <param name="clearExisting"><c>True</c> to remove existing entries before loading.</param>
        /// <param name="loadedCount">The number of properties loaded.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if the properties were loaded.</returns>
        [Category("ValueStore - Json")]
        [Description("Loads a POCO's public readable properties into the store via reflection. The operation is atomic. Never throws.")]
        public bool TryLoadFromObject(object source, bool clearExisting, out int loadedCount, out string message)
        {
            loadedCount = 0; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireActive(out message)) return false;
                    var candidate = clearExisting ? NewDictionary() : new Dictionary<string, object>(data, data.Comparer);
                    int count = 0;
                    if (source != null)
                    {
                        foreach (var property in source.GetType().GetProperties())
                        {
                            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                            candidate[property.Name] = property.GetValue(source);
                            count++;
                        }
                    }
                    data = candidate;
                    loadedCount = count;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { loadedCount = 0; message = NeverThrowsGuard.Failure(nameof(TryLoadFromObject), ex); return false; }
        }

        #endregion

        /// <summary>Clears in-memory contents when disposing.</summary>
        /// <param name="disposing"><c>True</c> if called from <see cref="IDisposable.Dispose"/>; <c>false</c> from a finalizer.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { lock (syncRoot) { data.Clear(); disposed = true; } }
            base.Dispose(disposing);
        }

        private static object NormalizeJsonValue(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty prop in el.EnumerateObject()) obj[prop.Name] = NormalizeJsonValue(prop.Value);
                    return obj;
                case JsonValueKind.Array:
                    return el.EnumerateArray().Select(NormalizeJsonValue).ToList();
                case JsonValueKind.String:
                    return el.GetString();
                case JsonValueKind.Number:
                    return el.TryGetInt64(out long l) ? (object)l : el.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }

        private object Peek(string key)
        {
            if (key == null) return null;
            lock (syncRoot) { return !disposed && data.TryGetValue(key, out object v) ? v : null; }
        }

        private object PeekPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (syncRoot)
            {
                if (disposed) return null;
                object current = data;
                foreach (string segment in path.Split('.'))
                {
                    if (current is Dictionary<string, object> dict && dict.TryGetValue(segment, out object next)) current = next;
                    else return null;
                }
                return current;
            }
        }

        private bool TryConvertBoxed(string key, Type targetType, out object value)
        {
            object raw = Peek(key);
            if (raw == null) { value = null; return false; }
            if (targetType.IsInstanceOfType(raw)) { value = raw; return true; }
            try { value = Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture); return true; }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { value = null; return false; }
        }

        private static object ConvertOrDefault(object raw, Type targetType, object defaultValue)
        {
            if (raw == null) return defaultValue;
            if (targetType.IsInstanceOfType(raw)) return raw;
            try { return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { return defaultValue; }
        }

        private static bool ParseBool(object raw, bool defaultValue)
        {
            if (raw == null) return defaultValue;
            if (raw is bool b) return b;
            string s = SafeToString(raw)?.Trim();
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (bool.TryParse(s, out bool parsed)) return parsed;
            if (s == "1" || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "y", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "n", StringComparison.OrdinalIgnoreCase)) return false;
            return defaultValue;
        }

        private static string SafeToString(object raw)
        {
            try { return Convert.ToString(raw, CultureInfo.InvariantCulture); }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { return null; }
        }

        private Dictionary<string, object> NewDictionary() => new Dictionary<string, object>(caseSensitiveKeys ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        private bool ValidKey(string key, out string message)
        {
            message = string.IsNullOrWhiteSpace(key) ? "key is required." : null;
            return message == null;
        }

        private bool RequireActive(out string message)
        {
            message = disposed ? "This ValueStoreUtils component has been disposed. Drag a new ValueStoreUtils component onto the automation surface." : null;
            return !disposed;
        }
    }
}
