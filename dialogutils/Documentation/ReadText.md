# Read Text

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
