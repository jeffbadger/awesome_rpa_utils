# Enumeration & Lookup

## Find a window by its title (substring match)

```csharp
IntPtr notepad = window.FindWindowByTitle("Notepad", exactMatch: false);
```

## Find a window by exact title

```csharp
IntPtr exact = window.FindWindowByTitle("Untitled - Notepad");
```

## Find all windows belonging to a process

```csharp
var process = System.Diagnostics.Process.Start("notepad.exe");
var handles = window.FindWindowsByProcessId(process.Id);
```

## List every top-level window currently open

```csharp
foreach (var hWnd in window.GetTopLevelWindows())
{
    Console.WriteLine(window.GetWindowTitle(hWnd));
}
```

## Get the currently active window

```csharp
IntPtr active = window.GetForegroundWindow();
```
