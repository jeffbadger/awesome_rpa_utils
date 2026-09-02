# ReadAndCapture

## Read the cursor's position before deciding where to type

```csharp
tu.GetCursorPosition(processId, out int row, out int column, out string message);
```

Both are 0-based and relative to the visible viewport's top-left corner -
the same coordinate space as `ReadScreenRowsJson`'s row index.

## Read the screen as structured rows, with a heuristic column split

```csharp
tu.ReadScreenRowsJson(processId, out string json, out string message);

// json is an array of { RowIndex, Text, Fields } - Fields splits Text on
// runs of 2+ whitespace characters, good enough for typical table-style
// command-line output (NOT true attribute-based field parsing).
```

## Capture the whole visible screen as plain text, for logging or pattern matching

```csharp
tu.CaptureScreenText(processId, preserveAnsi: false, out string text, out string message);
```

## Capture with re-synthesized ANSI color codes (for a log a human will view in a real terminal)

```csharp
tu.CaptureScreenText(processId, preserveAnsi: true, out string coloredText, out string message);
```

This re-synthesizes ANSI SGR color codes from each cell's stored color
attribute - it is not a literal capture of whatever escape sequences the
target application originally emitted (those no longer exist by the time
the console screen buffer is read), but it gives an equivalent-looking
result when viewed in a real terminal or a terminal-aware log viewer.
