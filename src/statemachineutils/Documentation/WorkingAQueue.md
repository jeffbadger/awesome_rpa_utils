# Worked example: a robot that works a queue

This page builds a complete, crash-safe worker for a queue of invoices using
`StateMachineUtils` for the **run-level control flow** and `LocalQueueUtils`
for the **item-level work**. The two divide the job cleanly:

| Question | Answered by |
|---|---|
| Which item is next? Was it completed? How many attempts are left? | `LocalQueueUtils` (leases, retry, reject) |
| Is the robot starting, working, waiting for work that isn't ready yet, finished, or stopped because too many items in a row failed? Where was it when it crashed? | `StateMachineUtils` |

The machine is what turns "a loop with a handful of flags" into something you
can read, resume after a crash, and reason about.

## The behaviour we want

- Open the queue and start working.
- Take the next item. If nothing is ready but work is still **outstanding** -
  retries scheduled for later, or a lease left behind by a crashed run that has
  not expired yet - wait and look again. Only when nothing is ready *and* nothing
  is outstanding is the run **finished**.
- For each item: complete it on success, reject it if its data is bad (a
  business problem, so retrying would be pointless), and retry it if something
  technical went wrong.
- If **five items in a row** fail technically, stop and ask for help instead of
  burning through the whole queue (a circuit breaker).
- Allow an operator to abort at any point.
- If the robot or the machine crashes mid-item, pick up where it left off.

## The state machine

```
                 opened
   [Starting] ----------> [Idle] <-------------------------------------+
                           |  ^  \                                     |
             polled        |  |   \ polled (work outstanding)             |
        (item taken)       |  |    v                                   |
                           v  |  [Waiting] --waitElapsed--> back to Idle
                     [Processing] --succeeded / rejected--> Idle
                       |    |  \-- recovered (after a crash) --> Idle
             failed x5 |    \----- failed (fewer than 5 in a row) --> Idle
                       v
                  [[Suspended]]        [[Finished]]  <- polled, nothing left
                                       [[Stopped]]   <- abort, from anywhere
```

`[[double brackets]]` are final states. As a definition:

```json
{
  "name": "InvoiceWorker",
  "initial": "Starting",
  "states": [
    "Starting",
    "Idle",
    "Waiting",
    "Processing",
    { "name": "Finished",  "final": true },
    { "name": "Suspended", "final": true },
    { "name": "Stopped",   "final": true }
  ],
  "transitions": [
    { "from": "Starting",   "trigger": "opened",      "to": "Idle" },

    { "from": "Idle",       "trigger": "polled",      "to": "Processing",
      "guards": [ { "key": "itemAvailable", "op": "equals",      "value": "true" } ] },
    { "from": "Idle",       "trigger": "polled",      "to": "Waiting",
      "guards": [ { "key": "pendingCount",  "op": "greaterThan", "value": 0 } ] },
    { "from": "Idle",       "trigger": "polled",      "to": "Finished" },

    { "from": "Waiting",    "trigger": "waitElapsed", "to": "Idle" },

    { "from": "Processing", "trigger": "succeeded",   "to": "Idle" },
    { "from": "Processing", "trigger": "rejected",    "to": "Idle" },
    { "from": "Processing", "trigger": "failed",      "to": "Suspended",
      "guards": [ { "key": "consecutiveFailures", "op": "greaterThan", "value": 4 } ] },
    { "from": "Processing", "trigger": "failed",      "to": "Idle" },
    { "from": "Processing", "trigger": "recovered",   "to": "Idle" },

    { "from": "*",          "trigger": "abort",       "to": "Stopped" }
  ]
}
```

Three things in that definition are worth understanding, because they are how
you express decisions without writing code:

1. **`polled` has three transitions from `Idle`, tried in order.** The first
   whose guards pass wins: an item was taken, else work is still outstanding
   (delayed retries, or a lease that has not expired), else the last, unguarded
   one is the "nothing left" fallback. This is the machine's
   if / else-if / else.
2. **`failed` is the circuit breaker.** The guarded transition to `Suspended`
   comes first; it only passes once `consecutiveFailures` exceeds 4. Otherwise
   the unguarded fallback returns to `Idle` for the next item.
3. **`"from": "*"` makes `abort` legal everywhere** (in any non-final state)
   with a single line.

Validate it before you build on it - `ValidateDefinitionJson` also reports
warnings such as unreachable states or a transition that an earlier one always
shadows. This definition produces none.

## What lives in the context

The context is the machine's memory of *facts the guards need*. Everything is
text, keys are case-insensitive:

| Key | Set by | Used by |
|---|---|---|
| `itemAvailable` | the poll step, from `TryTakeNext` | `Idle -> Processing` guard |
| `pendingCount` | the poll step: `delayed + inProgress` from `GetCounts` | `Idle -> Waiting` guard |
| `consecutiveFailures` | the process step | the circuit breaker guard |
| `itemId`, `leaseToken` | the poll step | the process step, and crash recovery |

Keep the item's **payload** out of the context: it is written to disk when
persistence is on, it can be large, and after a crash you cannot resume a
half-finished item anyway (its lease expires and the queue hands it out again).
Hold the payload in an ordinary automation variable.

## Setup

```csharp
// The queue survives crashes; the machine remembers where the robot was.
queue.CreateQueue("invoices", QueueLifetime.Persistent, null,
                  out string queuePath, out bool existed, out string message);

machine.LoadDefinitionJson(definitionJson, out message);

// Persistence must be enabled after the definition is loaded and BEFORE Start.
// If a previous run crashed, this restores its state, context and history.
machine.EnablePersistence("InvoiceWorker", out string statePath, out bool restored, out message);

machine.IsStarted(out bool started, out message);
if (!started)
{
    machine.SetContext("consecutiveFailures", "0", out message);
    machine.Start(out string state, out message);          // Starting; raises StateEntered
}
else
{
    // A resumed run. If it died mid-item, the machine still says Processing.
    machine.GetCurrentState(out string state, out message);
    if (state == "Processing")
    {
        queue.RecoverExpiredLeases(queuePath, out int recovered, out int rejected, out message);
        machine.Fire("recovered", out bool fired, out state, out string reason, out message);
    }
    else if (state == "Finished" || state == "Stopped")
    {
        // The previous run ended on its own terms; this launch is a new run over whatever is in the
        // queue now. Without this, a restored final state would end the loop below immediately.
        machine.Reset(true, out state, out message);                 // back to Starting, context cleared
        machine.SetContext("consecutiveFailures", "0", out message);
    }
    // "Suspended" is left alone on purpose: the last run gave up and needs a person (see "Operating
    // it"), so the loop below exits at once until someone Resets it.
}
```

Two ordering rules in that setup are easy to get wrong. `EnablePersistence` comes
**after** `LoadDefinitionJson` (it needs the definition to recognize a saved run)
and **before** `Start`. And `SetContext` comes **after** `EnablePersistence`:
restoring a saved run replaces the whole run state, so context set beforehand
would be thrown away - `EnablePersistence` refuses rather than let that happen
silently.

Restoring is silent - no events fire when a saved run is picked up, because
nothing changed - so the loop below reads the current state instead of waiting
for an event.

`RecoverExpiredLeases` only returns leases that have **already expired**; an item
leased by the crashed run stays "in progress" until its lease runs out (up to
`leaseSeconds` later). That is why the poll step counts in-progress work as
*pending*: without it, the restarted worker would find nothing ready and nothing
delayed and declare itself `Finished` while the crashed item was still waiting
to be handed out again.

## The loop

Drive the machine from a **flat loop that reads the current state and does that
state's work**. On the Robot Studio surface this is a `StringSwitch` on the state
text, one case output per state, each branch ending in a link back to the top of
the loop.

```csharp
while (true)
{
    machine.GetCurrentState(out string state, out message);

    if (state == "Starting")
        machine.Fire("opened", out _, out _, out _, out message);

    else if (state == "Idle")
        PollQueue();

    else if (state == "Waiting")
    {
        Pause(5000);
        machine.Fire("waitElapsed", out _, out _, out _, out message);
    }

    else if (state == "Processing")
        ProcessCurrentItem();

    else
        break;                       // Finished, Suspended or Stopped: a final state
}
```

### The `Idle` step: look for work

```csharp
void PollQueue()
{
    queue.TryTakeNext(queuePath, out bool itemAvailable, out string itemId, out string payload,
                      out string storedFilePath, out int attempt, out string leaseToken,
                      out string leaseExpiresUtc, out message, leaseSeconds: 300);

    queue.GetCounts(queuePath, out int ready, out int delayed, out int inProgress,
                    out int completed, out int rejected, out int corrupt, out message);

    machine.SetContext("itemAvailable", itemAvailable ? "true" : "false", out message);
    machine.SetContext("itemId", itemId ?? "", out message);
    machine.SetContext("leaseToken", leaseToken ?? "", out message);
    // Not ready yet, but not gone either: retries scheduled for later, plus any lease still
    // outstanding (this worker holds none at this point, so it is a leftover from a crash).
    machine.SetContext("pendingCount", (delayed + inProgress).ToString(), out message);
    currentPayload = payload;                         // an ordinary variable, not context

    // The machine decides: Processing, Waiting or Finished. No if/else here.
    machine.Fire("polled", out bool fired, out string newState, out string reason, out message);
}
```

### The `Processing` step: do the work, then report it

```csharp
void ProcessCurrentItem()
{
    machine.GetContext("itemId",     out _, out string itemId,     out message);
    machine.GetContext("leaseToken", out _, out string leaseToken, out message);

    bool ok = HandleInvoice(currentPayload, out string businessProblem, out string technicalProblem);

    if (ok)
    {
        queue.CompleteItem(queuePath, itemId, leaseToken, out message, resultJson: "{\"posted\":true}");
        machine.SetContext("consecutiveFailures", "0", out message);
        machine.Fire("succeeded", out _, out _, out _, out message);
    }
    else if (businessProblem != null)
    {
        // Bad data: retrying cannot help. Reject it and move on; it is not a "failure".
        queue.RejectItem(queuePath, itemId, leaseToken, businessProblem, out message);
        machine.Fire("rejected", out _, out _, out _, out message);
    }
    else
    {
        queue.RetryItem(queuePath, itemId, leaseToken, technicalProblem,
                        out bool willRetry, out bool rejectedNow, out message, delaySeconds: 30);

        machine.GetContext("consecutiveFailures", out _, out string count, out message);
        int.TryParse(count, out int failures);
        machine.SetContext("consecutiveFailures", (failures + 1).ToString(), out message);

        // The machine trips the breaker (Suspended) or returns to Idle.
        machine.Fire("failed", out _, out _, out _, out message);
    }
}
```

**Report to the queue first, then fire the trigger.** The order matters when
the robot dies between the two calls:

| Order | Crash between the two calls | Result |
|---|---|---|
| queue, **then** trigger | item is already completed; machine still says `Processing` | resume fires `recovered`, goes to `Idle`. Correct, nothing redone. |
| trigger, **then** queue | machine says `Idle`; item is still leased | the lease later expires and the item is processed **again**. |

For long items, call `queue.RenewLease(...)` from inside `HandleInvoice` so the
lease does not expire while the work is still running.

## Reacting to what happens: events

The loop does the work; **events are for the things that should happen *because*
of a state change** - logging, alerts, cleanup. Subscribe once, at setup:

```csharp
machine.StateEntered += (s, e) =>
    Log($"{e.PreviousState} -> {e.NewState} (trigger '{e.Trigger}')");

machine.TransitionRejected += (s, e) =>
    Log($"Unexpected: '{e.Trigger}' declined in {e.State}: {e.Reason} - {e.Detail}");

machine.MachineFinished += (s, e) =>
{
    if (e.NewState == "Suspended")
        SendAlert("InvoiceWorker stopped after 5 consecutive failures - needs a person.");
    else if (e.NewState == "Finished")
        queue.DeleteCompletedItems(queuePath, 7, out int removed, out message);   // housekeeping
};
```

Events fire on the thread that called `Fire`, after the state change is
committed, so a handler always sees the new state. A `TransitionRejected` in
this worker is a bug signal: every legitimate trigger above should be legal in
the state that fires it.

### Do not run the loop through event handlers

It is tempting to make each `StateEntered` handler do the work for that state
and fire the next trigger, so the machine "drives itself". **Don't, for a loop
that runs over many items.** Each nested `Fire` inside a handler adds a level
of nesting, and nesting is capped at 16 - the worker above would refuse to
continue after about eight items, with `ReentrancyLimit`. The flat loop has no
such limit because every `Fire` returns before the next one starts. Handlers are
for reactions, not for advancing the machine.

## Operating it

- **Stop it from anywhere:** `machine.Fire("abort", ...)` from a button or a
  second automation thread. The loop sees `Stopped` on its next turn. Work
  already leased simply expires and returns to the queue (the queue counts the
  interrupted attempt against the item's attempt limit).
- **See why it stopped:** `machine.GetHistoryJson(out json, out message)` returns
  every start, transition and declined trigger, oldest first.
- **Resume a `Suspended` run** after the underlying problem is fixed:
  `machine.Reset(false, out state, out message)` returns to `Starting` and keeps
  the context; set `consecutiveFailures` back to `"0"` first, or the breaker
  trips again on the next failure.
- **Start completely fresh:** `machine.DisablePersistence(...)`, then
  `machine.DiscardPersistedState("InvoiceWorker", out discarded, out message)`.
- **Detect a wedged item:** `machine.GetSecondsInState(out double seconds, ...)`
  while in `Processing`; if it exceeds your limit, `Fire("failed")` or `abort`.

## A run, end to end

Queue: invoice A (good), B (bad data), C (technical failure, retried after 30 s),
D (good).

| Step | State entering | What happened |
|---|---|---|
| 1 | `Idle` | started; `opened` |
| 2 | `Processing` | polled: A taken |
| 3 | `Idle` | A completed (`succeeded`) |
| 4 | `Processing` | polled: B taken |
| 5 | `Idle` | B rejected (`rejected`), not counted as a failure |
| 6 | `Processing` | polled: C taken |
| 7 | `Idle` | C failed technically (`failed`, count 1), retry scheduled for 30 s |
| 8 | `Processing` | polled: D taken |
| 9 | `Idle` | D completed; `consecutiveFailures` reset to 0 |
| 10 | `Waiting` | polled: nothing ready, but C is delayed |
| 11 | `Idle` | 5 s later (`waitElapsed`) |
| 12 | `Processing` | polled: C's retry is due, taken again |
| 13 | `Idle` | C completed |
| 14 | `Finished` | polled: nothing ready, nothing pending; `MachineFinished` tidies completed items older than a week |

If the robot's machine loses power at step 6, the restart restores state
`Processing` and `recovered` sends it to `Idle`. The poll then finds nothing
ready but C's lease still outstanding, so the machine goes to `Waiting` rather
than `Finished`; once the lease expires the queue hands C back out and it is
processed. No item is lost and none is completed twice.
