# Set Values

Filling in a dialog: typing into a text box, ticking a check box, choosing a radio
button, picking an item from a drop-down. Each of these takes a **control handle**,
which you get from `ListDialogControls`, `FindButtonById`, or `FindButtonByText`.

All of them return `bool` (success) with an `out string message` explaining why on
failure - none of them throw. Each one confirms the result by reading the control
back, so a `true` means the control is now in the requested state, not just that a
message was sent.

## Type into a text box

**Scenario:** A legacy app pops up a "Connect to server" dialog asking for a host name.

```csharp
var controls = dialog.ListDialogControls(hDialog);
var hostBox = controls.First(c => c.ClassName == "Edit");

if (!dialog.SetControlText(hostBox.Handle, "claims-prod-02", out string message))
{
    Logger.Error($"Could not fill in the host name: {message}");
}
dialog.ClickDialogButtonById(hDialog, (int)DialogButton.Ok, out _);
```

`SetControlText` writes the text directly, as if typed and committed, and reads it
back. Most applications only look at the box when the dialog is confirmed, so
confirm it (above) rather than expecting the app to react as you type. An empty
string clears the box; `null` is refused.

It works on the editable part of a drop-down too. A Font dialog's size box accepts
`"13"` even though `13` is not in its list.

**Sensitive text is never echoed.** A failure message describes what went wrong, but
never includes the text that was being set, so a password typed into a login dialog
does not end up in a log.

If the text does not stick - the box is read-only, length-limited, or reformats what
it is given - `SetControlText` returns `false` and says so.

### Reading text back

`GetControlText` now reads edit boxes and drop-downs in *other applications*. It
previously returned an empty string for them, because `GetWindowText` deliberately
does not read another process's edit box; it now asks the control directly.

```csharp
string current = dialog.GetControlText(hostBox.Handle);
```

## Check a box, or select a radio button

**Scenario:** A print or export dialog has an "Include headers" check box that must be
on, and an orientation choice that must be Landscape.

```csharp
dialog.SetControlChecked(includeHeaders, true, out _);    // clicks only if it is not already checked
dialog.SetControlChecked(landscape, true, out _);         // selects the radio button
```

It is safe to call whatever the current state is: it reads the state first and clicks
only if a change is needed. To only look, without changing anything:

```csharp
if (dialog.TryGetControlCheckState(includeHeaders, out ControlCheckState state, out _)
    && state == ControlCheckState.Checked)
{
    // already on
}
```

`ControlCheckState` is `Unchecked`, `Checked` (also the selected radio button), or
`Indeterminate` (the third state of a three-state check box).

It *clicks* rather than setting the state directly, so the application's own click
handling runs and it finds out. Setting the state directly changes the box on screen
without telling the app, which then behaves as if nothing changed.

Two things to know:

- **A radio button cannot be unchecked directly.** Select another button in its group
  instead. `SetControlChecked(radio, false, ...)` on a selected radio button returns
  `false` with a message saying so (on one that is already off it simply succeeds).
- **Some applications draw their own check boxes.** WinForms does, for example, so
  there is no Win32 state to read or click reliably, and these methods refuse with a
  message pointing at `UIAutomationUtils` (`IsToggled` / `Toggle`), which is the right
  tool for them. The same is true of any button that is not a check box or radio button.

## Choose an item from a drop-down

**Scenario:** A Font dialog's style must be Bold.

```csharp
dialog.FindButtonById(hDialog, out IntPtr styleList, 1137);
if (!dialog.SelectComboItem(styleList, "Bold", out string message))
{
    Logger.Warn(message);   // lists the items that are actually there
}
```

Items are matched by their text, ignoring case; `exactMatch: false` chooses the first
item that *contains* the text. After selecting, the dialog is told the selection
changed - it is not enough to move the highlight, because a dialog that reacts to the
choice (an Open dialog re-filtering its file list) would carry on as if nothing
happened. It is safe to call when the item is already selected.

A `ComboBoxEx32` (the address-bar style drop-down) answers to its inner `ComboBox`;
pass that handle. Lists that draw their own items without storing text cannot be
matched by text.

## Finding the handle you need

```csharp
foreach (var c in dialog.ListDialogControls(hDialog))
    Console.WriteLine($"id={c.Id,5}  {c.ClassName,-16}  '{c.Text}'");
```

`FindButtonById` finds a direct child by control ID (despite the name it is not limited
to buttons). Controls inside a host window - as in a Save As dialog - are only reached
through `ListDialogControls`, which lists nested controls too.
