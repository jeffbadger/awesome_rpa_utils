# Find & Click

## Dismiss a Yes/No confirmation

```csharp
if (dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100, out IntPtr hWnd))
{
    dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _);
}
```

## Click a button by its visible label instead of a control ID

```csharp
dialog.ClickDialogButtonByText(hWnd, "Don't Save", out _);
```

## Click a button whose text you only know part of

Useful when a button's text includes a variable suffix, e.g. `"Retry (3 left)"`
or a trailing ellipsis like `"Details..."`:

```csharp
dialog.ClickDialogButtonByText(hWnd, "Retry", out _, exactMatch: false);
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
dialog.ClickDialogButtonByText(hWnd, "Yes", out _); // matches a button whose real text is "&Yes"
```

## Lower-level: find then click

```csharp
if (dialog.FindButtonByText(hWnd, out IntPtr okButton, "OK"))
{
    dialog.ClickButton(okButton);
}
```

## Click a button by control ID — the go-to for RPA

Most useful once you've discovered a control's ID with `ListDialogControls`
(see [Read Text](ReadText.md)): `ClickDialogButtonById` takes any ID directly, well-known
or app-specific, with no need to first look up a matching `DialogButton` enum value:

```csharp
dialog.ClickDialogButtonById(hWnd, 1001, out _);
```

Lower-level, if you need the handle itself for something else (e.g. `HighlightControl`):

```csharp
if (dialog.FindButtonById(hWnd, out IntPtr detailsButton, 1001))
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
if (dialog.FindDialog("Confirm", out IntPtr hWnd, out bool canDismiss, exactMatch: false))
{
    if (canDismiss)
        dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _);
    else
        keyboard.PressKey(VirtualKey.Enter); // drive it via keyboard instead
}
```

`CanDismissDialog(hWnd)` is also available standalone, for a handle you already have
(e.g. from `WaitForDialog`).

## Only match dialogs from a specific application

`FindDialog` scans every visible top-level window on the desktop by default. To keep a
coincidentally similar title in another app from ever matching (and being clicked), scope
the search to the target process with `processId` — get it from
[WindowUtils](../../windowutils/README.md)'s `GetWindowProcessId`/`FindWindowsByProcessId`.
Hidden windows are skipped in either mode, so an app's pre-created-but-invisible forms
can't match before the dialog actually shows:

```csharp
if (dialog.FindDialog("Confirm", out IntPtr hWnd, out bool canDismiss,
                      exactMatch: false, processId: targetPid))
{
    if (canDismiss)
        dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _);
    else
        keyboard.PressKey(VirtualKey.Enter); // drive it via keyboard instead
}
```

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
dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _, waitForEnabledMs: 2000);

// Or skip the wait entirely (previous behavior):
dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _, waitForEnabledMs: 0);
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
bool closed = dialog.ClickDialogButtonByText(hWnd, "Yes", out string message, exactMatch: false);
if (!closed)
{
    // Still open after every attempt, or no button matched at all — message explains which:
    Console.WriteLine(message);
}

// Tune the retry, or disable it (click exactly once):
dialog.ClickDialogButtonByText(hWnd, "Yes", out _, exactMatch: false, maxAttempts: 5, retryDelayMs: 500);
dialog.ClickDialogButtonByText(hWnd, "Yes", out _, exactMatch: false, maxAttempts: 1);
```

Only meaningful for a click expected to close the dialog — a button that intentionally
keeps it open (e.g. "Apply") will use up every attempt and return `false` (with `message`
saying so). This method never throws — a missing button is also reported via `message`,
not an exception.
