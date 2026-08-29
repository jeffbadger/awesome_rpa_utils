# Run

Both `Run` and `RunShellCommand` return `bool` (whether the process ran at all)
with an `out CommandResult result` and `out string message` — never throws,
including for a missing executable.

## Run a program directly and check its exit code

```csharp
if (cmd.Run(@"C:\Windows\System32\ping.exe", out CommandResult result, out _, "-n 4 127.0.0.1") && result.ExitCode == 0)
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

## Guard a shell command with an allowlist

`RunShellCommand` executes its input verbatim, so anything that ends up in the command
string runs. When a command's text is at all dynamic, pass `allowedPrograms`: cmd would
run the parts between `&`/`&&`/`|`/`||` as separate commands, and *every* segment must
start with an allowed program or the method returns `false` with nothing executed:

```csharp
var allowed = new[] { "robocopy", "findstr" };

if (!cmd.RunShellCommand("robocopy C:\\data \\\\share\\data /mir", out CommandResult result, out string message, allowedPrograms: allowed))
{
    // A segment launched something outside the guardrail — message says which.
    Console.WriteLine(message);
}
```

This is a guardrail against mistakes and typos, not a security sandbox: an allowed
program's arguments and any child processes it starts are not constrained, and shell
built-ins you depend on (e.g. `dir`) must themselves be listed to be allowed. For
anything with untrusted content, prefer `Run` (no shell at all) and validate inputs
yourself first.

## Known output was garbled? Set the child's encoding

Redirected output is decoded with the system's default encoding; a child that writes
UTF-8 comes back garbled unless you say so:

```csharp
cmd.RunShellCommand("tool-writing-utf8.exe", out CommandResult result, out _, outputEncoding: Encoding.UTF8);
```

## Watch `OutputTruncated` on chatty processes

Captured output is capped (~8 MB per stream) so a runaway child can't exhaust memory;
when the cap is hit the tail is replaced with a notice and `result.OutputTruncated`
is `true`.

## Checking why a run failed

```csharp
if (!cmd.Run(@"C:\no-such-dir\does-not-exist.exe", out CommandResult result, out string message))
{
    Console.WriteLine($"Could not run process: {message}");
}
```
