# Common Tasks

Start here if you just want to accomplish one of three everyday things —
click a button, read or set a text field, or pick an item from a combo
box — without first learning UI Automation's `Find`/element-proxy model.
Every example below takes a window handle directly (typically from
`WindowUtils.FindWindowByTitle` or `DialogUtils`) and does the rest in one
call. See [OneShot](OneShot.md) for the full one-shot method reference, and
[Find](Find.md)/[Actions](Actions.md) for the composable API these build on
when you need more control (searching more than once, or acting on an
element you already have).

## Click a button

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.InvokeByName(hWnd, "Save", out string message);
```

Works on any control whose text you can read on screen — not just buttons —
as long as it supports being invoked (`InvokePattern`).

## Read or set a text field

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.SetValueByName(hWnd, "Username", "jbadger", out string message);
uia.GetValueByName(hWnd, "Username", out string current, out message);
```

`"Username"` here is the field's visible name — usually its label text, or
its own text if it has none — not an internal identifier.

## Select an item in a combo box

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
uia.SelectListItemByName(hWnd, "Country", "United States", out string message);
```

This finds the combo box by its own name (`"Country"`), opens it if needed,
then finds and selects the item inside it by name (`"United States"`). The
same method works for a list box or tree, not just a combo box.

## Don't know what name to type?

If you're not sure what a control's visible name is (or it doesn't have
one), dump every child of the window first:

```csharp
IntPtr hWnd = window.FindWindowByTitle("Settings", exactMatch: false);
if (uia.GetChildrenSummaryJsonFromWindowHandle(hWnd, out string json, out string message))
{
    Logger.Info(json); // [{"name":"...","automationId":"...","className":"...","controlType":"...","bounds":{...}}]
}
```

The `name` field in that output is exactly what `InvokeByName`/
`SetValueByName`/`GetValueByName`/`SelectListItemByName` expect.

## Checking why a call failed

```csharp
if (!uia.InvokeByName(hWnd, "Save", out string message))
{
    Logger.Warn($"Could not click Save: {message}");
}
```
