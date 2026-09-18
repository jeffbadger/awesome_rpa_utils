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
| `ClickDialogButtonById` | Chainable | Ports are technically scalar; the method accepts `int` rather than the repository-owned `DialogButton` enum, a deliberate decision (see Recommended changes) rather than a gap - a caller wanting a standard button casts from the enum. Its return means "found," while `wasEnabled` carries a different outcome. |
| `ClickDialogButtonByText` | Chainable | Finds a button by text and sends a single click, returning whether it was found plus an `out bool wasEnabled`. Deliberately does not verify dialog closure or retry - see `WaitForDialogToClose` for that, called separately. |
| `GetDialogText`, `GetControlText` | Chainable | Handle inputs come from local producers and results are strings. Empty text also represents invalid/no matching controls, so failure is not separately diagnosable. |
| `ListDialogControls` | Significant proxy friction | Returns `List<DialogControlInfo>`; each item contains a handle and scalar properties. Pega needs both collection and object proxies before it can inspect or reuse a control. Add JSON or indexed scalar access for discovery workflows. |
| `HighlightControl` | Chainable | Handle and timing inputs are chainable/scalar; the color input is a `System.Drawing.Color`, not a raw Win32 BGR integer. The method still blocks the automation thread during flashes. |
| `WaitForDialog` | Chainable producer | Scalar criteria produce a handle. `exactMatch` is a required parameter here and on `FindDialog` - no default on either, so there's no cross-method inconsistency to miss on a design surface. `false` does not distinguish timeout from invalid criteria. |
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
4. **Done.** `exactMatch` is now a required parameter on both `FindDialog` and
   `WaitForDialog` - no default on either, removing the inconsistency where
   the two methods previously defaulted differently.
5. **Done.** `ClickDialogButtonByText` now sends a single click and returns
   whether the button was found plus an `out bool wasEnabled`, instead of
   polling/retrying up to `maxAttempts` and verifying the dialog closed (those
   parameters are gone). `WaitForDialogToClose` remains a separate method for
   workflows that explicitly need to verify the original dialog handle closed.
6. **Done.** `HighlightControl` takes a `System.Drawing.Color` parameter
   instead of a raw Win32 BGR `colorRef` int, converting to `COLORREF`
   internally.

*(Items 4-6 were implemented in `7e19e59` shortly after this review was
written, but this doc's status markers weren't updated at the time - caught
during a later cross-repo audit of every component's Recommended-changes
section for genuinely-outstanding items. The Method review table above was
also stale in three places describing the pre-fix behavior; corrected
alongside this.)*

## Verdict

Routine dialog automation is Pega-friendly because handles are produced and
consumed within the utility. `FindAllDialogs` and `ListDialogControls` require
proxy work. `ClickDialogButtonById` deliberately keeps its `int controlId`
parameter rather than adding a `DialogButton`-typed overload (decision 1
above) - callers needing a standard button already have the enum to cast
from, and Pega's own int literal input covers the rest.
