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
empty string means "not applicable". `StateExited` adds one number,
`ElapsedMs`: how long the machine was in the state it is leaving.

```csharp
machine.StateExited       += (s, e) => Log($"left {e.PreviousState} after {e.ElapsedMs} ms");
machine.StateEntered      += (s, e) => Log($"{e.PreviousState} -> {e.NewState}");
machine.TransitionRejected += (s, e) => Log($"{e.Trigger} declined in {e.State}: {e.Reason}");
machine.MachineFinished   += (s, e) => Notify($"finished in {e.NewState}");
```

`ElapsedMs` runs from the state's recorded entry time to the entry time recorded for
the new state - the same timestamp that is saved with the run when persistence is on -
so the durations of successive states add up exactly. It does not count the time a
persistence write or your handlers take (that time falls in the *new* state). It is
never negative, and for a run restored from persistence it includes the time the robot
was down. It is only reported on a transition that fired: a declined trigger raises no
`StateExited`.

## Threading

Unlike `FileWatchUtils` and `InterruptUtils`, this component starts no threads.
Events are raised **synchronously on the thread that called `Fire`, `Start` or
`Reset`**, after the change is committed and outside the component's lock.
That means:

- a handler sees the **new** state (`machine.CurrentState` already reflects it);
- a handler may safely call back into the machine, even from another thread;
- the call that raised the event does not return until every handler has run;
- **concurrent callers take turns.** A transition and its events are one unit:
  while one thread's handlers run, another thread's `Fire` / `Start` / `Reset`
  waits, so a handler can never be told about one transition after another
  thread has already committed the next. Events therefore arrive in exactly the
  order the transitions happened. Reads (`CurrentState`, history, `CanFire`, the
  context methods) never wait for handlers.

## What a handler must not do

- **Do not advance the machine in a loop through handlers.** A handler that
  fires the next trigger nests one level deeper each time; nesting is capped at
  16 per thread. Past that, `Fire` returns `ReentrancyLimit` (recorded in
  history, deliberately with no event, so a `TransitionRejected` handler cannot
  recurse forever) and `Start`/`Reset` return `False`. Use a flat loop that reads
  `CurrentState` and calls `Fire` - see [Working a queue](WorkingAQueue.md).
  Short, bounded chains of a few transitions are fine.
- **Do not replace the definition or restore state from a handler.** `LoadDefinitionJson`,
  `ClearDefinition` and `EnablePersistence` (which can restore a saved run) change the machine wholesale, which would pull the rug out from under the
  event batch still being delivered, so they are refused (`False` with a message)
  when called from inside one of this machine's handlers. From another thread they
  simply wait for the handlers to finish.
- **Do not make a handler wait for another thread that fires the same
  machine.** That thread is queued behind the handler, so the two would wait on
  each other. Have the handler start work and return, and let the other thread
  fire on its own.
- **Disposing from a handler is safe.** The transition that raised the event has already
  committed and its remaining handlers still run; every later call reports the component
  as disposed. `Dispose` itself never waits for handlers to finish.
- **Do not rely on a handler's exception being seen.** A handler that throws is
  caught and logged (`Debug.WriteLine`); the other handlers still run and the
  change stands, because it was committed before any event fired.

## Not raised

`CanFire`, the query methods, context changes and restoring a
[persisted](Persistence.md) run raise no events. A change that could not be
saved (persistence write failure) raises none either, because it did not happen.
