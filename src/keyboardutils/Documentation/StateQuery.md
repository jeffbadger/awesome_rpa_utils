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

`IsModifierDown(ModifierKeys.None)` returns `false` — no modifier is "held" when
none is requested.

## Poll for a specific key being released before continuing

```csharp
while (keyboard.IsKeyDown(VirtualKey.Escape))
{
    System.Threading.Thread.Sleep(50);
}
```

## Check Caps Lock, Num Lock, and Scroll Lock before typing key by key

`IsCapsLockOn`, `IsNumLockOn`, and `IsScrollLockOn` report a lock key's toggle
state - on or off - not whether the key is being pressed at this instant (that
is `IsKeyDown`). Lock state is a classic cause of an automation that types the
wrong thing on one machine and not another: Caps Lock inverts the case of every
letter sent with `PressKey`, Num Lock off turns the numeric keypad's digits into
Home/End/arrows, and Scroll Lock on makes the arrow keys scroll the view instead
of moving the selection in Excel.

```csharp
if (keyboard.IsCapsLockOn())
{
    keyboard.PressKey(VirtualKey.CapsLock, out _);   // turn it off before typing
}
```

The state is system-wide and includes injected presses, so `PressKey` on a lock key
flips it, as above. `TypeText` sends characters directly and is unaffected by
Caps Lock, so prefer it for plain text and reserve this check for key-by-key
typing and shortcuts. These are plain `bool` returns that cannot fail.

## Check which keyboard layout the target window is using

A key press names a physical key, so the character it produces depends on the
layout: the same key types different characters on US and German layouts, and on
US-International a quote is a dead key that combines with the next letter.
`GetKeyboardLayout` reads the layout the foreground window - where typed input
will go - is using:

```csharp
if (keyboard.GetKeyboardLayout(out string layoutName, out string layoutId, out string languageTag, out string message))
{
    if (layoutId != "00000409")   // 00000409 = US
    {
        Logger.Warn($"Expected the US layout but the window is using {layoutName} ({languageTag}).");
    }
}
else
{
    Logger.Error($"Could not read the keyboard layout: {message}");
}
```

The three outputs answer different questions:

| Output | Example | Use it to |
|---|---|---|
| `layoutName` | `US`, `United Kingdom`, `United States-International` | Log something a person can read. Empty where Windows records no name. |
| `layoutId` | `00000409`, `00020409`, `00000407` | Compare reliably. This is what tells US from US-International. |
| `languageTag` | `en-US`, `de-DE` | Check the language only. US and US-International are both `en-US`. |

Layouts are tracked per window, so the layout can differ from one window to the
next and from the layout shown as the machine default. To check a specific
window rather than the foreground one, pass its handle (for example from
`WindowUtils`) as `hWnd`:

```csharp
keyboard.GetKeyboardLayout(out _, out string layoutId, out _, out _, hWnd: notepadHandle);
```

It returns `false` with a message when there is no foreground window (for
example on a locked workstation) or `hWnd` is not a valid window. `TypeText` is
not affected by the layout, so prefer it for plain text.
