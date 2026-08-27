# Press / Hold / Combo

## Press a single key

```csharp
keyboard.PressKey(VirtualKey.Enter);
```

## Select-all and delete

```csharp
keyboard.PressKeyWithModifiers(VirtualKey.A, ModifierKeys.Control);
keyboard.PressKey(VirtualKey.Delete);
```

## A three-key combo (Ctrl+Shift+Esc — open Task Manager)

```csharp
keyboard.PressKeyCombo(VirtualKey.Control, VirtualKey.Shift, VirtualKey.Escape);
```

## Hold a key down for a duration

```csharp
// Holds Tab down for 2 seconds, e.g. to trigger an app's "cycle windows" overlay.
keyboard.HoldKey(VirtualKey.Tab, 2000);
```

## Manual down/up pairing (for a press that spans other actions)

```csharp
keyboard.KeyDown(VirtualKey.Shift);
try
{
    // ... click-drag a selection while Shift is held, via MouseUtils ...
}
finally
{
    keyboard.KeyUp(VirtualKey.Shift);
}
```
