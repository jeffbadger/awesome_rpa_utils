# Never-Throws Standard

## Purpose

The utilities in this repository are used from Pega Robot Studio, where an
unexpected exception can terminate an automation path and leave desktop or
operating-system state partially changed. Public operations advertised as
"never throws" must therefore convert recoverable failures into explicit method
results that can be wired and handled on the design surface.

This standard applies independently to every utility project. Projects remain
standalone and must not add project references merely to share the implementation.
A small helper may be duplicated consistently in each project.

## Contract

For a public method following the standard:

- Success returns `true` and sets `message` to `null`.
- Failure returns `false` and sets `message` to a concise, actionable reason.
- Every other `out` parameter is initialized to its documented failure sentinel.
- Expected argument, state, managed-runtime, external API, and native interop
  failures do not escape to Pega as exceptions.
- Cleanup is attempted after partial success and its failure is not hidden.
- The method does not report success when required cleanup failed.

"Never throws" means no recoverable exception escapes the public automation
boundary. It cannot guarantee recovery from process-fatal conditions such as
stack overflow, process termination, runtime failure, or corrupted process state.
Documentation must use this precise meaning rather than making an absolute claim
about conditions application code cannot control.

## Standard public shape

Use this shape where the operation has a meaningful failure outcome:

```csharp
public bool Operation(
    /* inputs */,
    out TResult result,
    out string message)
```

At method entry, initialize outputs:

```csharp
result = default;
message = null;
```

Pega-facing APIs should prefer primitive and simple serializable ports. When a
framework type is useful, consider an overload with primitive outputs rather than
forcing complex design-surface wiring.

Normal negative outcomes must be distinguished from failures when both return
`false`. Use `message = null` for a normal negative result such as not found or a
genuine timeout, and a non-empty message for an operational failure. Document
this convention on every affected method.

## Four-part implementation approach

### 1. Define the method contract

Before changing implementation, record for each public method:

- What constitutes success.
- Which negative outcomes are normal.
- Failure sentinels for all outputs.
- State or resources the method may acquire.
- Cleanup required after partial success.
- Which failures are safe to expose as `false` rather than exceptions.

XML comments, `[Description]`, README tables, examples, and implementation must
agree. Do not label a method never-throws until its boundary enforces the policy.

### 2. Centralize recoverable exception translation

Each project should contain one private, consistently named guard/helper that:

- Executes an operation within the project boundary.
- Converts recoverable exceptions to a contextual message.
- Preserves an existing, more specific failure message.
- Safely formats the operation name and exception details.
- Does not catch process-fatal or corrupted-state conditions intentionally.

The policy should cover exception families actually possible for that project,
for example:

- Native loading/marshalling: `DllNotFoundException`,
  `EntryPointNotFoundException`, `BadImageFormatException`, `ExternalException`.
- Files/processes: `IOException`, `UnauthorizedAccessException`,
  `InvalidOperationException`.
- Invalid runtime state/input: appropriate `ArgumentException` variants and
  `ObjectDisposedException`.
- UI Automation: stale-element and COM exceptions.
- WinRT/OCR: activation, COM, and asynchronous operation exceptions.

Do not use an unexplained `catch (Exception)` that returns only "failed." If a
broad recoverable boundary is necessary, retain the exception type, operation,
and useful message while excluding fatal conditions by policy.

Avoid multiple nested public guards. Prefer an unguarded private `Core` method
called by one guarded public boundary, or guarded native adapters called by a
single public boundary. This keeps messages from being repeatedly wrapped.

### 3. Guard external and native boundaries

Every call that crosses into Win32, COM, WinRT, UI Automation, the file system,
services, processes, or another external runtime must have both forms of failure
handled:

1. A returned failure code, null handle, or unsuccessful status.
2. A thrown recoverable exception.

Private helpers named `Try...` must honor the same rule; a `Try` prefix must not
allow recoverable interop exceptions to escape.

Guard cleanup calls too. Releasing a handle, unsubscribing a hook, restoring
desktop state, disposing an object, or terminating a process can fail or throw.
Cleanup paths are part of the public contract.

### 4. Standardize compound results and cleanup

Track primary work and cleanup separately. Use these result rules:

| Primary operation | Required cleanup | Public result |
|---|---|---|
| Succeeded | Succeeded/not required | `true`, `message = null` |
| Failed | Succeeded/not required | `false`, primary failure message |
| Succeeded | Failed | `false`, cleanup failure message |
| Failed | Failed | `false`, message containing both failures |

Once a method acquires state or a resource, release it in `finally` or an
equivalent guaranteed cleanup path. Examples include:

- Mouse buttons and keyboard modifiers.
- Input blocking, cursor visibility, and cursor clipping.
- Window/event hooks and subscriptions.
- GDI objects, device contexts, images, streams, and clipboard ownership.
- Processes, redirected streams, cancellation registrations, and timers.
- Service controllers and handles.
- UI Automation/COM/WinRT objects where explicit cleanup is required.

Best-effort cleanup must not erase the primary failure. Conversely, cleanup
failure must not be discarded after primary success.

## Recommended internal design

Each project should use three layers where practical:

1. **Public Pega boundary** — initializes outputs, validates inputs, invokes the
   operation, and applies the exception/result contract.
2. **Operation core** — performs the workflow and explicitly tracks acquired
   state and required cleanup.
3. **External adapters** — small `Try...` wrappers for native, COM, WinRT, file,
   process, or service calls.

Illustrative structure:

```csharp
public bool PerformAction(string input, out string value, out string message)
{
    value = null;
    message = null;

    return TryRunPublicOperation(
        "PerformAction",
        () => PerformActionCore(input, out value, out message),
        ref message);
}
```

The exact helper signature may differ by project to avoid awkward `out` capture.
Consistency of behavior matters more than forcing one delegate shape everywhere.
Do not introduce a shared assembly dependency solely for this helper.

## Validation rules

Validate inputs before acquiring external state:

- Required strings and file paths.
- Enum values and flag masks.
- Zero, invalid, or stale handles where they can be checked.
- Negative or excessive durations, timeouts, retries, and polling intervals.
- Numeric overflow and non-finite floating-point values.
- Rectangle, coordinate, range, and collection-size constraints.

Prefer returning `false` with an actionable message for Pega-wired input errors
when the method advertises never-throws. Do not silently coerce values unless the
coercion is an intentional, documented feature.

## Message standard

Failure messages should contain:

- The operation that failed.
- The relevant target without exposing secrets.
- The native/error code or exception type when useful.
- A practical reason or next diagnostic step when known.

Messages should not contain:

- Passwords, tokens, full command secrets, or sensitive document content.
- Full exception stack traces intended only for diagnostic logs.
- Assertions that guess a cause as certainty.

Example:

```text
MoveTo failed: SetCursorPos returned false (Win32 error 5: Access is denied).
The desktop may be locked or the target may run at a higher integrity level.
```

When both primary work and cleanup fail, label both parts:

```text
Drag failed: SetCursorPos returned false. Cleanup also failed: the left mouse
button could not be released.
```

## Methods without `bool` and `message`

Do not claim never-throws for a method that cannot report why it failed.
Choose one of these deliberately:

- Add a `Try...`/`bool` plus message overload and keep the simple method only when
  its failure behavior is unambiguous.
- Return a documented sentinel for normal absence while offering a message-bearing
  method for operational failures.
- Allow documented exceptions when exception semantics are genuinely preferable.

Avoid silently converting an operational failure to an ordinary `false`, zero,
empty collection, or null when the caller cannot distinguish the two.

## Testing requirements

Every project adopting the standard needs tests for:

- Each validation branch and output sentinel.
- Returned external/native failures.
- Thrown recoverable exceptions at external boundaries.
- Primary success plus cleanup failure.
- Primary failure plus cleanup success.
- Primary failure plus cleanup failure.
- No acquired resource or state remains after a compound failure.
- Normal negative results remain distinguishable from failures.
- Success always returns `message = null`.

Introduce internal adapters/delegates or interfaces for fault injection rather
than requiring every failure test to manipulate a live Windows desktop. Keep a
separate Windows integration suite for real interop behavior.

## Adoption workflow for each utility

1. Inventory public methods and current exception claims.
2. Record method-specific success, negative, sentinel, and cleanup semantics.
3. Add the project-local guard and external adapters.
4. Refactor compound operations to guaranteed cleanup.
5. Add fault-injection and regression tests.
6. Update XML documentation, designer descriptions, README, and examples.
7. Build with zero warnings and run platform-independent tests.
8. Run Windows integration tests under supported desktop conditions.
9. Load the assembly in supported Pega Robot Studio and Robot Runtime versions.

Adopt one utility at a time. Do not mechanically wrap every method before its
state ownership and cleanup requirements are understood.

## Utility-specific cleanup focus

| Utility | Primary cleanup/failure focus |
|---|---|
| MouseUtils | Button/modifier release; cursor, clipping, and input-block restoration. |
| KeyboardUtils | Key/modifier release and clipboard restoration. |
| WindowUtils | Stale handles, asynchronous close/activation, and window-state races. |
| DialogUtils | Stale controls, message delivery, timeout, and retry ambiguity. |
| ScreenCaptureUtils | GDI/DC/image/stream/clipboard disposal and partial files. |
| OcrUtils | WinRT activation, async completion, language availability, and image disposal. |
| UIAutomationUtils | Stale elements, COM exceptions, timeouts, and pattern availability. |
| CommandLineUtils | Process/stream disposal, timeout termination, deadlocks, and partial output. |
| ServiceUtils | Controller/handle disposal and service-state races. |
| EventUtils | Hook unregistration, callback isolation, subscription cleanup, and concurrency. |

## Completion criteria

A utility conforms when:

- Every public never-throws claim is enforced at the code boundary.
- Outputs and messages follow one consistent contract.
- Required cleanup is guaranteed and tested under partial failure.
- External return failures and thrown exceptions are both covered.
- Documentation describes normal negative outcomes separately from failures.
- Unit, Windows integration, and supported Pega-host verification are recorded.
