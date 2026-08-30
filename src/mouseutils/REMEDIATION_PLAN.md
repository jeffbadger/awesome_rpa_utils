# MouseUtils Remediation Plan

## Goals

1. Make the documented never-throws contract real for recoverable operational,
   argument, managed-runtime, and Win32/P/Invoke failures.
2. Guarantee best-effort release of mouse buttons and modifier keys acquired by
   compound operations.

Process-fatal conditions such as stack overflow, process termination, and
corrupted process state are outside the recoverable never-throws contract.

## 1. Define and apply the contract

- Inventory every public method documented as never throwing, especially the
  `bool` plus `out string message` APIs.
- On failure, return `false`, populate `message`, and initialize every other
  `out` value to its documented sentinel.
- Do not replace a useful underlying failure with a generic wrapper message when
  public methods call one another.
- Update XML comments and designer descriptions to state the precise contract.

## 2. Centralize recoverable exception translation

- Add a private guarded-execution mechanism that converts recoverable exceptions
  into a contextual failure message.
- Cover expected interop and operational types, including
  `DllNotFoundException`, `EntryPointNotFoundException`,
  `BadImageFormatException`, `ExternalException`, `InvalidOperationException`,
  `ArgumentException`, `IOException`, and `UnauthorizedAccessException`.
- Do not indiscriminately swallow fatal exceptions.
- Ensure an exception in error formatting or cleanup cannot escape an advertised
  never-throw boundary.

## 3. Guard native boundaries

- Route cursor, input, window, DPI, GDI, pixel, and system-setting calls through
  guarded private helpers.
- Handle exceptions from `Lazy<IntPtr>.Value` when loading busy-cursor handles;
  remember that `Lazy<T>` caches exceptions.
- Replace `IsProcessDpiAware`'s one-off catch with the common policy.
- Guard cleanup calls such as `ReleaseDC`, `DeleteObject`, `DestroyCursor`, and
  cursor restoration as well as primary operations.

## 4. Standardize compound-operation results

Track the primary result/message and cleanup result/message separately:

- Primary failure, cleanup success: return `false` with the primary message.
- Primary success, cleanup failure: return `false` with the cleanup message.
- Both fail: return `false` with both messages.
- Both succeed: return `true` with `message = null`.

Apply this rule to `ClickAndRestore` so restoration failure can no longer return
`true`.

## 5. Guarantee button release

Update these methods first:

- `Click`
- `ClickAndHold`
- `DragAndDrop` (both overloads through the core overload)
- `DragAndHold`
- `BezierDragAndDrop`

After a successful `MouseDown`, execute a best-effort `MouseUp` from `finally`,
regardless of movement, delay, or other failure. Preserve both the primary and
release failures using the compound-result rules.

`RubberBandSelect` will inherit the corrected drag behavior, but its modifier
cleanup must stop discarding release failures. `ClickWithModifiers` should retain
batched release and fall back to best-effort individual releases when the batch
fails or throws.

`MouseDown` remains intentionally stateful; its caller owns the matching
`MouseUp`, and this must be prominent in Pega documentation.

## 6. Add emergency component cleanup

Track state acquired through this component:

- Injected mouse buttons currently down.
- Injected modifier keys currently down.
- Whether this instance blocked physical input.
- Whether this instance hid the cursor and the owning thread.
- Whether this instance changed cursor clipping and the previous clip rectangle.

During `Dispose`, attempt non-throwing best-effort cleanup: release tracked input,
unblock physical input, rebalance cursor visibility on the owning thread when
possible, and restore the previous clip.

Treat system-cursor replacement separately: globally resetting every cursor can
overwrite concurrent changes made by the user or another process, so define safe
ownership/restoration behavior before adding it to disposal.

## 7. Tighten validation

- Reject inappropriate negative delays, durations, attempts, and notch counts
  instead of silently coercing them.
- Reject `NaN` and infinity in `ClickAtRelativePosition`.
- Validate zero or stale window handles where practical.
- Validate enum values and undefined modifier bits.
- Bound unusually long waits so bad Pega wiring cannot block indefinitely.

## 8. Add fault-injection seams

Introduce internal wrappers or delegates around at least:

- `SendInput`
- `SetCursorPos` and cursor-position queries
- Window coordinate-conversion APIs
- GDI calls

Use them to simulate exceptions and partial failures without requiring live user
input in every unit test.

## 9. Expand tests

Prove that:

- Every advertised never-throw method converts injected recoverable exceptions
  into `false` with a useful message and correct output sentinels.
- Every compound operation releases input state it acquired.
- Cleanup failure changes an otherwise successful result to `false`.
- Primary and cleanup failures are both retained.
- `ClickAndRestore` cannot report success after restoration failure.
- Public `MouseDown` remains held until its caller invokes `MouseUp`.
- Modifier and button cleanup works when batch release fails or throws.

## 10. Validate on Windows and Pega

- Build with zero warnings and run all unit tests.
- Run interactive Windows tests for real `SendInput` behavior.
- Exercise locked desktop, UAC/integrity mismatch, RDP disconnect, mixed-DPI
  monitors, multiple-monitor layouts, and automation cancellation.
- Load the assembly into every supported Pega Robot Studio and Robot Runtime
  version and verify method ports, return values, messages, and cleanup behavior.

## Implementation order

1. Shared result and cleanup helpers.
2. Drag release fixes.
3. Click, hold, and modifier release fixes.
4. Public-boundary/native exception guards.
5. Disposal cleanup and state ownership.
6. Validation tightening.
7. Fault-injection and regression tests.
8. Windows/Pega verification and documentation updates.
