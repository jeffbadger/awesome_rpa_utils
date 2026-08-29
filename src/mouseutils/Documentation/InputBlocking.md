# Input Blocking (Attended Sessions)

Temporarily locking out the operator's real keyboard/mouse input so a critical
sequence of synthetic clicks cannot be interrupted or interleaved with.

`BlockUserInput` returns `bool` (success) with an `out string message` — it
never throws; `UnblockUserInput` was already never-throw and is unchanged.

## `BlockUserInput()` / `UnblockUserInput()`

**Scenario:** An attended bot is about to submit a financial transaction using
a sequence of five precisely-timed clicks. If the operator moves the mouse or
clicks anything mid-sequence, the transaction could be submitted twice or to
the wrong account. The automation locks out real input for the few seconds the
sequence takes, then always unlocks it.

```csharp
if (!mouse.BlockUserInput(out string message))
{
    Logger.Warn($"Could not block input, aborting transaction: {message}");
    return;
}
try
{
    mouse.ClickAt(300, 400, MouseButton.Left, out _);  // select account
    mouse.ClickAt(600, 400, MouseButton.Left, out _);  // select "Transfer"
    mouse.ClickAt(600, 460, MouseButton.Left, out _);  // confirm amount field
    mouse.ClickAt(600, 520, MouseButton.Left, out _);  // "Submit"
}
finally
{
    // Always unblock, even on failure — UnblockUserInput is safe to call
    // even when nothing is currently blocked.
    mouse.UnblockUserInput();
}
```

Note that the operator can always break the block with Ctrl+Alt+Del as a
Windows safety hatch, and the block requires an interactive, non-secure desktop.
