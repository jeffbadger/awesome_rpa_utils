# Annotation & Redaction

Marking up a screenshot that's already on disk — for pointing a reviewer at
what mattered, or permanently blacking out something that shouldn't be kept
in an audit trail. All three methods load, modify, and overwrite the image
file in place; pass a copy of the path first if the un-annotated original
needs to survive.

**Coordinates here are image-local pixels** — (0, 0) is the saved file's own
top-left corner, *not* a point on the screen. This is different from every
Core Capture / Verification method in this component, which all use
absolute screen pixels. The methods here operate purely on a file already on
disk; they have no way to know where on screen it was captured from.

## Converting screen coordinates to image coordinates

**Scenario:** A region was captured with `CaptureRegionToFile`, and the
automation now wants to highlight the exact same absolute screen area inside
that saved file — the coordinates need translating, not reusing directly.

```csharp
// The region that was captured:
const int captureLeft = 400, captureTop = 300, captureWidth = 300, captureHeight = 150;
screenCapture.CaptureRegionToFile(captureLeft, captureTop, captureWidth, captureHeight,
    @"C:\evidence\order_total.png", out _);

// Highlight the sub-rectangle at absolute screen (450, 330)-(600, 380) inside that file:
const int screenLeft = 450, screenTop = 330, screenRight = 600, screenBottom = 380;
screenCapture.DrawHighlightBox(
    @"C:\evidence\order_total.png",
    screenLeft - captureLeft, screenTop - captureTop,
    screenRight - captureLeft, screenBottom - captureTop,
    System.Drawing.Color.Red, out string message);
```

Passing `screenLeft`/`screenTop` straight in without subtracting the
capture's own origin compiles and runs without error — it just highlights
the wrong spot in the file, since any in-bounds rectangle is a valid one.

## `DrawHighlightBox(string imagePath, int left, int top, int right, int bottom, Color color)`

**Scenario:** A bug report screenshot needs a red box around the field that
had the wrong value, so a reviewer doesn't have to guess which one.

```csharp
screenCapture.CaptureWindowToFile(hWnd, @"C:\evidence\order_form.png", out _);
screenCapture.DrawHighlightBox(@"C:\evidence\order_form.png",
    120, 240, 340, 270,
    System.Drawing.Color.Red, out string message, 4);
```

Three overloads take the same box, differing only in how the color is
supplied — `colorRef` (`0x00BBGGRR`, matching MouseUtils' `colorRef`
parameters), RGB components (`red`/`green`/`blue`, each 0-255), or a
`System.Drawing.Color` as shown above. Pick whichever fits how the color is
chosen upstream (a hardcoded constant, a designer-selected `Color` proxy,
etc.) — all three produce an identical result for the same color.

## `DrawArrowToPoint(string imagePath, int x, int y, Color color)`

**Scenario:** Point at a specific button or icon rather than boxing a whole
region — useful when the target is small and a box would be ambiguous about
exactly which pixel matters.

```csharp
screenCapture.DrawArrowToPoint(@"C:\evidence\toolbar.png",
    88, 22, System.Drawing.Color.Blue, out string message, 50);
```

The arrow always approaches from the upper-left at a fixed 45-degree angle
with its tip landing exactly on `(x, y)` — there's no way to change the
approach direction. If the target sits near the image's top-left corner, the
shaft can run off the edge of the image; leave margin around targets that
are within `length` pixels of the top-left, or capture a slightly larger
region so the arrow has room to draw.

## `RedactRegion(string imagePath, int left, int top, int width, int height)`

**Scenario:** A screenshot captured for a support ticket happens to include
an account number or SSN that must not leave the building — black it out
permanently before the file is attached anywhere.

```csharp
screenCapture.CaptureWindowToFile(hWnd, @"C:\evidence\account_page.png", out _);
screenCapture.RedactRegion(@"C:\evidence\account_page.png", 200, 150, 180, 24, out string message);
// Defaults to solid black; pass a colorRef/RGB/Color overload for a different fill.
```

This uses an opaque solid fill, not a blur — deliberately: blurred text can
sometimes be partially reconstructed, whereas a solid fill permanently
discards the underlying pixels. There's no way to recover the original once
this has run, so redact a *copy* of the evidence file if the unredacted
original also needs to be retained somewhere less exposed:

```csharp
System.IO.File.Copy(@"C:\evidence\account_page.png", @"C:\evidence\account_page_redacted.png");
screenCapture.RedactRegion(@"C:\evidence\account_page_redacted.png",
    200, 150, 180, 24, out string message);
```
