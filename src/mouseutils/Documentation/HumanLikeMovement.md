# Human-like Movement

Cursor movement along a randomized Bezier curve with ease-in-out timing,
instead of the linear paths used by `SmoothMoveTo`.

Every method here returns `bool` (success) with an `out string message` —
none of them throw. Note `out message` comes before the optional `durationMs`
parameter, since an `out` parameter can't follow one with a default value.

## `MoveMouseBezier(int x, int y, out string message, int durationMs = 500)`

**Scenario:** An automated QA smoke test drives a web app that has bot-detection
middleware flagging perfectly straight, constant-velocity cursor paths as
non-human traffic and throttling the session. Switching those moves to the
Bezier mover — which varies its curve, timing, and jitter on every call —
lets the test suite run without tripping the detector.

```csharp
mouse.MoveMouseBezier(x: 850, y: 420, out _); // default 500 ms, natural curve
mouse.LeftClick(out _);
```

**Scenario:** A product demo video needs the cursor to glide unhurriedly and
naturally across the screen to a call-to-action button, rather than snapping
there instantly, so the automation uses a longer duration for a slower, more
deliberate-looking move.

```csharp
mouse.MoveMouseBezier(x: ctaButtonX, y: ctaButtonY, out _, durationMs: 1200);
mouse.LeftClick(out _);
```

> The endpoint is always exact — jitter is only applied to intermediate steps —
> so the final click still lands precisely on the intended target regardless of
> how the path wandered to get there.

## `BezierClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)`

**Scenario:** The same bot-detection-sensitive web app from above needs a
one-line replacement for `ClickAt` throughout an existing script, so every
click glides in on a natural curve instead of jumping straight there.

```csharp
mouse.BezierClickAt(x: 850, y: 420, MouseButton.Left, out _);
```

## `BezierDoubleClickAt(int x, int y, MouseButton button, out string message, int durationMs = 500)`

**Scenario:** Opening a file in a desktop grid during a recorded product demo,
where the double-click itself — not just the approach — should look like a
person did it, rather than an instantaneous double-click at the destination.

```csharp
mouse.BezierDoubleClickAt(x: 340, y: 512, MouseButton.Left, out _, durationMs: 800);
```

## `BezierDragAndDrop(int startX, int startY, int endX, int endY, out string message, int durationMs = 500)`

**Scenario:** A QA smoke test needs to drag a card between Kanban columns in
the same bot-detection-sensitive app, where a perfectly straight drag path
(as produced by the plain `DragAndDrop`) is one of the signals the anti-bot
middleware flags.

```csharp
mouse.BezierDragAndDrop(startX: 220, startY: 300, endX: 620, endY: 300, out _, durationMs: 700);
```
