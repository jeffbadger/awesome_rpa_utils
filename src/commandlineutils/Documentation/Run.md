# Run

## Run a program directly and check its exit code

```csharp
CommandResult result = cmd.Run("ping.exe", "-n 4 127.0.0.1");
if (result.ExitCode == 0)
{
    // Succeeded — result.StandardOutput has the full ping output.
}
```

## Run with a timeout, and handle a hang

```csharp
CommandResult result = cmd.Run("some-slow-tool.exe", timeoutMs: 30000);
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
CommandResult result = cmd.Run("my-tool.exe", environmentVariables: env);
```

## Run a shell command that needs pipes/redirection

```csharp
CommandResult result = cmd.RunShellCommand("dir *.log > filelist.txt");
```
