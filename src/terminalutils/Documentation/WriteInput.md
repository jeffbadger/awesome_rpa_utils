# WriteInput

## Send a command line to a shell REPL and press Enter

```csharp
tu.WriteLine(processId, "dir /b", out string message);
```

Appends a trailing carriage return so the shell submits the line, the same
as pressing Enter.

## Type text without submitting it

```csharp
tu.WriteText(processId, "partial-file-nam", out string message);
```

## Type into a console window that's minimized or not in focus

```csharp
tu.WriteText(processId, "still works", out string message);
```

`WriteText`/`WriteLine` inject keystrokes directly into the console's input
buffer (`WriteConsoleInputW`) rather than simulating keyboard input at the
OS level - unlike `KeyboardUtils`, this does not require the target window
to be focused or even visible. Injected input is only consumed promptly if
the target application is actually blocked on a console read call; a
target that's busy doing something else won't echo the input instantly -
that's normal console behavior, not a bug.
