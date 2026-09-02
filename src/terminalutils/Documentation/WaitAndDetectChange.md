# WaitAndDetectChange

## Wait for a shell prompt before sending the next command

```csharp
tu.WaitForScreenTextSimple(processId, "C:\\>", useRegex: false, timeoutMs: 15000,
    pollIntervalMs: 250, out string message);
```

A plain, case-sensitive substring search is the common case. `WaitForFileStable`
alone would prevent many "robot opened the export before the application
finished writing it" failures for files - `WaitForScreenText` is the same
idea for an interactive console session.

## Wait for one of several possible outcomes using a regular expression

```csharp
tu.WaitForScreenText(processId, @"(Success|Error|Failed)", useRegex: true, timeoutMs: 30000,
    pollIntervalMs: 500, out bool timedOut, out string message);

if (timedOut)
{
    // Neither outcome appeared within 30 seconds.
}
```

## Detect that a screen redraw happened, without knowing the new content in advance

```csharp
tu.WaitForScreenChangeSimple(processId, timeoutMs: 10000, pollIntervalMs: 200, out string message);
```

Captures the screen once at call time as a baseline, then polls until a
later capture differs from it. Useful for a TUI menu or progress screen
where you know *something* will change but not what the new text will say.
A `timeoutMs` of `0` times out immediately, since no time has elapsed for
anything to have changed yet.
