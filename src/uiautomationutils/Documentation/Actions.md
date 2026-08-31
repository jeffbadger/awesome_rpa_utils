# Actions

Every method here returns `bool` with an `out string message` — never
throws, including for a null element or an unsupported pattern (e.g. calling
`Toggle` on a plain button). `IsToggled`/`IsSelected` keep their `bool` as
the actual state; `message` is only set for a real error.

## Click a button

```csharp
uia.Invoke(saveButton, out _);
```

## Type into a text field without simulated keystrokes

```csharp
uia.SetValue(usernameField, "jbadger", out _);
uia.GetValue(usernameField, out string current, out _);
```

## Check a checkbox only if it isn't already checked

```csharp
if (!uia.IsToggled(rememberMeCheckbox, out _))
{
    uia.Toggle(rememberMeCheckbox, out _);
}
```

## Expand a tree node and select a child

```csharp
uia.Expand(treeNode, out _);
if (uia.FindByName(treeNode, "Documents", out AutomationElement child, out _))
{
    uia.Select(child, out _);
}
```

## Checking why an action failed

```csharp
if (!uia.Invoke(saveButton, out string message))
{
    Logger.Warn($"Invoke failed: {message}");
}
```

## Check toggled/selected state without a null-message test

`IsToggled`/`IsSelected` have a `querySucceeded`-output overload that makes
the existing null-message convention explicit as a Boolean:

```csharp
if (!uia.IsToggled(rememberMeCheckbox, out bool querySucceeded, out string message))
{
    if (!querySucceeded)
        Logger.Error($"Could not check the checkbox state: {message}");
    // else: querySucceeded is true and the checkbox is genuinely unchecked.
}
```
