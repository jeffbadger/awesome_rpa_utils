# Fire and Forget

## Start a long-running background tool without blocking the automation

```csharp
cmd.StartFireAndForget(@"C:\sync\background-sync.exe", out int pid, out _);
// The automation continues immediately; background-sync.exe keeps running
// on its own. Use WindowUtils.FindWindowsByProcessId(pid) if you later need
// to find a window it opened.
```

Console executables are started with `CreateNoWindow`, so no console window
flashes up on the robot's desktop. Custom environment variables work the same
way as on `Run` (`environmentVariables:`). As everywhere in this component,
prefer an **absolute** `fileName` — a bare name is resolved via PATH, outside
this component's control.

## For designers without an `IDictionary<string, string>` proxy

`StartFireAndForgetWithEnvironment` is a distinctly-named (not an overload)
Pega-friendly counterpart that takes environment variables as newline-delimited
`NAME=VALUE` text instead of a dictionary object:

```csharp
cmd.StartFireAndForgetWithEnvironment(@"C:\sync\background-sync.exe",
    out int pid, out string message,
    environmentVariablesText: "SYNC_MODE=fast\nLOG_LEVEL=verbose");
```

A malformed line (missing `=`, or an empty name) returns `false` with a message
before anything is started.

## Checking why a fire-and-forget start failed

`StartFireAndForget` returns `bool` with an `out int processId` and
`out string message` — never throws, including for a missing executable.

```csharp
if (!cmd.StartFireAndForget(@"C:\no-such-dir\does-not-exist.exe", out int pid, out string message))
{
    Console.WriteLine($"Could not start process: {message}");
}
```
