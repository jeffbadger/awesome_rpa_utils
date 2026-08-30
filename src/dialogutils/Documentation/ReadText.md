# Read Text

`GetDialogText` reads a dialog's message body by returning the first `Static`-class child with non-empty text, skipping any empty-text `Static` controls (such as an icon). This ensures it finds the message text correctly even on MessageBox dialogs with icons.

## Log an error message before dismissing it

```csharp
string message = dialog.GetDialogText(hWnd);
Console.WriteLine($"Dialog said: {message}");
dialog.ClickDialogButtonById(hWnd, (int)DialogButton.Ok, out _);
```

## Read the dialog's own title bar text

```csharp
string title = dialog.GetControlText(hWnd);
```

## List a dialog's controls when you don't know their IDs or text

Useful for discovering what's actually on a dialog before writing automation
against it — run this once against a target dialog and inspect the output.

```csharp
foreach (var control in dialog.ListDialogControls(hWnd))
{
    Console.WriteLine($"Id={control.Id}, Class={control.ClassName}, Text=\"{control.Text}\"");
}
```

## Highlight a control to confirm which one it is

Once you have a handle (e.g. from `ListDialogControls`), flash a rectangle around
it on screen to confirm you've got the right one before scripting against it.

```csharp
var controls = dialog.ListDialogControls(hWnd);
dialog.HighlightControl(controls[2].Handle, System.Drawing.Color.Red);
```
