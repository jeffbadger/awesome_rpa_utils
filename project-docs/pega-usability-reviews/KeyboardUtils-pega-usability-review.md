# KeyboardUtils Pega API Usability Review

## Summary

Most methods use strings, integers, Booleans, or designer-selectable enums and
should be directly usable in Pega Robot Studio. `PressKeyCombo` takes its keys as a
`params VirtualKey[]`, which Robot Studio can call with individual scalar key
arguments the same way it would call a fixed-arity overload — no array proxy or
script is needed.

## Method review

| Method or overload group | Rating | Assessment |
|---|---|---|
| `KeyDown` | Direct, stateful | The enum and message ports are usable. A later automation failure can leave the key held; prefer bounded operations when possible and route cleanup through `KeyUp`. |
| `KeyUp` | Direct | Provides the required scalar-port cleanup operation for `KeyDown`. |
| `PressKey` | Direct | Simple enum input. A failed release can still leave the key held, so the failure branch should call `KeyUp`. |
| `PressKeyWithModifiers` | Direct, verify flags | This is the Pega-friendly shortcut API when `ModifierKeys` flags can be combined in the designer. Add named shortcut methods or separate modifier Boolean ports if the supported Robot Studio version only permits one enum constant. |
| `PressKeyCombo` | Direct | `params VirtualKey[]` lets Robot Studio pass individual scalar `VirtualKey` arguments (`PressKeyCombo(out _, VirtualKey.Control, VirtualKey.Shift, VirtualKey.Escape)`), same as a fixed-arity overload. No adapter needed. |
| `HoldKey` | Direct | Enum and integer inputs are usable. It blocks the automation thread for the hold duration, and a failed release requires a `KeyUp` cleanup branch. |
| `TypeText(string, out string)` and `TypeText(string, int, out string)` | Direct | Both overloads are Pega-friendly. The shorter overload is the normal choice; the delay overload supports controls that lose fast input. |
| `PasteText` | Direct ports; operational risk | All ports are scalar and raw Win32 clipboard use avoids an STA requirement. Calling it can permanently destroy non-text clipboard content, and paste delivery can race clipboard restoration. Make the destructive clipboard limitation prominent on the method in the designer. |
| `IsKeyDown` | Direct | Enum input and Boolean result are easy to branch on. |
| `IsModifierDown` | Direct, verify flags | Provides a Boolean alternative to consuming the flags returned by `GetActiveModifiers`. Combined input flags depend on designer support. |
| `GetActiveModifiers` | Direct enum output | No object proxy is required, but branching on combined flags may be less convenient than calling `IsModifierDown` for each required flag. |

## Coverage concerns

`VirtualKey` omits OEM punctuation keys and other less-common virtual keys. This
does not affect ordinary text because `TypeText` uses Unicode input, but it makes
shortcuts involving punctuation or an omitted special key impossible without an
enum expansion or a raw-key-code adapter.

## Recommended changes

1. ~~Add scalar fixed-arity combo methods...~~ **Not needed.** `PressKeyCombo`
   already takes a `params VirtualKey[]`, which Robot Studio calls with individual
   scalar key arguments the same as a fixed-arity overload — see the Method review
   table above.
2. Verify that the supported Robot Studio designer can combine `ModifierKeys`
   flags. Add Boolean modifier inputs or named helpers if it cannot. *(Out of
   scope for this pass.)*
3. Expand `VirtualKey` for shortcuts involving OEM punctuation and other required
   Windows keys. *(Out of scope for this pass.)*
4. **Done.** Documented the `KeyDown`/`KeyUp` failure-connection requirement and
   the preference for bounded methods (`PressKey`/`HoldKey`) in the `KeyDown` XML
   remarks, [README](../../src/keyboardutils/README.md#notes--caveats), and
   [Documentation/PressHoldCombo.md](../../src/keyboardutils/Documentation/PressHoldCombo.md).
5. **Done.** Flagged `PasteText`'s destructive clipboard behavior in its
   designer-visible `[Description]` attribute, the [README](../../src/keyboardutils/README.md) method
   table, and [Documentation/ClipboardPaste.md](../../src/keyboardutils/Documentation/ClipboardPaste.md).

## Verdict

All public methods are directly usable in Robot Studio, including `PressKeyCombo`
via its `params VirtualKey[]`. Stateful key-down operations and clipboard
replacement are workflow safety concerns, now called out prominently in the
docs, rather than port-type blockers. `ModifierKeys` flag-combining support and
`VirtualKey` coverage of OEM/punctuation keys remain open questions for a future
pass.
