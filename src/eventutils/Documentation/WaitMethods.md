# WaitMethods

Block until a matching event or timeout:

```csharp
bool ok = events.WaitForWindowCreated("{\"process\":\"notepad\"}", 10000,
    out EventData created, out string message);
if (ok)
    window.ActivateWindow(new IntPtr(created.Hwnd), out _); // ready to pass to WindowUtils directly

// Non-blocking lookback instead of waiting:
bool wasCreated = events.WasWindowCreated("{\"process\":\"notepad\"}", 5000, out message);
```

A wait always resolves to exactly one event — the first match, then it stops
listening — so every `WaitForX` method returns a single `EventData` object
directly; there's no separate JSON+handle overload or `...AsEventData`
sibling to pick between.

A wait returns `False` on any failure — a timeout, a stopped engine, a
malformed filter, or an uncompilable regex — with `message` explaining which;
there is no separate `timedOut` output. A timeout's message reads `"Timed out
after <n> ms waiting for <event>."`, so an automation that needs to branch on
timeout specifically (rather than treat every failure the same way) can check
for that prefix. A `timeoutMs` of 0 or negative is also an immediate timeout.
**Do not use `WaitForX` for file or process waits** — these are UI-event
waits; use `commandlineutils`/`serviceutils` for those. `CancelWaits`
releases every pending wait (each reports a timeout).

`WasWindowCreated` folds its answer into the return value the same way:
`True` means a matching event was found; `False` means either "not found"
(normal — `message` is null) or an operational failure (`message` is set).
There is no separate `out bool wasCreated`.
