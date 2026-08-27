# Read Text

`GetDialogText` reads a dialog's message body by returning the first `Static`-class child with non-empty text, skipping any empty-text `Static` controls (such as an icon). This ensures it finds the message text correctly even on MessageBox dialogs with icons.

## Log an error message before dismissing it

```csharp
string message = dialog.GetDialogText(hWnd);
Console.WriteLine($"Dialog said: {message}");
dialog.ClickDialogButton(hWnd, DialogButton.Ok);
```

## Read the dialog's own title bar text

```csharp
string title = dialog.GetControlText(hWnd);
```
