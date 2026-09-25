# Running a machine

```csharp
machine.Start(out string state, out string message);                    // enters the initial state
bool fired = machine.Fire("submit", out state, out string reason, out message);
```

## The result means "it fired"

`Fire` returns **`True` only when the transition happened**, and `message` always
says what happened ("Fired 'submit': Draft -> Review.", or why it did not).
When it returns `False`, `rejectionReason` tells a *declined* trigger from an
*error*:

| Result | `rejectionReason` | Meaning |
|---|---|---|
| `True` | `null` | The transition happened; `newState` is the state you are now in. |
| `False` | `NoTransition` | The current state has no transition for that trigger. |
| `False` | `GuardFailed` | It has some, but every candidate's guards failed. |
| `False` | `Finished` | The machine is in a final state. |
| `False` | `NotStarted` | `Start` has not been called. |
| `False` | `ReentrancyLimit` | Handlers nested `Fire` more than 16 deep (see [Events](Events.md)). |

`newState` is the state the machine is (still) in, so you can branch on it
either way. A `False` result with **no** `rejectionReason` is an error, not a
decline: bad input (an empty trigger, no definition loaded) or a persistence
write failure. The machine is unchanged, and `message` says what was wrong.

If you only need to know whether it fired, use the minimal form:

```csharp
if (!machine.FireSimple("approve", out message))
    Log(message);          // the reason it did not fire; when it fires, message says "Fired 'approve': ..."
```

## Asking without acting

```csharp
machine.CanFire("approve", out bool can, out string reason, out message);
machine.GetAvailableTriggersDelimited(out string triggers, out message);          // "approve,reject"
machine.GetAvailableTriggersDelimited(out triggers, out message, delimiter: "|"); // "approve|reject"
machine.GetAvailableTriggersJson(out string json, out message);                   // ["approve","reject"]
```

These evaluate guards against the current context, so a trigger whose guard
would fail is not listed. None of them raises events or changes anything.

## Where am I?

```csharp
machine.GetCurrentState(out state, out message);      // "" before Start
machine.IsStarted(out bool started, out message);
machine.IsInFinalState(out bool isFinal, out message);
machine.GetSecondsInState(out double seconds, out message);   // poll for a stuck step
```

The read-only properties `CurrentState`, `IsFinished` and `MachineName` expose
the same information to a Robot Studio data port.

## Starting over

`Reset(clearContext, ...)` returns to the initial state, clears the history and
raises `StateEntered`; it also starts a machine that was never started, and it
restarts one that has finished. Pass `clearContext: true` to wipe the context
too.

## History

```csharp
machine.GetHistoryJson(out string history, out message, maxEntries: 20);
```

Oldest first. Sequence numbers (`seq`) run consecutively and restart at 1 whenever the
history is cleared (`Start`, `Reset`, or replacing the definition). Each entry: `seq`, `utc`, `kind` (`start`, `reset`, `transition`,
`rejected`), `trigger`, `from`, `to`, `reason`, `detail` (fields that do not
apply are omitted). Declined triggers are recorded, which makes "why did nothing
happen?" answerable after the fact. History is bounded by
`MaximumHistoryEntries` (default 100); the oldest entries are dropped first.
Details name a failing guard as declared in the definition (key, operator and
the expected value) but never the context's actual value.
