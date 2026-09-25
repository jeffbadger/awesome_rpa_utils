# Persistence

By default a machine lives and dies with its component. Enable persistence and a
long-running flow can resume after a crash, a restart or a reboot.

```csharp
machine.LoadDefinitionJson(json, out string message);
machine.EnablePersistence("InvoiceWorker", out string statePath, out bool restored, out message);

machine.IsStarted(out bool started, out message);
if (!started) machine.Start(out string state, out message);     // fresh run
else          Log("resumed in " + machine.CurrentState);        // picked up where it stopped
```

- Call it **after** the definition is loaded and **before** `Start`. The saved
  run is restored (`restored == True`) or a fresh one is begun.
- The state lives in `%LOCALAPPDATA%\AwesomeRpaUtils\StateMachines\<machineName>\`
  (per Windows user, machine-local). `statePath` tells you where.
- **Set the context after enabling persistence.** Restoring replaces the whole run
  state with the saved one, so context set *before* `EnablePersistence` would be
  discarded; when a saved run exists the call is refused with a message instead of
  losing it silently. (With no saved run, context set beforehand is kept.)
- **A run that ended stays ended.** Restoring a machine that had reached a final
  state gives you a machine in that final state, so a restart does not begin a new
  run by itself. `Reset` starts one; see the setup in
  [Working a queue](WorkingAQueue.md).
- **Restoring is silent:** no events fire, because nothing changed. Read
  `CurrentState` to see where the run stopped.
- What is saved: current state, when it was entered, the context, and the
  history. `GetSecondsInState` keeps counting from the original entry time.

## Durable first

With persistence enabled, every state change and every context change is
**written before it is applied** (and flushed to the disk before the file is swapped in,
so a power loss right after cannot leave an empty or partial saved state). If the write fails (disk full, permissions,
antivirus lock) the call returns `False` with a message and **nothing changes** -
no transition, no context change, no events. You can fix the problem and retry
the same call. The machine can never be in a state that disk does not know about.

Declined triggers are recorded in history but do not cause a write, so a
`Fire` that would only be declined cannot fail because of a disk problem; they
reach disk with the next real change.

## What is refused, and why

| Situation | Result |
|---|---|
| Saved run was made with a **different definition** | `EnablePersistence` returns `False` naming `DiscardPersistedState`. It is never silently resumed into a machine it does not fit. |
| Saved file is larger than 64 MB, or holds more context keys, longer keys/values, or more history entries than this component would ever write | `False` with a message, same remedy. A saved context is treated as untrusted input and held to the same limits as `SetContext`. |
| The saved **sequence counter** is negative or at `long.MaxValue` (the next entry would wrap it) | `False` with a message, same remedy. The component itself never writes a counter it would refuse: past the ceiling a change fails with "sequence counter is exhausted" and `Reset` (which clears the history and restarts the numbering) recovers. |
| A saved **history record** is invalid: unknown kind, missing or unparseable timestamp, a record missing what its kind needs, a state the definition does not declare, a **rejection reason** other than NoTransition / GuardFailed / Finished / NotStarted / ReentrancyLimit, fields a kind never carries (a reason on a transition, a destination on a rejection), a transition the definition does not declare, a rejection whose reason does not fit the state it was made in ('Finished' outside a final state, 'NoTransition'/'GuardFailed' inside one, 'NotStarted' naming a state), a start/reset that enters anything but the definition's initial state, a move out of a final state (even through a wildcard), moves that do not chain (each move must leave the state the previous one entered, and the last must end in the saved current state), sequence numbers that do not run consecutively, a last number that does not match the saved sequence counter, or an **empty history** where one cannot be (a started machine always has at least its start entry, and a counter above zero means entries existed) | `False` with a message naming the bad entry, same remedy. History is read back by `GetHistoryJson` and numbered onward from, so it is checked as strictly as the rest. |
| The saved file records a **different machine name** than the one requested (a `state.json` copied or renamed in from another machine's folder) | `False` naming both names, same remedy. The folder name is the machine's identity, so a run is never resumed under the wrong one, even when two machines share a definition. |
| Saved file is corrupt, incomplete (any of the nine top-level fields absent - including `started`, which would otherwise read as "not started" - or a missing or invalid `enteredUtc` - required of unstarted snapshots too), inconsistent (not started yet naming a current state), from a newer schema, or names an unknown state | `False` with a message, same remedy. It is never patched up: a missing timestamp is not replaced with "now", and a missing context or history is not treated as empty. |
| Another component or process already owns that `machineName` | `False` ("already open"). One owner per saved machine. |
| Enabling after `Start` | `False`: the saved run would be overwritten. |
| Changing the definition while enabled | `False`: the saved run belongs to the current definition. |

## Letting go

```csharp
machine.DisablePersistence(out message);                       // keeps the saved file; releases ownership
machine.DiscardPersistedState("InvoiceWorker", out bool discarded, out message);  // abandon it
```

`DiscardPersistedState` is refused while this component (or another owner) has
that machine enabled. Disposing the component releases ownership. It removes only
the saved state, while holding the ownership lock; the (empty) folder and the lock
marker file are deliberately left, because deleting them after letting go of the
lock could race with a new owner acquiring it.

## Cautions

- **Context is saved in plain text.** Never put a password, token or personal
  data in it. History and event details never contain the context's actual values.
- Persistence keeps the machine's memory, not the world's. After a crash the
  outside world may have moved on - a queue item may have been leased, a form
  half filled. Decide what each state means on resume (the queue-worker example
  maps a resumed `Processing` to a `recovered` trigger).
- Every change writes the whole state, including up to `MaximumHistoryEntries`
  history entries. Leave it at the default 100 unless you need a longer trail.
  Lowering `MaximumHistoryEntries` hides older entries immediately but does not
  rewrite the saved file by itself; the next change that is saved - a transition
  or a context change - trims it.
