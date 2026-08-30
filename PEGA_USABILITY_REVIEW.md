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
- Repository result objects such as `CommandResult`, `OcrResult`, and `EventData`.
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
| EventUtils | `EventData` and `EventData[]` outputs require proxies; JSON dump methods provide a partial scalar escape hatch. |

## Review order

1. MouseUtils
2. ScreenCaptureUtils
3. KeyboardUtils
4. WindowUtils
5. OcrUtils
6. DialogUtils
7. UIAutomationUtils
8. CommandLineUtils
9. ServiceUtils
10. EventUtils

Each utility records its detailed findings in its own project directory.

## Completed review and remediation priority

All ten utility reviews are complete. Recommended implementation order:

1. **EventUtils handle correctness:** replace the 32-bit `EventData.Hwnd`, expose
   properties, and add flattened/JSON single-event methods.
2. **CommandLineUtils scalar API:** flatten `CommandResult` and add scalar adapters
   for environment variables, encoding, and shell allowlists.
3. **KeyboardUtils combo adapter:** replace the required `VirtualKey[]` design-
   surface path with fixed-arity scalar combo methods.
4. **Primitive geometry outputs:** add integer coordinate overloads for MouseUtils,
   WindowUtils, OcrUtils, and UIAutomationUtils.
5. **Collection adapters:** add first-match, JSON, or count/index alternatives for
   window, dialog, UIA, OCR-language, and service enumeration.
6. **Clipboard and result semantics:** internalize ScreenCaptureUtils STA handling,
   hide its throwing overload, and separate operation success from normal false
   states/timeouts across utilities.
7. **Designer-friendly enums:** expose `DialogButton` directly, wrap external
   service status, verify modifier flags, and replace string policies/event names
   where useful.

The individual records are located at
`src/<utility>/PEGA_USABILITY_REVIEW.md`.
