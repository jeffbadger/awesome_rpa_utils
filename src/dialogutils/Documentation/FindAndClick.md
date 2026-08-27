# Find & Click

## Dismiss a Yes/No confirmation

```csharp
if (dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd))
{
    dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes);
}
```

## Click a button by its visible label instead of a control ID

```csharp
dialog.ClickDialogButtonByText(hWnd, "Don't Save");
```

## Click a button whose text you only know part of

Useful when a button's text includes a variable suffix, e.g. `"Retry (3 left)"`
or a trailing ellipsis like `"Details..."`:

```csharp
dialog.ClickDialogButtonByText(hWnd, "Retry", exactMatch: false);
```

## Matching "Yes"/"No"/"OK" against real button text

A standard `MessageBox`'s buttons carry a raw `&` access-key mnemonic in their
actual window text — `GetWindowText` on a Yes/No `MessageBox`'s buttons returns
literally `"&Yes"`/`"&No"`, not `"Yes"`/`"No"` (Windows only *draws* the `&` as an
underline; it stays in the text itself). `FindButtonByText`/`ClickDialogButtonByText`
strip that mnemonic from both the button's text and the text you pass before
comparing, so matching against what you actually see on screen just works — no
special-casing needed, and this applies with either `exactMatch` setting:

```csharp
dialog.ClickDialogButtonByText(hWnd, "Yes"); // matches a button whose real text is "&Yes"
```

## Lower-level: find then click

```csharp
IntPtr okButton = dialog.FindButtonByText(hWnd, "OK");
if (okButton != IntPtr.Zero)
{
    dialog.ClickButton(okButton);
}
```

## Click a button by control ID — the go-to for RPA

Most useful once you've discovered a control's ID with `ListDialogControls`
(see [Read Text](ReadText.md)): `ClickDialogButtonById` takes any ID directly, well-known
or app-specific, with no need to first look up a matching `DialogButton` enum value:

```csharp
dialog.ClickDialogButtonById(hWnd, 1001);
```

Lower-level, if you need the handle itself for something else (e.g. `HighlightControl`):

```csharp
IntPtr detailsButton = dialog.FindButtonById(hWnd, 1001);
if (detailsButton != IntPtr.Zero)
{
    dialog.ClickButton(detailsButton);
}
```

## Check whether a found dialog can actually be dismissed by DialogUtils

Some "dialogs" (e.g. WinUI3/UWP apps' modal prompts, like the Windows 11 Notepad
"save changes?" prompt) are rendered as XAML content inside their host window
rather than as real `Button` controls — `FindDialog` still finds the window, but
there's nothing for `ClickButton` to target. Check `canDismiss` before relying on
it, and fall back to `KeyboardUtils` when it's `false`:

```csharp
IntPtr hWnd = dialog.FindDialog("Confirm", exactMatch: false, out bool canDismiss);
if (hWnd != IntPtr.Zero)
{
    if (canDismiss)
        dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes);
    else
        keyboard.PressKey(VirtualKey.Enter); // drive it via keyboard instead
}
```

`CanDismissDialog(hWnd)` is also available standalone, for a handle you already have
(e.g. from `WaitForDialog`).

## A click right after finding the dialog has no effect the first time

By default, `ClickButton`/`ClickDialogButtonById` wait up to 500 ms for the target button
to become enabled before clicking, since some dialogs finish enabling their buttons
slightly after the window and controls first appear — `BM_CLICK` on a disabled
button is silently ignored, which is why a click sent the instant a handle is found
can do nothing even though the button clearly exists. This is on by default, so the
pattern above (`WaitForDialog` immediately followed by `ClickDialogButtonById`) already
handles it. Tune it if needed:

```csharp
// Wait longer for a dialog known to be slow to become interactive:
dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, waitForEnabledMs: 2000);

// Or skip the wait entirely (previous behavior):
dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, waitForEnabledMs: 0);
```

## The click found the right button but the dialog just doesn't close (already enabled, no exception)

Some apps' click handlers ignore a click for reasons that have nothing to do with
Win32 (app-internal validation, async state not yet ready) — the button was already
enabled the whole time, so waiting for `IsWindowEnabled` doesn't help. Since Windows
gives no reliable success signal for a click, the only robust fix is to verify the
outcome and retry, which is what `ClickDialogButtonByText` does by default: it clicks,
checks whether the dialog actually closed, and re-clicks (up to `maxAttempts`, default
3, `retryDelayMs` apart, default 300 ms) if not — no separate call needed:

```csharp
bool closed = dialog.ClickDialogButtonByText(hWnd, "Yes", exactMatch: false);
if (!closed)
{
    // Still open after every attempt — something else is wrong (wrong button, app hung, etc.)
}

// Tune the retry, or disable it (click exactly once):
dialog.ClickDialogButtonByText(hWnd, "Yes", exactMatch: false, maxAttempts: 5, retryDelayMs: 500);
dialog.ClickDialogButtonByText(hWnd, "Yes", exactMatch: false, maxAttempts: 1);
```

Only meaningful for a click expected to close the dialog — a button that intentionally
keeps it open (e.g. "Apply") will use up every attempt and return `false`.
