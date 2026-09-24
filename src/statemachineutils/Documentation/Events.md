# Events

| Event | When |
|---|---|
| `StateExited` | the machine leaves a state |
| `TransitionFired` | once per successful transition |
| `StateEntered` | the machine enters a state - also the initial state on `Start` / `Reset` |
| `MachineFinished` | after `StateEntered`, when the new state is final |
| `TransitionRejected` | a trigger was declined (`NoTransition`, `GuardFailed`, `Finished`, `NotStarted`) |

For a successful transition the order is always `StateExited`, `TransitionFired`,
`StateEntered`, then `MachineFinished` if the destination is final. The payload
(`PreviousState`, `NewState`, `Trigger`; or `State`, `Trigger`, `Reason`,
`Detail` for a rejection) is all non-null text, so it wires to a data port; an
empty string means "not applicable".

```csharp
machine.StateEntered      += (s, e) => Log($"{e.PreviousState} -> {e.NewState}");
machine.TransitionRejected += (s, e) => Log($"{e.Trigger} declined in {e.State}: {e.Reason}");
machine.MachineFinished   += (s, e) => Notify($"finished in {e.NewState}");
```

## Threading

Unlike `FileWatchUtils` and `InterruptUtils`, this component starts no threads.
Events are raised **synchronously on the thread that called `Fire`, `Start` or
`Reset`**, after the change is committed and outside the component's lock.
That means:

- a handler sees the **new** state (`machine.CurrentState` already reflects it);
- a handler may safely call back into the machine, even from another thread;
- the call that raised the event does not return until every handler has run.

## What a handler must not do

- **Do not advance the machine in a loop through handlers.** A handler that
  fires the next trigger nests one level deeper each time; nesting is capped at
  16 per thread. Past that, `Fire` returns `ReentrancyLimit` (recorded in
  history, deliberately with no event, so a `TransitionRejected` handler cannot
  recurse forever) and `Start`/`Reset` return `False`. Use a flat loop that reads
  `CurrentState` and calls `Fire` - see [Working a queue](WorkingAQueue.md).
  Short, bounded chains of a few transitions are fine.
- **Do not rely on a handler's exception being seen.** A handler that throws is
  caught and logged (`Debug.WriteLine`); the other handlers still run and the
  change stands, because it was committed before any event fired.

## Not raised

`CanFire`, the query methods, context changes and restoring a
[persisted](Persistence.md) run raise no events. A change that could not be
saved (persistence write failure) raises none either, because it did not happen.
