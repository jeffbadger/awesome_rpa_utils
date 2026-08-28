# Elevated

## Run an installer that requires admin rights

```csharp
int exitCode = cmd.RunElevated("setup.exe", out bool timedOut, "/quiet", timeoutMs: 300000);
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
cmd.RunElevated("cmd.exe", out _, "/c whoami /priv > C:\\temp\\priv.txt");
string output = System.IO.File.ReadAllText(@"C:\temp\priv.txt");
```
