# Running a machine

```csharp
machine.Start(out string state, out string message);                    // enters the initial state
machine.Fire("submit", out state, out message);
```

## A declined trigger is not an error

`Fire` returns **`True`** when it worked out what to do - including when the
answer is "no" - and `message` tells you which:

| Result | `message` | Meaning |
|---|---|---|
| `True` | *(empty)* | The transition happened; `newState` is the state you are now in. |
| `True` | `NoTransition` | The current state has no transition for that trigger. |
| `True` | `GuardFailed` | It has some, but every candidate's guards failed. |
| `True` | `Finished` | The machine is in a final state. |
| `True` | `NotStarted` | `Start` has not been called. |
| `True` | `ReentrancyLimit` | Handlers nested `Fire` more than 16 deep (see [Events](Events.md)). |
| `False` | *error text* | Bad input (an empty trigger, no definition loaded) or a persistence write failure. |

`newState` is the state the machine is (still) in, so you can branch on it
either way. A `False` result is always an error, and then the machine is
unchanged. Which guard failed is not in `message` (it is a short code you can
switch on); it is in the `TransitionRejected` event and in the history.

`FireSimple(trigger, out message)` is the same thing without `newState`:

```csharp
machine.FireSimple("approve", out message);   // True + empty message: fired
                                              // True + "GuardFailed": declined
                                              // False + text: an error
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
