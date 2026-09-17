# ScreenCaptureUtils Documentation

Real-world usage examples for every method on the `ScreenCaptureUtils`
component, organized by the same categories used in the source code and the
top-level [README](../README.md).

- [Core Capture](CoreCapture.md) — screen/region/window/clipboard capture and evidence trails
- [Verification & Comparison](VerificationAndComparison.md) — hashing, change-detection, and baseline diffing
- [Annotation & Redaction](AnnotationAndRedaction.md) — highlighting, arrows, and PII redaction on saved screenshots

All examples assume a `ScreenCaptureUtils` instance named `screenCapture`, as
it would appear dropped onto a Pega Robot Studio automation's design surface
(or instantiated directly: `var screenCapture = new ScreenCaptureUtils();`).

**Coordinate spaces differ by category** — Core Capture and Verification &
Comparison methods take absolute screen pixels; Annotation & Redaction
methods take pixels local to the saved image file. See
[Annotation & Redaction](AnnotationAndRedaction.md) for the conversion when
you need to annotate the exact spot a screen capture just wrote.
