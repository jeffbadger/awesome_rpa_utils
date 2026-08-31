# Child Windows

`GetChildWindows` returns `List<IntPtr>`, which needs a Pega collection proxy
and a loop to consume. Prefer `FindChildWindow` for the common single-match
case (below); reach for `GetChildWindows` only when the automation genuinely
needs every descendant.

## List every control inside a window

```csharp
foreach (var child in window.GetChildWindows(hWnd))
{
    Console.WriteLine(window.GetWindowClassName(child));
}
```

## Find Notepad's text-edit control

```csharp
IntPtr editControl = window.FindChildWindow(hWnd, title: null, className: "Edit");
```

## Find a specific labeled button

```csharp
IntPtr okButton = window.FindChildWindow(hWnd, title: "OK", className: "Button");
```
