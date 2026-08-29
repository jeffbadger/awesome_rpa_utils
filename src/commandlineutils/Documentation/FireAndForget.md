# Fire and Forget

## Start a long-running background tool without blocking the automation

```csharp
cmd.StartFireAndForget("background-sync.exe", out int pid, out _);
// The automation continues immediately; background-sync.exe keeps running
// on its own. Use WindowUtils.FindWindowsByProcessId(pid) if you later need
// to find a window it opened.
```

## Checking why a fire-and-forget start failed

`StartFireAndForget` returns `bool` with an `out int processId` and
`out string message` — never throws, including for a missing executable.

```csharp
if (!cmd.StartFireAndForget("does-not-exist.exe", out int pid, out string message))
{
    Console.WriteLine($"Could not start process: {message}");
}
```
