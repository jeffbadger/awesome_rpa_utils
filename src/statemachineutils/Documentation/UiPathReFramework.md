# Tutorial: reproduce the UiPath REFramework with a state machine

UiPath's **Robotic Enterprise Framework (REFramework)** is a state machine: Init,
Get Transaction Data, Process Transaction, End Process, with rules for what happens
after a business or a system exception. This tutorial rebuilds that control flow with
`StateMachineUtils` and `LocalQueueUtils`, step by step, so a Pega Robot Studio
automation gets the same well-understood structure: retry after a technical failure,
skip a bad item without counting it against the robot, stop after too many failures in a
row.

If you have not used the component before, do the [first tutorial](Tutorial.md) first.
The definition and the loop below are exercised by the test suite
(`ReFrameworkTutorialTests`), so what you read is what runs.

## Step 1 - What maps to what

| REFramework | Here |
|---|---|
| The four states (Init, Get Transaction Data, Process Transaction, End Process) | The four states of the machine |
| Transitions between states, with conditions | Triggers, with guards for the conditions |
| Orchestrator queue, `TransactionItem`, "Set Transaction Status" | `LocalQueueUtils`: `TryTakeNext`, `CompleteItem`, `RejectItem`, `RetryItem` |
| `BusinessRuleException` | You call `RejectItem`, then fire `businessException` |
| Any other exception (a *system exception*) | You call `RetryItem`, then fire `systemException` |
| `ConsecutiveSystemExceptions` vs `MaxConsecutiveSystemExceptions` | Two context values and one guard |
| Item retries (`MaxRetryNumber`) | The queue's per-item `maximumAttempts`: `RetryItem` reports `willRetry` / `rejected` |
| End Process: report, close applications | The `MachineFinished` event and the End Process branch of your loop |

What stays in your automation, exactly as in UiPath: reading configuration, opening and
closing applications, and the work of each transaction. The machine owns **which step
comes next and why**; it never runs your steps.

## Step 2 - The flow

```
        initialized                      fetched (transaction found)
 [Init] ------------> [GetTransactionData] --------------------------> [ProcessTransaction]
   |  ^                  |   |  ^                                         |   |   |
   |  |                  |   |  +------- success / businessException -----+   |   |
   |  |  systemException |   |                                                |   |
   |  +------------------+   | fetched (nothing left)                         |   |
   |  +---------------------------- systemException (under the limit) --------+   |
   |                          v                                                   |
   | systemException     [[EndProcess]] <--- systemException (limit reached) -----+
   +---------------------->
```

`[[EndProcess]]` is the final state. A system exception normally goes back to **Init**
(so the applications are closed and reopened for a clean start), unless too many have
happened in a row, in which case the run ends.

## Step 3 - The definition

```json
{
  "name": "ReFramework",
  "initial": "Init",
  "states": [
    "Init",
    "GetTransactionData",
    "ProcessTransaction",
    { "name": "EndProcess", "final": true }
  ],
  "transitions": [
    { "from": "Init", "trigger": "initialized", "to": "GetTransactionData" },
    { "from": "Init", "trigger": "systemException", "to": "EndProcess" },

    { "from": "GetTransactionData", "trigger": "fetched", "to": "ProcessTransaction",
      "guards": [ { "key": "transactionFound", "op": "equals", "value": "true" } ] },
    { "from": "GetTransactionData", "trigger": "fetched", "to": "EndProcess" },
    { "from": "GetTransactionData", "trigger": "systemException", "to": "EndProcess",
      "guards": [ { "key": "maxConsecutiveReached", "op": "equals", "value": "true" } ] },
    { "from": "GetTransactionData", "trigger": "systemException", "to": "Init" },

    { "from": "ProcessTransaction", "trigger": "success", "to": "GetTransactionData" },
    { "from": "ProcessTransaction", "trigger": "businessException", "to": "GetTransactionData" },
    { "from": "ProcessTransaction", "trigger": "systemException", "to": "EndProcess",
      "guards": [ { "key": "maxConsecutiveReached", "op": "equals", "value": "true" } ] },
    { "from": "ProcessTransaction", "trigger": "systemException", "to": "Init" }
  ]
}
```

Read it as the REFramework's transition table:

- **`fetched` has two exits from `GetTransactionData`**, tried in order: a transaction was
  found (go process it), otherwise there is nothing left (end). One trigger, and the
  guard makes the decision.
- **`systemException` is guarded the same way from two states.** The first transition
  that passes wins: if the limit has been reached, end; otherwise back to `Init`.
- **`businessException` goes straight back for the next item** and, unlike
  `systemException`, is never counted.
- A failure inside `Init` ends the run: there is nothing to retry into.

`ValidateDefinitionJson` reports no errors or warnings for it.

## Step 4 - The context: the framework's "in arguments"

Guards can only read the machine's context, so the two facts they need live there:

| Key | Meaning | Set by |
|---|---|---|
| `transactionFound` | `"true"` or `"false"` | the Get Transaction Data step |
| `consecutiveSystemExceptions` | how many in a row so far | the exception handler |
| `maxConsecutiveReached` | `"true"` once the count reaches your limit | the exception handler |

The limit itself (`MaxConsecutiveSystemExceptions`, say 3) is an ordinary automation
variable read from your config. **The machine holds only what guards decide on**; the
transaction payload stays in an automation variable, exactly like `TransactionItem` in
UiPath.

## Step 5 - Set up

```csharp
const int MaxConsecutiveSystemExceptions = 3;      // from your Config

machine.LoadDefinitionJson(definitionJson, out string message);
machine.SetContext("consecutiveSystemExceptions", "0", out message);
machine.SetContext("maxConsecutiveReached", "false", out message);
machine.SetContext("transactionFound", "false", out message);
machine.Start(out string state, out message);                 // state == "Init"
```

## Step 6 - The loop

The REFramework is a loop that runs the current state's workflow, then follows the
transition it produced. Do the same: a flat loop that reads the current state.
In Robot Studio this is a `StringSwitch` on `CurrentState` with one output per state,
each branch ending in a link back to the top.

```csharp
while (true)
{
    machine.GetCurrentState(out state, out message);

    if      (state == "Init")               RunInit();
    else if (state == "GetTransactionData") RunGetTransactionData();
    else if (state == "ProcessTransaction") RunProcessTransaction();
    else                                    break;        // EndProcess: the final state
}
```

## Step 7 - Init

Open applications, read settings. Any failure is a system exception:

```csharp
void RunInit()
{
    bool ok = InitAllSettings() && InitAllApplications(out string error);

    if (ok)
        machine.Fire("initialized", out _, out _, out _, out message);
    else
        machine.Fire("systemException", out _, out _, out _, out message);   // Init -> EndProcess
}
```

Init is also where every system-exception retry lands, so `InitAllApplications` should
first close whatever is left open, as the UiPath template's `KillAllProcesses` does.

## Step 8 - Get Transaction Data

```csharp
void RunGetTransactionData()
{
    bool ok = queue.TryTakeNext(queuePath, out bool found, out string itemId, out string payload,
                                out _, out _, out string leaseToken, out _, out message);
    if (!ok) { OnSystemException(null, null, message); return; }

    currentItemId = itemId;  currentLease = leaseToken;  currentPayload = payload;   // like TransactionItem
    machine.SetContext("transactionFound", found ? "true" : "false", out message);

    // Found -> ProcessTransaction. Nothing left -> EndProcess. The guard decides.
    machine.Fire("fetched", out _, out _, out _, out message);
}
```

`TryTakeNext` hands out fresh items and due retries alike (highest priority first, then
whichever became available earliest), so a retried transaction simply arrives here again
on a later turn - behind items that were already waiting, unless you gave it a higher
priority.

## Step 9 - Process Transaction

```csharp
void RunProcessTransaction()
{
    try
    {
        ProcessTransaction(currentPayload);          // the real work; throws on failure

        queue.CompleteItem(queuePath, currentItemId, currentLease, out message);
        machine.SetContext("consecutiveSystemExceptions", "0", out message);   // a success resets the run
        machine.SetContext("maxConsecutiveReached", "false", out message);
        machine.Fire("success", out _, out _, out _, out message);
    }
    catch (BusinessRuleException ex)
    {
        // Bad data: retrying cannot help, and it says nothing about the robot's health.
        queue.RejectItem(queuePath, currentItemId, currentLease, ex.Message, out message);
        machine.Fire("businessException", out _, out _, out _, out message);
    }
    catch (Exception ex)
    {
        OnSystemException(currentItemId, currentLease, ex.Message);
    }
}
```

In Robot Studio, `ProcessTransaction` is your automation, and the two `catch` branches
are the Try/Catch component's exception outputs.

**Report to the queue first, then fire the trigger.** If the robot dies between the two
calls, the queue is already right and the machine merely replays the step; the other
order can process an item twice. `WorkingAQueue.md` walks through this.

## Step 10 - The system-exception handler: retries and the circuit breaker

```csharp
void OnSystemException(string itemId, string lease, string error)
{
    if (itemId != null)   // an item was in flight: hand it back; the queue retries or rejects it per its attempt limit
        queue.RetryItem(queuePath, itemId, lease, error, out bool willRetry, out bool rejectedNow,
                        out message, delaySeconds: 0);

    machine.GetContext("consecutiveSystemExceptions", out _, out string text, out message);
    int.TryParse(text, out int count);
    count++;
    machine.SetContext("consecutiveSystemExceptions", count.ToString(), out message);
    machine.SetContext("maxConsecutiveReached",
                       count >= MaxConsecutiveSystemExceptions ? "true" : "false", out message);

    // Back to Init for a clean restart, or to EndProcess if the limit was reached. The guard decides.
    machine.Fire("systemException", out _, out _, out _, out message);
}
```

This one method is the REFramework's "system exception" logic. The machine picks Init
or EndProcess; you never write an `if (count >= max)` in the loop.

The two retry limits are separate, as in UiPath: **per item** (the `maximumAttempts` you
gave `AddJson` - 3 by default - decides, through `RetryItem`, whether an item is retried
or finally rejected) and
**per run** (consecutive failures, decided by the machine).

## Step 11 - End Process

The loop ends when the machine reaches its final state, and `MachineFinished` fires once.
Put the REFramework's End Process work in the handler or right after the loop:

```csharp
machine.StateEntered += (s, e) => Log($"{e.PreviousState} -> {e.NewState}");
machine.MachineFinished += (s, e) =>
{
    CloseAllApplications();
    queue.GetCounts(queuePath, out int ready, out int delayed, out int inProgress,
                    out int completed, out int rejected, out int corrupt, out message);
    Log($"Finished: {completed} completed, {rejected} rejected");
};
```

Also wire `TransitionRejected` to a log line: a declined trigger in this design means a
step fired something it should not have, and the history says exactly what
(`GetHistoryJson`).

## Step 12 - A run, end to end

Queue: A (good), B (business rule violation), C (technical failure once, then fine),
D (good). Limit 3.

| Turn | State | What happened |
|---|---|---|
| 1 | `Init` | settings and applications ready; `initialized` |
| 2 | `GetTransactionData` | A taken; `fetched` |
| 3 | `ProcessTransaction` | A completed; `success` |
| 4 | `GetTransactionData` | B taken |
| 5 | `ProcessTransaction` | B rejected; `businessException` (not counted) |
| 6 | `GetTransactionData` | C taken |
| 7 | `ProcessTransaction` | C threw; retried in the queue; count 1; `systemException` |
| 8 | `Init` | applications closed and reopened; `initialized` |
| 9 | `GetTransactionData` | D taken (C's retry waits behind it) |
| 10 | `ProcessTransaction` | D completed; `success`; count back to 0 |
| 11 | `GetTransactionData` | C taken again (attempt 2) |
| 12 | `ProcessTransaction` | C completed; `success` |
| 13 | `GetTransactionData` | nothing left; `fetched` goes to `EndProcess` |

If instead the applications were down for good, every turn would end in `systemException`
and after the third in a row the guard sends the machine to `EndProcess` rather than
retrying forever.

## Step 13 - Beyond the template: crash-safe and stoppable

The UiPath REFramework forgets everything if the robot process dies. With this component
you can keep its position across a restart. Add persistence **after loading the
definition and before `Start`**:

```csharp
machine.LoadDefinitionJson(definitionJson, out message);
machine.EnablePersistence("ReFramework", out string statePath, out bool restored, out message);

machine.IsStarted(out bool started, out message);
if (!started)
{
    machine.SetContext("consecutiveSystemExceptions", "0", out message);   // set context AFTER EnablePersistence
    machine.SetContext("maxConsecutiveReached", "false", out message);
    machine.SetContext("transactionFound", "false", out message);
    machine.Start(out state, out message);
}
else
{
    machine.GetCurrentState(out state, out message);
    if (state == "EndProcess")
        machine.Reset(true, out state, out message);          // last run ended: this launch is a new one (then set the context again)
    else
    {
        // The robot died mid-run. In UiPath terms that is a system exception: return to Init.
        queue.RecoverExpiredLeases(queuePath, out int recovered, out int rejected, out message);
        OnSystemException(null, null, "process restarted");
    }
}
```

A crash counts as one consecutive system exception, so a robot that keeps crashing
eventually stops instead of looping. The full recovery reasoning (leases that have not
expired yet, and so on) is in [Working a queue](WorkingAQueue.md).

An operator stop is one more transition. Add
`{ "from": "*", "trigger": "stop", "to": "EndProcess" }` to the definition and fire `stop`
from a button; it works from any non-final state.

## Where this differs from UiPath

- **No Orchestrator.** `LocalQueueUtils` is a machine-local persistent queue with leases,
  delayed retries and attempt limits; it does not distribute work across robots.
- **The machine does not run anything.** The REFramework's state workflows are your
  automation's steps; the machine only decides the next step and holds the guard facts.
- **Guards compare against literal values**, so a configured limit is turned into a
  context flag (`maxConsecutiveReached`) by your handler rather than compared by the
  machine.
- **A business exception is a call you make** (`RejectItem`, then the trigger); nothing
  detects one for you. Decide what counts as one before you build.

## Where next

- [Working a queue](WorkingAQueue.md) - the same idea with a circuit breaker, delayed retries and full crash recovery
- [Definition](Definition.md), [Context and guards](Context.md), [Events](Events.md), [Persistence](Persistence.md)
