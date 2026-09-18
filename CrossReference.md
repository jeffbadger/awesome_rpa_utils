# PME Cross-Reference

A single searchable index of every **PME** — Property, Method, and Event —
exposed by every component in this repository. Use it to answer "does
anything in this library already do X" without opening 20 different
READMEs: `Ctrl+F` for a method name, a keyword from what you're trying to
do, or a parameter/return type, or jump straight to a component from the
quick-reference table below.

Each component is a self-contained Pega Robot Studio `.csproj` (see the
[root README](README.md) for how they fit together, and each component's
own README/Documentation folder for full usage scenarios and examples).
Nearly every method follows this repository's never-throws convention —
`bool` return (success) plus an `out string message` (human-readable
failure reason) — noted per-method below only where it isn't the case.

## Quick Reference

| Component | Assembly | Methods | Props | Events | What it does |
|---|---|---|---|---|---|
| [ArchiveUtils](#archiveutils) | `ArchiveAutomation` | 18 | 0 | 0 | Creates, extracts, inspects, and validates ZIP archives, with zip-slip/zip-bomb protection and CRC-32 verification. |
| [CommandLineUtils](#commandlineutils) | `CommandLineAutomation` | 7 | 0 | 0 | Runs external commands/processes and captures exit code, stdout, and stderr — including elevated and fire-and-forget launches. |
| [DataContractUtils](#datacontractutils) | `DataContractAutomation` | 36 | 6 | 0 | A typed named-value contract defined at initialization, then sealed, with strict scalar getters/setters at runtime. |
| [DialogUtils](#dialogutils) | `DialogAutomation` | 14 | 0 | 0 | Finds and dismisses native dialogs by button text/control ID via `BM_CLICK`, without moving the cursor. |
| [EventLogUtils](#eventlogutils) | `EventLogAutomation` | 20 | 0 | 0 | Reads, queries, waits for, writes, and exports/imports Windows Event Log entries. |
| [FileWatchUtils](#filewatchutils) | `FileWatchAutomation` | 28 | 0 | 5 | Waits for file existence/deletion/change/stability/unlock, watches for filesystem events, and atomically moves/replaces/claims files. |
| [JsonUtils](#jsonutils) | `JsonAutomation` | 24 | 0 | 0 | Reads, updates, validates, and transforms JSON via real JSONPath. |
| [KeyboardUtils](#keyboardutils) | `KeyboardAutomation` | 11 (12 rows) | 0 | 0 | Injects keyboard input via `SendInput` — key presses, combos, typed text — and queries key/modifier state. |
| [LocalQueueUtils](#localqueueutils) | `LocalQueueAutomation` | 21 | 0 | 0 | A persistent, machine-local work queue with a lease/process/complete loop, for variable-count work without a Pega collection proxy. |
| [MouseUtils](#mouseutils) | `MouseAutomation` | 91 (98 rows) | 0 | 0 | Moves, clicks, drags, and scrolls the mouse; controls cursor appearance, visibility, and confinement. |
| [OcrUtils](#ocrutils) | `OcrAutomation` | 11 | 0 | 0 | Recognizes text from the screen or an image file via `Windows.Media.Ocr`. |
| [ScreenCaptureUtils](#screencaptureutils) | `ScreenCaptureAutomation` | 28 (34 rows) | 0 | 0 | Captures the screen/region/window to file or clipboard; compares against a baseline; annotates/redacts saved screenshots. |
| [ServiceUtils](#serviceutils) | `ServiceAutomation` | 24 (25 rows) | 0 | 0 | Queries, starts, stops, restarts, pauses/resumes, and configures the startup type of Windows services. |
| [SessionUtils](#sessionutils) | `SessionAutomation` | 31 | 0 | 0 | Reports on and acts on Windows session/workstation state — identity, kind, connect state, lock, idle time. |
| [StackUtils](#stackutils) | `StackAutomation` | 13 | 1 | 0 | An instance-local, in-memory LIFO stack for variable-count RPA work. |
| [TerminalUtils](#terminalutils) | `TerminalAutomation` | 11 | 0 | 0 | Reads a console app's live screen buffer and injects keystrokes into it, for terminal UIs normal UI automation can't see. |
| [UIAutomationUtils](#uiautomationutils) | `UIAutomation` | 53 (55 rows) | 0 | 0 | Finds and drives modern (WinUI3/UWP/WPF/browser-hosted) UI via Windows UI Automation. |
| [ValueStoreUtils](#valuestoreutils) | `ValueStoreAutomation` | 49 | 1 | 0 | A freeform key/value bag with forgiving typed conversion and dot-notation access into JSON-shaped values. |
| [WindowUtils](#windowutils) | `WindowAutomation` | 29 | 0 | 0 | Enumerates, locates, moves/resizes, activates, and closes windows via Win32 window APIs. |
| [WinEventUtils](#wineventutils) | `WinEventAutomation` | 29 (32 rows) | 0 | 0 | Watches Windows UI events via `SetWinEventHook` and delivers them instantly — synchronous waits or background subscriptions. |

*"Methods" counts distinct method names; "rows" (where different) includes
every overload, each listed as its own row in that component's table below.*

---

## ArchiveUtils

Pega Robot Studio-ready component for handling ZIP archives: creating/extracting archives, listing contents before extraction, extracting a single matching entry, validating CRC-32 checksums, detecting encrypted entries, building diagnostic/failure bundles, mutating an existing archive, merging two archives, and creating/extracting password-protected archives.

**Namespace:** `ArchiveAutomation` | **Assembly:** `ArchiveAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `AddOrReplaceFilesInArchive` | `bool AddOrReplaceFilesInArchive(string archivePath, string sourceFilePathsCsv, string entryNamesCsv, out string message)` | Adds files to an existing archive, replacing any existing entry of the same name. Never throws. |
| `CreateArchive` | `bool CreateArchive(string sourceDirectoryPath, string archivePath, bool overwrite, bool includeBaseDirectory, bool normalizeTimestamps, string normalizedTimestampUtcIso8601, out string message)` | Creates a ZIP archive from a directory's contents, published atomically only once fully built. Never throws. |
| `CreateDiagnosticBundle` | `bool CreateDiagnosticBundle(string sourceFilePathsCsv, string outputArchivePath, bool overwrite, string manifestText, out string message)` | Creates a diagnostic/failure bundle by zipping a list of files, with an optional manifest entry, built atomically. Never throws. |
| `CreateDiagnosticBundleSimple` | `bool CreateDiagnosticBundleSimple(string sourceFilePathsCsv, string outputDirectoryPath, out string createdArchivePath, out string message)` | Same as `CreateDiagnosticBundle`, auto-naming the archive with a timestamp and returning its resolved path. Never throws. |
| `CreateEncryptedArchive` | `bool CreateEncryptedArchive(string sourceDirectoryPath, string archivePath, string password, bool overwrite, bool includeBaseDirectory, bool useLegacyZipCrypto, out string message)` | Creates a password-protected ZIP archive from a directory's contents, using AES-256 by default. Never throws. |
| `ExtractArchive` | `bool ExtractArchive(string archivePath, string destinationDirectoryPath, bool overwrite, long maxTotalExpandedSizeBytes, double maxCompressionRatio, bool preserveTimestamps, out string message)` | Extracts a ZIP archive to a directory, failing closed up front if declared sizes/ratios exceed the given limits. Never throws. |
| `ExtractArchiveSimple` | `bool ExtractArchiveSimple(string archivePath, string destinationDirectoryPath, bool overwrite, out string message)` | Same as `ExtractArchive`, defaulting to a 1 GiB total-expanded-size limit, a 100:1 compression-ratio limit, and preserved timestamps. Never throws. |
| `ExtractArchiveWithPassword` | `bool ExtractArchiveWithPassword(string archivePath, string destinationDirectoryPath, string password, bool overwrite, long maxTotalExpandedSizeBytes, double maxCompressionRatio, out string message)` | Extracts a password-protected ZIP archive to a directory, with the same size/ratio pre-check and zip-slip guard as `ExtractArchive`. Never throws. |
| `ExtractFirstMatchingFile` | `bool ExtractFirstMatchingFile(string archivePath, string entryNamePattern, string destinationDirectoryPath, bool overwrite, long maxExpandedSizeBytes, double maxCompressionRatio, out string matchedEntryName, out string message)` | Extracts the first entry whose name matches a glob pattern, guarded against path traversal and zip-bomb limits. Never throws. |
| `ExtractSingleFile` | `bool ExtractSingleFile(string archivePath, string entryFullName, string destinationDirectoryPath, bool overwrite, long maxExpandedSizeBytes, double maxCompressionRatio, out string message)` | Extracts one entry from a ZIP archive by its exact name, guarded against path traversal and zip-bomb limits. Never throws. |
| `HasEncryptedEntries` | `bool HasEncryptedEntries(string archivePath, out bool hasEncryptedEntries, out string message)` | Scans a ZIP archive for any encrypted entry, stopping at the first one found (detection only, no password). Never throws. |
| `ListArchiveContentsJson` | `bool ListArchiveContentsJson(string archivePath, out string json, out string message)` | Lists every entry in a ZIP archive as a JSON array, without extracting anything. Never throws. |
| `MergeArchives` | `bool MergeArchives(string firstArchivePath, string secondArchivePath, string outputArchivePath, bool overwrite, out string message)` | Builds a new archive containing every entry from two existing archives, without modifying either input. Never throws. |
| `RemoveArchiveEntry` | `bool RemoveArchiveEntry(string archivePath, string entryFullName, out string message)` | Removes one named entry from an existing archive. Never throws. |
| `RenameArchiveEntry` | `bool RenameArchiveEntry(string archivePath, string entryFullName, string newEntryName, out string message)` | Renames one entry in an existing archive, preserving its content and timestamp. Never throws. |
| `TryGetArchiveMetadata` | `bool TryGetArchiveMetadata(string archivePath, out int entryCount, out long totalUncompressedBytes, out long totalCompressedBytes, out bool hasEncryptedEntries, out string message)` | Gets summary metadata for a ZIP archive: entry count, total declared sizes, and whether any entry is encrypted. Never throws. |
| `ValidateArchiveCrc` | `bool ValidateArchiveCrc(string archivePath, out bool allEntriesValid, out string message)` | Verifies every non-encrypted entry's actual decompressed content against its declared CRC-32 checksum. Never throws. |
| `ValidateArchiveCrcJson` | `bool ValidateArchiveCrcJson(string archivePath, out string json, out string message)` | Same as `ValidateArchiveCrc`, but reports a JSON array with each entry's declared/computed CRC-32 and status. Never throws. |

## CommandLineUtils

Pega Robot Studio-ready component that runs external commands/processes and captures their exit code, standard output, and standard error.

**Namespace:** `CommandLineAutomation` | **Assembly:** `CommandLineAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `Run` | `bool Run(string fileName, out CommandResult result, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null, Encoding outputEncoding = null)` | Runs a file directly (no shell) with the given arguments, waits for it to exit, and captures its exit code, stdout, and stderr. Never throws. |
| `RunElevated` | `bool RunElevated(string fileName, out int exitCode, out bool timedOut, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1)` | Runs a file elevated (triggers the UAC consent prompt) and waits for it to exit; output cannot be captured for an elevated process. Never throws. |
| `RunFlat` | `bool RunFlat(string fileName, out int exitCode, out string standardOutput, out string standardError, out bool timedOut, out bool outputTruncated, out string message, string arguments = null, string workingDirectory = null, int timeoutMs = -1, string environmentVariablesText = null, string outputEncodingName = null)` | Same as `Run`, but with every object-typed port replaced by a scalar equivalent (individual outputs, a `NAME=VALUE` environment text, an encoding name). Never throws. |
| `RunShellCommand` | `bool RunShellCommand(string command, out CommandResult result, out string message, string[] allowedPrograms = null, string workingDirectory = null, int timeoutMs = -1, IDictionary<string, string> environmentVariables = null, Encoding outputEncoding = null)` | Runs a command through `cmd.exe /d /s /c "command"`, waits for it to exit, and captures its exit code, stdout, and stderr; optionally validates every command segment starts with an allowed program first. Never throws. |
| `RunShellCommandFlat` | `bool RunShellCommandFlat(string command, out int exitCode, out string standardOutput, out string standardError, out bool timedOut, out bool outputTruncated, out string message, string allowedProgramsCsv = null, string workingDirectory = null, int timeoutMs = -1, string environmentVariablesText = null, string outputEncodingName = null)` | Same as `RunShellCommand`, but with every object-typed port replaced by a scalar equivalent (individual outputs, a comma-separated allowlist, a `NAME=VALUE` environment text, an encoding name). Never throws. |
| `StartFireAndForget` | `bool StartFireAndForget(string fileName, out int processId, out string message, string arguments = null, string workingDirectory = null, IDictionary<string, string> environmentVariables = null)` | Starts a file without redirecting output or waiting for it to exit, and returns its process ID immediately. Never throws. |
| `StartFireAndForgetWithEnvironment` | `bool StartFireAndForgetWithEnvironment(string fileName, out int processId, out string message, string arguments = null, string workingDirectory = null, string environmentVariablesText = null)` | Same as `StartFireAndForget`, but takes a `NAME=VALUE` environment text instead of an `IDictionary<string, string>`. Never throws. |

## DataContractUtils

Pega Robot Studio-ready component that provides an instance-local, typed, named data contract for phased RPA workflows: definitions are initialized and sealed before runtime updates can occur.

**Namespace:** `DataContractAutomation` | **Assembly:** `DataContractAutomation`

### Properties

| Property | Type | Description |
|---|---|---|
| `CaseSensitiveNames` | `bool` | Whether names use case-sensitive comparison. |
| `InitialItemsFilePath` | `string` | Definition-file path used by `Initialize`. |
| `InitialItemsJson` | `string` | Embedded typed definitions used by `Initialize`. |
| `InitializationSource` | `DataContractInitializationSource` | Source used by `Initialize`. |
| `MaximumItems` | `int` | Maximum number of definitions allowed. |
| `SealAfterInitialization` | `bool` | Whether `Initialize` automatically seals the schema. |

### Methods

| Method | Signature | Description |
|---|---|---|
| `BeginInitialization` | `bool BeginInitialization(bool clearExisting, out string message)` | Starts an atomic programmatic initialization transaction. Never throws. |
| `CancelInitialization` | `bool CancelInitialization(out int discardedCount, out string message)` | Discards staged initialization without changing the active contract. Never throws. |
| `CompleteInitialization` | `bool CompleteInitialization(out int itemCount, out string message)` | Atomically publishes staged definitions and seals the contract. Never throws. |
| `Contains` | `bool Contains(string name, out bool exists, out string message)` | Checks whether an item is defined. Never throws. |
| `GetSnapshotJson` | `bool GetSnapshotJson(out string snapshotJson, out string message)` | Returns a redacted typed JSON snapshot of definitions and values. Never throws. |
| `GetState` | `bool GetState(out DataContractState dataContractState, out int itemCount, out string message)` | Returns lifecycle state and item count. Never throws. |
| `Initialize` | `bool Initialize(out int loadedCount, out string message)` | Initializes atomically from the configured design-time source. Never throws. |
| `LoadDataTable` | `bool LoadDataTable(DataTable table, DataContractConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Loads Name, Type, Value, MustHaveValue, ReadOnly, WriteOnce, and Sensitive definition columns. Never throws. |
| `LoadDataTableMapped` | `bool LoadDataTableMapped(DataTable table, string nameColumn, string typeColumn, string valueColumn, string mustHaveValueColumn, string readOnlyColumn, string writeOnceColumn, string sensitiveColumn, DataContractConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Loads definitions from explicitly mapped DataTable columns. Never throws. |
| `LoadJsonObject` | `bool LoadJsonObject(string jsonObject, DataContractConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Loads inferred definitions from a flat JSON object into staging. Never throws. |
| `LoadTypedJson` | `bool LoadTypedJson(string typedJson, DataContractConflictPolicy conflictPolicy, out int addedCount, out int replacedCount, out int skippedCount, out string message)` | Loads typed definitions from JSON into staging. Never throws. |
| `PopulateInitialItemsJsonTemplate` | `bool PopulateInitialItemsJsonTemplate(out string message)` | Populates `InitialItemsJson` with an editable one-row definition template. Never throws. |
| `ResetValues` | `bool ResetValues(out int resetCount, out string message)` | Restores mutable entries to declared defaults. Never throws. |
| `SetBoolean` | `bool SetBoolean(string name, bool value, out string message)` | Updates an existing Boolean entry. Never throws. |
| `SetDateTime` | `bool SetDateTime(string name, DateTime value, out string message)` | Updates an existing DateTime entry. Never throws. |
| `SetDecimal` | `bool SetDecimal(string name, decimal value, out string message)` | Updates an existing Decimal entry. Never throws. |
| `SetDouble` | `bool SetDouble(string name, double value, out string message)` | Updates an existing Double entry. Never throws. |
| `SetFromDataRow` | `bool SetFromDataRow(DataTable table, int rowIndex, DataContractUnknownNamePolicy unknownNamePolicy, out int updatedCount, out int skippedCount, out string message)` | Atomically maps one DataTable row's column names to existing items. Never throws. |
| `SetFromJsonObject` | `bool SetFromJsonObject(string valuesJson, DataContractUnknownNamePolicy unknownNamePolicy, out int updatedCount, out int skippedCount, out string message)` | Atomically updates existing entries from a flat JSON object. Never throws. |
| `SetInt32` | `bool SetInt32(string name, int value, out string message)` | Updates an existing Int32 entry. Never throws. |
| `SetInt64` | `bool SetInt64(string name, long value, out string message)` | Updates an existing Int64 entry. Never throws. |
| `SetJson` | `bool SetJson(string name, string valueJson, out string message)` | Updates an existing Json entry after validation. Never throws. |
| `SetNull` | `bool SetNull(string name, out string message)` | Sets an existing item to null. Never throws. |
| `SetString` | `bool SetString(string name, string value, out string message)` | Updates an existing string entry. Never throws. |
| `SetTypedValue` | `bool SetTypedValue(string name, DataContractValueType valueType, string value, out string message)` | Defines or updates a typed value during initialization. Never throws. |
| `SetValue` | `bool SetValue(string name, string value, out string message)` | Updates an existing entry from its invariant string representation. Never throws. |
| `TryGetBoolean` | `bool TryGetBoolean(string name, out bool found, out bool value, out string message)` | Gets a Boolean item. Never throws. |
| `TryGetDateTime` | `bool TryGetDateTime(string name, out bool found, out DateTime value, out string message)` | Gets a DateTime item. Never throws. |
| `TryGetDecimal` | `bool TryGetDecimal(string name, out bool found, out decimal value, out string message)` | Gets a Decimal item. Never throws. |
| `TryGetDouble` | `bool TryGetDouble(string name, out bool found, out double value, out string message)` | Gets a Double item. Never throws. |
| `TryGetInt32` | `bool TryGetInt32(string name, out bool found, out int value, out string message)` | Gets an Int32 item. Never throws. |
| `TryGetInt64` | `bool TryGetInt64(string name, out bool found, out long value, out string message)` | Gets an Int64 item. Never throws. |
| `TryGetJson` | `bool TryGetJson(string name, out bool found, out string valueJson, out string message)` | Gets a Json item. Never throws. |
| `TryGetString` | `bool TryGetString(string name, out bool found, out string value, out string message)` | Gets a String item. Never throws. |
| `TryGetValue` | `bool TryGetValue(string name, out bool found, out DataContractValueType valueType, out string value, out string message)` | Gets an existing value and its declared type as invariant text. Never throws. |
| `ValidateRequiredValuesPresent` | `bool ValidateRequiredValuesPresent(out bool ready, out int missingCount, out string missingNamesJson, out string message)` | Reports must-have-value items whose values are null or empty strings. Never throws. |

## DialogUtils

Pega Robot Studio-ready component that finds and dismisses native dialogs (message boxes, common dialogs) by button text or control ID, via `BM_CLICK` — no cursor movement required, and it works even if the dialog is behind other windows.

**Namespace:** `DialogAutomation` | **Assembly:** `DialogAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `CanDismissDialog` | `bool CanDismissDialog(IntPtr hDialog)` | Checks whether a dialog has at least one native `Button` control that the click methods can target. |
| `ClickButton` | `bool ClickButton(IntPtr hButton, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Invokes a button by sending it `BM_CLICK` (no cursor movement), after briefly polling for it to become enabled. |
| `ClickDialogButtonById` | `bool ClickDialogButtonById(IntPtr hDialog, int controlId, out bool wasEnabled, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Invokes a button by its control ID (`GetDlgItem`). |
| `ClickDialogButtonByText` | `bool ClickDialogButtonByText(IntPtr hDialog, string buttonText, out bool wasEnabled, out string message, bool exactMatch = true, int waitForEnabledMs = 500, int pollIntervalMs = 25)` | Finds a button on a dialog by its visible text and invokes it once. |
| `FindAllDialogs` | `List<IntPtr> FindAllDialogs(string titlePattern, bool exactMatch = true, int processId = 0)` | Finds every visible top-level window whose title matches a pattern, returning all matches instead of just the first. Never throws. |
| `FindButtonById` | `bool FindButtonById(IntPtr hDialog, out IntPtr hButton, int controlId)` | Finds a control on a dialog by its control ID (`GetDlgItem`). Never throws. |
| `FindButtonByText` | `bool FindButtonByText(IntPtr hDialog, out IntPtr hButton, string buttonText, bool exactMatch = true)` | Finds a button on a dialog by its visible text (case-insensitive). Never throws. |
| `FindDialog` | `bool FindDialog(string titlePattern, out IntPtr hDialog, out bool canDismiss, bool exactMatch, int processId = 0)` | Finds a top-level dialog window by its title, and reports whether it has a native `Button` control that can be clicked. Never throws. |
| `GetControlText` | `string GetControlText(IntPtr hControl)` | Gets any control's text via `GetWindowText` (buttons, static labels, edit fields, and the dialog's own title bar). |
| `GetDialogText` | `string GetDialogText(IntPtr hDialog)` | Gets a dialog's message body: the text of the first non-empty `Static` child control. |
| `HighlightControl` | `bool HighlightControl(IntPtr hControl, System.Drawing.Color color, int flashes = 3, int flashMs = 200, int lineWidth = 3)` | Flashes an inverting rectangle around a control to visually confirm which on-screen control a handle corresponds to. Never throws. |
| `ListDialogControls` | `List<DialogControlInfo> ListDialogControls(IntPtr hDialog)` | Lists every control on a dialog — including nested controls — with its ID, text, and window class. |
| `WaitForDialog` | `bool WaitForDialog(string titlePattern, int timeoutMs, int pollIntervalMs, out IntPtr hWnd, bool exactMatch, int processId = 0)` | Polls for a visible top-level dialog matching a title pattern until it appears or the timeout elapses. |
| `WaitForDialogToClose` | `bool WaitForDialogToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a dialog handle is no longer valid (the dialog closed), or the timeout elapses. |

## EventLogUtils

Reads, queries, waits for, writes, and exports/imports Windows Event Log entries on the local machine.

**Namespace:** `EventLogAutomation` | **Assembly:** `EventLogAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `CountMatchingEntries` | `bool CountMatchingEntries(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, out int count, out string message)` | Counts entries in a log matching the given filters; materializes every match before counting. |
| `CreateEventSource` | `bool CreateEventSource(string sourceName, string logName, out bool alreadyExisted, out string message)` | Registers a new event source, creating the log if needed. Requires administrator rights. |
| `CreateEventSourceSimple` | `bool CreateEventSourceSimple(string sourceName, string logName, out string message)` | Same as `CreateEventSource`, without the `alreadyExisted` output. |
| `DoesLogExist` | `bool DoesLogExist(string logName, out bool querySucceeded, out string message)` | Same as `DoesLogExistSimple`, plus a `querySucceeded` output separating query success from log existence. |
| `DoesLogExistSimple` | `bool DoesLogExistSimple(string logName, out string message)` | Returns True if a log with the given name exists on the local machine. |
| `DoesSourceExist` | `bool DoesSourceExist(string sourceName, out bool querySucceeded, out string message)` | Same as `DoesSourceExistSimple`, plus a `querySucceeded` output. |
| `DoesSourceExistSimple` | `bool DoesSourceExistSimple(string sourceName, out string message)` | Returns True if an event source with the given name is registered on the local machine. |
| `DumpRecentEntriesJson` | `bool DumpRecentEntriesJson(string logName, int count, out string json, out string message)` | Gets the last N entries from a log, unfiltered, as a JSON array. |
| `ExportFilteredLog` | `bool ExportFilteredLog(string logName, string xpath, string exportFilePath, out string message)` | Exports entries matching an XPath filter from a live log to a `.evtx` file. |
| `ListLogNames` | `List<string> ListLogNames()` | Gets the names of every event log on the local machine; empty list on failure. |
| `ListLogNamesDelimited` | `string ListLogNamesDelimited(string delimiter = ",")` | Same as `ListLogNames`, joined into a single delimited string. |
| `QueryByXPath` | `bool QueryByXPath(string logName, string xpath, int maxCount, out string json, out string message)` | Queries a live log with a raw XPath expression for filters the scalar helpers can't express; returns matches as a JSON array, newest first. |
| `QueryExportedLog` | `bool QueryExportedLog(string evtxFilePath, string xpath, int maxCount, out string json, out string message)` | Queries a previously exported `.evtx` file with a raw XPath filter; returns matches as a JSON array. |
| `QueryRecentEntriesJson` | `bool QueryRecentEntriesJson(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, int maxCount, out string json, out string message)` | Queries a log with scalar filters and returns matches as a JSON array, newest first; the main bulk-read method. |
| `TryGetLogNameForSource` | `bool TryGetLogNameForSource(string sourceName, out string logName, out string message)` | Finds which log a registered event source currently writes to. |
| `TryGetMostRecentEntry` | `bool TryGetMostRecentEntry(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, string sinceIso8601, out string timeCreatedIso8601, out int eventId, out string level, out string source, out string entryMessage, out string message)` | Finds the single most recent entry in a log matching the given filters. |
| `WaitForEntry` | `bool WaitForEntry(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, int timeoutMs, int pollIntervalMs, out bool timedOut, out string timeCreatedIso8601, out int eventId, out string entryMessage, out string message)` | Polls a log until a new entry matching the given filters appears, or the timeout elapses. |
| `WaitForEntrySimple` | `bool WaitForEntrySimple(string logName, string sourceFilter, string levelFilter, int eventIdFilter, string messageContains, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForEntry`, without the `timedOut`/entry outputs. |
| `WriteEntry` | `bool WriteEntry(string sourceName, string message, EventLogLevel level, int eventId, out string errorMessage)` | Writes an entry to the log the given source is already registered to. |
| `WriteEntrySimple` | `bool WriteEntrySimple(string sourceName, string message, out string errorMessage)` | Same as `WriteEntry`, defaulting to `Informational` level and event id 0. |

## FileWatchUtils

Coordinates with files produced by other applications: waits for existence/deletion/change/stability/unlock, watches for filesystem events, atomically moves/replaces/claims files, hashes files, and reads file metadata.

**Namespace:** `FileWatchAutomation` | **Assembly:** `FileWatchAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `AreFilesIdenticalByHash` | `bool AreFilesIdenticalByHash(string pathA, string pathB, out bool identical, out string message)` | Returns True if two files have identical SHA-256 hashes. |
| `AtomicMoveFile` | `bool AtomicMoveFile(string sourcePath, string destinationPath, bool overwrite, out string message)` | Moves a file to a new path, optionally overwriting an existing destination; atomic only on the same volume. |
| `ClaimFile` | `bool ClaimFile(string sourcePath, string inProgressDirectoryPath, out string claimedPath, out string message)` | Claims a work file by copying it into an in-progress directory under a source-scoped exclusive lock, then deleting the source. |
| `ComputeFileHash` | `bool ComputeFileHash(string path, string algorithmName, out string hashHex, out string message)` | Computes a file's hash using the named algorithm (SHA256, SHA1, MD5, SHA384, SHA512), as a lowercase hex string. |
| `ComputeFileHashSha256` | `bool ComputeFileHashSha256(string path, out string hashHex, out string message)` | Computes the SHA-256 hash of a file's contents, as a lowercase hex string. |
| `GetDirectoryListingJson` | `bool GetDirectoryListingJson(string directoryPath, string searchPattern, bool includeSubdirectories, out string json, out string message)` | Lists files in a directory matching a pattern as a JSON array of metadata. |
| `GetFileMetadataJson` | `bool GetFileMetadataJson(string path, out string json, out string message)` | Gets a file or directory's metadata as a JSON object (includes `IsDirectory`/`Extension`). |
| `IsFileLocked` | `bool IsFileLocked(string path, out bool querySucceeded, out string message)` | Same as `IsFileLockedSimple`, plus a `querySucceeded` output. |
| `IsFileLockedSimple` | `bool IsFileLockedSimple(string path, out string message)` | Returns True if the file is currently locked (cannot be opened for exclusive read access). |
| `IsWatching` | `bool IsWatching()` | Reports whether a background watch is currently running. |
| `ReplaceFile` | `bool ReplaceFile(string sourcePath, string destinationPath, string backupPath, out string message)` | Replaces a destination file's contents with a source file's, optionally keeping a backup; requires the destination to already exist. |
| `StartWatching` | `bool StartWatching(string directoryPath, string filter, bool includeSubdirectories, out string message)` | Starts watching a directory for filesystem changes in the background, without blocking. |
| `StopWatching` | `bool StopWatching(out string message)` | Stops and disposes the background watcher started by `StartWatching`. |
| `TryGetFileMetadata` | `bool TryGetFileMetadata(string path, out long sizeBytes, out string lastWriteUtcIso8601, out string createdUtcIso8601, out string message)` | Gets a file's size and timestamps as scalar outputs. |
| `WaitForFileMatchingPattern` | `bool WaitForFileMatchingPattern(string directoryPath, string searchPattern, bool includeSubdirectories, int timeoutMs, int pollIntervalMs, out string matchedFilePath, out bool timedOut, out string message)` | Polls a directory until it contains at least one file matching a search pattern, or the timeout elapses. |
| `WaitForFileMatchingPatternSimple` | `bool WaitForFileMatchingPatternSimple(string directoryPath, string searchPattern, bool includeSubdirectories, int timeoutMs, int pollIntervalMs, out string matchedFilePath, out string message)` | Same as `WaitForFileMatchingPattern`, without the `timedOut` output. |
| `WaitForFileStable` | `bool WaitForFileStable(string path, int stableDurationMs, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until a file's size and last-write time are unchanged for a continuous interval, or the timeout elapses; this component's flagship method. |
| `WaitForFileStableSimple` | `bool WaitForFileStableSimple(string path, int stableDurationMs, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForFileStable`, without the `timedOut` output. |
| `WaitForFileToBeDeleted` | `bool WaitForFileToBeDeleted(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until the file at the given path no longer exists, or the timeout elapses. |
| `WaitForFileToBeDeletedSimple` | `bool WaitForFileToBeDeletedSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForFileToBeDeleted`, without the `timedOut` output. |
| `WaitForFileToChange` | `bool WaitForFileToChange(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Snapshots a file's size and last-write time, then polls until either differs (or the file is deleted), or the timeout elapses. |
| `WaitForFileToChangeSimple` | `bool WaitForFileToChangeSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForFileToChange`, without the `timedOut` output. |
| `WaitForFileToExist` | `bool WaitForFileToExist(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until a file appears at the given path, or the timeout elapses. |
| `WaitForFileToExistSimple` | `bool WaitForFileToExistSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForFileToExist`, without the `timedOut` output. |
| `WaitForFileUnlocked` | `bool WaitForFileUnlocked(string path, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls `IsFileLocked` until it reports the file unlocked, or the timeout elapses. |
| `WaitForFileUnlockedSimple` | `bool WaitForFileUnlockedSimple(string path, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForFileUnlocked`, without the `timedOut` output. |
| `WatchForChange` | `bool WatchForChange(string directoryPath, string filter, string changeKindsFilter, bool includeSubdirectories, int timeoutMs, out string changedPath, out FileChangeKind detectedKind, out bool timedOut, out string message)` | Blocks until a filesystem event matching the given change kinds occurs in a directory, or the timeout elapses, via the OS's native change-notification API. |
| `WatchForChangeSimple` | `bool WatchForChangeSimple(string directoryPath, string filter, string changeKindsFilter, bool includeSubdirectories, int timeoutMs, out string changedPath, out FileChangeKind detectedKind, out string message)` | Same as `WatchForChange`, without the `timedOut` output. |

### Events

| Event | Type | Description |
|---|---|---|
| `Changed` | `EventHandler<FileWatchChangeEventArgs>` | Raised on a content/attribute/timestamp change, while a background watch (`StartWatching`) is running. |
| `Created` | `EventHandler<FileWatchChangeEventArgs>` | Raised when a file or directory is created, while a background watch (`StartWatching`) is running. |
| `Deleted` | `EventHandler<FileWatchChangeEventArgs>` | Raised when a file or directory is deleted, while a background watch (`StartWatching`) is running. |
| `Renamed` | `EventHandler<FileWatchRenamedEventArgs>` | Raised on a rename, while a background watch (`StartWatching`) is running. |
| `WatchError` | `EventHandler<FileWatchErrorEventArgs>` | Raised if the underlying watcher itself fails (e.g. an internal notification-buffer overflow); never raised for a subscriber's own handler exception. |

## JsonUtils

Reads, updates, validates, and transforms JSON via real JSONPath, as a full replacement for the native `Json` component's dot-notation-only path support.

**Namespace:** `JsonAutomation` | **Assembly:** `JsonAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `IsValidJson` | `bool IsValidJson(string json, out string message)` | Checks whether a string is well-formed JSON. |
| `TryAppendToJsonArray` | `bool TryAppendToJsonArray(string json, string path, string valueJson, out string updatedJson, out string message)` | Appends a JSON-fragment element to an array at a JSONPath. |
| `TryConvertJsonToXml` | `bool TryConvertJsonToXml(string json, string rootElementName, out string xml, out string message)` | Converts JSON text to XML text. |
| `TryConvertXmlToJson` | `bool TryConvertXmlToJson(string xml, out string json, out string message)` | Converts XML text to JSON text; rejects any XML containing a DOCTYPE declaration (DTD). |
| `TryDeserializeObject` | `bool TryDeserializeObject(string json, string typeName, out object result, out string message)` | Deserializes a JSON string into an instance of the given .NET type, resolved by name via `Type.GetType`. |
| `TryDiffJson` | `bool TryDiffJson(string json1, string json2, string delimiter, out bool areEqual, out string differingPaths, out string message)` | Compares two JSON documents and reports the paths where they differ. |
| `TryFilterJsonArrayByField` | `bool TryFilterJsonArrayByField(string json, string path, string fieldName, JsonComparisonOperator comparisonOperator, string value, out string filteredJson, out string message)` | Filters an array at a JSONPath to elements whose field matches a comparison. |
| `TryFindPathsByName` | `bool TryFindPathsByName(string json, string name, string delimiter, out string paths, out string message)` | Finds every path in the document where a property with the given name exists, at any depth, and joins them into one delimited string. |
| `TryGetArrayLength` | `bool TryGetArrayLength(string json, string path, out int length, out string message)` | Reports the element count of an array at a JSONPath. |
| `TryGetBoolValue` | `bool TryGetBoolValue(string json, string path, out bool value, out string message)` | Extracts a value at a JSONPath as a bool. |
| `TryGetDateTimeValue` | `bool TryGetDateTimeValue(string json, string path, out DateTime value, out string message)` | Extracts a value at a JSONPath as a DateTime. |
| `TryGetDoubleValue` | `bool TryGetDoubleValue(string json, string path, out double value, out string message)` | Extracts a value at a JSONPath as a double. |
| `TryGetIntValue` | `bool TryGetIntValue(string json, string path, out int value, out string message)` | Extracts a value at a JSONPath as an int. |
| `TryGetStringValue` | `bool TryGetStringValue(string json, string path, out string value, out string message)` | Extracts a value at a JSONPath as a string. |
| `TryGetValueFromJson` | `bool TryGetValueFromJson(string json, string path, out string value, out string message)` | Extracts a single value from a JSON string using a JSONPath expression. |
| `TryGetValueType` | `bool TryGetValueType(string json, string path, out JsonValueKind kind, out string message)` | Reports the kind of value found at a JSONPath. |
| `TryGetValuesFromJson` | `bool TryGetValuesFromJson(string json, string path, string delimiter, out string delimitedValues, out string message)` | Extracts every value matching a JSONPath (e.g. a wildcard or filter expression) and joins them into one delimited string. |
| `TryMergeJson` | `bool TryMergeJson(string baseJson, string overrideJson, out string mergedJson, out string message)` | Merges two JSON objects; values in the override document win on scalar conflicts, arrays are concatenated, and nested objects merge recursively. |
| `TryMinifyJson` | `bool TryMinifyJson(string json, out string minifiedJson, out string message)` | Reformats JSON text with all insignificant whitespace removed. |
| `TryPrettyPrintJson` | `bool TryPrettyPrintJson(string json, out string formattedJson, out string message)` | Reformats JSON text with indentation. |
| `TryRemoveValueFromJson` | `bool TryRemoveValueFromJson(string json, string path, out string updatedJson, out string message)` | Removes a value at a JSONPath. |
| `TrySerializeObject` | `bool TrySerializeObject(object value, out string json, out string message)` | Serializes an object to a JSON string. |
| `TrySetValueInJson` | `bool TrySetValueInJson(string json, string path, string value, out string updatedJson, out string message)` | Updates a value in a JSON string using a JSONPath expression; the path must already resolve to an existing value. |
| `TrySortJsonArrayByField` | `bool TrySortJsonArrayByField(string json, string path, string fieldName, bool ascending, out string sortedJson, out string message)` | Sorts an array at a JSONPath by a field's value. |

## KeyboardUtils

Pega Robot Studio-ready component that injects keyboard input (key presses, combos, text typing) using the Windows `SendInput` API, and reads keyboard/modifier state via `GetAsyncKeyState`.

**Namespace:** `KeyboardAutomation` | **Assembly:** `KeyboardAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `GetActiveModifiers` | `ModifierKeys GetActiveModifiers()` | Returns the combination of Ctrl/Shift/Alt/Win currently held, as flags. |
| `HoldKey` | `bool HoldKey(VirtualKey key, int holdMilliseconds, out string message)` | Holds a key down for the given duration, then releases it. Never throws. |
| `IsKeyDown` | `bool IsKeyDown(VirtualKey key)` | Returns `true` while the given key is currently held down. |
| `IsModifierDown` | `bool IsModifierDown(ModifierKeys modifier)` | Returns `true` if every modifier flag set in the argument is currently held down; `false` for `ModifierKeys.None`. |
| `KeyDown` | `bool KeyDown(VirtualKey key, out string message)` | Presses and holds a key down. Pair with `KeyUp`. Never throws. |
| `KeyUp` | `bool KeyUp(VirtualKey key, out string message)` | Releases a key previously pressed with `KeyDown`. Never throws. |
| `PasteText` | `bool PasteText(string text, out string message, int postPasteDelayMilliseconds = 50)` | Saves the clipboard, sets it to `text`, sends Ctrl+V, then restores the original clipboard. Destroys non-text clipboard content permanently. Never throws. |
| `PressKey` | `bool PressKey(VirtualKey key, out string message)` | Presses and releases a key (~20 ms between down and up). Never throws. |
| `PressKeyCombo` | `bool PressKeyCombo(out string message, params VirtualKey[] keys)` | Presses all given keys down in order, then releases them in reverse order, as one atomic batch. Never throws. |
| `PressKeyWithModifiers` | `bool PressKeyWithModifiers(VirtualKey key, ModifierKeys modifiers, out string message)` | Presses a key while holding modifier keys (Control/Shift/Alt/Win), injected as one atomic batch. Never throws. |
| `TypeText` | `bool TypeText(string text, out string message)` | Types a string via `KEYEVENTF_UNICODE` (default ~10 ms/character). Never throws. |
| `TypeText` | `bool TypeText(string text, int delayMilliseconds, out string message)` | Types a string via `KEYEVENTF_UNICODE` with a custom per-character delay; Unicode-safe (surrogate pairs). Never throws. |

## LocalQueueUtils

Provides a persistent, machine-local work queue for variable-count RPA sub-work without requiring a Pega collection proxy.

**Namespace:** `LocalQueueAutomation` | **Assembly:** `LocalQueueAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `AddFileReferences` | `bool AddFileReferences(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Adds references to matching local files without copying or moving them. Never throws. |
| `AddJson` | `bool AddJson(string queuePath, string payloadJson, out string itemId, out bool duplicate, out string message, string businessKey = null, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)` | Adds one JSON value to the queue; an active duplicate business key is reported without adding another item. Never throws. |
| `AddJsonArray` | `bool AddJsonArray(string queuePath, string jsonArray, out int addedCount, out string message, int priority = 50, int delaySeconds = 0, int maximumAttempts = 3)` | Adds each element of a JSON array as a separate work item. Never throws. |
| `AddLines` | `bool AddLines(string queuePath, string text, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Adds every non-empty text line as a JSON-string work item. Never throws. |
| `CompleteItem` | `bool CompleteItem(string queuePath, string itemId, string leaseToken, out string message, string resultJson = null)` | Marks an in-progress item completed and optionally records result JSON. Never throws. |
| `CreateQueue` | `bool CreateQueue(string queueName, QueueLifetime lifetime, string runId, out string queuePath, out bool alreadyExisted, out string message)` | Creates or opens a run-scoped or persistent local queue and acquires its exclusive process lock. Never throws. |
| `DeleteCompletedItems` | `bool DeleteCompletedItems(string queuePath, int olderThanDays, out int removedCount, out string message)` | Deletes completed item metadata (and any imported files) older than a specified age. Never throws. |
| `DeleteQueue` | `bool DeleteQueue(string queuePath, bool confirmDeleteNonEmpty, out bool deleted, out int deletedItemCount, out string message)` | Deletes an entire queue, requiring explicit confirmation when it contains work. Never throws. |
| `DeleteQueueIfEmpty` | `bool DeleteQueueIfEmpty(string queuePath, out bool deleted, out string message)` | Deletes a queue only when it contains no item metadata or imported files. Never throws. |
| `DeleteRejectedItems` | `bool DeleteRejectedItems(string queuePath, int olderThanDays, out int removedCount, out string message)` | Deletes rejected item metadata and queue-owned files older than a specified age. Never throws. |
| `GetCounts` | `bool GetCounts(string queuePath, out int ready, out int delayed, out int inProgress, out int completed, out int rejected, out int corrupt, out string message)` | Returns scalar counts for every item state. Never throws. |
| `GetItem` | `bool GetItem(string queuePath, string itemId, out bool found, out string state, out string itemJson, out string message)` | Returns one item's state and JSON metadata; not found is a normal result. Never throws. |
| `ImportFiles` | `bool ImportFiles(string queuePath, string sourceDirectory, string searchPattern, bool recursive, out int addedCount, out string message, int priority = 50, int maximumAttempts = 3)` | Copies matching files into queue storage and adds them as work. Never throws. |
| `OpenQueue` | `bool OpenQueue(string queuePath, out string message)` | Opens an existing local queue by path and acquires its exclusive process lock. Never throws. |
| `RecoverExpiredLeases` | `bool RecoverExpiredLeases(string queuePath, out int recoveredCount, out int rejectedCount, out string message)` | Recovers all expired in-progress leases, consuming an attempt and rejecting exhausted items. Never throws. |
| `RejectItem` | `bool RejectItem(string queuePath, string itemId, string leaseToken, string reason, out string message)` | Rejects an in-progress item immediately. Never throws. |
| `RenewLease` | `bool RenewLease(string queuePath, string itemId, string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)` | Renews an in-progress item's lease. Never throws. |
| `RetryItem` | `bool RetryItem(string queuePath, string itemId, string leaseToken, string errorMessage, out bool willRetry, out bool rejected, out string message, int delaySeconds = 0)` | Records failure and retries or rejects an in-progress item after its maximum attempts. Never throws. |
| `RetryRejectedItem` | `bool RetryRejectedItem(string queuePath, string itemId, bool resetAttemptCount, out string message, int delaySeconds = 0)` | Returns a rejected item to ready or delayed state. Never throws. |
| `TryTakeNext` | `bool TryTakeNext(string queuePath, out bool itemAvailable, out string itemId, out string payloadJson, out string storedFilePath, out int attempt, out string leaseToken, out string leaseExpiresUtc, out string message, int leaseSeconds = 300)` | Takes and leases the next available item; no item available is a normal successful result. Never throws. |
| `ValidateQueue` | `bool ValidateQueue(string queuePath, out string reportJson, out string message)` | Validates a queue and returns a JSON report. Never throws. |

## MouseUtils

Pega Robot Studio-ready component that moves, clicks, drags, and scrolls the mouse using the Windows `SendInput`/`SetCursorPos` APIs, and that can change the cursor's appearance, visibility, and confinement rectangle.

**Namespace:** `MouseAutomation` | **Assembly:** `MouseAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `BezierClickAt` | `bool BezierClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)` | Moves along a randomized Bezier curve to the target, then clicks - the human-like counterpart to `ClickAt`. Never throws. |
| `BezierDoubleClickAt` | `bool BezierDoubleClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)` | Moves along a randomized Bezier curve to the target, then double-clicks. Never throws. |
| `BezierDragAndDrop` | `bool BezierDragAndDrop(int startX, int startY, int endX, int endY, out string message, int durationMs = 500)` | Performs a left-button drag along a randomized Bezier curve instead of a straight line - the human-like counterpart to `DragAndDrop`. Never throws. |
| `BlockUserInput` | `bool BlockUserInput(out string message)` | Blocks all real keyboard/mouse input system-wide until `UnblockUserInput` (injected input still works). Never throws. |
| `ClampToScreenX` | `int ClampToScreenX(int x)` | Clamps an X coordinate into the virtual screen's horizontal range. |
| `ClampToScreenY` | `int ClampToScreenY(int y)` | Clamps a Y coordinate into the virtual screen's vertical range. |
| `Click` | `bool Click(MouseButton button, out string message)` | Clicks the given button at the current cursor position (a short ~20 ms press/release cycle). Never throws. |
| `ClickAndHold` | `bool ClickAndHold(MouseButton button, int holdMilliseconds, out string message)` | Holds the given button down for the specified time, then releases it. Never throws. |
| `ClickAndRestore` | `bool ClickAndRestore(int x, int y, MouseButton button, out string message)` | Clicks at the given coordinates, then jumps the cursor back to wherever it was. Never throws. |
| `ClickAt` | `bool ClickAt(int x, int y, MouseButton button, out string message)` | Moves the cursor to the coordinates and clicks the given button. Never throws. |
| `ClickAtClientPoint` | `bool ClickAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button, out string message)` | Moves the real cursor to a window-relative client point and clicks there (works where PostMessage-based clicks are ignored, e.g. WPF/Electron/Chromium). Never throws. |
| `ClickAtRelativePosition` | `bool ClickAtRelativePosition(IntPtr hWnd, double xFraction, double yFraction, MouseButton button, out string message)` | Clicks at a fractional position within a window's client area (e.g. 0.5, 0.9), resilient to minor resizes across machines. Never throws. |
| `ClickWindow` | `bool ClickWindow(IntPtr hWnd, MouseButton button, out string message)` | Posts a click to the center of a window handle without moving the cursor or stealing focus. Never throws. |
| `ClickWindowAtClientPoint` | `bool ClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, MouseButton button, out string message)` | Posts a click to a window at client-area coordinates (Spy++ style). Supports all five mouse buttons. Never throws. |
| `ClickWindowAtPoint` | `bool ClickWindowAtPoint(IntPtr hWnd, int screenX, int screenY, MouseButton button, out string message)` | Posts a click to a window at the given screen coordinates (converted to client coordinates). Never throws. |
| `ClickWithModifiers` | `bool ClickWithModifiers(MouseButton button, ModifierKeys modifiers, out string message)` | Clicks the given button while holding modifier keys (Control/Shift/Alt), injected as two atomic batches. Never throws. |
| `ClickWithRetry` | `bool ClickWithRetry(int x, int y, MouseButton button, int maxAttempts, int retryDelayMilliseconds, out string message)` | Clicks at the given coordinates, retrying on transient failure up to `maxAttempts` times. Never throws. |
| `ClientPointToScreen` | `bool ClientPointToScreen(IntPtr hWnd, int clientX, int clientY, out int screenX, out int screenY, out string message)` | Converts a point in a window's client area to screen coordinates. Never throws. |
| `ClipCursor` | `bool ClipCursor(int left, int top, int right, int bottom, out string message)` | Confines the cursor to the given screen rectangle until `ReleaseCursorClip` is called. Never throws. |
| `DoubleClick` | `bool DoubleClick(MouseButton button, out string message)` | Double-clicks the given button at the current cursor position (50 ms gap, within the system double-click time). Never throws. |
| `DoubleClickAt` | `bool DoubleClickAt(int x, int y, MouseButton button, out string message)` | Moves the cursor to the coordinates and double-clicks the given button. Never throws. |
| `DoubleClickWindowAtClientPoint` | `bool DoubleClickWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY, out string message)` | Posts a double-click message sequence (DOWN/UP/DBLCLK/UP) to a window at client coordinates. Never throws. |
| `DragAndDrop` | `bool DragAndDrop(int startX, int startY, int endX, int endY, out string message)` | Performs a left-button drag from start to end (30 steps, 10 ms per step). Never throws. |
| `DragAndDrop` | `bool DragAndDrop(int startX, int startY, int endX, int endY, int steps, int stepDelayMilliseconds, out string message)` | Performs a left-button drag from start to end, moving smoothly in the given number of steps. Never throws. |
| `DragAndHold` | `bool DragAndHold(int startX, int startY, int endX, int endY, int holdMilliseconds, out string message)` | Drags from start to end, then holds the button down at the destination before releasing (for hover-to-expand drop targets). Never throws. |
| `FlashCursorHighlight` | `bool FlashCursorHighlight(out string message, int radius = 30, int flashes = 3, int flashMs = 200, int ringWidth = 3, int colorRef = 0x0000FF)` | Flashes an inverting ring around the cursor for demos/recordings; erases itself exactly via XOR drawing. Never throws. |
| `GetCursorClip` | `bool GetCursorClip(out int left, out int top, out int width, out int height, out string message)` | Gets the rectangle the cursor is currently confined to, as scalar outputs. Never throws. |
| `GetCursorClipAsRectangle` | `bool GetCursorClipAsRectangle(out Rectangle clip, out string message)` | Gets the rectangle the cursor is currently confined to (full virtual screen when unclipped). Never throws. |
| `GetDoubleClickTimeMs` | `int GetDoubleClickTimeMs()` | Gets the system double-click time in milliseconds. |
| `GetPhysicalCursorX` | `bool GetPhysicalCursorX(out int x, out string message)` | Gets the cursor X in physical pixels, unaffected by DPI scaling. Never throws. |
| `GetPhysicalCursorY` | `bool GetPhysicalCursorY(out int y, out string message)` | Gets the cursor Y in physical pixels, unaffected by DPI scaling. Never throws. |
| `GetPixelColor` | `bool GetPixelColor(int x, int y, out int color, out string message)` | Reads the color of the screen pixel at the given coordinates, as a 0x00BBGGRR COLORREF value. Never throws. |
| `GetPosition` | `bool GetPosition(out int x, out int y, out string message)` | Gets the current cursor position as scalar X/Y outputs from one atomic native read. Never throws. |
| `GetPositionAsPoint` | `bool GetPositionAsPoint(out Point position, out string message)` | Gets the current cursor position as a `System.Drawing.Point`. Never throws. |
| `GetScreenHeight` | `int GetScreenHeight()` | Gets the height of the primary screen in pixels. |
| `GetScreenWidth` | `int GetScreenWidth()` | Gets the width of the primary screen in pixels. |
| `GetVirtualScreenBounds` | `void GetVirtualScreenBounds(out int left, out int top, out int width, out int height)` | Gets the bounding rectangle of the whole virtual screen (all monitors combined). |
| `GetWindowAtPoint` | `IntPtr GetWindowAtPoint(int x, int y)` | Gets the handle of the (topmost, visible) window at the given screen point (`WindowFromPoint`). |
| `GetWindowBounds` | `bool GetWindowBounds(IntPtr hWnd, out int left, out int top, out int width, out int height, out string message)` | Gets the screen-space bounding rectangle of a window, as scalar outputs. Never throws. |
| `GetWindowBoundsAsRectangle` | `bool GetWindowBoundsAsRectangle(IntPtr hWnd, out Rectangle bounds, out string message)` | Gets the screen-space bounding rectangle of a window. Never throws. |
| `GetX` | `bool GetX(out int x, out string message)` | Gets the current X coordinate of the cursor. Never throws. |
| `GetY` | `bool GetY(out int y, out string message)` | Gets the current Y coordinate of the cursor. Never throws. |
| `HideCursor` | `void HideCursor()` | Hides the cursor (until a matching `ShowCursor` call through this component). |
| `IsBusyCursorActive` | `bool IsBusyCursorActive(out string message)` | Returns `true` if the current system cursor is the Wait or AppStarting busy indicator. Never throws. |
| `IsCursorVisible` | `bool IsCursorVisible()` | Returns `false` if the cursor was hidden via this component's `HideCursor` method. |
| `IsLeftButtonDown` | `bool IsLeftButtonDown()` | Returns `true` while the left mouse button is currently held down. |
| `IsMiddleButtonDown` | `bool IsMiddleButtonDown()` | Returns `true` while the middle mouse button is currently held down. |
| `IsPointOnScreen` | `bool IsPointOnScreen(int x, int y)` | Returns `true` if the coordinates lie inside the virtual screen bounds. |
| `IsProcessDpiAware` | `bool IsProcessDpiAware()` | Returns `true` if the process is DPI-aware (any level); `false` if DPI-unaware. |
| `IsRightButtonDown` | `bool IsRightButtonDown()` | Returns `true` while the right mouse button is currently held down. |
| `JiggleMouse` | `bool JiggleMouse(out string message, int pixels = 1)` | Nudges the cursor by a tiny amount and immediately back, leaving its position unchanged but generating real mouse-move input (resets idle/screensaver timers). Never throws. |
| `LeftClick` | `bool LeftClick(out string message)` | Left-clicks at the current cursor position. Never throws. |
| `LeftClickAt` | `bool LeftClickAt(int x, int y, out string message)` | Left-clicks at the given screen coordinates. Never throws. |
| `LeftDoubleClick` | `bool LeftDoubleClick(out string message)` | Double left-clicks at the current cursor position. Never throws. |
| `LeftDoubleClickAt` | `bool LeftDoubleClickAt(int x, int y, out string message)` | Double left-clicks at the given screen coordinates. Never throws. |
| `MiddleClick` | `bool MiddleClick(out string message)` | Middle-clicks at the current cursor position. Never throws. |
| `MiddleClickAt` | `bool MiddleClickAt(int x, int y, out string message)` | Middle-clicks at the given screen coordinates. Never throws. |
| `MiddleDoubleClick` | `bool MiddleDoubleClick(out string message)` | Double middle-clicks at the current cursor position. Never throws. |
| `MiddleDoubleClickAt` | `bool MiddleDoubleClickAt(int x, int y, out string message)` | Double middle-clicks at the given screen coordinates. Never throws. |
| `MouseDown` | `bool MouseDown(MouseButton button, out string message)` | Presses and holds the given mouse button. Pair with `MouseUp`; the one method here intentionally stateful across separate calls. Never throws. |
| `MouseUp` | `bool MouseUp(MouseButton button, out string message)` | Releases the given mouse button. Never throws. |
| `MoveBy` | `bool MoveBy(int deltaX, int deltaY, out string message)` | Moves the cursor by the given offsets relative to its current position. Never throws. |
| `MoveMouseBezier` | `bool MoveMouseBezier(int x, int y, out string message, int durationMs = 500)` | Moves the cursor to the target along a randomized Bezier curve with ease-in-out timing, simulating human-like cursor movement. Never throws. |
| `MoveTo` | `bool MoveTo(int x, int y, out string message)` | Instantly moves the cursor to the given screen coordinates. Never throws. |
| `ReleaseCursorClip` | `bool ReleaseCursorClip(out string message)` | Removes any cursor confinement set by `ClipCursor`. Never throws. |
| `ReplaceSystemCursor` | `bool ReplaceSystemCursor(SystemCursorType slotToReplace, SystemCursorType newCursor, out string message)` | Replaces any system cursor slot with another standard system cursor. Never throws. |
| `ResetSystemCursors` | `bool ResetSystemCursors(out string message)` | Restores all system cursors to the user's configured Windows defaults. Never throws. |
| `RightClick` | `bool RightClick(out string message)` | Right-clicks at the current cursor position. Never throws. |
| `RightClickAt` | `bool RightClickAt(int x, int y, out string message)` | Right-clicks at the given screen coordinates. Never throws. |
| `RightDoubleClick` | `bool RightDoubleClick(out string message)` | Double right-clicks at the current cursor position. Never throws. |
| `RightDoubleClickAt` | `bool RightDoubleClickAt(int x, int y, out string message)` | Double right-clicks at the given screen coordinates. Never throws. |
| `RubberBandSelect` | `bool RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, out string message)` | Performs a left-button rubber-band drag while holding modifier keys (e.g. Ctrl-drag to add to a selection). Never throws. |
| `RubberBandSelect` | `bool RubberBandSelect(int startX, int startY, int endX, int endY, ModifierKeys modifiers, int steps, int stepDelayMilliseconds, out string message)` | Performs a left-button rubber-band drag while holding modifier keys, moving smoothly in the given number of steps. Never throws. |
| `SafeClickAt` | `bool SafeClickAt(int x, int y, MouseButton button, IntPtr expectedWindowHandle, out string message)` | Clicks only if the window under the point matches the expected window (or a descendant) - guards against misclicks from a shifted layout. Never throws. |
| `ScreenPointToClient` | `bool ScreenPointToClient(IntPtr hWnd, int screenX, int screenY, out int clientX, out int clientY, out string message)` | Converts a screen coordinate to a point relative to a window's client area. Never throws. |
| `Scroll` | `bool Scroll(int wheelDelta, out string message)` | Scrolls vertically at the current cursor position. Positive scrolls up, negative scrolls down; 120 = one notch. Never throws. |
| `ScrollAt` | `bool ScrollAt(int x, int y, int wheelDelta, out string message)` | Moves the cursor to the coordinates, scrolls vertically there, then restores the cursor's original position. Never throws. |
| `ScrollDown` | `bool ScrollDown(out string message)` | Scrolls down one wheel notch. Never throws. |
| `ScrollDown` | `bool ScrollDown(int notches, out string message)` | Scrolls down the given number of wheel notches. Never throws. |
| `ScrollHorizontal` | `bool ScrollHorizontal(int wheelDelta, out string message)` | Scrolls horizontally at the current cursor position. Positive scrolls right, negative scrolls left. Never throws. |
| `ScrollHorizontalAt` | `bool ScrollHorizontalAt(int x, int y, int wheelDelta, out string message)` | Moves the cursor to the coordinates, scrolls horizontally there, then restores the cursor's original position. Never throws. |
| `ScrollLeft` | `bool ScrollLeft(out string message)` | Scrolls left one wheel notch. Never throws. |
| `ScrollLeft` | `bool ScrollLeft(int notches, out string message)` | Scrolls left the given number of wheel notches. Never throws. |
| `ScrollRight` | `bool ScrollRight(out string message)` | Scrolls right one wheel notch. Never throws. |
| `ScrollRight` | `bool ScrollRight(int notches, out string message)` | Scrolls right the given number of wheel notches. Never throws. |
| `ScrollUp` | `bool ScrollUp(out string message)` | Scrolls up one wheel notch. Never throws. |
| `ScrollUp` | `bool ScrollUp(int notches, out string message)` | Scrolls up the given number of wheel notches. Never throws. |
| `SetCursor` | `bool SetCursor(SystemCursorType cursor, out string message)` | Changes the standard arrow cursor to the given system cursor. Equivalent to `ReplaceSystemCursor(Arrow, cursor)`. Never throws. |
| `SetCursorFromFile` | `bool SetCursorFromFile(SystemCursorType slotToReplace, string filePath, out string message)` | Loads a custom cursor from a .cur/.ani file and installs it into the given system cursor slot. Never throws. |
| `SetDoubleClickTimeMs` | `bool SetDoubleClickTimeMs(int milliseconds, out string message)` | Sets the system double-click time in milliseconds (0 restores the 500 ms default; max 5000). Never throws. |
| `ShowCursor` | `void ShowCursor()` | Shows the cursor again after a `HideCursor` call made through this component. |
| `SmoothMoveTo` | `bool SmoothMoveTo(int x, int y, out string message)` | Smoothly moves the cursor to the target position (25 steps, 5 ms per step) to simulate human movement. Never throws. |
| `SmoothMoveTo` | `bool SmoothMoveTo(int x, int y, int steps, int delayMilliseconds, out string message)` | Smoothly moves the cursor to the target position using the given number of steps and delay between steps. Never throws. |
| `TripleClick` | `bool TripleClick(MouseButton button, out string message)` | Triple-clicks the given button at the current cursor position (select-line/paragraph gesture). Never throws. |
| `UnblockUserInput` | `void UnblockUserInput()` | Re-enables real keyboard/mouse input after `BlockUserInput`. Safe to call even when nothing is blocked. |
| `WaitForIdleCursor` | `bool WaitForIdleCursor(int timeoutMs, int pollIntervalMs, out string message)` | Waits until the busy cursor (Wait/AppStarting) clears, or the timeout elapses. Never throws. |
| `WaitForPixelChange` | `bool WaitForPixelChange(int x, int y, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen pixel until its color changes from its value at call time, or the timeout elapses. Never throws. |
| `WaitForPixelColor` | `bool WaitForPixelColor(int x, int y, int expectedColorRef, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen pixel until it matches the expected COLORREF or the timeout elapses. Never throws. |

## OcrUtils

Pega Robot Studio-ready component that recognizes text from a screen region or an image file using `Windows.Media.Ocr`, with plain-text and structured (positioned) result shapes.

**Namespace:** `OcrAutomation` | **Assembly:** `OcrAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `FindTextLocation` | `bool FindTextLocation(string searchText, int left, int top, int width, int height, out int foundLeft, out int foundTop, out int foundWidth, out int foundHeight, out string message)` | Same as `FindTextLocationAsRectangle`, but reports the matched bounding rectangle as scalar left/top/width/height outputs for designers without a `Rectangle` proxy. |
| `FindTextLocationAsRectangle` | `bool FindTextLocationAsRectangle(string searchText, int left, int top, int width, int height, out Rectangle location, out string message)` | Searches a screen region for text matching a case-insensitive substring and returns its bounding rectangle in screen coordinates. Returns True if found; never throws. |
| `GetAvailableLanguages` | `List<string> GetAvailableLanguages()` | Gets the BCP-47 language tags of every OCR language pack currently installed. Never throws; returns an empty list if the language list cannot be queried. |
| `GetAvailableLanguagesDelimited` | `bool GetAvailableLanguagesDelimited(out string tags, out string message, string delimiter = ",")` | Gets the installed OCR language tags as a single delimited string (default comma-separated), for designers without a `List<string>` proxy. Never throws. |
| `GetStructuredTextFromRegion` | `bool GetStructuredTextFromRegion(int left, int top, int width, int height, out OcrResult result, out string message, string languageTag = null)` | Captures a screen region and returns its recognized text as lines/words with screen-space bounding rectangles. Returns True on success; never throws. |
| `GetStructuredTextFromRegionAsJson` | `bool GetStructuredTextFromRegionAsJson(int left, int top, int width, int height, out string json, out string message, string languageTag = null)` | Same as `GetStructuredTextFromRegion`, serialized to a JSON string, for designers that cannot construct an `OcrResult` proxy. |
| `GetTextFromImageFile` | `bool GetTextFromImageFile(string filePath, out string text, out string message, string languageTag = null)` | Loads an image file and returns its recognized text. Returns True on success; never throws. |
| `GetTextFromRegion` | `bool GetTextFromRegion(int left, int top, int width, int height, out string text, out string message, string languageTag = null)` | Captures a screen region and returns its recognized text. Returns True on success; never throws. |
| `TryGetAvailableLanguages` | `bool TryGetAvailableLanguages(out List<string> tags, out string message)` | Gets the BCP-47 language tags of every OCR language pack currently installed, reporting failures via `message` instead of an empty list. Never throws. |
| `WaitForTextToAppear` | `bool WaitForTextToAppear(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Same as `WaitForTextToAppearSimple`, but also reports whether the wait ended because the timeout elapsed via `timedOut`. Never throws. |
| `WaitForTextToAppearSimple` | `bool WaitForTextToAppearSimple(int left, int top, int width, int height, string expectedText, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen region until it contains text matching `expectedText` (case-insensitive substring), or the timeout elapses. Never throws. |

## ScreenCaptureUtils

Pega Robot Studio-ready component that captures the screen, a region, or a window to a file/clipboard, compares captures against a baseline for visual verification, and annotates or redacts saved screenshots for audit evidence.

**Namespace:** `ScreenCaptureAutomation` | **Assembly:** `ScreenCaptureAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `CaptureActiveWindow` | `bool CaptureActiveWindow(out Bitmap image, out string message)` | Captures the current foreground window to an in-memory image. Caller must `Dispose()` the returned image. Never throws. |
| `CaptureActiveWindowToClipboard` | `bool CaptureActiveWindowToClipboard(out string message)` | Captures the current foreground window and copies it to the clipboard as an image. Never throws. |
| `CaptureActiveWindowToFile` | `bool CaptureActiveWindowToFile(string filePath, out string message)` | Captures the current foreground window to an image file. Never throws. |
| `CaptureAllScreens` | `bool CaptureAllScreens(out Bitmap image, out string message)` | Captures the entire virtual screen (all monitors) to an in-memory image. Caller must `Dispose()` the returned image. Never throws. |
| `CaptureAllScreensToClipboard` | `bool CaptureAllScreensToClipboard(out string message)` | Captures the entire virtual screen and copies it to the clipboard as an image, via an internal STA thread regardless of the caller's apartment state. Never throws. |
| `CaptureAllScreensToFile` | `bool CaptureAllScreensToFile(string filePath, out string message)` | Captures the entire virtual screen (all monitors) to an image file. Never throws. |
| `CaptureAroundPoint` | `bool CaptureAroundPoint(int x, int y, int width, int height, out Bitmap image, out string message)` | Captures a square region centered on the given point to an in-memory image. Caller must `Dispose()` the returned image. Never throws. |
| `CaptureAroundPointToClipboard` | `bool CaptureAroundPointToClipboard(int x, int y, int width, int height, out string message)` | Captures a square region centered on the given point and copies it to the clipboard as an image. Never throws. |
| `CaptureAroundPointToFile` | `bool CaptureAroundPointToFile(int x, int y, int width, int height, string filePath, out string message)` | Captures a square region centered on the given point to an image file. Never throws. |
| `CaptureRegion` | `bool CaptureRegion(int left, int top, int width, int height, out Bitmap image, out string message)` | Captures a specific screen region to an in-memory image. Caller must `Dispose()` the returned image. Never throws. |
| `CaptureRegionToClipboard` | `bool CaptureRegionToClipboard(int left, int top, int width, int height, out string message)` | Captures a specific screen region and copies it to the clipboard as an image. Never throws. |
| `CaptureRegionToFile` | `bool CaptureRegionToFile(int left, int top, int width, int height, string filePath, out string message)` | Captures a specific screen region to an image file. Never throws. |
| `CaptureScreen` | `bool CaptureScreen(int screenIndex, out Bitmap image, out string message)` | Captures a single screen (monitor) by index to an in-memory image, for a multi-monitor session. Caller must `Dispose()` the returned image. Never throws. |
| `CaptureScreenToClipboard` | `bool CaptureScreenToClipboard(int screenIndex, out string message)` | Captures a single screen (monitor) by index and copies it to the clipboard as an image. Never throws. |
| `CaptureScreenToFile` | `bool CaptureScreenToFile(int screenIndex, string filePath, out string message)` | Captures a single screen (monitor) by index to an image file. Never throws. |
| `CaptureStepEvidence` | `bool CaptureStepEvidence(string stepName, string folderPath, out string fullPath, out string message)` | Captures the full screen to a sequentially- and time-stamped evidence file (`{counter}_{stepName}_{timestamp}.png`). Never throws. |
| `CaptureWindow` | `bool CaptureWindow(IntPtr hWnd, out Bitmap image, out string message)` | Captures a window to an in-memory image using `PrintWindow`, which can succeed even when the window is covered by other windows. Caller must `Dispose()` the returned image. Never throws. |
| `CaptureWindowToClipboard` | `bool CaptureWindowToClipboard(IntPtr hWnd, out string message)` | Captures a window and copies it to the clipboard as an image, using `PrintWindow`. Never throws. |
| `CaptureWindowToFile` | `bool CaptureWindowToFile(IntPtr hWnd, string filePath, out string message)` | Captures a window to an image file using `PrintWindow`. Never throws. |
| `CompareRegionToBaseline` | `bool CompareRegionToBaseline(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent, out bool comparisonCompleted, out string message)` | Same as `CompareRegionToBaselineSimple`, but also reports whether the comparison actually ran to completion via `comparisonCompleted`. Never throws. |
| `CompareRegionToBaselineSimple` | `bool CompareRegionToBaselineSimple(int left, int top, int width, int height, string baselineImagePath, double tolerancePercent, out double actualDifferencePercent, out string message)` | Compares a screen region against a previously-saved baseline image, pixel-by-pixel, and reports whether the difference is within tolerance. Never throws. |
| `DrawArrowToPoint` | `bool DrawArrowToPoint(string imagePath, int x, int y, int colorRef, out string message, int length = 40, int lineWidth = 3)` | Draws an arrow pointing at the given coordinates onto a saved screenshot and overwrites it in place. Never throws. |
| `DrawArrowToPoint` | `bool DrawArrowToPoint(string imagePath, int x, int y, Color color, out string message, int length = 40, int lineWidth = 3)` | Same, taking the color as a `System.Drawing.Color` instead of a packed colorRef. |
| `DrawArrowToPoint` | `bool DrawArrowToPoint(string imagePath, int x, int y, int red, int green, int blue, out string message, int length = 40, int lineWidth = 3)` | Same, taking the color as RGB components (0-255 each, clamped) instead of a packed colorRef. |
| `DrawHighlightBox` | `bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int colorRef, out string message, int lineWidth = 3)` | Draws a rectangular highlight box onto a saved screenshot and overwrites it in place. Never throws. |
| `DrawHighlightBox` | `bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, Color color, out string message, int lineWidth = 3)` | Same, taking the color as a `System.Drawing.Color` instead of a packed colorRef. |
| `DrawHighlightBox` | `bool DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, int red, int green, int blue, out string message, int lineWidth = 3)` | Same, taking the color as RGB components (0-255 each, clamped) instead of a packed colorRef. |
| `GetRegionHash` | `bool GetRegionHash(int left, int top, int width, int height, out string hash, out string message)` | Computes a lightweight perceptual hash (an 8x8 average hash) of a screen region, for cheap "did this area visibly change" checks. Never throws. |
| `GetScreenCount` | `bool GetScreenCount(out int count, out string message)` | Reports how many screens (monitors) this session sees, so an automation can validate a `screenIndex` before passing it to the indexed capture overloads. Never throws. |
| `RedactRegion` | `bool RedactRegion(string imagePath, int left, int top, int width, int height, out string message, int colorRef = 0x000000)` | Fills a rectangular region of a saved screenshot with a solid color (default black), permanently redacting it in place. Never throws. |
| `RedactRegion` | `bool RedactRegion(string imagePath, int left, int top, int width, int height, Color color, out string message)` | Same, taking the fill color as a `System.Drawing.Color` instead of a packed colorRef. |
| `RedactRegion` | `bool RedactRegion(string imagePath, int left, int top, int width, int height, int red, int green, int blue, out string message)` | Same, taking the fill color as RGB components (0-255 each, clamped) instead of a packed colorRef. |
| `WaitForRegionToChange` | `bool WaitForRegionToChange(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Same as `WaitForRegionToChangeSimple`, but also reports whether the wait ended because the timeout elapsed via `timedOut`. Never throws. |
| `WaitForRegionToChangeSimple` | `bool WaitForRegionToChangeSimple(int left, int top, int width, int height, int timeoutMs, int pollIntervalMs, out string message)` | Polls a screen region until its perceptual hash changes from what it was when this method was called, or the timeout elapses. Never throws. |

## ServiceUtils

Pega Robot Studio-ready component that queries, starts, stops, restarts, pauses/resumes, and configures the startup type of Windows services.

**Namespace:** `ServiceAutomation` | **Assembly:** `ServiceAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `FindServiceNamesByDisplayName` | `List<string> FindServiceNamesByDisplayName(string displayName, bool exactMatch = true)` | Finds the service name(s) of installed services matching a display name (exact or substring match). Returns an empty list if none match or the query fails. Never throws. |
| `FindServiceNamesByDisplayNameDelimited` | `string FindServiceNamesByDisplayNameDelimited(string displayName, bool exactMatch = true, string delimiter = ",")` | Same as `FindServiceNamesByDisplayName`, joined into a single delimited string, for designers without a `List<string>` proxy. |
| `IsRunning` | `bool IsRunning(string serviceName, out bool querySucceeded, out string message)` | Same as `IsRunningSimple`, but separates "the query completed" from "the service is running" via `querySucceeded`. Never throws. |
| `IsRunningSimple` | `bool IsRunningSimple(string serviceName, out string message)` | Returns True if a service with the given name is installed and currently running. Never throws. |
| `IsServiceInstalled` | `bool IsServiceInstalled(string serviceName, out bool querySucceeded, out string message)` | Same as `IsServiceInstalledSimple`, but separates "the query completed" from "the service is installed" via `querySucceeded`. Never throws. |
| `IsServiceInstalledSimple` | `bool IsServiceInstalledSimple(string serviceName, out string message)` | Returns True if a service with the given name is installed. Never throws. |
| `ListServiceNames` | `List<string> ListServiceNames()` | Gets the service names of every installed service. Returns an empty list if the Service Control Manager couldn't be queried. Never throws. |
| `ListServiceNamesDelimited` | `string ListServiceNamesDelimited(string delimiter = ",")` | Same as `ListServiceNames`, joined into a single delimited string, for designers without a `List<string>` proxy. |
| `PauseService` | `bool PauseService(string serviceName, out bool wasAlreadyPaused, out string message)` | Same as `PauseServiceSimple`, but reports the already-paused case via `wasAlreadyPaused` instead of an informational `message`. Never throws. |
| `PauseServiceSimple` | `bool PauseServiceSimple(string serviceName, out string message)` | Pauses a running service. Idempotent: pausing an already-paused service reports success. Never throws. |
| `RestartService` | `bool RestartService(string serviceName, int timeoutMs, out bool wasAlreadyStopped, out string message)` | Same as `RestartServiceSimple`, but reports whether the stop phase found the service already stopped via `wasAlreadyStopped`. Never throws. |
| `RestartServiceSimple` | `bool RestartServiceSimple(string serviceName, int timeoutMs, out string message)` | Stops then starts a service; skips the start phase if the stop phase fails. Never throws. |
| `ResumeService` | `bool ResumeService(string serviceName, out bool wasAlreadyRunning, out string message)` | Same as `ResumeServiceSimple`, but reports the already-running case via `wasAlreadyRunning` instead of an informational `message`. Never throws. |
| `ResumeServiceSimple` | `bool ResumeServiceSimple(string serviceName, out string message)` | Resumes a paused service. Idempotent: resuming an already-running service reports success. Never throws. |
| `SetStartType` | `bool SetStartType(string serviceName, ServiceStartType startType, out string message)` | Sets a service's startup type, including delayed-auto-start, via direct `advapi32.dll` P/Invoke calls. Never throws. |
| `StartService` | `bool StartService(string serviceName, int timeoutMs, out bool wasAlreadyRunning, out string message)` | Same as `StartServiceSimple`, but reports the already-running case via `wasAlreadyRunning` instead of an informational `message`. Never throws. |
| `StartServiceSimple` | `bool StartServiceSimple(string serviceName, int timeoutMs, out string message)` | Starts a service and waits for it to reach Running. Idempotent: starting an already-running service reports success. Never throws. |
| `StopService` | `bool StopService(string serviceName, int timeoutMs, out bool wasAlreadyStopped, out string message)` | Same as `StopServiceSimple`, but reports the already-stopped case via `wasAlreadyStopped` instead of an informational `message`. Never throws. |
| `StopServiceSimple` | `bool StopServiceSimple(string serviceName, int timeoutMs, out string message)` | Stops a service and waits for it to reach Stopped. Idempotent: stopping an already-stopped service reports success. Never throws. |
| `TryFindFirstServiceNameByDisplayName` | `bool TryFindFirstServiceNameByDisplayName(string displayName, out string serviceName, out string message, bool exactMatch = true)` | Finds the single first service name matching a display name, for the common case where exactly one match is expected. Never throws. |
| `TryGetStartType` | `bool TryGetStartType(string serviceName, out ServiceStartType startType, out string message)` | Gets a service's configured startup type. Never throws. |
| `TryGetStatus` | `bool TryGetStatus(string serviceName, out ServiceStatus status, out string message)` | Gets a service's current status as the repository-owned `ServiceStatus` enum, so Pega Robot Studio does not need a reference to `System.ServiceProcess`. Never throws. |
| `TryGetStatusAsServiceControllerStatus` | `bool TryGetStatusAsServiceControllerStatus(string serviceName, out ServiceControllerStatus status, out string message)` | Same as `TryGetStatus`, but reports the status as `ServiceControllerStatus` for .NET callers that already depend on that assembly. Never throws. |
| `WaitForServiceStatus` | `bool WaitForServiceStatus(string serviceName, ServiceControllerStatus expectedStatus, int timeoutMs, out string message)` | Polls for a service to reach the given status until it does, or the timeout elapses. Never throws. |
| `WaitForServiceStatus` | `bool WaitForServiceStatus(string serviceName, ServiceStatus expectedStatus, int timeoutMs, out string message)` | Same, but takes the expected status as the repository-owned `ServiceStatus` enum instead of `ServiceControllerStatus`. Never throws. |

## SessionUtils

Pega Robot Studio-ready component that reports on and acts on Windows session/workstation state on the local machine: session identity and kind (console/RDP/service), session enumeration and connect state, logged-on user, workstation lock state, input-desktop availability, idle time, and deliberate lock/disconnect actions.

**Namespace:** `SessionAutomation` | **Assembly:** `SessionAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `DisconnectCurrentSession` | `bool DisconnectCurrentSession(out string message)` | Disconnects the calling process's own session (not logoff). |
| `DisconnectSession` | `bool DisconnectSession(int sessionId, out string message)` | Disconnects an arbitrary session (not logoff); disconnecting another session typically requires administrator rights. |
| `EnumerateSessionsJson` | `bool EnumerateSessionsJson(string connectStateFilter, out string json, out string message)` | Enumerates every session on the local machine as a JSON array of session summaries, optionally filtered by connect state. |
| `GetActiveConsoleSessionId` | `bool GetActiveConsoleSessionId(out int sessionId, out string message)` | Gets the session ID attached to the machine's physical console. |
| `GetCurrentSessionConnectState` | `bool GetCurrentSessionConnectState(out SessionConnectState state, out string message)` | Gets the connect state of the calling process's own session. |
| `GetCurrentSessionId` | `bool GetCurrentSessionId(out int sessionId, out string message)` | Gets the session ID the calling process is actually running in. |
| `GetCurrentSessionKind` | `bool GetCurrentSessionKind(out SessionKind kind, out string message)` | Gets the calling process's own session kind (Console/RDP/Service). |
| `GetCurrentSessionUptime` | `bool GetCurrentSessionUptime(out long milliseconds, out string message)` | Gets how long the calling process's own session has been logged on, in milliseconds. |
| `GetCurrentSessionUser` | `bool GetCurrentSessionUser(out string userName, out string domainName, out string message)` | Gets the logged-on user/domain for the calling process's own session. |
| `GetIdleTimeMilliseconds` | `bool GetIdleTimeMilliseconds(out long idleMilliseconds, out string message)` | Gets the number of milliseconds since the last keyboard/mouse input to the calling process's own session. |
| `GetSessionConnectState` | `bool GetSessionConnectState(int sessionId, out SessionConnectState state, out string message)` | Gets an arbitrary session's connect state. |
| `GetSessionKind` | `bool GetSessionKind(int sessionId, out SessionKind kind, out string message)` | Gets an arbitrary session's kind (Console/RDP/Service). |
| `GetSessionUser` | `bool GetSessionUser(int sessionId, out string userName, out string domainName, out string message)` | Gets the logged-on user/domain for an arbitrary session. |
| `GetSystemUptime` | `bool GetSystemUptime(out long milliseconds, out string message)` | Gets how long the local machine has been running since it last booted, in milliseconds. |
| `IsCurrentSessionDisconnected` | `bool IsCurrentSessionDisconnected(out string message)` | Returns true if the calling process's own session is disconnected (the literal "RDP disconnect" state). |
| `IsCurrentSessionOnConsole` | `bool IsCurrentSessionOnConsole(out string message)` | Returns true if the calling process's session is the one attached to the physical console. |
| `IsInputDesktopAvailable` | `bool IsInputDesktopAvailable(out bool querySucceeded, out string message)` | Same as `IsInputDesktopAvailableSimple`, plus a `querySucceeded` output. |
| `IsInputDesktopAvailableSimple` | `bool IsInputDesktopAvailableSimple(out string message)` | Returns true if the default interactive desktop is currently receiving input. |
| `IsRunningAsServiceSession` | `bool IsRunningAsServiceSession(out string message)` | Returns true if the calling process is running in Session 0 (an isolated Windows service session with no desktop access). |
| `IsSessionDisconnected` | `bool IsSessionDisconnected(int sessionId, out string message)` | Returns true if an arbitrary session is disconnected (the literal "RDP disconnect" state). |
| `IsSessionInteractive` | `bool IsSessionInteractive(out bool querySucceeded, out string message)` | Same as `IsSessionInteractiveSimple`, plus a `querySucceeded` output. |
| `IsSessionInteractiveSimple` | `bool IsSessionInteractiveSimple(out string message)` | Returns true if the calling process's session is interactive (has a visible window station). |
| `IsWorkstationLocked` | `bool IsWorkstationLocked(out bool querySucceeded, out string message)` | Same as `IsWorkstationLockedSimple`, plus a `querySucceeded` output separating "the query completed" from "the workstation is locked". |
| `IsWorkstationLockedSimple` | `bool IsWorkstationLockedSimple(out string message)` | Returns true if the workstation is locked, via the authoritative session-flags check. |
| `LockWorkstation` | `bool LockWorkstation(out string message)` | Locks the workstation; only works from the calling process's own interactive session and ends its desktop interaction. |
| `WaitForInputDesktopAvailable` | `bool WaitForInputDesktopAvailable(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until the default interactive desktop becomes available, or the timeout elapses. |
| `WaitForInputDesktopAvailableSimple` | `bool WaitForInputDesktopAvailableSimple(int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForInputDesktopAvailable`, without the `timedOut` output. |
| `WaitForSessionConnectState` | `bool WaitForSessionConnectState(int sessionId, SessionConnectState expectedState, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until an arbitrary session reaches the expected connect state, or the timeout elapses. |
| `WaitForSessionConnectStateSimple` | `bool WaitForSessionConnectStateSimple(int sessionId, SessionConnectState expectedState, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForSessionConnectState`, without the `timedOut` output. |
| `WaitForWorkstationUnlocked` | `bool WaitForWorkstationUnlocked(int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls until the workstation is unlocked, or the timeout elapses; only observes an externally-driven unlock, never performs one. |
| `WaitForWorkstationUnlockedSimple` | `bool WaitForWorkstationUnlockedSimple(int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForWorkstationUnlocked`, without the `timedOut` output. |

## StackUtils

Provides an instance-local, in-memory LIFO stack for variable-count RPA work.

**Namespace:** `StackAutomation` | **Assembly:** `StackAutomation`

### Properties

| Property | Type | Description |
|---|---|---|
| `MaximumItems` | `int` | Gets or sets the item capacity used by this component instance (valid range 1 through 1,000,000; default 10,000). |

### Methods

| Method | Signature | Description |
|---|---|---|
| `Clear` | `bool Clear(out int removedCount, out string message)` | Removes every item from the stack. |
| `GetCount` | `bool GetCount(out int count, out string message)` | Returns the current item count. |
| `GetMaximumItems` | `bool GetMaximumItems(out int maximumItems, out string message)` | Returns the configured item capacity. |
| `GetSnapshotJson` | `bool GetSnapshotJson(out string snapshotJson, out string message)` | Returns a JSON snapshot of all items in next-to-pop order. |
| `PushFileReference` | `bool PushFileReference(string filePath, out string absolutePath, out string message)` | Pushes a normalized reference to an existing file. |
| `PushFileReferences` | `bool PushFileReferences(string directoryPath, string searchPattern, bool recursive, out int pushedCount, out string message)` | Pushes sorted references to all matching files atomically. |
| `PushJson` | `bool PushJson(string valueJson, out string message)` | Validates and pushes one raw JSON value. |
| `PushJsonArray` | `bool PushJsonArray(string jsonArray, out int pushedCount, out string message)` | Pushes all elements of a JSON array atomically. |
| `PushLines` | `bool PushLines(string text, out int pushedCount, out string message)` | Pushes all non-empty lines atomically. |
| `PushText` | `bool PushText(string value, out string message)` | Pushes one text item. |
| `SetMaximumItems` | `bool SetMaximumItems(int maximumItems, out string message)` | Sets the maximum number of items accepted by this stack. |
| `TryPeek` | `bool TryPeek(out bool itemAvailable, out StackItemKind itemKind, out string value, out string message)` | Returns the next item without removing it. |
| `TryPop` | `bool TryPop(out bool itemAvailable, out StackItemKind itemKind, out string value, out string message)` | Removes and returns the next item. |

## TerminalUtils

Pega Robot Studio-ready component for terminal-style console applications that expose text poorly through normal UI automation: reading a target process's live console screen buffer, waiting for a prompt or a screen change, injecting keystrokes, and starting/attaching to a console process.

**Namespace:** `TerminalAutomation` | **Assembly:** `TerminalAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `CaptureScreenText` | `bool CaptureScreenText(int processId, bool preserveAnsi, out string text, out string message)` | Captures the console's visible viewport as plain text, one line per row. |
| `GetCursorPosition` | `bool GetCursorPosition(int processId, out int row, out int column, out string message)` | Gets the cursor's row and column within the console's visible viewport. |
| `IsConsoleAttachable` | `bool IsConsoleAttachable(int processId, out bool attachable, out string message)` | Checks whether the calling process can currently attach to the given process's console, via a real attach/detach cycle. |
| `ReadScreenRowsJson` | `bool ReadScreenRowsJson(int processId, out string json, out string message)` | Reads the console's visible viewport as a JSON array of rows, each with its full text and a heuristic field split. |
| `StartConsoleProcess` | `bool StartConsoleProcess(string fileName, string arguments, string workingDirectory, out int processId, out string message)` | Starts a new process with its own real console window, so it can be attached to afterward. |
| `WaitForScreenChange` | `bool WaitForScreenChange(int processId, int timeoutMs, int pollIntervalMs, out bool changed, out bool timedOut, out string message)` | Captures the console's visible screen text as a baseline, then polls until a later capture differs from it, or the timeout elapses. |
| `WaitForScreenChangeSimple` | `bool WaitForScreenChangeSimple(int processId, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForScreenChange`, without the `changed`/`timedOut` outputs. |
| `WaitForScreenText` | `bool WaitForScreenText(int processId, string pattern, bool useRegex, int timeoutMs, int pollIntervalMs, out bool timedOut, out string message)` | Polls the console's visible screen text until it contains `pattern` (or matches it as a regular expression), or the timeout elapses. |
| `WaitForScreenTextSimple` | `bool WaitForScreenTextSimple(int processId, string pattern, bool useRegex, int timeoutMs, int pollIntervalMs, out string message)` | Same as `WaitForScreenText`, without the `timedOut` output. |
| `WriteLine` | `bool WriteLine(int processId, string text, out string message)` | Same as `WriteText`, appending a trailing carriage return so a shell or REPL submits the line. |
| `WriteText` | `bool WriteText(int processId, string text, out string message)` | Injects text as keystrokes directly into the console's input buffer via `WriteConsoleInputW`. |

## UIAutomationUtils

Finds and drives modern (WinUI3/UWP/WPF/browser-hosted) UI via Windows UI Automation (UIA) — the controls that `WindowUtils`/`DialogUtils` can't see, since those operate on native Win32 windows/controls by handle and window class.

**Namespace:** `UIAutomation` | **Assembly:** `UIAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `Collapse` | `bool Collapse(AutomationElement element, out string message)` | Collapses an element via ExpandCollapsePattern. Returns True on success; never throws. |
| `Expand` | `bool Expand(AutomationElement element, out string message)` | Expands an element (e.g. a combo box or tree node) via ExpandCollapsePattern. Returns True on success; never throws. |
| `FindAllByControlType` | `bool FindAllByControlType(AutomationElement parent, UiControlType controlType, out List<AutomationElement> elements, out string message, bool descendantsOnly = true)` | Finds every descendant (or child) element of the given control type. Returns True on success; never throws. |
| `FindByAutomationId` | `bool FindByAutomationId(AutomationElement parent, string automationId, out AutomationElement element, out string message, bool descendantsOnly = true)` | Finds a descendant (or child) element by its AutomationId. Returns True if found; never throws. |
| `FindByClassName` | `bool FindByClassName(AutomationElement parent, string className, out AutomationElement element, out string message, bool descendantsOnly = true)` | Finds a descendant (or child) element by its window class name. Returns True if found; never throws. |
| `FindByControlType` | `bool FindByControlType(AutomationElement parent, UiControlType controlType, out AutomationElement element, out string message, bool descendantsOnly = true)` | Finds the first descendant (or child) element of the given control type. Returns True if found; never throws. |
| `FindByName` | `bool FindByName(AutomationElement parent, string name, out AutomationElement element, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant (or child) element by its Name (exact or substring match). Returns True if found; never throws. |
| `FromPoint` | `AutomationElement FromPoint(int x, int y)` | Gets the UI Automation element at a screen point (bridges MouseUtils coordinates), or null on failure. |
| `FromWindowHandle` | `AutomationElement FromWindowHandle(IntPtr hWnd)` | Gets the UI Automation element for a window handle (bridges WindowUtils/DialogUtils), or null if invalid. |
| `GetAutomationId` | `bool GetAutomationId(AutomationElement element, out string automationId, out string message)` | Gets an element's AutomationId property. Returns True on success; never throws. |
| `GetBoundingRectangle` | `bool GetBoundingRectangle(AutomationElement element, out int left, out int top, out int width, out int height, out string message)` | Same as `GetBoundingRectangleAsRectangle`, as scalar left/top/width/height outputs for designers without a Rectangle proxy. |
| `GetBoundingRectangleAsRectangle` | `bool GetBoundingRectangleAsRectangle(AutomationElement element, out Rectangle bounds, out string message)` | Gets an element's screen-space bounding rectangle; false (+ message) if it has no on-screen bounding rectangle. |
| `GetChildren` | `bool GetChildren(AutomationElement parent, out List<AutomationElement> children, out string message)` | Gets all immediate children of an element. Returns True on success; never throws. |
| `GetChildrenFromWindowHandle` | `bool GetChildrenFromWindowHandle(IntPtr hWnd, out List<AutomationElement> children, out string message)` | Gets all immediate children of a top-level window from its handle, in one call. |
| `GetChildrenSummaryJson` | `bool GetChildrenSummaryJson(AutomationElement parent, out string json, out string message)` | Same as `GetChildren`, summarized as a JSON array (name/automationId/className/controlType/bounds). |
| `GetChildrenSummaryJsonFromWindowHandle` | `bool GetChildrenSummaryJsonFromWindowHandle(IntPtr hWnd, out string json, out string message)` | Same as `GetChildrenFromWindowHandle`, summarized as JSON. |
| `GetClassName` | `bool GetClassName(AutomationElement element, out string className, out string message)` | Gets an element's window class name. Returns True on success; never throws. |
| `GetControlTypeName` | `bool GetControlTypeName(AutomationElement element, out string controlTypeName, out string message)` | Gets a friendly name for an element's control type (e.g. "Button"); false + message if it reports no ControlType. |
| `GetElementAt` | `bool GetElementAt(List<AutomationElement> elements, int index, out AutomationElement element, out string message)` | Gets the element at a given index in a list from `GetChildren`/`FindAllByControlType`. |
| `GetElementCount` | `bool GetElementCount(List<AutomationElement> elements, out int count, out string message)` | Gets the number of elements in a list from `GetChildren`/`FindAllByControlType`. |
| `GetName` | `bool GetName(AutomationElement element, out string name, out string message)` | Gets an element's Name property. Returns True on success; never throws. |
| `GetRootElement` | `AutomationElement GetRootElement()` | Gets the desktop root element. |
| `GetValue` | `bool GetValue(AutomationElement element, out string value, out string message)` | Gets an element's value via ValuePattern. Returns True on success; never throws. |
| `GetValueByAutomationId` | `bool GetValueByAutomationId(IntPtr hWnd, string automationId, out string value, out string message, bool descendantsOnly = true)` | Finds a descendant of a window by AutomationId and gets its value, in one call. |
| `GetValueByName` | `bool GetValueByName(IntPtr hWnd, string name, out string value, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and gets its value, in one call. |
| `HighlightElement` | `bool HighlightElement(AutomationElement element, out string message, int flashes = 3, int flashMs = 200, int lineWidth = 3, int colorRef = 0x0000FF)` | Flashes an inverting rectangle around an element to visually confirm which on-screen element it corresponds to. |
| `HighlightElement` | `bool HighlightElement(AutomationElement element, System.Drawing.Color color, out string message, int flashes = 3, int flashMs = 200, int lineWidth = 3)` | Same, taking a `System.Drawing.Color` instead of a packed colorRef. |
| `HighlightElement` | `bool HighlightElement(AutomationElement element, int red, int green, int blue, out string message, int flashes = 3, int flashMs = 200, int lineWidth = 3)` | Same, as RGB color components (0-255 each) instead of a packed colorRef. |
| `Invoke` | `bool Invoke(AutomationElement element, out string message)` | Invokes an element (click-equivalent for buttons/menu items) via InvokePattern. Returns True on success; never throws. |
| `InvokeByAutomationId` | `bool InvokeByAutomationId(IntPtr hWnd, string automationId, out string message, bool descendantsOnly = true)` | Finds a descendant of a window by AutomationId and invokes it, in one call. |
| `InvokeByName` | `bool InvokeByName(IntPtr hWnd, string name, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and invokes it (e.g. clicks a button), in one call. |
| `IsElementAvailable` | `bool IsElementAvailable(AutomationElement element)` | Returns True if the element is still available (its underlying UI hasn't gone away). Accepts null without an out message. |
| `IsEnabled` | `bool IsEnabled(AutomationElement element, out bool querySucceeded, out string message)` | Same as `IsEnabledSimple`, plus a querySucceeded output equivalent to message == null. |
| `IsEnabledByName` | `bool IsEnabledByName(IntPtr hWnd, string name, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and returns True if it's enabled, in one call. |
| `IsEnabledSimple` | `bool IsEnabledSimple(AutomationElement element, out string message)` | Returns True if the element is enabled. Never throws. |
| `IsOffscreen` | `bool IsOffscreen(AutomationElement element, out bool querySucceeded, out string message)` | Same as `IsOffscreenSimple`, plus a querySucceeded output. |
| `IsOffscreenSimple` | `bool IsOffscreenSimple(AutomationElement element, out string message)` | Returns True if the element is offscreen. Never throws. |
| `IsSelected` | `bool IsSelected(AutomationElement element, out bool querySucceeded, out string message)` | Same as `IsSelectedSimple`, plus a querySucceeded output. |
| `IsSelectedByName` | `bool IsSelectedByName(IntPtr hWnd, string name, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and returns True if it's selected, in one call. |
| `IsSelectedSimple` | `bool IsSelectedSimple(AutomationElement element, out string message)` | Returns True if a selectable element is currently selected. Never throws. |
| `IsToggled` | `bool IsToggled(AutomationElement element, out bool querySucceeded, out string message)` | Same as `IsToggledSimple`, plus a querySucceeded output. |
| `IsToggledByName` | `bool IsToggledByName(IntPtr hWnd, string name, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and returns True if it's toggled On, in one call. |
| `IsToggledSimple` | `bool IsToggledSimple(AutomationElement element, out string message)` | Returns True if a toggleable element is currently On. Never throws. |
| `Select` | `bool Select(AutomationElement element, out string message)` | Selects an element (e.g. a list item) via SelectionItemPattern. Returns True on success; never throws. |
| `SelectByName` | `bool SelectByName(IntPtr hWnd, string name, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and selects it (e.g. switches to a tab, picks a radio button), in one call. |
| `SelectListItemByName` | `bool SelectListItemByName(IntPtr hWnd, string containerName, string itemName, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a combo box/list box/tree by name, expands it if needed, then finds and selects an item within it by name, in one call. |
| `SetValue` | `bool SetValue(AutomationElement element, string value, out string message)` | Sets an element's value via ValuePattern. Returns True on success; never throws. |
| `SetValueByAutomationId` | `bool SetValueByAutomationId(IntPtr hWnd, string automationId, string value, out string message, bool descendantsOnly = true)` | Finds a descendant of a window by AutomationId and sets its value, in one call. |
| `SetValueByName` | `bool SetValueByName(IntPtr hWnd, string name, string value, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and sets its value, in one call. |
| `Toggle` | `bool Toggle(AutomationElement element, out string message)` | Toggles an element (e.g. a checkbox) via TogglePattern. Returns True on success; never throws. |
| `ToggleByName` | `bool ToggleByName(IntPtr hWnd, string name, out string message, bool exactMatch = true, bool descendantsOnly = true)` | Finds a descendant of a window by its visible name and toggles it (e.g. checks/unchecks a checkbox), in one call. |
| `WaitForElementByAutomationId` | `bool WaitForElementByAutomationId(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element, out bool timedOut, out string message)` | Same as `WaitForElementByAutomationIdSimple`, plus a timedOut output distinguishing a genuine timeout from a real argument error. |
| `WaitForElementByAutomationIdSimple` | `bool WaitForElementByAutomationIdSimple(AutomationElement parent, string automationId, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)` | Polls for a descendant element matching the given AutomationId until it appears or the timeout elapses. |
| `WaitForElementByName` | `bool WaitForElementByName(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element, out bool timedOut, out string message)` | Same as `WaitForElementByNameSimple`, plus a timedOut output. |
| `WaitForElementByNameSimple` | `bool WaitForElementByNameSimple(AutomationElement parent, string name, bool exactMatch, int timeoutMs, int pollIntervalMs, out AutomationElement element, out string message)` | Polls for a descendant element matching the given Name until it appears or the timeout elapses. |

## ValueStoreUtils

Provides an instance-local, freeform key/value bag for passing loosely-typed data (screen-scrape results, business-object fields, config values) between RPA automation steps, with forgiving typed conversion and dot-notation access into JSON-shaped values.

**Namespace:** `ValueStoreAutomation` | **Assembly:** `ValueStoreAutomation`

### Properties

| Property | Type | Description |
|---|---|---|
| `CaseSensitiveKeys` | `bool` | Whether keys use case-sensitive comparison. Default False. Changing this rebuilds the internal map with the new comparer, preserving existing entries. |

### Methods

| Method | Signature | Description |
|---|---|---|
| `AddIfMissing` | `bool AddIfMissing(string key, object value, out bool added, out string message)` | Sets a value only if the key is not already present. |
| `Clear` | `bool Clear(out int removedCount, out string message)` | Removes every entry and returns the number removed. |
| `ContainsKey` | `bool ContainsKey(string key, out bool exists, out string message)` | Checks whether a key is present. |
| `FindKeysJson` | `bool FindKeysJson(string wildcardPattern, out string keysJson, out string message)` | Returns keys matching a simple `*` wildcard pattern as a JSON array. |
| `GetBoolean` | `bool GetBoolean(string key, bool defaultValue = false)` | Gets the value as a Boolean. Accepts true/false, 1/0, and yes/no/y/n. Returns defaultValue on failure. |
| `GetCount` | `bool GetCount(out int count, out string message)` | Returns the current entry count. |
| `GetDateTime` | `DateTime GetDateTime(string key, DateTime defaultValue = default, string format = null)` | Gets the value as a DateTime, with an optional exact parse format. Returns defaultValue on failure. |
| `GetDecimal` | `decimal GetDecimal(string key, decimal defaultValue = 0m)` | Gets the value as a Decimal, converting from string/numeric sources. Returns defaultValue on failure. |
| `GetDouble` | `double GetDouble(string key, double defaultValue = 0d)` | Gets the value as a Double, converting from string/numeric sources. Returns defaultValue on failure. |
| `GetGuid` | `Guid GetGuid(string key, Guid defaultValue = default)` | Gets the value as a Guid, parsing from string if necessary. Returns defaultValue on failure. |
| `GetInt32` | `int GetInt32(string key, int defaultValue = 0)` | Gets the value as an Int32, converting from string/numeric sources. Returns defaultValue on failure. |
| `GetInt64` | `long GetInt64(string key, long defaultValue = 0L)` | Gets the value as an Int64, converting from string/numeric sources. Returns defaultValue on failure. |
| `GetKeys` | `bool GetKeys(out string[] keys, out string message)` | Returns every key as a string array, in insertion order, for direct iteration. |
| `GetKeysJson` | `bool GetKeysJson(out string keysJson, out string message)` | Returns every key as a JSON array, in insertion order. |
| `GetPathBoolean` | `bool GetPathBoolean(string path, bool defaultValue = false)` | Dot-notation get returning a Boolean, accepting the same formats as `GetBoolean`. |
| `GetPathDateTime` | `DateTime GetPathDateTime(string path, DateTime defaultValue = default)` | Dot-notation get returning a DateTime. Returns defaultValue if any segment is missing. |
| `GetPathInt32` | `int GetPathInt32(string path, int defaultValue = 0)` | Dot-notation get returning an Int32. Returns defaultValue if any segment is missing. |
| `GetPathString` | `string GetPathString(string path, string defaultValue = null)` | Dot-notation get returning a string. Returns defaultValue if any segment is missing or unconvertible. |
| `GetString` | `string GetString(string key, string defaultValue = null)` | Gets the value as a string, converting non-string values. Returns defaultValue if missing, null, or unconvertible. |
| `IsEmpty` | `bool IsEmpty(string key, out bool isEmpty, out string message)` | Reports whether a key is missing, null, or blank/whitespace text. |
| `Merge` | `bool Merge(string sourceJson, bool overwrite, out int mergedCount, out string message)` | Merges a flat JSON object's top-level properties into this store. The operation is atomic. |
| `Remove` | `bool Remove(string key, out bool removed, out string message)` | Removes a key. Removing a key that is not present is a normal result with removed False. |
| `RemoveAndGetValue` | `bool RemoveAndGetValue(string key, out bool found, out object value, out string message)` | Removes a key and returns its raw value in one step. |
| `SetBoolean` | `bool SetBoolean(string key, bool value, out string message)` | Sets a Boolean entry. |
| `SetDateTime` | `bool SetDateTime(string key, DateTime value, out string message)` | Sets a DateTime entry. |
| `SetDecimal` | `bool SetDecimal(string key, decimal value, out string message)` | Sets a Decimal entry. |
| `SetDouble` | `bool SetDouble(string key, double value, out string message)` | Sets a Double entry. |
| `SetGuid` | `bool SetGuid(string key, Guid value, out string message)` | Sets a Guid entry. |
| `SetInt32` | `bool SetInt32(string key, int value, out string message)` | Sets an Int32 entry. |
| `SetInt64` | `bool SetInt64(string key, long value, out string message)` | Sets an Int64 entry. |
| `SetJson` | `bool SetJson(string key, string valueJson, out string message)` | Validates and sets an entry from a JSON fragment. Objects/arrays become nested structures. |
| `SetNull` | `bool SetNull(string key, out string message)` | Sets an entry to null without removing the key. |
| `SetPath` | `bool SetPath(string path, object value, out string message)` | Dot-notation set, creating intermediate nested structures as needed. |
| `SetString` | `bool SetString(string key, string value, out string message)` | Sets a String entry. |
| `SetValue` | `bool SetValue(string key, object value, out string message)` | Sets a value for a key, creating or overwriting it. |
| `TryGetBoolean` | `bool TryGetBoolean(string key, out bool value)` | Attempts to read the value as a Boolean (true/false, 1/0, yes/no/y/n). |
| `TryGetDateTime` | `bool TryGetDateTime(string key, out DateTime value)` | Attempts to read the value as a DateTime. |
| `TryGetDecimal` | `bool TryGetDecimal(string key, out decimal value)` | Attempts to read the value as a Decimal. |
| `TryGetDouble` | `bool TryGetDouble(string key, out double value)` | Attempts to read the value as a Double. |
| `TryGetEnum` | `bool TryGetEnum(string key, string enumTypeName, out bool found, out object value, out string message)` | Attempts to read the value as a member of a named enum type, parsed by name (case-insensitive). |
| `TryGetGuid` | `bool TryGetGuid(string key, out Guid value)` | Attempts to read the value as a Guid. |
| `TryGetInt32` | `bool TryGetInt32(string key, out int value)` | Attempts to read the value as an Int32. |
| `TryGetInt64` | `bool TryGetInt64(string key, out long value)` | Attempts to read the value as an Int64. |
| `TryGetJson` | `bool TryGetJson(out string json, out string message)` | Serializes the whole store to a JSON string. |
| `TryGetPathValue` | `bool TryGetPathValue(string path, out bool found, out object value, out string message)` | Dot-notation get through nested structures. A path that does not resolve is a normal result with found False. |
| `TryGetString` | `bool TryGetString(string key, out string value)` | Attempts to read the value as a string. |
| `TryGetValue` | `bool TryGetValue(string key, out bool found, out object value, out string message)` | Gets the raw value for a key. A missing key is a normal result with found False. |
| `TryLoadFromJson` | `bool TryLoadFromJson(string json, bool clearExisting, out int loadedCount, out string message)` | Loads a JSON object's properties into the store. The operation is atomic. |
| `TryLoadFromObject` | `bool TryLoadFromObject(object source, bool clearExisting, out int loadedCount, out string message)` | Loads a POCO's public readable properties into the store via reflection (shallow, atomic). |

## WindowUtils

Enumerates, locates, moves/resizes, activates, and closes windows using the Win32 window APIs (`EnumWindows`, `GetWindowRect`, `SetWindowPos`, and friends).

**Namespace:** `WindowAutomation` | **Assembly:** `WindowAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `ActivateWindow` | `bool ActivateWindow(IntPtr hWnd, out string message)` | Brings a window to the foreground and gives it input focus. Returns True on success; never throws. |
| `CloseWindow` | `bool CloseWindow(IntPtr hWnd, out string message)` | Asks a window to close by posting `WM_CLOSE` to it. Returns True on success; never throws. |
| `FindChildWindow` | `IntPtr FindChildWindow(IntPtr hWndParent, string title, string className, bool exactMatch = true)` | Finds a child window matching the given title and/or class name (exact and case-sensitive by default; exactMatch: false switches to case-insensitive substring). |
| `FindFirstWindowByProcessId` | `IntPtr FindFirstWindowByProcessId(int processId)` | Finds the first top-level window owned by the given process ID, for the common single-window case. |
| `FindWindowByClass` | `IntPtr FindWindowByClass(string className)` | Finds the first top-level window of the given window class. |
| `FindWindowByTitle` | `IntPtr FindWindowByTitle(string title, bool exactMatch = true)` | Finds a top-level window by title (exact or substring match). Returns IntPtr.Zero if none matches. |
| `FindWindowsByProcessId` | `List<IntPtr> FindWindowsByProcessId(int processId)` | Finds all top-level windows owned by the given process ID (a process can own more than one). |
| `GetChildWindows` | `List<IntPtr> GetChildWindows(IntPtr hWndParent)` | Gets all descendant windows/controls of a parent window (recursively, not just immediate children). |
| `GetForegroundWindow` | `IntPtr GetForegroundWindow()` | Gets the handle of the current foreground (active) window. |
| `GetTopLevelWindows` | `List<IntPtr> GetTopLevelWindows()` | Gets all top-level windows via `EnumWindows`. |
| `GetWindowBounds` | `bool GetWindowBounds(IntPtr hWnd, out int left, out int top, out int width, out int height, out string message)` | Gets the screen-space bounding rectangle of a window, as scalar left/top/width/height outputs. |
| `GetWindowBoundsAsRectangle` | `bool GetWindowBoundsAsRectangle(IntPtr hWnd, out Rectangle bounds, out string message)` | Gets the screen-space bounding rectangle of a window. Returns True on success; never throws. |
| `GetWindowClassName` | `string GetWindowClassName(IntPtr hWnd)` | Gets a window's window-class name (empty string for an invalid handle too). |
| `GetWindowProcessId` | `int GetWindowProcessId(IntPtr hWnd)` | Gets the process ID that owns a window (0 for an invalid handle too). |
| `GetWindowTitle` | `string GetWindowTitle(IntPtr hWnd)` | Gets a window's title text (empty string for both an invalid handle and a legitimately titleless window). |
| `IsWindowResponding` | `bool IsWindowResponding(IntPtr hWnd)` | Returns true if the window is responding to messages (inverse of `IsHungAppWindow`). |
| `IsWindowVisible` | `bool IsWindowVisible(IntPtr hWnd)` | Returns true if the window is visible. |
| `MoveWindow` | `bool MoveWindow(IntPtr hWnd, int left, int top, out string message)` | Moves a window without changing its size. Returns True on success; never throws. |
| `ResizeWindow` | `bool ResizeWindow(IntPtr hWnd, int width, int height, out string message)` | Resizes a window without changing its position. Returns True on success; never throws. |
| `SetAlwaysOnTop` | `bool SetAlwaysOnTop(IntPtr hWnd, bool alwaysOnTop, out string message)` | Makes a window always-on-top (or removes that state). Returns True on success; never throws. |
| `SetWindowBounds` | `bool SetWindowBounds(IntPtr hWnd, int left, int top, int width, int height, out string message)` | Moves and/or resizes a window to the given rectangle. Returns True on success; never throws. |
| `SetWindowState` | `void SetWindowState(IntPtr hWnd, ShowWindowCommand command)` | Applies a show/hide/minimize/maximize/restore state to a window. |
| `TryGetWindowClassName` | `bool TryGetWindowClassName(IntPtr hWnd, out string className, out string message)` | Same as `GetWindowClassName`, but returns False for an invalid/nonexistent handle instead of collapsing it to an empty string. |
| `TryGetWindowProcessId` | `bool TryGetWindowProcessId(IntPtr hWnd, out int processId, out string message)` | Same as `GetWindowProcessId`, but returns False for an invalid/nonexistent handle instead of collapsing it to 0. |
| `TryGetWindowTitle` | `bool TryGetWindowTitle(IntPtr hWnd, out string title, out string message)` | Same as `GetWindowTitle`, but returns False for an invalid/nonexistent handle instead of collapsing it to an empty string. |
| `WaitForWindow` | `bool WaitForWindow(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd, out string message)` | Same as `WaitForWindowSimple`, plus a message output distinguishing a genuine timeout from an invalid title. |
| `WaitForWindowActive` | `bool WaitForWindowActive(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until the given window becomes the foreground window, or the timeout elapses. |
| `WaitForWindowSimple` | `bool WaitForWindowSimple(string title, int timeoutMs, int pollIntervalMs, out IntPtr hWnd)` | Polls for a window matching the title (substring, case-insensitive) until it appears or the timeout elapses. |
| `WaitForWindowToClose` | `bool WaitForWindowToClose(IntPtr hWnd, int timeoutMs, int pollIntervalMs)` | Polls until a window handle is no longer valid (the window closed), or the timeout elapses. |

## WinEventUtils

Watches Windows UI events via the `SetWinEventHook` API and delivers them the moment they happen (no polling), exposing both a synchronous Wait model and a background Subscribe model over one engine.

**Namespace:** `WinEventAutomation` | **Assembly:** `WinEventAutomation`

### Methods

| Method | Signature | Description |
|---|---|---|
| `BuildFilterJson` | `string BuildFilterJson(WinEventFilterField field, string value, bool? hasButtonChildren = null)` | Same as the full overload, but takes a single designer-selectable `WinEventFilterField` instead of naming all seven parameters — the common case of filtering on exactly one field. |
| `BuildFilterJson` | `string BuildFilterJson(string process = null, string processesCsv = null, string className = null, string titleContains = null, string titleMatches = null, bool? hasButtonChildren = null, bool? excludeSelf = null)` | Builds a compact filter JSON string from scalar parameters for `Subscribe`/`WaitForX`/`WasWindowCreated`; omitted parameters are left out of the JSON. Never throws. |
| `CancelWaits` | `bool CancelWaits(out string message)` | Releases all pending waits immediately; each returns False and reports a timeout. |
| `ClearQueue` | `bool ClearQueue(string subscriptionId, out string message)` | Drops all queued events for a subscription. Returns True when the subscription exists. |
| `DumpRecentEvents` | `bool DumpRecentEvents(int count, out string json, out string message)` | Returns the last count events (newest last) as a JSON array, regardless of subscriptions. |
| `GetNextEvent` | `bool GetNextEvent(string subscriptionId, int timeoutMs, out WinEventData eventData, out string message)` | Blocks up to timeoutMs for the next queued event; exactly one `WinEventData` is dequeued on success. |
| `GetNextEvents` | `bool GetNextEvents(string subscriptionId, int maxCount, int drainMs, out WinEventData[] events, out string message)` | Drains up to maxCount queued events, waiting up to drainMs for the first one. |
| `GetNextEventsJson` | `bool GetNextEventsJson(string subscriptionId, int maxCount, int drainMs, out string json, out string message)` | Same as `GetNextEvents`, but reports the drained events as a JSON array. |
| `HasEvents` | `bool HasEvents(string subscriptionId, out int count, out string message)` | Reports the number of queued events for a subscription. Returns True when the subscription exists. |
| `Initialize` | `bool Initialize(out string message)` | Starts the background hook thread (idempotent). Call before `Start`/`StartCategories`. |
| `IsDialog` | `bool IsDialog(string filterJson, out IntPtr hwnd, out string message)` | Non-blocking check for a matching live `#32770` dialog window; returns its handle. |
| `IsMenu` | `bool IsMenu(string filterJson, out IntPtr hwnd, out string message)` | Non-blocking check for a matching live `#32768` menu window; returns its handle. |
| `IsWindow` | `bool IsWindow(string filterJson, out IntPtr hwnd, out string message)` | Non-blocking check for a matching live top-level window; returns its handle. |
| `SetDebounce` | `bool SetDebounce(string eventName, int debounceMs, out string message)` | Sets the debounce window (ms) for an event name; a second identical (hwnd, event-name) event within the window is dropped. |
| `SetDebounce` | `bool SetDebounce(WinEventName eventName, int debounceMs, out string message)` | Same as the string overload, but takes the repository-owned `WinEventName` enum instead of a free-form, typo-prone string. |
| `SetQueueLimits` | `bool SetQueueLimits(int maxEvents, string overflowPolicy, out string message)` | Sets the per-subscription queue limit and overflow policy ("DropOldest" \| "DropNewest" \| "Block"). |
| `SetQueueLimits` | `bool SetQueueLimits(int maxEvents, WinEventOverflowPolicy overflowPolicy, out string message)` | Same as the string overload, but takes the repository-owned `WinEventOverflowPolicy` enum instead of a free-form string. |
| `Start` | `bool Start(WinEventCategory category, out string message)` | Activates a single category and installs the WinEvent hook — the common case for a wait that only needs one category. |
| `StartCategories` | `bool StartCategories(bool? windows, bool? foreground, bool? dialogs, bool? titles, bool? states, bool? menus, bool? windowOps, bool? session, out string message)` | Same as `Start`, but with one Boolean checkbox per category instead of a comma-separated, typo-prone string. |
| `Stop` | `bool Stop(out string message)` | Unhooks the WinEvent hook and clears the active category set; queues are preserved so they can still be drained. |
| `Subscribe` | `bool Subscribe(WinEventCategory category, string filterJson, string subscriptionId, out string message)` | Registers a subscription for a single category — the common case; matching events are queued under subscriptionId. |
| `SubscribeCategories` | `bool SubscribeCategories(bool? windows, bool? foreground, bool? dialogs, bool? titles, bool? states, bool? menus, bool? windowOps, bool? session, string filterJson, string subscriptionId, out string message)` | Same as `Subscribe`, but with one Boolean checkbox per category instead of a comma-separated, typo-prone string. |
| `Unsubscribe` | `bool Unsubscribe(string subscriptionId, out string message)` | Removes a subscription and drops its queue; a blocked `GetNextEvent` is woken and returns False with a message. |
| `WaitForDialogAppeared` | `bool WaitForDialogAppeared(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a dialog event (SYSTEM_DIALOGSTART/END or a "#32770" window being created/shown) matching the filter. |
| `WaitForForegroundChanged` | `bool WaitForForegroundChanged(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a foreground-change event matching the filter. |
| `WaitForMenuOpened` | `bool WaitForMenuOpened(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a menu-opened or menu-popup-opened event matching the filter. |
| `WaitForStateChanged` | `bool WaitForStateChanged(string filterJson, string stateRegex, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a state-change event matching the filter whose normalized state matches stateRegex (null/empty matches any). |
| `WaitForTitleChanged` | `bool WaitForTitleChanged(string filterJson, string titleRegex, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a title-change event matching the filter whose new title matches titleRegex (null/empty matches any). |
| `WaitForWindowCreated` | `bool WaitForWindowCreated(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a window-created event matching the filter. |
| `WaitForWindowDestroyed` | `bool WaitForWindowDestroyed(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a window-destroyed event matching the filter. |
| `WaitForWindowShown` | `bool WaitForWindowShown(string filterJson, int timeoutMs, out WinEventData eventData, out string message)` | Waits for a window-shown event matching the filter. |
| `WasWindowCreated` | `bool WasWindowCreated(string filterJson, int withinLastMs, out string message)` | Non-blocking lookback: reports whether a window-created event matching filterJson was captured within the last withinLastMs milliseconds. |

*Generated by reading every component's source (`.cs`) files directly, cross-checked against each component's own README. If a component's public API changes, regenerate the affected section(s) from source rather than hand-editing, so this stays trustworthy as a search index.*
