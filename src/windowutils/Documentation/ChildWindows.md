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

## Find a WinForms control whose class name changes every run

WinForms generates control class names such as
`WindowsForms10.BUTTON.app.0.141b42a_r14_ad1`, and the suffix is different on
every run, so `FindChildWindow`'s exact match can never find it. Match the
stable part with a regular expression instead:

```csharp
if (window.TryFindChildWindowByRegex(hWnd, titlePattern: "^&?Submit$", classNamePattern: @"^WindowsForms10\.BUTTON\.",
        out IntPtr submit, out string message))
{
    // submit is the button's handle
}
else if (message != null)
{
    Logger.Error($"Could not search: {message}");   // stale parent handle, no pattern, or an invalid one
}
```

Either pattern may be `null` to skip that axis, but at least one is required.
Descendants are searched recursively, and matching, case-sensitivity, and the
one-second-per-match cap work exactly as described for `TryFindWindowByRegex` in
[Enumeration & Lookup](EnumerationAndLookup.md). Unlike `FindChildWindow`, a
parent handle that has gone stale is reported (`message` is set) rather than read
as "no match".
