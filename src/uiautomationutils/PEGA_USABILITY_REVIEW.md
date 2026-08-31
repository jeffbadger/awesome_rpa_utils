# UIAutomationUtils Pega API Usability Review

## Summary

`AutomationElement` is the utility's intentional chain type. It cannot be typed
or sensibly persisted, but root/handle/point methods produce it and find methods
continue the chain. This is usable through Pega object proxies, although nearly
every workflow depends on correct execution and data-port ordering. Element-list
results and `Rectangle` output add further proxy friction.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `GetRootElement` | Proxy-producing entry point | Requires no input but returns an `AutomationElement` proxy. It provides a valid starting point when no window handle is available, though desktop-wide descendant searches can be broad and slow. |
| `FromWindowHandle` | Chainable bridge | Converts a handle from WindowUtils/DialogUtils into an element proxy. This is the strongest scoped entry point; document the exact producer-data-port to consumer-data-port connection. |
| `FromPoint` | Proxy-producing entry point | Scalar coordinates produce an element proxy and bridge naturally from MouseUtils. |
| `FindByAutomationId`, `FindByName`, `FindByClassName`, `FindByControlType` | Chainable with proxy dependency | A parent element produces another element. Criteria are scalar or enum values. Pega must execute the parent-producing step before the proxy value is valid. `false` combines not-found with argument/UIA failure and requires inspecting `message`. |
| `FindAllByControlType` | Proxy friction; scalar alternatives added | Accepts an element proxy and returns `List<AutomationElement>`. `GetElementCount`/`GetElementAt` now provide indexed scalar access to the result; `GetChildren`'s JSON summary method covers the "just show me what's there" case. |
| `GetChildren` | Proxy friction; scalar/JSON alternatives added | `GetElementCount`/`GetElementAt` give scalar count/index access to the result; `GetChildrenSummaryJson` returns each child's name/automationId/className/controlType/bounds as a JSON string for designers without any element proxy at all. |
| `GetName`, `GetAutomationId`, `GetClassName`, `GetControlTypeName` | Chainable | Consume an element proxy and return strings. Once the element chain exists, these are straightforward. |
| `GetBoundingRectangle` | Chainable input; scalar overload added | The original `Rectangle` overload remains; a new overload returns `left`/`top`/`width`/`height` as integers directly, for designers without a `Rectangle` proxy. |
| `IsEnabled`, `IsOffscreen` | Chainable, disambiguated | The original ambiguous-Boolean overloads remain; each now has a `querySucceeded`-output overload making the existing null-message convention explicit as a Boolean. |
| `IsElementAvailable` | Chainable | A simple Boolean stale-reference check. The element still must originate from an earlier producer and should be re-found rather than stored long-term. |
| `Invoke`, `SetValue`, `GetValue`, `Toggle`, `Expand`, `Collapse`, `Select` | Chainable; one-shot alternatives added for the first three | Element input is intentionally produced by a find operation; other ports are scalar. Unsupported UIA patterns are reported through Boolean/message. `InvokeByAutomationId`/`SetValueByAutomationId`/`GetValueByAutomationId` now let a window-handle-scoped automation skip the intermediate element proxy for these three. |
| `IsToggled`, `IsSelected` | Chainable, disambiguated | The original ambiguous-Boolean overloads remain; each now has a `querySucceeded`-output overload. |
| `WaitForElementByAutomationId`, `WaitForElementByName` | Chainable, disambiguated | Each now has a `timedOut`-output overload distinguishing timeout from a real argument error without a null-message test; the original message-only overloads remain. |
| `HighlightElement` | Chainable, additional overloads added | The original `colorRef` overload remains; new RGB-component and `System.Drawing.Color` overloads avoid the non-obvious Win32 BGR-ordering hex entry. It still blocks the automation thread while flashing (unchanged). |

## Missing Pega-oriented surfaces

**Done.** Added `InvokeByAutomationId`, `SetValueByAutomationId`, and
`GetValueByAutomationId`, scoped by a window handle, so common automations
don't need to retain an `AutomationElement` proxy between steps. Also added
`GetChildrenFromWindowHandle` and `GetChildrenSummaryJsonFromWindowHandle` -
the same one-shot pattern applied to listing a window's children, which
directly answers the recurring "find a top-level window, then list its child
elements" workflow in a single call. These complement, rather than replace,
the composable element API.

**Done.** `UiControlType` now includes `DataGrid`, `DataItem`, `Group`,
`Header`, `Slider`, `Spinner`, and `ToolBar`, alongside the original 19
values.

## Recommended changes

1. **Done.** Added a scalar `left`/`top`/`width`/`height` overload of
   `GetBoundingRectangle`.
2. **Done.** Added `GetElementCount`/`GetElementAt` (indexed scalar access
   to a `List<AutomationElement>` from `GetChildren`/`FindAllByControlType`)
   and `GetChildrenSummaryJson` (a JSON summary of an element's children).
3. **Done.** Added `InvokeByAutomationId`, `SetValueByAutomationId`,
   `GetValueByAutomationId`, `GetChildrenFromWindowHandle`, and
   `GetChildrenSummaryJsonFromWindowHandle` - see "Missing Pega-oriented
   surfaces" above.
4. **Done.** Added `querySucceeded`-output overloads of `IsEnabled`,
   `IsOffscreen`, `IsToggled`, and `IsSelected`.
5. **Done.** Added `timedOut`-output overloads of
   `WaitForElementByAutomationId` and `WaitForElementByName`.
6. **Done.** Expanded `UiControlType` for common business-application
   controls - see "Missing Pega-oriented surfaces" above.
7. **Done.** Added RGB-component and `System.Drawing.Color` overloads of
   `HighlightElement`, alongside the original packed `colorRef` overload.

## Verdict

The API is chainable, and UIAutomationUtils still necessarily has the
repository's highest object-proxy dependency - no `AutomationElement`
consumer is orphaned from a producer. All seven recommended changes and both
missing-surface items are implemented additively: every original overload
remains for .NET consumers or backward compatibility, while the new scalar
bounds, collection adapters (indexed access and JSON summaries), one-shot
handle-scoped methods, disambiguating outputs, expanded control-type
coverage, and color overloads substantially reduce Pega design-surface
complexity for the common cases.

## Addendum: naming ambiguity fix

The additive overloads above solved usability, but several of them introduced
a new problem: two same-named methods whose Pega-visible (non-`out`)
parameter lists became identical, differing only in an `out` parameter's
type or presence. C# overload resolution handles this fine, but Pega Robot
Studio's designer surface cannot disambiguate two overloads by `out`
parameter type/shape alone - both entries in the designer's method picker
would look the same.

Seven method groups in this component had this problem: `GetBoundingRectangle`,
`IsEnabled`, `IsOffscreen`, `IsToggled`, `IsSelected`,
`WaitForElementByAutomationId`, `WaitForElementByName`. In each case, one
overload takes just `(AutomationElement element, ..., out string message)`
and the other adds one extra `out` parameter (`querySucceeded`, `timedOut`)
or returns a different `out` type (`Rectangle` vs. scalar `int`s) -
identical from Pega's point of view.

Fixed by renaming the less-disambiguated overload (the one without the
extra output) rather than the newer, more Pega-usable one, following this
repository's convention of breaking changes over compatibility shims:

| Old name | New name | Reason |
|---|---|---|
| `GetBoundingRectangle(AutomationElement, out Rectangle, out string)` | `GetBoundingRectangleAsRectangle` | Return type, not an extra output, was the only difference |
| `IsEnabled(AutomationElement, out string)` | `IsEnabledSimple` | Missing the `querySucceeded` disambiguator |
| `IsOffscreen(AutomationElement, out string)` | `IsOffscreenSimple` | Missing the `querySucceeded` disambiguator |
| `IsToggled(AutomationElement, out string)` | `IsToggledSimple` | Missing the `querySucceeded` disambiguator |
| `IsSelected(AutomationElement, out string)` | `IsSelectedSimple` | Missing the `querySucceeded` disambiguator |
| `WaitForElementByAutomationId(..., out AutomationElement, out string)` | `WaitForElementByAutomationIdSimple` | Missing the `timedOut` disambiguator |
| `WaitForElementByName(..., out AutomationElement, out string)` | `WaitForElementByNameSimple` | Missing the `timedOut` disambiguator |

The plain (un-suffixed) name in each pair now belongs to the overload with
the extra disambiguating output - the one most useful to a Pega automation -
per the naming convention documented in the component `README.md`.
`GetBoundingRectangle(AutomationElement, out int, out int, out int, out int, out string)`
(the scalar left/top/width/height overload) was unaffected: it keeps the
plain name since its parameter count already disambiguates it from every
other overload.
