# Rules

A rule says how to recognize one known popup and what to do about it. Add rules
before (or after) `Start`; they can be added, removed and switched on and off while
watching.

## Match a popup

```csharp
// A session-timeout warning from the claims application: click "Yes" to stay signed in.
interrupt.AddDismissRuleByText(
    ruleName: "keep-session",
    titleContains: "Session Timeout",
    messageContains: "will expire",
    processName: "ClaimsApp",
    buttonText: "Yes",
    out string message);
```

Three things identify a popup, and **every one you set must match**:

- `titleContains` - its title bar text.
- `messageContains` - its message: the first non-empty text control inside it.
- `processName` - the application that owns it, with or without `.exe`.

All three compare ignoring case, and the first two by substring. Leave a criterion
empty (`""`) to not check it. **At least one is required**: a rule with none would
match every dialog on the desktop, so it is refused.

Prefer the narrowest rule that is still reliable. Adding the process name means a
"Session Timeout" popup from some other application is left alone.

## The four kinds of rule

| Method | Does |
|---|---|
| `AddDismissRuleByText` | Clicks the button with this text. Ignores case and the `&` access-key marker, so `Yes` finds `&Yes`. `exactButtonText: false` accepts the first button that *contains* the text. |
| `AddDismissRuleById` | Clicks a standard button by ID (`InterruptButton.Ok`, `Yes`, ...). Independent of the language of the text, so it suits a machine that is not in English. Works on classic message boxes. |
| `AddCloseWindowRule` | Closes the window, as its close box does. For a popup with no button. For a message box with a Cancel button that means Cancel. |
| `AddWatchOnlyRule` | Reports the popup (event and log) and never touches it. Use it to find out what appears before deciding how to answer it. |

```csharp
// Language-independent: click OK on any "Update available" notice, by control ID.
interrupt.AddDismissRuleById("update-ok", "Update available", "", "", InterruptButton.Ok, out _);

// A toast with no button at all.
interrupt.AddCloseWindowRule("toast", "Notice", "", "ClaimsApp", out _, className: "*");

// Just tell me about it.
interrupt.AddWatchOnlyRule("surprise", "", "unexpected error", "ClaimsApp", out _);
```

## Order: the first match wins

Rules are tried in the order you added them. A watch-only rule placed *before* a
dismiss rule for the same popup will report it and stop there, so the dismiss rule
never runs. Put specific rules before general ones.

## Window classes

By default a rule matches a standard dialog (window class `#32770`), which is what a
message box and most common dialogs are. Pass `className` to match something else:

- `className: "*"` - any window class. Needed for a popup that is an ordinary
  application window, such as a WinForms or WPF form used as a notification.
- a specific class name - just that class. Read it with `WindowUtils.GetWindowClassName`, or a tool such as Spy++.

A `*` rule still needs a title, message or process, so it cannot match everything.

## Turn a rule off and on

```csharp
interrupt.SetRuleEnabled("keep-session", false, out _);   // leave these popups alone for now
// ...
interrupt.SetRuleEnabled("keep-session", true, out _);
```

Turning a rule on also clears a stop caused by it dismissing too many popups (see
[Lifecycle](Lifecycle.md#a-popup-that-keeps-coming-back)).

## See what is defined

```csharp
interrupt.ListRulesJson(out string json, out _);
// [{"name":"keep-session","action":"ClickButtonText","titleContains":"Session Timeout",
//   "messageContains":"will expire","processName":"ClaimsApp","className":"#32770",
//   "button":"Yes","enabled":true,"stopped":false,"dismissals":3}]
```

A rule name may be at most 64 characters and must be unique (ignoring case); up to
100 rules can be defined.

## What a rule cannot match

- **Popups with no native buttons** (WinUI/UWP dialogs, custom-drawn popups): a click
  rule fails with a message saying so. Use a close rule, or drive it with
  `KeyboardUtils` or `UIAutomationUtils`.
- **A message the popup draws itself** cannot be matched with `messageContains`,
  because there is no text control to read. Match on title or process instead.
- **Popups of the automation's own process** are never touched.
