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
| `FindAllByControlType` | Significant proxy friction | Accepts an element proxy and returns `List<AutomationElement>`, requiring both object and collection proxy handling. Add indexed/scalar alternatives for common iteration workflows. |
| `GetChildren` | Significant proxy friction | Same nested proxy issue as `FindAllByControlType`; there is no scalar count/item accessor. |
| `GetName`, `GetAutomationId`, `GetClassName`, `GetControlTypeName` | Chainable | Consume an element proxy and return strings. Once the element chain exists, these are straightforward. |
| `GetBoundingRectangle` | Chainable input; proxy-friction output | Returns `System.Drawing.Rectangle`, forcing another framework-object proxy before coordinates can feed MouseUtils or ScreenCaptureUtils. Add integer coordinate outputs. |
| `IsEnabled`, `IsOffscreen` | Chainable, ambiguous Boolean | `false` can mean either the actual state or failure; callers must inspect `message`. Separate success and state outputs would create clearer Pega branches. |
| `IsElementAvailable` | Chainable | A simple Boolean stale-reference check. The element still must originate from an earlier producer and should be re-found rather than stored long-term. |
| `Invoke`, `SetValue`, `GetValue`, `Toggle`, `Expand`, `Collapse`, `Select` | Chainable | Element input is intentionally produced by a find operation; other ports are scalar. Unsupported UIA patterns are reported through Boolean/message. |
| `IsToggled`, `IsSelected` | Chainable, ambiguous Boolean | `false` represents both a legitimate state and an unsupported/stale/error result. Add a separate state output under the Boolean/message success convention. |
| `WaitForElementByAutomationId`, `WaitForElementByName` | Chainable | Parent and result are element proxies; search/timing inputs are scalar. Timeout and failure both return `false`, distinguished only by `message`. |
| `HighlightElement` | Chainable but awkward | Consumes an element proxy. Timing values are scalar, but `colorRef` uses non-obvious Win32 BGR ordering. It also blocks the automation thread while flashing. |

## Missing Pega-oriented surfaces

One-shot methods such as `InvokeByAutomationId`, `SetValueByAutomationId`, or
`GetValueByAutomationId`, scoped by a window handle, would let common automations
avoid retaining an `AutomationElement` proxy between steps. They should complement,
not replace, the composable element API.

`UiControlType` also omits several UIA control types that may occur in business
applications, such as DataGrid, DataItem, Group, Header, Slider, Spinner, and
ToolBar. Those elements can still be found by other properties, but not through
the control-type methods.

## Recommended changes

1. Add scalar bounds outputs to `GetBoundingRectangle`.
2. Add count/index accessors or scalar summaries for element collections.
3. Add a small set of handle-scoped one-shot find-and-act methods for common Pega
   workflows.
4. Separate operation success from the actual state returned by `IsEnabled`,
   `IsOffscreen`, `IsToggled`, and `IsSelected`.
5. Distinguish timeout from lookup failure in wait methods.
6. Expand `UiControlType` for common business-application controls.
7. Add RGB-component or named-color inputs for `HighlightElement`.

## Verdict

The API is chainable, but UIAutomationUtils necessarily has the repository's
highest object-proxy dependency. No `AutomationElement` consumer is orphaned from
a producer. Scalar bounds, collection adapters, and a few one-shot operations
would substantially reduce Pega design-surface complexity.
