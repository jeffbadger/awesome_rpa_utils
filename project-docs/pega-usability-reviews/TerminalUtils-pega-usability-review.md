# TerminalUtils Pega API Usability Review

## Summary

Like `EventLogUtils`, `SessionUtils`, `FileWatchUtils`, and `ArchiveUtils`,
this review is written **up front**, alongside the initial implementation,
rather than as a post-hoc pass over an already-shipped surface - there is no
legacy API to retrofit here. It records the signature-uniqueness decisions
made before any code was written, per the
[Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md).

All method ports are scalars, strings, ints, Booleans, or JSON strings -
directly usable in Pega. No repository-owned enum was needed: the one place
a color value crosses the boundary (`AnsiReconstruction`'s internal use of
`System.ConsoleColor`) never reaches a public method signature - color
information only ever leaves this component embedded in a JSON row's
`Text`/re-synthesized-ANSI string, not as a typed parameter or return value.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `IsConsoleAttachable` | Direct | Scalar `int` in, Boolean/message out. Performs a real attach/detach cycle internally since there is no separate Win32 query for "does this process have a console." |
| `StartConsoleProcess` | Direct | Three string inputs, scalar `int` process ID out. |
| `GetCursorPosition` | Direct | Scalar `int` in, two scalar `int` outs. |
| `ReadScreenRowsJson` | Direct, JSON | Returns a JSON array string rather than a typed collection, consistent with this suite's preference for JSON over complex objects on the Pega boundary. |
| `CaptureScreenText` | Direct | A single `bool preserveAnsi` flag toggles output format on otherwise identical inputs - a binary choice, not an overload pair, so it needs no `Simple` treatment. |
| `WaitForScreenText`/`WaitForScreenTextSimple` | Direct, disambiguated | `useRegex` is likewise a flag, not an overload dimension. The `Simple` sibling is a genuine signature-uniqueness necessity - both share the identical `(int, string, bool, int, int)` non-`out` parameter list. |
| `WaitForScreenChange`/`WaitForScreenChangeSimple` | Direct, disambiguated | Same necessity - both share the identical `(int, int, int)` non-`out` parameter list. |
| `WriteText`/`WriteLine` | Direct | Distinct names, not an overload pair - both happen to share `(int, string)`, but `WriteLine`'s behavior (appending a line terminator) is different enough to warrant its own name rather than a `Simple`/plain split, matching this suite's general preference for distinct names over ambiguous overloads. |

## Signature-uniqueness decisions made up front

- `WaitForScreenTextSimple(int, string, bool, int, int, out string)` vs. `WaitForScreenText(int, string, bool, int, int, out bool, out string)` - `Simple` suffix on the overload missing the disambiguating `timedOut` output; the non-`out` parameter list is identical otherwise, so this is required, not optional.
- `WaitForScreenChangeSimple(int, int, int, out string)` vs. `WaitForScreenChange(int, int, int, out bool, out bool, out string)` - same necessity, missing `changed`/`timedOut`.
- `CaptureScreenText`'s `preserveAnsi` and `WaitForScreenText`'s `useRegex` are deliberate flag parameters, not overload dimensions - neither triggers the `Simple`/`As<Type>` convention, since there is no second overload with an identical non-`out` parameter list to disambiguate from.
- `WriteText`/`WriteLine` are distinct method names, not a `Simple`/plain pair, despite sharing `(int, string)` - this suite prefers a new name over an ambiguous overload wherever the behaviors genuinely differ (same precedent as `ValidateArchiveCrc`/`ValidateArchiveCrcJson` in `ArchiveUtils`).
- No `As<Type>`-suffixed overloads exist in this component - there is no case here where two overloads return the same logical value via a different type.

## Operational concerns

- `AttachConsole`/`FreeConsole` are process-wide Win32 APIs, not per-thread
  - every method in this component attaches, acts, and detaches internally
  and serializes concurrent calls via an internal lock, so two automations
  calling `TerminalUtils` methods at the same time cannot corrupt each
  other's console attachment. See `ConsoleAttachScope`.
- If the calling process (the Pega Robot Runtime host) already has a
  console attached before a call - uncommon for a GUI host, but possible -
  restoring it afterward is best-effort, not a guaranteed perfect restore.
- Reading a target process's console works even when its window is
  minimized or not currently visible - a genuine advantage over UI
  Automation-based approaches, but also means this component can silently
  interact with a console the operator isn't actually looking at.
- `WriteText`/`WriteLine` inject key events carrying only a Unicode
  character (no virtual key code) - expected to work for typical console
  line-input readers, but not verified against every possible console
  application; flagged as an open item to confirm during real-world use.
- Windows Terminal/ConPTY behavior is an open, unverified risk - see the
  component README's Notes & Caveats.

## Recommended changes

None outstanding - this is the initial design pass, not a retrofit. Full
scrollback-buffer reading (beyond the visible viewport), remote/cross-
machine console attachment, and true attribute-based field parsing (this
component's "rows and fields" is a heuristic whitespace split, not
3270/5250-grade field parsing, by explicit scope decision) are tracked as
open extensions rather than here, since there is no existing rating to
revise.
