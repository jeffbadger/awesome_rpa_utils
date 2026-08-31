# DialogUtils Pega API Usability Review

## Summary

Dialog and control handles are well-formed chain values: find/wait methods produce
them and click/read/highlight methods consume them. The difficult surfaces are the
two collection results and the unused-on-input `DialogButton` enum. Most routine
find-and-click workflows avoid proxies entirely.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `FindDialog` | Chainable producer | Scalar criteria produce a dialog handle plus a Boolean capability flag. This is a strong Pega shape and avoids requiring the caller to construct a handle. |
| `FindAllDialogs` | Proxy friction | `List<IntPtr>` requires a collection proxy and loop. Process scoping on `FindDialog` covers many cases where callers would otherwise enumerate. |
| `CanDismissDialog` | Chainable | Consumes the handle produced by `FindDialog` or `WaitForDialog` and returns a Boolean. |
| `FindButtonByText`, `FindButtonById` | Chainable | A dialog handle produces a button/control handle that connects directly to `ClickButton`, `GetControlText`, or `HighlightControl`. Remaining ports are scalar. |
| `ClickButton` | Chainable, ambiguous result | The handle is naturally produced. Its Boolean means “enabled when sent,” not “click succeeded,” and no message explains failure; the name/result can therefore overstate what Pega should branch on. |
| `ClickDialogButtonById` | Chainable, awkward ID | Ports are technically scalar, but the method accepts `int` even though `DialogButton` exists. Add an overload accepting `DialogButton` so standard buttons are selectable rather than memorized/cast numeric IDs. Its return means “found,” while `wasEnabled` carries a different outcome. |
| `ClickDialogButtonByText` | Chainable | Provides the most complete scalar workflow because it finds, clicks, verifies closure, retries, and returns a message. Numerous tuning ports may clutter the designer; a short default-settings entry point would improve the common case if optional parameters are not collapsed by Pega. |
| `GetDialogText`, `GetControlText` | Chainable | Handle inputs come from local producers and results are strings. Empty text also represents invalid/no matching controls, so failure is not separately diagnosable. |
| `ListDialogControls` | Significant proxy friction | Returns `List<DialogControlInfo>`; each item contains a handle and scalar properties. Pega needs both collection and object proxies before it can inspect or reuse a control. Add JSON or indexed scalar access for discovery workflows. |
| `HighlightControl` | Chainable but awkward | Handle and timing inputs are chainable/scalar. `colorRef` uses non-obvious Win32 BGR integer ordering; add RGB-component or named-color inputs. The method blocks the automation thread during flashes. |
| `WaitForDialog` | Chainable producer | Scalar criteria produce a handle. Its default is substring matching, while `FindDialog` defaults to exact matching; this inconsistency is easy to miss on a design surface. `false` does not distinguish timeout from invalid criteria. |
| `WaitForDialogToClose` | Chainable | Consumes a recently produced dialog handle and returns a Boolean timeout outcome. A stale/invalid handle immediately looks like successful closure. |

## Recommended changes

1. **Decision: no change required.** Keep `ClickDialogButtonById` accepting an
   integer control ID; the existing API is acceptable.
2. **Decision: no change required.** Keep `ListDialogControls` returning
   `List<DialogControlInfo>`; Pega proxy handling is acceptable for this
   discovery method.
3. **Decision: no change required.** `FindAllDialogs` can use Pega's standard
   ListLoop process, so additional collection-iteration documentation is not
   needed.
4. **Decision: change required.** Remove the optional default from the
   `exactMatch` parameter on both `FindDialog` and `WaitForDialog`, making the
   caller choose explicitly. If Pega supplies no value for the non-nullable
   Boolean port, treat its default value as `false`.
5. **Decision: change required.** Standardize `ClickDialogButtonByText` on the
   least shared observable behavior used by `ClickDialogButtonById`: return
   whether the button was found and add an `out bool wasEnabled` result. Send a
   single click, remove its automatic `WaitForDialogToClose`/retry behavior, and
   remove the now-unused `maxAttempts` and `retryDelayMs` parameters. Keep
   `WaitForDialogToClose` as a separate method for workflows that explicitly
   want to verify that the original dialog handle disappeared.
6. **Decision: change required.** Replace `HighlightControl`'s integer
   `colorRef` parameter with a `System.Drawing.Color` parameter; convert the
   color to Win32 `COLORREF` internally.

## Verdict

Routine dialog automation is Pega-friendly because handles are produced and
consumed within the utility. `FindAllDialogs` and `ListDialogControls` require
proxy work. The easiest usability win is exposing the existing `DialogButton`
enum directly on the standard-button click method.
