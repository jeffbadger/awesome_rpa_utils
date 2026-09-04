using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DataBagAutomation
{
    /// <summary>Provides an instance-local, typed, named data bag for phased RPA workflows.</summary>
    [Description("Stores named, typed values behind scalar Pega-friendly methods. Definitions are initialized and sealed before runtime updates. Never throws.")]
    public sealed class DataBagUtils : Component
    {
        private const int AbsoluteMaximumItems = 1000000;
        private readonly object syncRoot = new object();
        private Dictionary<string, Entry> active;
        private Dictionary<string, Entry> staging;
        private DataBagState state;
        private int maximumItems = 10000;
        private bool caseSensitiveNames;
        private string initialItemsJson = "";
        private string initialItemsFilePath = "";
        private DataBagInitializationSource initializationSource;
        private bool sealAfterInitialization = true;

        /// <summary>Creates an empty, uninitialized data bag.</summary>
        public DataBagUtils() { active = NewDictionary(); }
        /// <summary>Creates a data bag and attaches it to a designer container.</summary>
        public DataBagUtils(IContainer container) : this() { container?.Add(this); }

        /// <summary>Maximum number of definitions allowed.</summary>
        [Category("Data Bag - Configuration"), DefaultValue(10000), Description("Maximum number of named values. Valid range: 1 through 1,000,000.")]
        public int MaximumItems { get => maximumItems; set { lock (syncRoot) { RequireConfigurable(); if (value < 1 || value > AbsoluteMaximumItems) throw new ArgumentOutOfRangeException(nameof(value)); maximumItems = value; } } }

        /// <summary>Whether names use case-sensitive comparison.</summary>
        [Category("Data Bag - Configuration"), DefaultValue(false), Description("Whether item names are case-sensitive. Configure before initialization.")]
        public bool CaseSensitiveNames { get => caseSensitiveNames; set { lock (syncRoot) { RequireConfigurable(); caseSensitiveNames = value; active = NewDictionary(); } } }

        /// <summary>Embedded typed definitions used by Initialize.</summary>
        [Category("Data Bag - Initialization"), DefaultValue(""), Description("Typed definition JSON loaded by Initialize when InitializationSource is DesignTimeJson.")]
        public string InitialItemsJson { get => initialItemsJson; set { lock (syncRoot) { RequireConfigurable(); initialItemsJson = value ?? ""; } } }

        /// <summary>Definition-file path used by Initialize.</summary>
        [Category("Data Bag - Initialization"), DefaultValue(""), Description("Typed JSON definition file loaded by Initialize when InitializationSource is JsonFile.")]
        public string InitialItemsFilePath { get => initialItemsFilePath; set { lock (syncRoot) { RequireConfigurable(); initialItemsFilePath = value ?? ""; } } }

        /// <summary>Source used by Initialize.</summary>
        [Category("Data Bag - Initialization"), DefaultValue(DataBagInitializationSource.None), Description("Selects no preload, embedded typed JSON, or a typed JSON file.")]
        public DataBagInitializationSource InitializationSource { get => initializationSource; set { lock (syncRoot) { RequireConfigurable(); if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); initializationSource = value; } } }

        /// <summary>Whether Initialize automatically seals the schema.</summary>
        [Category("Data Bag - Initialization"), DefaultValue(true), Description("Seals definitions after Initialize so runtime operations cannot create or retype items.")]
        public bool SealAfterInitialization { get => sealAfterInitialization; set { lock (syncRoot) { RequireConfigurable(); sealAfterInitialization = value; } } }

        /// <summary>Populates InitialItemsJson with an editable one-row definition template.</summary>
        [Category("Data Bag - Initialization"), Description("Writes a one-row JSON definition template containing every supported option into InitialItemsJson. Replace the blank name before Initialize. Never throws.")]
        public bool PopulateInitialItemsJsonTemplate(out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    RequireConfigurable();
                    initialItemsJson = "{\"values\":[{\"name\":\"\",\"type\":\"String\",\"defaultValue\":null,\"mustHaveValue\":false,\"readOnly\":false,\"writeOnce\":false,\"sensitive\":false}]}";
                    initializationSource = DataBagInitializationSource.DesignTimeJson;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("PopulateInitialItemsJsonTemplate", ex); return false; }
        }

        /// <summary>Initializes atomically from the configured design-time source.</summary>
        [Category("Data Bag - Initialization"), Description("Loads the selected design-time source atomically and optionally seals the bag. Never throws.")]
        public bool Initialize(out int loadedCount, out string message)
        {
            loadedCount = 0; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireNotDisposed(out message) || state != DataBagState.NotInitialized) { if (message == null) message = "The data bag has already been initialized."; return false; }
                    var candidate = NewDictionary();
                    if (initializationSource == DataBagInitializationSource.DesignTimeJson)
                    {
                        if (string.IsNullOrWhiteSpace(initialItemsJson)) { message = "InitialItemsJson is required for DesignTimeJson initialization."; return false; }
                        if (!TryParseTypedDefinitions(initialItemsJson, candidate, DataBagConflictPolicy.Fail, out _, out _, out _, out message)) return false;
                    }
                    else if (initializationSource == DataBagInitializationSource.JsonFile)
                    {
                        if (string.IsNullOrWhiteSpace(initialItemsFilePath)) { message = "InitialItemsFilePath is required for JsonFile initialization."; return false; }
                        string full = Path.GetFullPath(initialItemsFilePath, AppContext.BaseDirectory);
                        if (!File.Exists(full)) { message = "The initialization file does not exist: " + full; return false; }
                        if (!TryParseTypedDefinitions(File.ReadAllText(full), candidate, DataBagConflictPolicy.Fail, out _, out _, out _, out message)) return false;
                    }
                    loadedCount = candidate.Count;
                    if (sealAfterInitialization) { active = candidate; staging = null; state = DataBagState.Ready; }
                    else { staging = candidate; state = DataBagState.Initializing; }
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("Initialize", ex); return false; }
        }

        /// <summary>Starts an atomic programmatic initialization transaction.</summary>
        [Category("Data Bag - Initialization"), Description("Starts staging definitions. Existing active definitions may be copied or cleared. Never throws.")]
        public bool BeginInitialization(bool clearExisting, out string message)
        {
            message = null;
            try { lock (syncRoot) { if (!RequireNotDisposed(out message)) return false; if (state == DataBagState.Initializing) { message = "Initialization is already in progress."; return false; } staging = clearExisting ? NewDictionary() : Clone(active); state = DataBagState.Initializing; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("BeginInitialization", ex); return false; }
        }

        /// <summary>Atomically publishes staged definitions and seals the bag.</summary>
        [Category("Data Bag - Initialization"), Description("Validates and publishes staged definitions, then enters Ready state. Never throws.")]
        public bool CompleteInitialization(out int itemCount, out string message)
        {
            itemCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireInitializing(out message)) return false; active = Clone(staging); staging = null; state = DataBagState.Ready; itemCount = active.Count; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("CompleteInitialization", ex); return false; }
        }

        /// <summary>Discards staged initialization without changing the active bag.</summary>
        [Category("Data Bag - Initialization"), Description("Cancels staged initialization and preserves the last active bag. Never throws.")]
        public bool CancelInitialization(out int discardedCount, out string message)
        {
            discardedCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireInitializing(out message)) return false; discardedCount = staging.Count; staging = null; state = active.Count == 0 ? DataBagState.NotInitialized : DataBagState.Ready; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("CancelInitialization", ex); return false; }
        }

        /// <summary>Defines or updates a typed value during initialization.</summary>
        [Category("Data Bag - Initialization"), Description("Defines a typed value in the staged schema. Never throws.")]
        public bool SetTypedValue(string name, DataBagValueType valueType, string value, out string message) => Guard("SetTypedValue", (out string inner) => SetTypedCore(name, valueType, value, false, false, false, staging, out inner), out message);

        /// <summary>Loads typed definitions from JSON into staging.</summary>
        [Category("Data Bag - Initialization"), Description("Loads typed definition JSON atomically into staging. Never throws.")]
        public bool LoadTypedJson(string typedJson, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)
        {
            addedCount = replacedCount = skippedCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireInitializing(out message) || !ValidEnum(conflictPolicy, nameof(conflictPolicy), out message)) return false; var candidate = Clone(staging); if (!TryParseTypedDefinitions(typedJson, candidate, conflictPolicy, out addedCount, out replacedCount, out skippedCount, out message)) { addedCount = replacedCount = skippedCount = 0; return false; } staging = candidate; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { addedCount = replacedCount = skippedCount = 0; message = NeverThrowsGuard.Failure("LoadTypedJson", ex); return false; }
        }

        /// <summary>Loads inferred definitions from a flat JSON object into staging.</summary>
        [Category("Data Bag - Initialization"), Description("Infers types from a flat JSON object and loads definitions atomically. Never throws.")]
        public bool LoadJsonObject(string jsonObject, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)
        {
            addedCount = replacedCount = skippedCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireInitializing(out message) || !ValidEnum(conflictPolicy, nameof(conflictPolicy), out message)) return false; using JsonDocument doc = JsonDocument.Parse(jsonObject); if (doc.RootElement.ValueKind != JsonValueKind.Object) { message = "jsonObject must contain a JSON object."; return false; } var candidate = Clone(staging); foreach (JsonProperty p in doc.RootElement.EnumerateObject()) { Entry entry = InferEntry(p.Name, p.Value); if (!MergeEntry(candidate, entry, conflictPolicy, ref addedCount, ref replacedCount, ref skippedCount, out message)) { addedCount = replacedCount = skippedCount = 0; return false; } } staging = candidate; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { addedCount = replacedCount = skippedCount = 0; message = NeverThrowsGuard.Failure("LoadJsonObject", ex); return false; }
        }

        /// <summary>Loads Name, Type, Value, MustHaveValue, ReadOnly, WriteOnce, and Sensitive definition columns.</summary>
        [Category("Data Bag - Initialization"), Description("Loads a definition DataTable using conventional column names. Never throws.")]
        public bool LoadDataTable(DataTable table, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message) =>
            LoadDataTableMapped(table, "Name", "Type", "Value", "MustHaveValue", "ReadOnly", "WriteOnce", "Sensitive", conflictPolicy, out addedCount, out replacedCount, out skippedCount, out message);

        /// <summary>Loads definitions from explicitly mapped DataTable columns.</summary>
        [Category("Data Bag - Initialization"), Description("Loads mapped definition rows atomically. Flag-column names may be empty. Never throws.")]
        public bool LoadDataTableMapped(DataTable table, string nameColumn, string typeColumn, string valueColumn, string mustHaveValueColumn, string readOnlyColumn, string writeOnceColumn, string sensitiveColumn, DataBagConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)
        {
            addedCount = replacedCount = skippedCount = 0; message = null;
            try
            {
                lock (syncRoot)
                {
                    if (!RequireInitializing(out message) || table == null) { if (message == null) message = "table is required."; return false; }
                    if (!ValidEnum(conflictPolicy, nameof(conflictPolicy), out message) || !HasColumn(table, nameColumn, "nameColumn", out message) || !HasColumn(table, typeColumn, "typeColumn", out message) || !HasColumn(table, valueColumn, "valueColumn", out message)) return false;
                    var candidate = Clone(staging);
                    foreach (DataRow row in table.Rows)
                    {
                        string name = Convert.ToString(row[nameColumn], CultureInfo.InvariantCulture);
                        if (!Enum.TryParse(Convert.ToString(row[typeColumn], CultureInfo.InvariantCulture), true, out DataBagValueType type) || !Enum.IsDefined(type)) { message = "Unknown data-bag type for '" + name + "'."; addedCount = replacedCount = skippedCount = 0; return false; }
                        string value = row[valueColumn] == DBNull.Value ? null : Convert.ToString(row[valueColumn], CultureInfo.InvariantCulture);
                        var entry = CreateEntry(name, type, value, ReadFlag(row, mustHaveValueColumn), ReadFlag(row, readOnlyColumn), ReadFlag(row, writeOnceColumn), ReadFlag(row, sensitiveColumn));
                        if (!MergeEntry(candidate, entry, conflictPolicy, ref addedCount, ref replacedCount, ref skippedCount, out message)) { addedCount = replacedCount = skippedCount = 0; return false; }
                    }
                    staging = candidate; return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { addedCount = replacedCount = skippedCount = 0; message = NeverThrowsGuard.Failure("LoadDataTableMapped", ex); return false; }
        }

        /// <summary>Updates an existing entry from its invariant string representation.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing item by parsing text according to its declared type. Never creates or retypes. Never throws.")]
        public bool SetValue(string name, string value, out string message) => Guard("SetValue", (out string inner) => SetExistingCore(name, null, value, out inner), out message);
        /// <summary>Updates an existing string entry.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing String item. Never throws.")]
        public bool SetString(string name, string value, out string message) => Guard("SetString", (out string inner) => SetExistingCore(name, DataBagValueType.String, value, out inner), out message);
        /// <summary>Updates an existing Boolean entry.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing Boolean item. Never throws.")]
        public bool SetBoolean(string name, bool value, out string message) => SetNative(name, DataBagValueType.Boolean, value, "SetBoolean", out message);
        /// <summary>Updates an existing Int32 entry.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing Int32 item. Never throws.")]
        public bool SetInt32(string name, int value, out string message) => SetNative(name, DataBagValueType.Int32, value, "SetInt32", out message);
        /// <summary>Updates an existing Int64 entry.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing Int64 item. Never throws.")]
        public bool SetInt64(string name, long value, out string message) => SetNative(name, DataBagValueType.Int64, value, "SetInt64", out message);
        /// <summary>Updates an existing Decimal entry.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing Decimal item. Never throws.")]
        public bool SetDecimal(string name, decimal value, out string message) => SetNative(name, DataBagValueType.Decimal, value, "SetDecimal", out message);
        /// <summary>Updates an existing Double entry.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing Double item. Never throws.")]
        public bool SetDouble(string name, double value, out string message) => SetNative(name, DataBagValueType.Double, value, "SetDouble", out message);
        /// <summary>Updates an existing DateTime entry.</summary>
        [Category("Data Bag - Set"), Description("Sets an existing DateTime item. Never throws.")]
        public bool SetDateTime(string name, DateTime value, out string message) => SetNative(name, DataBagValueType.DateTime, value, "SetDateTime", out message);
        /// <summary>Updates an existing Json entry after validation.</summary>
        [Category("Data Bag - Set"), Description("Validates and sets an existing Json item. Never throws.")]
        public bool SetJson(string name, string valueJson, out string message) => Guard("SetJson", (out string inner) => SetExistingCore(name, DataBagValueType.Json, valueJson, out inner), out message);
        /// <summary>Sets an existing item to null.</summary>
        [Category("Data Bag - Set"), Description("Clears an existing non-read-only item to null without changing its declared type. Never throws.")]
        public bool SetNull(string name, out string message) => Guard("SetNull", (out string inner) => SetExistingNativeCore(name, null, null, out inner), out message);

        /// <summary>Atomically updates existing entries from a flat JSON object.</summary>
        [Category("Data Bag - Set"), Description("Updates existing items from JSON properties. Unknown names fail or are ignored. Never throws.")]
        public bool SetFromJsonObject(string valuesJson, DataBagUnknownNamePolicy unknownNamePolicy, out int updatedCount, out int skippedCount, out string message)
        {
            updatedCount = skippedCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireReady(out message) || !ValidEnum(unknownNamePolicy, nameof(unknownNamePolicy), out message)) return false; using JsonDocument doc = JsonDocument.Parse(valuesJson); if (doc.RootElement.ValueKind != JsonValueKind.Object) { message = "valuesJson must contain a JSON object."; return false; } var candidate = Clone(active); foreach (JsonProperty p in doc.RootElement.EnumerateObject()) { if (!candidate.TryGetValue(p.Name, out Entry e)) { if (unknownNamePolicy == DataBagUnknownNamePolicy.Ignore) { skippedCount++; continue; } message = "The item '" + p.Name + "' is not defined."; updatedCount = skippedCount = 0; return false; } object value = ParseJsonForDeclaredType(p.Value, e.Type); if (!TryAssign(e, value, out message)) { updatedCount = skippedCount = 0; return false; } updatedCount++; } active = candidate; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { updatedCount = skippedCount = 0; message = NeverThrowsGuard.Failure("SetFromJsonObject", ex); return false; }
        }

        /// <summary>Atomically maps one DataTable row's column names to existing items.</summary>
        [Category("Data Bag - Set"), Description("Updates existing items from one DataTable row. Never throws.")]
        public bool SetFromDataRow(DataTable table, int rowIndex, DataBagUnknownNamePolicy unknownNamePolicy, out int updatedCount, out int skippedCount, out string message)
        {
            updatedCount = skippedCount = 0; message = null;
            try { lock (syncRoot) { if (!RequireReady(out message) || table == null) { if (message == null) message = "table is required."; return false; } if (rowIndex < 0 || rowIndex >= table.Rows.Count) { message = "rowIndex is outside the table's row range."; return false; } if (!ValidEnum(unknownNamePolicy, nameof(unknownNamePolicy), out message)) return false; var candidate = Clone(active); foreach (DataColumn column in table.Columns) { if (!candidate.TryGetValue(column.ColumnName, out Entry e)) { if (unknownNamePolicy == DataBagUnknownNamePolicy.Ignore) { skippedCount++; continue; } message = "The item '" + column.ColumnName + "' is not defined."; updatedCount = skippedCount = 0; return false; } object cell = table.Rows[rowIndex][column]; object value = cell == DBNull.Value ? null : ConvertNative(cell, e.Type); if (!TryAssign(e, value, out message)) { updatedCount = skippedCount = 0; return false; } updatedCount++; } active = candidate; return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { updatedCount = skippedCount = 0; message = NeverThrowsGuard.Failure("SetFromDataRow", ex); return false; }
        }

        /// <summary>Gets an existing value and its declared type as invariant text.</summary>
        [Category("Data Bag - Get"), Description("Gets any item as invariant text plus its declared type. Missing is normal. Never throws.")]
        public bool TryGetValue(string name, out bool found, out DataBagValueType valueType, out string value, out string message)
        {
            found = false; valueType = default; value = null; message = null;
            try { lock (syncRoot) { if (!RequireReady(out message) || !ValidName(name, out message)) return false; if (!active.TryGetValue(name, out Entry e)) return true; found = true; valueType = e.Type; value = FormatValue(e); return true; } }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("TryGetValue", ex); return false; }
        }

        /// <summary>Gets a String item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing String item. Missing is normal; a type mismatch is a failure. Never throws.")]
        public bool TryGetString(string name, out bool found, out string value, out string message) => TryGetTyped(name, DataBagValueType.String, out found, out value, out message);
        /// <summary>Gets a Boolean item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing Boolean item. Never throws.")]
        public bool TryGetBoolean(string name, out bool found, out bool value, out string message) => TryGetNative(name, DataBagValueType.Boolean, out found, out value, out message);
        /// <summary>Gets an Int32 item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing Int32 item. Never throws.")]
        public bool TryGetInt32(string name, out bool found, out int value, out string message) => TryGetNative(name, DataBagValueType.Int32, out found, out value, out message);
        /// <summary>Gets an Int64 item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing Int64 item. Never throws.")]
        public bool TryGetInt64(string name, out bool found, out long value, out string message) => TryGetNative(name, DataBagValueType.Int64, out found, out value, out message);
        /// <summary>Gets a Decimal item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing Decimal item. Never throws.")]
        public bool TryGetDecimal(string name, out bool found, out decimal value, out string message) => TryGetNative(name, DataBagValueType.Decimal, out found, out value, out message);
        /// <summary>Gets a Double item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing Double item. Never throws.")]
        public bool TryGetDouble(string name, out bool found, out double value, out string message) => TryGetNative(name, DataBagValueType.Double, out found, out value, out message);
        /// <summary>Gets a DateTime item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing DateTime item. Never throws.")]
        public bool TryGetDateTime(string name, out bool found, out DateTime value, out string message) => TryGetNative(name, DataBagValueType.DateTime, out found, out value, out message);
        /// <summary>Gets a Json item.</summary>
        [Category("Data Bag - Get"), Description("Gets an existing Json item's raw JSON. Never throws.")]
        public bool TryGetJson(string name, out bool found, out string valueJson, out string message) => TryGetTyped(name, DataBagValueType.Json, out found, out valueJson, out message);

        /// <summary>Checks whether an item is defined.</summary>
        [Category("Data Bag - Query"), Description("Checks whether a name is defined. Never throws.")]
        public bool Contains(string name, out bool exists, out string message) { exists = false; message = null; try { lock (syncRoot) { if (!RequireReady(out message) || !ValidName(name, out message)) return false; exists = active.ContainsKey(name); return true; } } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("Contains", ex); return false; } }
        /// <summary>Returns lifecycle state and item count.</summary>
        [Category("Data Bag - Query"), Description("Returns the current lifecycle state and active item count. Never throws.")]
        public bool GetState(out DataBagState dataBagState, out int itemCount, out string message) { dataBagState = default; itemCount = 0; message = null; try { lock (syncRoot) { if (state == DataBagState.Disposed) { dataBagState = state; message = "This DataBagUtils component has been disposed."; return false; } dataBagState = state; itemCount = active.Count; return true; } } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("GetState", ex); return false; } }
        /// <summary>Returns a redacted typed JSON snapshot.</summary>
        [Category("Data Bag - Query"), Description("Returns definitions and values as typed JSON; sensitive values are redacted. Never throws.")]
        public bool GetSnapshotJson(out string snapshotJson, out string message) { snapshotJson = null; message = null; try { lock (syncRoot) { if (!RequireReady(out message)) return false; snapshotJson = JsonSerializer.Serialize(new { values = active.Values.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => new { name = e.Name, type = e.Type.ToString(), value = e.Sensitive ? "***" : FormatValue(e), mustHaveValue = e.MustHaveValue, readOnly = e.ReadOnly, writeOnce = e.WriteOnce, sensitive = e.Sensitive }) }); return true; } } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("GetSnapshotJson", ex); return false; } }

        /// <summary>Restores mutable entries to declared defaults.</summary>
        [Category("Data Bag - Reset"), Description("Restores mutable values to initialization defaults and resets write-once assignment state. Never throws.")]
        public bool ResetValues(out int resetCount, out string message) { resetCount = 0; message = null; try { lock (syncRoot) { if (!RequireReady(out message)) return false; foreach (Entry e in active.Values.Where(e => !e.ReadOnly)) { e.Value = e.DefaultValue; e.HasRuntimeAssignment = false; resetCount++; } return true; } } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { resetCount = 0; message = NeverThrowsGuard.Failure("ResetValues", ex); return false; } }
        /// <summary>Reports must-have-value items whose values are null or empty strings.</summary>
        [Category("Data Bag - Validation"), Description("Reports must-have-value items that are null or empty. Never throws.")]
        public bool ValidateRequiredValuesPresent(out bool ready, out int missingCount, out string missingNamesJson, out string message) { ready = false; missingCount = 0; missingNamesJson = null; message = null; try { lock (syncRoot) { if (!RequireReady(out message)) return false; string[] missing = active.Values.Where(e => e.MustHaveValue && (e.Value == null || (e.Type == DataBagValueType.String && (string)e.Value == ""))).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(); missingCount = missing.Length; ready = missingCount == 0; missingNamesJson = JsonSerializer.Serialize(missing); return true; } } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("ValidateRequiredValuesPresent", ex); return false; } }

        /// <summary>Clears active and staged values during disposal.</summary>
        protected override void Dispose(bool disposing) { if (disposing) lock (syncRoot) { active.Clear(); staging?.Clear(); staging = null; state = DataBagState.Disposed; } base.Dispose(disposing); }

        private delegate bool MessageAction(out string message);
        private bool Guard(string operation, MessageAction action, out string message) { message = null; try { lock (syncRoot) return action(out message); } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(operation, ex); return false; } }
        private bool SetNative(string name, DataBagValueType type, object value, string operation, out string message) => Guard(operation, (out string inner) => SetExistingNativeCore(name, type, value, out inner), out message);
        private bool SetTypedCore(string name, DataBagValueType type, string value, bool mustHaveValue, bool readOnly, bool writeOnce, Dictionary<string, Entry> target, out string message) { message = null; if (!RequireInitializing(out message) || !ValidName(name, out message) || !ValidEnum(type, nameof(type), out message)) return false; if (target.Count >= maximumItems && !target.ContainsKey(name)) { message = "The operation would exceed MaximumItems (" + maximumItems + ")."; return false; } Entry entry = CreateEntry(name, type, value, mustHaveValue, readOnly, writeOnce, false); if (target.TryGetValue(name, out Entry existing) && existing.Type != type) { message = "The item '" + name + "' is already declared as " + existing.Type + "."; return false; } target[name] = entry; return true; }
        private bool SetExistingCore(string name, DataBagValueType? expected, string text, out string message) { message = null; if (!RequireReady(out message) || !ValidName(name, out message) || !active.TryGetValue(name, out Entry e)) { if (message == null) message = "The item '" + name + "' is not defined."; return false; } if (expected.HasValue && e.Type != expected.Value) { message = TypeMismatch(name, expected.Value, e.Type); return false; } return TryAssign(e, ParseText(text, e.Type), out message); }
        private bool SetExistingNativeCore(string name, DataBagValueType? expected, object value, out string message) { message = null; if (!RequireReady(out message) || !ValidName(name, out message) || !active.TryGetValue(name, out Entry e)) { if (message == null) message = "The item '" + name + "' is not defined."; return false; } if (expected.HasValue && e.Type != expected.Value) { message = TypeMismatch(name, expected.Value, e.Type); return false; } return TryAssign(e, value, out message); }
        private static bool TryAssign(Entry e, object value, out string message) { message = null; if (e.ReadOnly) { message = "The item '" + e.Name + "' is read-only."; return false; } if (e.WriteOnce && e.HasRuntimeAssignment) { message = "The item '" + e.Name + "' is write-once and has already been assigned."; return false; } e.Value = value; e.HasRuntimeAssignment = true; return true; }
        private bool TryGetTyped(string name, DataBagValueType expected, out bool found, out string value, out string message) { found = false; value = null; message = null; try { lock (syncRoot) { if (!RequireReady(out message) || !ValidName(name, out message)) return false; if (!active.TryGetValue(name, out Entry e)) return true; found = true; if (e.Type != expected) { message = TypeMismatch(name, expected, e.Type); return false; } value = e.Value as string; return true; } } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("TryGet" + expected, ex); return false; } }
        private bool TryGetNative<T>(string name, DataBagValueType expected, out bool found, out T value, out string message) { found = false; value = default; message = null; try { lock (syncRoot) { if (!RequireReady(out message) || !ValidName(name, out message)) return false; if (!active.TryGetValue(name, out Entry e)) return true; found = true; if (e.Type != expected) { message = TypeMismatch(name, expected, e.Type); return false; } if (e.Value != null) value = (T)e.Value; return true; } } catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure("TryGet" + expected, ex); return false; } }
        private bool TryParseTypedDefinitions(string json, Dictionary<string, Entry> target, DataBagConflictPolicy policy, out int added, out int replaced, out int skipped, out string message) { added = replaced = skipped = 0; message = null; using JsonDocument doc = JsonDocument.Parse(json); JsonElement root = doc.RootElement; JsonElement values; if (root.ValueKind == JsonValueKind.Array) values = root; else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("values", out values) && values.ValueKind == JsonValueKind.Array) { } else { message = "Typed definition JSON must be an array or an object containing a values array."; return false; } foreach (JsonElement item in values.EnumerateArray()) { if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out JsonElement n) || !item.TryGetProperty("type", out JsonElement t)) { message = "Every definition requires name and type."; return false; } string name = n.GetString(); if (!ValidName(name, out message)) return false; if (!Enum.TryParse(t.GetString(), true, out DataBagValueType type) || !Enum.IsDefined(type)) { message = "Unknown type for item '" + name + "'."; return false; } JsonElement v = item.TryGetProperty("defaultValue", out JsonElement dv) ? dv : item.TryGetProperty("value", out JsonElement iv) ? iv : default; object value = v.ValueKind == JsonValueKind.Undefined ? null : ParseJsonForDeclaredType(v, type); var entry = new Entry { Name = name, Type = type, Value = value, DefaultValue = value, MustHaveValue = GetFlag(item, "mustHaveValue"), ReadOnly = GetFlag(item, "readOnly"), WriteOnce = GetFlag(item, "writeOnce"), Sensitive = GetFlag(item, "sensitive") }; if (!MergeEntry(target, entry, policy, ref added, ref replaced, ref skipped, out message)) return false; } return true; }
        private bool MergeEntry(Dictionary<string, Entry> target, Entry entry, DataBagConflictPolicy policy, ref int added, ref int replaced, ref int skipped, out string message) { message = null; if (target.TryGetValue(entry.Name, out _)) { if (policy == DataBagConflictPolicy.Fail) { message = "The item '" + entry.Name + "' is already defined."; return false; } if (policy == DataBagConflictPolicy.KeepExisting) { skipped++; return true; } target[entry.Name] = entry; replaced++; return true; } if (target.Count >= maximumItems) { message = "The operation would exceed MaximumItems (" + maximumItems + ")."; return false; } target.Add(entry.Name, entry); added++; return true; }
        private Entry InferEntry(string name, JsonElement value) { DataBagValueType type = value.ValueKind switch { JsonValueKind.String => DataBagValueType.String, JsonValueKind.True or JsonValueKind.False => DataBagValueType.Boolean, JsonValueKind.Number when value.TryGetInt32(out _) => DataBagValueType.Int32, JsonValueKind.Number when value.TryGetInt64(out _) => DataBagValueType.Int64, JsonValueKind.Number when value.TryGetDecimal(out _) => DataBagValueType.Decimal, JsonValueKind.Number => DataBagValueType.Double, JsonValueKind.Null => DataBagValueType.Null, _ => DataBagValueType.Json }; object parsed = ParseJsonForDeclaredType(value, type); return new Entry { Name = name, Type = type, Value = parsed, DefaultValue = parsed }; }
        private Entry CreateEntry(string name, DataBagValueType type, string value, bool mustHaveValue, bool readOnly, bool writeOnce, bool sensitive) { if (!ValidName(name, out string error)) throw new ArgumentException(error, nameof(name)); object parsed = ParseText(value, type); return new Entry { Name = name, Type = type, Value = parsed, DefaultValue = parsed, MustHaveValue = mustHaveValue, ReadOnly = readOnly, WriteOnce = writeOnce, Sensitive = sensitive }; }
        private static object ParseText(string value, DataBagValueType type) { if (value == null || type == DataBagValueType.Null) return null; return type switch { DataBagValueType.String => value, DataBagValueType.Boolean => bool.Parse(value), DataBagValueType.Int32 => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture), DataBagValueType.Int64 => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture), DataBagValueType.Decimal => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture), DataBagValueType.Double => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture), DataBagValueType.DateTime => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DataBagValueType.Json => ValidateJson(value), _ => null }; }
        private static object ParseJsonForDeclaredType(JsonElement value, DataBagValueType type) { if (value.ValueKind == JsonValueKind.Null) return null; return type switch { DataBagValueType.String when value.ValueKind == JsonValueKind.String => value.GetString(), DataBagValueType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean(), DataBagValueType.Int32 when value.TryGetInt32(out int i) => i, DataBagValueType.Int64 when value.TryGetInt64(out long l) => l, DataBagValueType.Decimal when value.TryGetDecimal(out decimal d) => d, DataBagValueType.Double when value.TryGetDouble(out double f) => f, DataBagValueType.DateTime when value.ValueKind == JsonValueKind.String => DateTime.Parse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DataBagValueType.Json => value.GetRawText(), DataBagValueType.Null when value.ValueKind == JsonValueKind.Null => null, _ => throw new FormatException("The JSON value does not match the declared " + type + " type.") }; }
        private static object ConvertNative(object value, DataBagValueType type) { if (type == DataBagValueType.Json) return ValidateJson(Convert.ToString(value, CultureInfo.InvariantCulture)); if (type == DataBagValueType.DateTime) return value is DateTime dt ? dt : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); if (type == DataBagValueType.String) return Convert.ToString(value, CultureInfo.InvariantCulture); if (type == DataBagValueType.Null) return null; return Convert.ChangeType(value, type switch { DataBagValueType.Boolean => typeof(bool), DataBagValueType.Int32 => typeof(int), DataBagValueType.Int64 => typeof(long), DataBagValueType.Decimal => typeof(decimal), _ => typeof(double) }, CultureInfo.InvariantCulture); }
        private static string ValidateJson(string value) { using (JsonDocument.Parse(value)) { } return value; }
        private static string FormatValue(Entry e) { if (e.Value == null) return null; return e.Type switch { DataBagValueType.Boolean => ((bool)e.Value) ? "true" : "false", DataBagValueType.DateTime => ((DateTime)e.Value).ToString("O", CultureInfo.InvariantCulture), DataBagValueType.Decimal => ((decimal)e.Value).ToString(CultureInfo.InvariantCulture), DataBagValueType.Double => ((double)e.Value).ToString("R", CultureInfo.InvariantCulture), _ => Convert.ToString(e.Value, CultureInfo.InvariantCulture) }; }
        private Dictionary<string, Entry> NewDictionary() => new Dictionary<string, Entry>(caseSensitiveNames ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Entry> Clone(Dictionary<string, Entry> source) { var result = NewDictionary(); foreach (var pair in source) result.Add(pair.Key, pair.Value.Clone()); return result; }
        private bool ValidName(string name, out string message) { message = null; if (string.IsNullOrWhiteSpace(name)) { message = "name is required."; return false; } if (name.Length > 256) { message = "name must not exceed 256 characters."; return false; } if (!string.Equals(name, name.Trim(), StringComparison.Ordinal)) { message = "name must not have leading or trailing whitespace."; return false; } return true; }
        private static bool ValidEnum<T>(T value, string name, out string message) where T : struct, Enum { message = Enum.IsDefined(value) ? null : name + " is not a defined value."; return message == null; }
        private bool RequireNotDisposed(out string message) { message = state == DataBagState.Disposed ? "This DataBagUtils component has been disposed. Drag a new component onto the automation surface." : null; return message == null; }
        private bool RequireInitializing(out string message) { if (!RequireNotDisposed(out message)) return false; if (state != DataBagState.Initializing || staging == null) { message = "Begin initialization before defining or loading items."; return false; } return true; }
        private bool RequireReady(out string message) { if (!RequireNotDisposed(out message)) return false; if (state != DataBagState.Ready) { message = "The data bag is not ready. Initialize and complete its definitions first."; return false; } return true; }
        private void RequireConfigurable() { if (state == DataBagState.Disposed) throw new ObjectDisposedException(nameof(DataBagUtils)); if (state != DataBagState.NotInitialized) throw new InvalidOperationException("Design-time configuration cannot change after initialization starts."); }
        private static string TypeMismatch(string name, DataBagValueType expected, DataBagValueType actual) => "The item '" + name + "' is declared as " + actual + ", not " + expected + ".";
        private static bool HasColumn(DataTable table, string column, string parameter, out string message) { message = null; if (string.IsNullOrWhiteSpace(column) || !table.Columns.Contains(column)) { message = parameter + " must name an existing table column."; return false; } return true; }
        private static bool ReadFlag(DataRow row, string column) => !string.IsNullOrWhiteSpace(column) && row.Table.Columns.Contains(column) && row[column] != DBNull.Value && Convert.ToBoolean(row[column], CultureInfo.InvariantCulture);
        private static bool GetFlag(JsonElement item, string name) => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

        private sealed class Entry
        {
            internal string Name; internal DataBagValueType Type; internal object Value; internal object DefaultValue;
            internal bool MustHaveValue; internal bool ReadOnly; internal bool WriteOnce; internal bool Sensitive; internal bool HasRuntimeAssignment;
            internal Entry Clone() => (Entry)MemberwiseClone();
        }
    }
}
