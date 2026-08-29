# Cursor Highlight

A flashing ring drawn around the cursor for demos, recordings, and
attended-RPA operator guidance.

`FlashCursorHighlight` returns `bool` (success) with an `out string message`
— it never throws. Note the `out message` parameter comes before the
optional `radius`/`flashes`/`flashMs`/`ringWidth`/`colorRef` parameters, since
an `out` parameter can't follow one with a default value in C#.

## `FlashCursorHighlight(out string message, int radius = 30, int flashes = 3, int flashMs = 200, int ringWidth = 3, int colorRef = 0x0000FF)`

**Scenario:** During a live training demo, the presenter wants viewers to
notice exactly where the automation is about to click before it happens, since
a plain cursor move is easy to miss on a projector.

```csharp
mouse.MoveTo(targetX, targetY, out _);
mouse.FlashCursorHighlight(out _); // default: 3 red flashes, 30px radius
mouse.LeftClick(out _);
```

**Scenario:** A QA recording needs a larger, longer-lasting, green highlight so
reviewers watching the recorded video at a distance can clearly see each click
target across a series of five form fields.

```csharp
foreach (var field in formFields)
{
    mouse.MoveTo(field.X, field.Y, out _);
    mouse.FlashCursorHighlight(out _, radius: 45, flashes: 2, flashMs: 350, ringWidth: 4, colorRef: 0x00FF00);
    mouse.LeftClick(out _);
}
```

> The ring uses XOR drawing so it erases itself exactly, but it won't draw over
> exclusive fullscreen (DirectX) applications, and a window repaint while the
> ring is visible can leave artifacts the erase pass can't clean up.
