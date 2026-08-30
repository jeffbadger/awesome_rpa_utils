# WindowUtils Pega API Usability Review

## Summary

`IntPtr` is the utility's intentional chain type. Lookup and wait methods produce
handles that connect directly to state, geometry, activation, wait, and child-
window methods. The difficult ports are the three `List<IntPtr>` results and the
`Rectangle` result from `GetWindowBounds`.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `GetTopLevelWindows` | Proxy friction | `List<IntPtr>` requires a configured collection proxy and loop. Add scalar query helpers for common needs instead of requiring general enumeration. |
| `FindWindowByTitle`, `FindWindowByClass`, `GetForegroundWindow` | Chainable producers | Inputs are scalar and the `IntPtr` result is meant to feed another method immediately. Returning zero for not found is workable, though a Boolean/message-style `TryFind` variant would be easier to branch and diagnose. |
| `FindWindowsByProcessId` | Proxy friction | The process ID is scalar, but `List<IntPtr>` consumption requires proxy/loop setup. Add `FindFirstWindowByProcessId`; retain the list method for advanced workflows. |
| `GetWindowBounds` | Chainable input; proxy-friction output | The handle is naturally produced. `Rectangle` requires property extraction through a proxy. Add an overload with scalar left, top, width, and height outputs. |
| `SetWindowBounds`, `MoveWindow`, `ResizeWindow` | Chainable | The handle comes from lookup/wait methods and all remaining ports are scalar. |
| `GetWindowTitle`, `GetWindowClassName`, `GetWindowProcessId` | Chainable | Handle input is natural and each result is scalar. Invalid handles can collapse to an empty string or zero, making “real empty value” and failure indistinguishable. Try-style variants would improve diagnostics. |
| `IsWindowVisible`, `IsWindowResponding` | Chainable | Boolean results are easy to branch on. Validate the handle first if an invalid handle must be distinguished from window state. |
| `SetWindowState` | Chainable | Handle plus enum is designer-friendly. It has no success/message output, so the automation cannot confirm that the requested state was reached; add a verifiable Boolean operation if confirmation is required. |
| `CloseWindow`, `ActivateWindow`, `SetAlwaysOnTop` | Chainable | All non-handle ports are scalar. `ActivateWindow` should be followed by `WaitForWindowActive`; `CloseWindow` should be followed by `WaitForWindowToClose`. |
| `WaitForWindow` | Chainable producer | Scalar search and timing inputs produce a handle. `false` combines timeout with invalid title and supplies no message; add a message or explicit status output. |
| `WaitForWindowToClose`, `WaitForWindowActive` | Chainable | Handle input is natural and result is Boolean. `false` means timeout, while invalid/stale-handle behavior is not consistently distinguishable. |
| `GetChildWindows` | Chainable input; proxy-friction output | Parent handles are produced naturally, but the `List<IntPtr>` result requires a proxy and loop. |
| `FindChildWindow` | Chainable | This is the Pega-friendly alternative to enumerating children: parent handle plus scalar filters produces another chainable handle. |

## Recommended changes

1. Add scalar bounds outputs to `GetWindowBounds`.
2. Add `FindFirstWindowByProcessId` for the common single-window case.
3. Provide message/status outputs for `WaitForWindow` so timeout and invalid input
   are distinct.
4. Consider Try-style title, class, and process-ID getters when callers need to
   distinguish failure from legitimate empty/zero values.
5. Document short-lived producer-to-consumer handle wiring and discourage storing
   handles across long automation intervals.
6. Keep collection-returning methods for advanced use, but document the required
   Pega collection proxy and iteration setup.

## Verdict

No individual handle-consuming method needs a scalar handle replacement. Handles
are well supported by local producers and should remain opaque. `List<IntPtr>` and
`Rectangle` are the design-surface limitations that merit adapters.
