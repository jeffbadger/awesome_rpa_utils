# Tutorial: build and run your first state machine

By the end of this tutorial you will have a working expense-claim machine that
routes small claims straight to approval, sends large ones to a manager, refuses
steps taken out of order, tells you when something happens, and picks up where it
left off after a crash. It takes about fifteen minutes. Every code block is
exercised by the test suite (`TutorialTests`), so what you read here is what runs.

The code is C# for clarity. In Pega Robot Studio you do the same thing by dropping
a **StateMachineUtils** component on the automation surface and wiring its methods
and events with links; [step 12](#step-12---use-it-in-robot-studio) shows the mapping.

## Step 1 - Decide whether you need one

Use a state machine when a process has **named stages** and only some moves between
them are legal - "a claim cannot be paid before it is approved". If you find yourself
keeping a `status` variable and a pile of Switch components to check it, that is the
job this component does for you. It rejects illegal moves instead of silently
accepting them, remembers what happened, and can survive a restart.

It does not do the work of a stage (filling a form, calling a service). Your
automation does that, then tells the machine what happened by firing a **trigger**.

| Word | Meaning here |
|---|---|
| **State** | A stage the process can be in: `Draft`, `Submitted`. |
| **Trigger** | Something that happened: `submit`, `approve`. You fire it. |
| **Transition** | "From this state, on this trigger, go to that state." |
| **Guard** | An optional condition on a transition, tested against the **context**. |
| **Final state** | An end point. Nothing leaves it. |

## Step 2 - Draw the process first

On paper, before any code:

```
Draft --submit--> Submitted --decide--> Approved --pay--> Paid (final)
                       |  (amount < 500)
                       +--decide--> ManagerReview --approve--> Approved
                                          |
                                          +--reject--> Rejected (final)
        any non-final state --cancel--> Cancelled (final)
```

Two things to notice. `decide` from `Submitted` has **two exits**: which one is used
depends on the claim amount. And `cancel` works from anywhere, so we will write it once
with the wildcard `*`.

## Step 3 - Write the definition

```csharp
const string definition = """
{
  "name": "ExpenseClaim",
  "initial": "Draft",
  "states": [
    "Draft", "Submitted", "ManagerReview", "Approved",
    { "name": "Paid",      "final": true },
    { "name": "Rejected",  "final": true },
    { "name": "Cancelled", "final": true }
  ],
  "transitions": [
    { "from": "Draft",         "trigger": "submit",  "to": "Submitted" },
    { "from": "Submitted",     "trigger": "decide",  "to": "Approved",
      "guards": [ { "key": "amount", "op": "lessThan", "value": "500" } ] },
    { "from": "Submitted",     "trigger": "decide",  "to": "ManagerReview" },
    { "from": "ManagerReview", "trigger": "approve", "to": "Approved" },
    { "from": "ManagerReview", "trigger": "reject",  "to": "Rejected" },
    { "from": "Approved",      "trigger": "pay",     "to": "Paid" },
    { "from": "*",             "trigger": "cancel",  "to": "Cancelled" }
  ]
}
""";
```

Rules worth knowing now:

- A state is a plain name, or `{ "name": ..., "final": true }`.
- Transitions with the same state and trigger are tried **in the order written**; the
  first whose guards pass wins. That is how `decide` works: the guarded one is tried
  first, and the unguarded one after it is the "otherwise".
- `"from": "*"` means any non-final state.
- Names ignore case (`Submit` = `submit`).

## Step 4 - Check it before you run it

```csharp
var machine = new StateMachineUtils();

machine.ValidateDefinitionJson(definition, out string report, out string message);
// {"valid":true,"errors":[],"warnings":[],"stateCount":7,"transitionCount":7}
```

`ValidateDefinitionJson` changes nothing. It lists every **error** (a typo such as
`"trigers"`, a transition to an undeclared state) and **warnings** for likely mistakes
(an unreachable state, or a transition that an earlier unguarded one always shadows).
Fix warnings too - they are usually a wrong name.

## Step 5 - Load it and start

```csharp
if (!machine.LoadDefinitionJson(definition, out message))
    throw new InvalidOperationException(message);   // says exactly what is wrong

machine.Start(out string state, out message);       // state == "Draft"
```

Nothing has a state until `Start`. A definition with errors is rejected whole, and the
component keeps the previous one, so a bad edit cannot leave a half-loaded machine.

## Step 6 - Fire triggers

```csharp
machine.Fire("submit", out state, out message);
// returns True, message == null (empty), state == "Submitted"
```

`Fire` returns `True` whenever it could work out an answer, and an **empty
`message` means the trigger fired**. Text in `message` means one of two things,
told apart by the result:

| Result | `message` | Meaning |
|---|---|---|
| `True` | empty | It fired. |
| `True` | `NoTransition`, `GuardFailed`, `Finished` or `NotStarted` | The call worked but the trigger was declined; nothing changed. |
| `False` | error text | Bad input or a disk problem; nothing changed. |

If you need only the result and message, use `machine.FireSimple("submit", out message)`.

## Step 7 - See what a "no" looks like

Try something illegal:

```csharp
machine.Fire("pay", out state, out message);
// returns True, message == "NoTransition", state == "Submitted"
```

That is a normal outcome, not an error: `Submitted` has no `pay` transition, so nothing
moved. The reasons are `NoTransition`, `GuardFailed`, `Finished` (already in a final
state) and `NotStarted`. A method that returns `False` means something else - bad input
such as an empty trigger, or a disk problem when persistence is on - and `message` says
what.

## Step 8 - Use the context and guards

`decide` has a guard on `amount`, so give the machine the amount. The context is a set of
text keys and values that your automation fills in:

```csharp
machine.SetContext("amount", "120", out message);

machine.Fire("decide", out state, out message);
// message == null (fired), state == "Approved"   (120 < 500, so the guarded transition won)
```

Had the amount been `900`, the guard would fail, the next `decide` transition would be
tried, and the claim would go to `ManagerReview`. A key that is **missing** fails its
guard (it never passes by accident), so always `SetContext` before you `Fire`.

Available guard operators: `equals`, `notEquals`, `in`, `notIn`, `greaterThan`,
`lessThan`, `exists`, `notExists` - see [Context and guards](Context.md). Guards compare
against the context only; they cannot call your code.

> Do not put passwords or personal data in the context: it is saved in plain text if you
> enable persistence (step 11).

## Step 9 - Ask before you act

You can find out what would happen without doing it, which is handy for enabling
buttons or choosing a branch:

```csharp
machine.CanFire("pay", out bool can, out reason, out message);               // can == true
machine.GetAvailableTriggersDelimited(out string triggers, out message);      // "pay,cancel"
machine.GetCurrentState(out state, out message);                              // "Approved"
machine.IsInFinalState(out bool isFinal, out message);                        // false
```

None of these raise events or change anything. Guards are evaluated against the current
context, so a trigger whose guard would fail is not listed.

Finish the claim:

```csharp
machine.Fire("pay", out state, out message);   // state == "Paid"
machine.IsInFinalState(out isFinal, out message);                     // true
machine.Fire("cancel", out state, out message);
// returns True, message == "Finished" - a finished machine accepts nothing
```

Start another run with `machine.Reset(true, out state, out message)`.

## Step 10 - React to events

Rather than polling, subscribe. Events are raised **on the thread that called `Fire`**,
after the change is committed:

```csharp
machine.StateEntered       += (s, e) => Log($"{e.PreviousState} -> {e.NewState} on '{e.Trigger}'");
machine.TransitionRejected += (s, e) => Log($"'{e.Trigger}' declined in {e.State}: {e.Reason}");
machine.MachineFinished    += (s, e) => Log($"claim ended in {e.NewState}");
```

For one successful transition the order is always `StateExited`, `TransitionFired`,
`StateEntered`, then `MachineFinished` if the new state is final. Keep handlers short;
[Events](Events.md) lists what a handler must not do (notably: do not chain `Fire` calls
through handlers - use a loop in your automation).

The history records every move **and every declined trigger**, so "why did nothing
happen?" can be answered afterwards:

```csharp
machine.GetHistoryJson(out string history, out message, maxEntries: 10);
```

## Step 11 - Survive a crash

Turn on persistence **after loading the definition and before `Start`**:

```csharp
machine.LoadDefinitionJson(definition, out message);
machine.EnablePersistence("ExpenseClaims", out string statePath, out bool restored, out message);

machine.IsStarted(out bool started, out message);
if (!started) machine.Start(out state, out message);       // first run
else          Log("resumed in " + machine.CurrentState);   // picked up after a restart

machine.SetContext("amount", "900", out message);          // set context AFTER EnablePersistence
```

From here every change is **written to disk first** and only applied if the write
succeeds, so the machine can never be in a state the disk does not know about. Restoring
raises no events - read `CurrentState` to see where the run stopped.

Points to remember:

- A run that reached a final state stays finished after a restart; call `Reset` to begin
  a new one.
- If you change the definition, the saved run is refused (it belongs to the old one). Call
  `DiscardPersistedState("ExpenseClaims", ...)` to abandon it and start fresh.
- After a crash the outside world may have moved on. Decide what each state means on
  resume; see [Persistence](Persistence.md).

## Step 12 - Use it in Robot Studio

Drop **StateMachineUtils** on the automation surface and wire it like any component:

| You want to... | Wire |
|---|---|
| Load the definition once at start-up | A string (from an Asset or a Script) into `LoadDefinitionJson`, then `Start` |
| Report a step's result | An execution link from the step's success/failure into `Fire`, with the trigger name as its argument (`submit`, `approve`) |
| Branch on the outcome | The result of `Fire` and its `message` into a Switch: empty = fired, text = the decline reason |
| Show or route on where you are | The `CurrentState` and `IsFinished` data ports |
| Run logic when something changes | `StateEntered`, `TransitionRejected`, `MachineFinished` events into the next step |
| Pass values to guards | `SetContext` before `Fire` |

One component holds one machine; drop a second instance for a second machine.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `LoadDefinitionJson` returns `False` | Read `message` - it names the exact problem (misspelled property, unknown state, ...). |
| `Fire` returns `True` with message `NoTransition` | The trigger is not defined for the current state, or you misspelled it. `GetAvailableTriggersDelimited` shows what is. |
| `GuardFailed` unexpectedly | The context key is missing or holds different text than you think; `GetContextJson` shows it. Numeric guards need numbers (`"120"`, not `"$120"`). |
| `NotStarted` | You skipped `Start` (or `Reset`). |
| `EnablePersistence` refused | Another process owns that name, the definition changed since the run was saved (`DiscardPersistedState`), or you already called `Start`. |
| A handler seems to run "late" | Handlers run before `Fire` returns, on the calling thread; another thread's `Fire` waits its turn. |

## Where next

- [Working a queue](WorkingAQueue.md) - a complete crash-safe worker with retries and a circuit breaker
- [Definition](Definition.md), [Running](Running.md), [Context and guards](Context.md), [Events](Events.md), [Persistence](Persistence.md) - reference pages for each area
