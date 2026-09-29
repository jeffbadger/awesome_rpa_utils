# Rules

A rule says how to recognize one known browser popup and what to do about it. Add rules
before (or after) `Start`; they can be added, removed and switched on and off while
watching. A rule added (or switched on) while a popup is already open applies to that popup too.

## Two scopes

The method you call picks the scope. The two kinds of popup are found in different ways and
dismissed in different ways, so the API keeps them apart rather than taking a flag.

| Scope | Popup | How it is found | How it is dismissed |
|---|---|---|---|
| `NativeDialog` | A JavaScript `alert`, `confirm`, `prompt` or `beforeunload` dialog. | It is a window of its own, so a new window in the browser's process is noticed through UI Automation window events and a periodic window sweep (`sweepIntervalMs`). | Invoke a button inside it by name or automation ID, or close its window. |
| `PageOverlay` | An element inside the page: an ARIA `role="dialog"`/`"alertdialog"` overlay, a cookie banner, a permission bar. | It has no window, so the component walks the browser window's UI Automation tree (bounded by `maxOverlayNodes` and `maxOverlayDepth`), woken by page-structure changes and by a periodic sweep (`overlaySweepIntervalMs`). | Invoke a control inside it by name or automation ID. There is no close rule: an overlay has no window to close. |

A page overlay costs more to find than a native dialog, so overlay discovery runs only for
browser processes that an overlay rule names (or every process, if an overlay rule names none:
always name the browser).

## Match a popup

```csharp
// Chrome's confirm("Delete this record?"): click OK.
browserInterrupt.AddNativeDialogDismissRuleByName(
    ruleName: "confirm-delete",
    nameContains: "",
    messageContains: "Delete this record",
    processName: "chrome",
    targetElementName: "OK",
    out string message);
```

Five things can identify a popup, and **every one you set must match**:

| Field | Compared against | Notes |
|---|---|---|
| `nameContains` | The popup element's UI Automation `Name`. | For a page overlay this is usually its accessible name (its `aria-label`, or its heading). |
| `automationIdContains` | The popup's `AutomationId`. Page overlay rules only. | **Best-effort for page content**: see below. |
| `roleContains` | The popup's `LocalizedControlType`, which is how UI Automation reports an ARIA role (for example `dialog`). | The most reliable way to say "any dialog", combined with a name. The text is localized, so it follows the Windows display language. |
| `messageContains` | The first non-empty text element inside the popup. | Read only when a rule needs it. See below. |
| `processName` | The owning process, with or without `.exe`. | Typical values: `chrome`, `msedge`, `firefox`. |

All of them compare ignoring case and by substring, except the `processName` (whole name)
and the `targetAutomationId` (a whole-string, case-insensitive match). Leave one empty (`""`
or `null`) to not check it. **At least one of name, role, process and (for an overlay)
automation ID is required**: a rule with none would match every popup, so it is refused.

### Always set enough to be specific

- A **native dialog** rule is checked against every window of the process it names, not only
  dialogs, so a rule that names only `chrome` could also match a browser window. Add a
  `messageContains`, a `nameContains` or a `roleContains`.
- A **page overlay** rule is checked against every element the bounded walk visits, not only
  elements that look like dialogs. A rule with only `nameContains: "Accept"` can match the
  Accept button itself rather than the banner around it. Set `roleContains` (for example
  `"dialog"`) together with a `nameContains` or `messageContains` that describes the
  container, and use `targetElementName` for the button.

### How `messageContains` reads the message

- **The text is matched literally.** No wildcards, no regular expressions.
- **Only one text element is read: the first non-empty one** the popup lists among its
  descendants (a small bounded search). Text in a later element is never seen, so if a popup
  has a heading and a separate body line, `messageContains` matches the heading only. What
  a real browser exposes for an `alert()`'s message is one of the things the live checks will
  confirm.
- **It is read last when choosing a rule**, only for a rule that has `messageContains` and
  whose other criteria already matched. Reading it is a call into the browser, so set
  `nameContains`, `roleContains` or `processName` too.

## The kinds of rule

| Method | Scope | Does |
|---|---|---|
| `AddNativeDialogDismissRuleByName` | `NativeDialog` | Invokes the descendant element with this name (usually a button such as `OK`). `exactTargetElementName: false` accepts the first descendant whose name *contains* the text. |
| `AddNativeDialogDismissRuleByAutomationId` | `NativeDialog` | Invokes the descendant with this automation ID. |
| `AddNativeDialogCloseRule` | `NativeDialog` | Closes the dialog's window through its window pattern, for a dialog with nothing worth clicking. |
| `AddNativeDialogWatchOnlyRule` | `NativeDialog` | Reports the dialog (event and log); never touches it. |
| `AddPageOverlayDismissRuleByName` | `PageOverlay` | Invokes the descendant element with this name (a button such as `Accept all` or a close control). |
| `AddPageOverlayDismissRuleByAutomationId` | `PageOverlay` | Invokes the descendant with this automation ID. |
| `AddPageOverlayWatchOnlyRule` | `PageOverlay` | Reports the overlay (event and log); never touches it. |

"Invoke" tries the element's invoke pattern and, for a control that only exposes a toggle,
its toggle pattern. An element that supports neither ends as a `PopupDismissFailed` whose
`Detail` says so.

## Worked recipes

### Dismiss a Chrome `confirm()` by clicking OK

```csharp
// Once, before the long step:
browserInterrupt.AddNativeDialogDismissRuleByName(
    "confirm-ok", nameContains: "", messageContains: "leave this page", processName: "chrome",
    targetElementName: "OK", out _);
browserInterrupt.Start(out string message);
```

To click OK on the browser's *Leave site?* (`beforeunload`) prompt, the button is usually
named `Leave` rather than `OK`; find the real text with a watch-only rule first.

### Dismiss a cookie banner by button name

```csharp
// The banner is an in-page overlay with an accessible name containing "cookies".
browserInterrupt.AddPageOverlayDismissRuleByName(
    "cookie-banner", nameContains: "cookies", messageContains: "", processName: "msedge",
    targetElementName: "Accept all", out _, roleContains: "dialog");
```

If the banner is not marked up as a dialog, drop `roleContains` but keep a specific
`nameContains` or `messageContains`. If the button has a stable `id` that shows up in UI
Automation, `AddPageOverlayDismissRuleByAutomationId` is less fragile than matching text,
but see the caveat below.

### Watch-only: find out what a real page shows

```csharp
browserInterrupt.AddNativeDialogWatchOnlyRule("any-chrome-window", "", "", "chrome", out _);
browserInterrupt.AddPageOverlayWatchOnlyRule("any-chrome-dialog-role", "", "", "chrome", out _, roleContains: "dialog");
browserInterrupt.Start(out _);
// ... run the automation normally ...
browserInterrupt.GetLogJson(100, out string json, out _);   // name, role, message, process of everything matched
```

The first rule reports every window of the browser process (the browser's own windows as
well as any dialog), so it is noisy: use it only for discovery. Then replace them with specific dismiss rules built from the names and roles the log shows.

## Finding the names and roles to use

A rule can only match what the browser exposes to UI Automation, so look at the real tree
before writing it.

- The watch-only rule above logs the `name`, `role` and `message` of the popup itself.
- [UIAutomationUtils](../../uiautomationutils/README.md) can list the elements under the
  browser window (`GetDescendantsSummaryJson`) with their names, automation IDs and control
  types, so you can find the button to invoke inside a popup.
- **Accessibility Insights for Windows** or the older **Inspect.exe** show the live tree
  with names, automation IDs, control types and patterns; point them at the open popup.
- Turn the browser's accessibility support on if the tree looks empty: Chromium builds its
  tree lazily, and the first query against a fresh tab can be slow or sparse.

### Caveats specific to UI Automation and browsers

- **`AutomationId` is best-effort for page content.** Chromium does not guarantee that a
  page's DOM `id` or `data-*` attributes reach UI Automation. It is far more dependable for
  the buttons of a native dialog than for elements of a web page, so match page elements by
  name and role first.
- **Names and roles are localized text.** `roleContains: "dialog"` and a button named
  `OK` follow the browser's and Windows' display language; a machine in another language
  needs the local text (or an automation ID).
- **The real names and structure of a native JS dialog are unverified.** Design assumes the
  dialog is a top-level window whose descendants are real buttons with an invoke pattern.
  The live checks will confirm the names Chrome, Edge and Firefox actually use.
- **Iframe content is unverified.** An overlay inside an iframe should appear in the same
  tree, but that has not yet been confirmed.
- **A browser that does not expose UI Automation cannot be matched at all**, and a headless
  browser has no UI Automation tree.

## Order: the first match wins

Rules are tried in the order you added them. A watch-only rule placed *before* a dismiss
rule for the same popup will report it and stop there, so the dismiss rule never runs. Put
specific rules before general ones.

## Turn a rule off and on

```csharp
browserInterrupt.SetRuleEnabled("cookie-banner", false, out _);   // leave this banner alone for now
// ...
browserInterrupt.SetRuleEnabled("cookie-banner", true, out _);
```

Turning a rule off returns once any click already under way has finished, which can take a
long time if the browser is hung (see [Lifecycle](Lifecycle.md#pause-can-block-on-a-hung-browser)).
Turning a rule on also clears a stop caused by it dismissing too many popups (see
[Lifecycle](Lifecycle.md#a-popup-that-keeps-coming-back)).

## See what is defined

```csharp
browserInterrupt.ListRulesJson(out string json, out _);
// [{"name":"confirm-ok","scope":"NativeDialog","action":"InvokeByName","nameContains":"",
//   "messageContains":"leave this page","processName":"chrome","roleContains":"",
//   "automationIdContains":"","target":"OK","exactTargetName":true,
//   "enabled":true,"stopped":false,"dismissals":2}]
```

A rule name may be at most 64 characters and must be unique (ignoring case); up to 100 rules
can be defined.

## What a rule cannot match

- **Anything in a browser that does not expose UI Automation**, or in a headless browser.
- **A popup drawn outside the browser's accessibility tree**, such as a canvas-only
  overlay: nothing in it is an element a rule can see.
- **Popups of the automation's own process** are never touched.
- **An ordinary Windows dialog** (a message box, a file dialog): use
  [InterruptUtils](../../interruptutils/README.md) or [DialogUtils](../../dialogutils/README.md).
