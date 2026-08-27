# State Query & Modifiers

## Guard an action on a modifier being held

```csharp
if (keyboard.IsModifierDown(ModifierKeys.Control))
{
    // e.g. skip a confirmation dialog when the operator is holding Ctrl
}
```

## Read the full set of active modifiers

```csharp
ModifierKeys active = keyboard.GetActiveModifiers();
if ((active & ModifierKeys.Shift) != 0)
{
    // ...
}
```

## Poll for a specific key being released before continuing

```csharp
while (keyboard.IsKeyDown(VirtualKey.Escape))
{
    System.Threading.Thread.Sleep(50);
}
```
