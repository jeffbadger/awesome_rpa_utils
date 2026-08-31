# WindowUtils Pega API Usability Review

## Summary

`IntPtr` is the utility's intentional chain type. Lookup and wait methods produce
handles that connect directly to state, geometry, activation, wait, and child-
window methods. The difficult ports were the three `List<IntPtr>` results and the
`Rectangle` result from `GetWindowBounds`; both now have documented scalar
alternatives for the common case.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `GetTopLevelWindows` | Proxy friction, documented | `List<IntPtr>` requires a configured collection proxy and loop; no scalar alternative exists (or should) for "every window" - the docs now say so and point to the scalar lookups below for the common case. |
| `FindWindowByTitle`, `FindWindowByClass`, `GetForegroundWindow` | Chainable producers | Inputs are scalar and the `IntPtr` result is meant to feed another method immediately. Returning zero for not found is workable and left as-is. |
| `FindWindowsByProcessId` | Proxy friction; scalar alternative added | `FindFirstWindowByProcessId` now covers the common single-window case; the list method remains for the multi-window case. |
| `GetWindowBounds` | Chainable input; scalar overload added | The handle is naturally produced. A new overload returns `left`/`top`/`width`/`height` as integers directly; the original `Rectangle` overload remains for callers with a proxy. |
| `SetWindowBounds`, `MoveWindow`, `ResizeWindow` | Chainable | The handle comes from lookup/wait methods and all remaining ports are scalar. |
| `GetWindowTitle`, `GetWindowClassName`, `GetWindowProcessId` | Chainable; Try-style overloads added | `TryGetWindowTitle`/`TryGetWindowClassName`/`TryGetWindowProcessId` now distinguish an invalid/nonexistent handle from a legitimately empty title/class/zero PID via the return value; the original overloads (which collapse both cases) remain. |
| `IsWindowVisible`, `IsWindowResponding` | Chainable | Boolean results are easy to branch on. Out of scope for this pass (not in the numbered recommendations). |
| `SetWindowState` | Chainable | Handle plus enum is designer-friendly. Out of scope for this pass (not in the numbered recommendations) - `ShowWindow`'s return value reports prior visibility, not success, so there is nothing meaningful to add without an extra confirmation query. |
| `CloseWindow`, `ActivateWindow`, `SetAlwaysOnTop` | Chainable | All non-handle ports are scalar. `ActivateWindow` should be followed by `WaitForWindowActive`; `CloseWindow` should be followed by `WaitForWindowToClose`. |
| `WaitForWindow` | Chainable producer; disambiguated | A new overload adds a `message` output, so a genuine timeout and an invalid title are distinguishable; the original overload (no message) remains. |
| `WaitForWindowToClose`, `WaitForWindowActive` | Chainable | Handle input is natural and result is Boolean. `false` means timeout; out of scope for this pass. |
| `GetChildWindows` | Chainable input; proxy friction, documented | Parent handles are produced naturally, but the `List<IntPtr>` result requires a proxy and loop - the docs now say so and point to `FindChildWindow` for the common single-match case. |
| `FindChildWindow` | Chainable | This is the Pega-friendly alternative to enumerating children: parent handle plus scalar filters produces another chainable handle. |

## Recommended changes

1. **Done.** Added a scalar `left`/`top`/`width`/`height` overload of
   `GetWindowBounds`.
2. **Done.** Added `FindFirstWindowByProcessId` for the common single-window
   case; `FindWindowsByProcessId` remains for the multi-window case.
3. **Done.** Added a `WaitForWindow` overload with a `message` output, so a
   genuine timeout and an invalid (null/empty) title are distinguishable
   without inferring it from `hWnd` alone.
4. **Done.** Added `TryGetWindowTitle`, `TryGetWindowClassName`, and
   `TryGetWindowProcessId`, distinguishing an invalid/nonexistent handle from
   a legitimately empty title/class name/zero PID via the return value; the
   original getters remain unchanged.
5. **Done.** Documented the short-lived producer → consumer handle-wiring
   pattern (a lookup/wait method's output feeds directly into the next
   steps' input) in `README.md` and `Documentation/EnumerationAndLookup.md`,
   with explicit guidance against caching a handle across a long-running
   step or between separate runs.
6. **Done.** Documented that `GetTopLevelWindows`/`FindWindowsByProcessId`/
   `GetChildWindows` need a Pega collection proxy and loop, and pointed to
   the scalar alternatives (`FindWindowByTitle`/`FindWindowByClass`/
   `FindFirstWindowByProcessId`/`FindChildWindow`) for the common
   single-result case, in `README.md` and the relevant `Documentation/*.md`
   pages.

## Verdict

No individual handle-consuming method needed a scalar handle replacement -
handles remain well supported by local producers and stay opaque. All six
recommended changes are implemented additively: every original overload
remains for backward compatibility, `GetWindowBounds` and `WaitForWindow`
each gained a friction-reducing/disambiguating overload, the three
State & Geometry getters gained Try-style variants, and the `List<IntPtr>`
methods are now paired with documented scalar alternatives for the common
case, with the collection-returning originals kept and clearly documented
for the genuine multi-result case.
