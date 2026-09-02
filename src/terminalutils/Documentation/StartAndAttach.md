# StartAndAttach

## Launch a legacy CLI tool and get a console to work with

```csharp
tu.StartConsoleProcess("legacy-report-tool.exe", "--interactive", null, out int processId, out string message);
```

The process gets its own real, visible console window (not hidden, not
redirected) so it can be attached to and read afterward. If Robot Studio's
own host process has no console of its own (the common case), this doesn't
disturb anything about the calling process.

## Check whether a process someone else started can be attached to

```csharp
tu.IsConsoleAttachable(processId, out bool attachable, out string message);

if (!attachable)
{
    // message explains why - e.g. the process has no console, or has already exited.
}
```

`TerminalUtils` can attach to any process's console by ID, not only ones it
started itself via `StartConsoleProcess` - useful when the target was
launched by something else (another automation, a scheduled task, a human).
