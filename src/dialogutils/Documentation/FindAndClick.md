# Find & Click

## Dismiss a Yes/No confirmation

```csharp
if (dialog.WaitForDialog("Confirm", timeoutMs: 5000, pollIntervalMs: 100,
                         out IntPtr hWnd, exactMatch: false))
{
    dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Yes, out _);
}
```

## Click a button by its visible label instead of a control ID

```csharp
dialog.ClickDialogButtonByText(hWnd, "Don't Save", out bool wasEnabled, out _);
```

## Click a button whose text you only know part of

Useful when a button's text includes a variable suffix, e.g. `"Retry (3 left)"`
or a trailing ellipsis like `"Details..."`:

```csharp
dialog.ClickDialogButtonByText(hWnd, "Retry", out bool wasEnabled, out _, exactMatch: false);
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
dialog.ClickDialogButtonByText(hWnd, "Yes", out bool wasEnabled, out _); // matches "&Yes"
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

## Find every dialog matching a title

`FindDialog`/`WaitForDialog` return the first matching window. When more than one window
could match (e.g. two apps both showing a "Confirm" dialog), `FindAllDialogs` returns every
match so you can pick the right one — same matching rules, same optional `processId` scoping:

```csharp
List<IntPtr> matches = dialog.FindAllDialogs("Confirm", exactMatch: false, processId: targetPid);
if (matches.Count > 0)
{
    // Pick the one you want (e.g. the topmost, or the one whose controls you recognize),
    // then drive it as usual:
    dialog.ClickDialogButtonById(matches[0], (int)DialogButton.Yes, out _);
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

## Understand what a click can report

Windows does not provide a reliable signal that an application acted on `BM_CLICK`.
`ClickDialogButtonByText` therefore reports only what it can observe: its return value
means the button was found, and `wasEnabled` means it was enabled when the single click
was sent.

```csharp
bool found = dialog.ClickDialogButtonByText(
    hWnd, "Yes", out bool wasEnabled, out string message, exactMatch: false);

if (!found)
    Console.WriteLine(message);
else if (!wasEnabled)
    Console.WriteLine("The button was found but disabled when clicked.");
```

When closing the dialog is the expected outcome, verify that separately:

```csharp
if (found && wasEnabled)
{
    bool closed = dialog.WaitForDialogToClose(hWnd, timeoutMs: 5000, pollIntervalMs: 100);
    // This confirms only that the original dialog handle disappeared.
}
```

The click waits up to `waitForEnabledMs` (default 500 ms) before sending. Increase it
for a dialog known to become interactive slowly.
