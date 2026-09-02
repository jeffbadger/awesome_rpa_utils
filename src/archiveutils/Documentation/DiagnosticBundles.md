# DiagnosticBundles

## Bundle logs and evidence after a failure, with a summary

```csharp
string filesCsv = $"{logPath},{screenshotPath},{configPath}";
az.CreateDiagnosticBundle(filesCsv, bundlePath, overwrite: false,
    manifestText: "Robot XYZ failed at step 4: invoice total mismatch.", out string message);
```

The bundle is published atomically, the same as `CreateArchive`. If two
source files share a file name (e.g. logs from two different machines both
named `run.log`), the second is stored as `run_2.log` rather than
overwriting the first entry.

## Let the component name the bundle for you

```csharp
az.CreateDiagnosticBundleSimple(filesCsv, evidenceDirectory, out string createdArchivePath, out string message);

// createdArchivePath is evidenceDirectory + "diagnostic-bundle-20260901-143022.zip"
```
