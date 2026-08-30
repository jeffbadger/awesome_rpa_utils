# OcrUtils Pega API Usability Review

## Summary

All public inputs are Pega-friendly scalars. Basic OCR is directly usable because
the utility provides plain-string results. Friction begins when an automation
needs positions, lines, words, or installed-language enumeration: those APIs
return framework objects or nested collections.

## Method review

| Method or overload group | Rating | Assessment |
|---|---|---|
| `GetTextFromRegion` | Direct | Coordinates, language tag, text, message, and result are scalar. This is the preferred Pega surface for screen OCR. |
| `GetTextFromImageFile` | Direct | File path and optional BCP-47 tag are strings, and recognized text is returned as a string. No bitmap object escapes the component. |
| `GetStructuredTextFromRegion` | Proxy friction | `OcrResult` requires a proxy; its `Lines` contain `OcrLine` objects, whose `Words` and `Rectangle` properties require additional nested proxies and loops. Add scalar accessors or a JSON/tabular representation for Pega workflows that need structure. |
| `FindTextLocation` | Proxy-friction output | All inputs are scalar, but `Rectangle` must be proxied before its coordinates can be used for a click or capture. Add an overload returning left, top, width, and height as integers. |
| `GetAvailableLanguages` | Proxy friction | `List<string>` requires collection proxy configuration and iteration. Returning an empty list also hides query failure. Add a delimited/JSON string or scalar count/index accessors. |
| `TryGetAvailableLanguages` | Proxy friction | The Boolean/message pattern exposes failure, but the useful output remains `List<string>`. It does not solve the Pega port problem. |
| `WaitForTextToAppear` | Direct | Every port is scalar. `false` represents either timeout or operational failure, so the automation must inspect `message`; an explicit status or `timedOut` output would simplify branching. |

## Structured-result adapter options

The existing `OcrResult` should remain available for .NET consumers. For Pega,
one or more of these scalar surfaces would avoid nested proxy construction:

1. `FindTextLocation` with integer coordinate outputs.
2. A JSON result containing text, lines, words, and numeric bounds.
3. Indexed access methods that accept line/word indexes and return text and bounds
   through scalar outputs.

JSON is the broadest escape hatch, while primitive `FindTextLocation` outputs
cover the most common OCR-to-click workflow with the least designer work.

## Recommended changes

1. Add scalar coordinate outputs for `FindTextLocation`.
2. Add a structured-result JSON method or scalar indexed access API.
3. Add a scalar representation of installed language tags; retain the list APIs
   for advanced consumers.
4. Distinguish timeout from execution failure in `WaitForTextToAppear` without
   requiring a null-message test.

## Verdict

Plain-text OCR is directly usable and no caller must construct a complex input.
Positioned and structured OCR are technically proxy-accessible but unnecessarily
difficult. The highest-value adapter is a scalar-coordinate overload for
`FindTextLocation`.
