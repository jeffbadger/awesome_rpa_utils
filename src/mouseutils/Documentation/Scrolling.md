# Wheel & Scrolling

Vertical and horizontal scroll-wheel simulation.

Every method here returns `bool` (success) with an `out string message` explaining
why on failure — none of them throw. The examples below discard `message` via
`out _` where the failure reason isn't needed.

## `Scroll(int wheelDelta)`

**Scenario:** Scrolling a chat transcript up by a precise, non-standard amount
to reveal a message that appeared two-thirds of a notch above the fold.

```csharp
mouse.Scroll(80, out _); // less than one full notch (120), fine-grained nudge up
```

## `ScrollUp()` / `ScrollDown()`

**Scenario:** Paging through a long dropdown list one wheel notch at a time
until the target item becomes visible, checking after each notch.

```csharp
while (!IsOptionVisible("Ohio"))
{
    mouse.ScrollDown(out _);
    Thread.Sleep(100);
}
```

## `ScrollUp(int notches)` / `ScrollDown(int notches)`

**Scenario:** Jumping to roughly the middle of a long PDF viewer in one motion
instead of looping notch-by-notch, when the automation already knows how far to go.

```csharp
mouse.ScrollDown(25, out _); // jump ~25 notches down the document
```

## `ScrollHorizontal(int wheelDelta)`

**Scenario:** Nudging a wide Gantt chart sideways by a small amount to align a
task bar with a fixed click target, without a full horizontal wheel notch.

```csharp
mouse.ScrollHorizontal(-40, out _); // small nudge left
```

## `ScrollRight()` / `ScrollRight(int notches)` and `ScrollLeft()` / `ScrollLeft(int notches)`

**Scenario:** Navigating a wide spreadsheet that extends past column Z by
scrolling right several notches to reach column AK, then back left to return
to column A before starting the next row.

```csharp
mouse.ScrollRight(10, out _); // move right to reach column AK
// ... read/write cells ...
mouse.ScrollLeft(10, out _);  // return to column A
```

## `ScrollHorizontalAt(int x, int y, int wheelDelta)`

**Scenario:** A dashboard has several independently-scrollable horizontal
carousels; the automation must scroll the *second* carousel specifically, which
requires the cursor to be over that control (wheel messages target whatever is
under the cursor) rather than wherever it happened to be left.

```csharp
mouse.ScrollHorizontalAt(x: 700, y: 480, wheelDelta: 120, out _); // scroll the carousel under (700,480)
```

## Checking why a scroll failed

```csharp
if (!mouse.ScrollDown(out string message))
{
    Logger.Warn($"Scroll failed: {message}");
}
```
