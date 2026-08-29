# Run

Both `Run` and `RunShellCommand` return `bool` (whether the process ran at all)
with an `out CommandResult result` and `out string message` — never throws,
including for a missing executable.

## Run a program directly and check its exit code

```csharp
if (cmd.Run("ping.exe", out CommandResult result, out _, "-n 4 127.0.0.1") && result.ExitCode == 0)
{
    // Succeeded — result.StandardOutput has the full ping output.
}
```

## Run with a timeout, and handle a hang

```csharp
cmd.Run("some-slow-tool.exe", out CommandResult result, out _, timeoutMs: 30000);
if (result.TimedOut)
{
    // some-slow-tool.exe (and any child processes it spawned) were killed
    // after 30 seconds. result.StandardOutput/StandardError still hold
    // whatever was captured before the kill.
}
```

## Run with custom environment variables

```csharp
var env = new Dictionary<string, string> { ["MY_FLAG"] = "1" };
cmd.Run("my-tool.exe", out CommandResult result, out _, environmentVariables: env);
```

## Run a shell command that needs pipes/redirection

```csharp
cmd.RunShellCommand("dir *.log > filelist.txt", out CommandResult result, out _);
```

## Checking why a run failed

```csharp
if (!cmd.Run("does-not-exist.exe", out CommandResult result, out string message))
{
    Console.WriteLine($"Could not run process: {message}");
}
```
