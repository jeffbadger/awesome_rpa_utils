# Pega Robot Studio API Usability Review

## Purpose

This review evaluates whether each public utility method is practical to place,
configure, and connect on a Pega Robot Studio automation surface. It focuses on
API shape rather than implementation correctness.

## Classification

- **Direct:** Every required value can reasonably be entered as a constant or
  supplied by a standard scalar Pega variable (`string`, integer, Boolean,
  floating-point value, or a designer-selectable enum).
- **Chainable:** A non-scalar input cannot reasonably be typed, but the same or a
  companion utility provides a natural producer whose output can connect directly.
- **Proxy friction:** Pega can represent the object or collection through a proxy,
  but the automation requires extra type configuration, property extraction, or
  looping. The value is usable only after its producing data port executes.
- **Adapter needed:** There is no natural producer or scalar alternative on the
  public surface, making routine use impractical without a custom variable,
  script, proxy construction, or additional .NET component.

`IntPtr` is not automatically a defect. It is **chainable** when a find/wait/from-
point method produces the handle used by the consumer. A method is downgraded
when it requires a handle but offers no discoverable producer for its expected
kind of object.

## Types included in the review

- `IntPtr` and native handles.
- Arrays, `params` arrays, lists, dictionaries, and other collections.
- Framework objects such as `AutomationElement`, `Encoding`, `Point`, and
  `Rectangle`.
- Repository result objects such as `CommandResult`, `OcrResult`, and `WinEventData`.
- Complex outputs that require proxies even when all inputs are scalar.
- Overload and optional-parameter shapes that expose difficult ports for common
  use cases.

Enums are treated as direct unless Robot Studio testing shows that a particular
enum is not rendered as a selectable constant.

## Initial cross-utility risk map

| Utility | Likely friction |
|---|---|
| MouseUtils | `IntPtr` window operations are chainable; `Point`/`Rectangle` outputs require proxies. |
| ScreenCaptureUtils | Window capture needs a chained handle; other public ports are scalar. |
| KeyboardUtils | `params VirtualKey[]` is difficult to construct on the design surface. |
| WindowUtils | Handles are naturally produced; `List<IntPtr>` results require collection proxies/loops. |
| OcrUtils | `OcrResult`, `Rectangle`, and language lists require proxies; plain-text alternatives reduce the impact. |
| DialogUtils | Handles are naturally produced; handle/control collections require proxy work. |
| UIAutomationUtils | `AutomationElement` is intentionally chain-based, but nearly the entire utility depends on object proxies; element lists add more friction. |
| CommandLineUtils | `IDictionary<string,string>`, `Encoding`, and `string[]` inputs have no simple Pega producer; `CommandResult` requires a proxy. |
| ServiceUtils | Control methods are scalar; service-name list results require proxies/loops. |
| WinEventUtils | `WinEventData` and `WinEventData[]` outputs require proxies; JSON dump methods provide a partial scalar escape hatch. |
| LocalQueueUtils | All ports are scalar; JSON and bulk-ingestion methods deliberately eliminate collection-proxy construction. |
| StackUtils | Instance-local LIFO work with scalar ports, mixed-kind routing, atomic bulk bridges, and explicit empty results. |
| DataContractUtils | Typed named values with design-time JSON preload, staged initialization, strict scalar access, and optional DataTable bridges. |

## Review order

1. [MouseUtils](MouseUtils-pega-usability-review.md)
2. [ScreenCaptureUtils](ScreenCaptureUtils-pega-usability-review.md)
3. [KeyboardUtils](KeyboardUtils-pega-usability-review.md)
4. [WindowUtils](WindowUtils-pega-usability-review.md)
5. [OcrUtils](OcrUtils-pega-usability-review.md)
6. [DialogUtils](DialogUtils-pega-usability-review.md)
7. [UIAutomationUtils](UIAutomationUtils-pega-usability-review.md)
8. [CommandLineUtils](CommandLineUtils-pega-usability-review.md)
9. [ServiceUtils](ServiceUtils-pega-usability-review.md)
10. [WinEventUtils](WinEventUtils-pega-usability-review.md)
11. [StackUtils](StackUtils-pega-usability-review.md)
12. [DataContractUtils](DataContractUtils-pega-usability-review.md)

Each utility's detailed findings are in its own file in this folder.

## Completed review and remediation priority

All twelve listed utility reviews are complete, and every item below has since
been implemented, rejected with a documented reason, or deliberately deferred
— see each item's own component doc for the final decision and status marker.
This list is kept as a historical record of the original remediation order,
not a live backlog.

1. **Done.** WinEventUtils handle correctness: replaced the 32-bit
   `WinEventData.Hwnd`, exposed properties, kept single-event methods as
   `WinEventData`, and added JSON adapters for multi-event output.
2. **Done.** CommandLineUtils scalar API: flattened `CommandResult` and added
   scalar adapters for environment variables, encoding, and shell allowlists.
3. **Done.** KeyboardUtils combo adapter: `PressKeyCombo`'s existing `params
   VirtualKey[]` was confirmed to already call like a fixed-arity scalar
   method from Robot Studio, so no separate adapter method was needed.
4. **Done.** Primitive geometry outputs: added integer coordinate overloads
   for MouseUtils, WindowUtils, OcrUtils, and UIAutomationUtils.
5. **Mostly done, one reconsidered.** Collection adapters: added first-match,
   JSON, or count/index alternatives for window, UIA, OCR-language, and
   service enumeration. Dialog enumeration (`FindAllDialogs`,
   `ListDialogControls`) was revisited during DialogUtils' own review and
   the proxy/loop shape was judged acceptable as-is - see that component's
   review, decisions 2-3.
6. **Done.** Clipboard and result semantics: internalized ScreenCaptureUtils
   STA handling, removed its throwing overload entirely, and separated
   operation success from normal false states/timeouts across utilities.
7. **Mixed - see below.** Designer-friendly enums:
   - **Done:** wrapped external service status (`ServiceStatus`) and added
     repository-owned enum overloads for string event policies/names
     (`WinEventName`, `WinEventOverflowPolicy`).
   - **Rejected, documented.** Exposing `DialogButton` directly on
     `ClickDialogButtonById` was reconsidered and explicitly declined in
     [DialogUtils' own review](DialogUtils-pega-usability-review.md) -
     callers needing a standard button already cast from the enum, and an
     `int` parameter covers the rest without an extra overload.
   - **Deferred, out of scope.** Verifying `ModifierKeys` flags-enum
     selection on the actual Robot Studio design surface was left out of
     scope for the KeyboardUtils pass - see that component's own review.

The individual records are located in this folder as
`<Utility>-pega-usability-review.md`.
