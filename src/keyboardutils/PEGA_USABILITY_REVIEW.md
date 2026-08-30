# KeyboardUtils Pega API Usability Review

## Summary

Most methods use strings, integers, Booleans, or designer-selectable enums and
should be directly usable in Pega Robot Studio. `PressKeyCombo` is the exception:
its `VirtualKey[]` input has no natural producer and is likely impractical without
a configured array proxy or script.

## Method review

| Method or overload group | Rating | Assessment |
|---|---|---|
| `KeyDown` | Direct, stateful | The enum and message ports are usable. A later automation failure can leave the key held; prefer bounded operations when possible and route cleanup through `KeyUp`. |
| `KeyUp` | Direct | Provides the required scalar-port cleanup operation for `KeyDown`. |
| `PressKey` | Direct | Simple enum input. A failed release can still leave the key held, so the failure branch should call `KeyUp`. |
| `PressKeyWithModifiers` | Direct, verify flags | This is the Pega-friendly shortcut API when `ModifierKeys` flags can be combined in the designer. Add named shortcut methods or separate modifier Boolean ports if the supported Robot Studio version only permits one enum constant. |
| `PressKeyCombo` | Adapter needed | `params VirtualKey[]` is still an array parameter to Robot Studio. There is no public producer for that array. Add fixed-arity scalar methods for two-, three-, and possibly four-key chords, or accept a parseable string specification. |
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

1. Add scalar fixed-arity combo methods, such as two-, three-, and four-key
   variants, so Pega users do not need to construct `VirtualKey[]`.
2. Verify that the supported Robot Studio designer can combine `ModifierKeys`
   flags. Add Boolean modifier inputs or named helpers if it cannot.
3. Expand `VirtualKey` for shortcuts involving OEM punctuation and other required
   Windows keys.
4. Document a failure connection from `PressKey`/`HoldKey` to `KeyUp`, and favor
   bounded methods over separate `KeyDown`/`KeyUp` steps.
5. Clearly flag `PasteText` as destructive when the clipboard holds non-text
   data.

## Verdict

`PressKeyCombo` is the only public method blocked by a difficult non-scalar
input. The remaining APIs are directly usable, subject to verifying flags-enum
editing. Stateful key-down operations and clipboard replacement are workflow
safety concerns rather than port-type blockers.
