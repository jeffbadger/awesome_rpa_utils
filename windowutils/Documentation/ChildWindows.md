# Child Windows

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
