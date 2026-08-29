# Press / Hold / Combo

Every method here returns `bool` (success) with an `out string message` explaining
why on failure — none of them throw. The examples below discard `message` via `out _`
where the failure reason isn't needed.

## Press a single key

```csharp
keyboard.PressKey(VirtualKey.Enter, out _);
```

## Select-all and delete

```csharp
keyboard.PressKeyWithModifiers(VirtualKey.A, ModifierKeys.Control, out _);
keyboard.PressKey(VirtualKey.Delete, out _);
```

## A three-key combo (Ctrl+Shift+Esc — open Task Manager)

`PressKeyCombo` takes its `out message` parameter before the `params` array, since a
`params` parameter must be last in C#:

```csharp
keyboard.PressKeyCombo(out _, VirtualKey.Control, VirtualKey.Shift, VirtualKey.Escape);
```

## Hold a key down for a duration

```csharp
// Holds Tab down for 2 seconds, e.g. to trigger an app's "cycle windows" overlay.
keyboard.HoldKey(VirtualKey.Tab, 2000, out _);
```

## Manual down/up pairing (for a press that spans other actions)

```csharp
keyboard.KeyDown(VirtualKey.Shift, out _);
try
{
    // ... click-drag a selection while Shift is held, via MouseUtils ...
}
finally
{
    keyboard.KeyUp(VirtualKey.Shift, out _);
}
```

## Checking why a press failed

```csharp
if (!keyboard.PressKey(VirtualKey.Enter, out string message))
{
    Console.WriteLine($"Key press failed: {message}");
}
```
