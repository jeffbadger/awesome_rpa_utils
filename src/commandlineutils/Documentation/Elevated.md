# Elevated

`RunElevated` returns `bool` (whether the process ran at all) with an
`out int exitCode`, `out bool timedOut`, and `out string message` — never
throws, including for a missing executable or a cancelled UAC prompt.

Only an **absolute** `fileName` is accepted (e.g. `@"C:\tools\setup.exe"`, not
`"setup.exe"`): a bare or relative name would be resolved through PATH and the
working directory before the UAC prompt, and that resolution would run with
admin rights — a planted executable earlier in PATH would be the one that gets
elevated. `RunElevated` rejects a non-absolute `fileName` with a `message`
rather than resolving it.

## Run an installer that requires admin rights

```csharp
cmd.RunElevated(@"C:\installers\setup.exe", out int exitCode, out bool timedOut, out string message, "/quiet", timeoutMs: 300000);
if (timedOut)
{
    // setup.exe (and any child processes) were killed after 300 seconds.
    // exitCode is meaningless in this case.
}
else if (exitCode != 0)
{
    // The installer ran to completion but reported failure.
    // No stdout/stderr is available for an elevated process.
}
```

## Get output from an elevated command anyway

Since Windows won't let you redirect output for an elevated process, have
the elevated command write to a file and read that file back afterward:

```csharp
cmd.RunElevated("cmd.exe", out _, out _, out _, "/c whoami /priv > C:\\temp\\priv.txt");
string output = System.IO.File.ReadAllText(@"C:\temp\priv.txt");
```

## Checking why an elevated run failed

```csharp
if (!cmd.RunElevated(@"C:\installers\setup.exe", out int exitCode, out bool timedOut, out string message))
{
    Console.WriteLine($"Could not run elevated: {message}");
}
```
