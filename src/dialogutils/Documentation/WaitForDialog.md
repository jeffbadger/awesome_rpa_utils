# Wait for Dialog

## Wait for a save-confirmation dialog that may or may not appear

```csharp
if (dialog.WaitForDialog("Save changes", timeoutMs: 3000, pollIntervalMs: 100, out IntPtr hWnd))
{
    dialog.ClickDialogButtonById(hWnd, (int)DialogButton.No, out _);
}
// else: the app closed without prompting - nothing to do
```

## Wait for a dialog to close after clicking its button

```csharp
dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Ok, out _);
dialog.WaitForDialogToClose(hWnd, timeoutMs: 5000, pollIntervalMs: 100);
```
