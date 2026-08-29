# Wait for Dialog

## Wait for a save-confirmation dialog that may or may not appear

```csharp
if (dialog.WaitForDialog("Save changes", timeoutMs: 3000, pollIntervalMs: 100, out IntPtr hWnd))
{
    dialog.ClickDialogButtonById(hWnd, (int)DialogButton.No, out _);
}
// else: the app closed without prompting - nothing to do
```

## Only wait for a dialog belonging to a specific process

`WaitForDialog`/`FindDialog` match any *visible* top-level window whose title matches —
pass `processId` to scope the wait to the target application, so another app's window with
a coincidentally similar title can never trigger the automation. Get the process ID from
[WindowUtils](../../windowutils/README.md) (`GetWindowProcessId` /
`FindWindowsByProcessId`); hidden windows are skipped, so the wait can't fire on a
pre-created form before the dialog actually appears:

```csharp
int targetPid = windows.GetWindowProcessId(appMainWindow);
if (dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100,
                         out IntPtr hWnd, processId: targetPid))
{
    dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _);
}
```

## Wait for an exact title match

`WaitForDialog` matches substrings by default (`exactMatch: false`), which differs from
`FindDialog`'s default of `true`. Pass `exactMatch: true` when the title must match exactly
(e.g. a dialog titled "Confirm" should not match a window titled "Confirm changes"):

```csharp
if (dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100,
                         out IntPtr hWnd, exactMatch: true))
{
    dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _);
}
```

## Wait for a dialog to close after clicking its button

```csharp
dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Ok, out _);
dialog.WaitForDialogToClose(hWnd, timeoutMs: 5000, pollIntervalMs: 100);
```
