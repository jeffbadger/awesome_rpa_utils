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
- **Restoring is silent:** no events fire, because nothing changed. Read
  `CurrentState` to see where the run stopped.
- What is saved: current state, when it was entered, the context, and the
  history. `GetSecondsInState` keeps counting from the original entry time.

## Durable first

With persistence enabled, every state change and every context change is
**written before it is applied**. If the write fails (disk full, permissions,
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
| Saved file is corrupt, from a newer schema, or names an unknown state | `False` with a message, same remedy. |
| Another component or process already owns that `machineName` | `False` ("already open"). One owner per saved machine. |
| Enabling after `Start` | `False`: the saved run would be overwritten. |
| Changing the definition while enabled | `False`: the saved run belongs to the current definition. |

## Letting go

```csharp
machine.DisablePersistence(out message);                       // keeps the saved file; releases ownership
machine.DiscardPersistedState("InvoiceWorker", out bool discarded, out message);  // abandon it
```

`DiscardPersistedState` is refused while this component (or another owner) has
that machine enabled. Disposing the component releases ownership.

## Cautions

- **Context is saved in plain text.** Never put a password, token or personal
  data in it. History and event details never contain context values.
- Persistence keeps the machine's memory, not the world's. After a crash the
  outside world may have moved on - a queue item may have been leased, a form
  half filled. Decide what each state means on resume (the queue-worker example
  maps a resumed `Processing` to a `recovered` trigger).
- Every change writes the whole state, including up to `MaximumHistoryEntries`
  history entries. Leave it at the default 100 unless you need a longer trail.
