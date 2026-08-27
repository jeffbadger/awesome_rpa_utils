# Find & Click

## Dismiss a Yes/No confirmation

```csharp
if (dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd))
{
    dialog.ClickDialogButton(hWnd, DialogButton.Yes);
}
```

## Click a button by its visible label instead of a standard control ID

```csharp
dialog.ClickDialogButtonByText(hWnd, "Don't Save");
```

## Lower-level: find then click

```csharp
IntPtr okButton = dialog.FindButtonByText(hWnd, "OK");
if (okButton != IntPtr.Zero)
{
    dialog.ClickButton(okButton);
}
```

## Click a button by a known custom control ID

```csharp
IntPtr detailsButton = dialog.FindButtonById(hWnd, 1001);
if (detailsButton != IntPtr.Zero)
{
    dialog.ClickButton(detailsButton);
}
```
