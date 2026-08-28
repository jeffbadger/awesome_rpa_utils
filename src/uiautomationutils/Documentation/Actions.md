# Actions

## Click a button

```csharp
uia.Invoke(saveButton);
```

## Type into a text field without simulated keystrokes

```csharp
uia.SetValue(usernameField, "jbadger");
string current = uia.GetValue(usernameField);
```

## Check a checkbox only if it isn't already checked

```csharp
if (!uia.IsToggled(rememberMeCheckbox))
{
    uia.Toggle(rememberMeCheckbox);
}
```

## Expand a tree node and select a child

```csharp
uia.Expand(treeNode);
AutomationElement child = uia.FindByName(treeNode, "Documents");
if (child != null)
{
    uia.Select(child);
}
```
