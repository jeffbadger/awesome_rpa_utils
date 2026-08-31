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
| `GetStructuredTextFromRegion` | Proxy friction; JSON overload added | `OcrResult` still requires a proxy for .NET consumers who want it, but `GetStructuredTextFromRegionAsJson` now returns the same lines/words/bounds as a single JSON string for Pega workflows. |
| `FindTextLocation` | Direct scalar overload added | Added an overload returning `foundLeft`/`foundTop`/`foundWidth`/`foundHeight` as integers; the `Rectangle` overload remains for callers with a proxy. |
| `GetAvailableLanguages` | Direct scalar overload added | Added `GetAvailableLanguagesDelimited`, returning the same tags as one delimited string; the `List<string>` overloads remain for .NET/advanced consumers. |
| `TryGetAvailableLanguages` | Direct scalar overload added | Same as above — `GetAvailableLanguagesDelimited` wraps this method and joins its list. |
| `WaitForTextToAppear` | Direct, disambiguated | Added a `timedOut`-output overload so the automation can branch on timeout vs. execution failure without a null-message test; the original `message`-only overload remains and delegates to it. |

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

1. **Done.** Added scalar coordinate outputs for `FindTextLocation`
   (`out int foundLeft, foundTop, foundWidth, foundHeight`).
2. **Done.** Added `GetStructuredTextFromRegionAsJson`, serializing the same
   lines/words/bounds `GetStructuredTextFromRegion` returns to a JSON string.
3. **Done.** Added `GetAvailableLanguagesDelimited`, joining the installed tags
   into one delimited string (default comma); the list APIs remain for
   advanced consumers.
4. **Done.** Added a `timedOut`-output overload of `WaitForTextToAppear` so the
   automation can branch on timeout vs. execution failure without a
   null-message test.

## Verdict

Plain-text OCR was already directly usable. Positioned and structured OCR —
previously proxy-accessible but unnecessarily difficult — now have scalar/JSON
overloads alongside their object-returning originals: `FindTextLocation`'s
scalar-coordinate overload, `GetStructuredTextFromRegionAsJson`,
`GetAvailableLanguagesDelimited`, and `WaitForTextToAppear`'s `timedOut`
overload. All four recommended changes are implemented; the object/list/
Rectangle-returning overloads remain for .NET consumers with a proxy.

## Addendum: naming ambiguity fix

The additive overloads above solved usability, but two of them introduced a
new problem: two same-named methods whose Pega-visible (non-`out`)
parameter lists became identical, differing only in an `out` parameter's
type or presence. C# overload resolution handles this fine, but Pega Robot
Studio's designer surface cannot disambiguate two overloads by `out`
parameter type/shape alone - both entries in the designer's method picker
would look the same.

Two method groups in this component had this problem: `FindTextLocation` and
`WaitForTextToAppear`. In each case, one overload takes the same non-`out`
inputs (`searchText`/`expectedText` plus the region coordinates) as the
other, differing only in returning a `Rectangle` via a single `out`
parameter (vs. scalar `out int`s) or missing the `timedOut` disambiguator.

Fixed by renaming the less-disambiguated overload (the one without the
extra output, or the one returning a struct instead of scalars) rather than
the newer, more Pega-usable one, following this repository's convention of
breaking changes over compatibility shims:

| Old name | New name | Reason |
|---|---|---|
| `FindTextLocation(string, int, int, int, int, out Rectangle, out string)` | `FindTextLocationAsRectangle` | Return type, not an extra output, was the only difference from the scalar overload |
| `WaitForTextToAppear(int, int, int, int, string, int, int, out string)` | `WaitForTextToAppearSimple` | Missing the `timedOut` disambiguator |

The plain (un-suffixed) name in each pair now belongs to the overload with
the extra disambiguating output or the scalar coordinates - the one most
useful to a Pega automation - per the naming convention documented in the
component `README.md`.
