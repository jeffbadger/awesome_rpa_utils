# Subscribe

Start a background watcher; events land in a per-subscription queue the robot
polls, instead of blocking on a single `WaitForX` call:

```csharp
events.Subscribe(WinEventCategory.Dialogs, "{\"process\":\"myapp\"}", "guardian", out string message);

// More than one category, with one checkbox per category (same nullable
// shape as StartCategories):
events.SubscribeCategories(
    windows: true, foreground: null, dialogs: true,
    titles: null, states: null, menus: null, windowOps: null, session: null,
    "{\"process\":\"myapp\"}", "guardian2", out message);

while (true)
{
    bool hasEvent = events.GetNextEvent("guardian", 5000, out WinEventData dialog, out message);
    if (!hasEvent) continue;

    // ... act on dialog ...
}

events.HasEvents("guardian", out int queued, out message);
events.ClearQueue("guardian", out message);
events.Unsubscribe("guardian", out message);
```

`GetNextEvent` always dequeues exactly one event, so it returns `WinEventData`
directly — there's no separate JSON+handle overload or `...AsEventData`
sibling. It folds "queue empty" into the return value itself (`False` with
`message` null) rather than a separate `hasEvent` output — check `message` to
tell a normal empty-queue timeout apart from a real failure like an unknown
subscription. `GetNextEvents`/`GetNextEventsJson` drain *multiple* events at
once instead of blocking one at a time — that's the one place a JSON array
form still makes sense, since a real `WinEventData[]` array may not have a
Pega-side proxy. `Unsubscribe` wakes a blocked `GetNextEvent` immediately —
instead of waiting out its full timeout on a removed subscription, the
blocked call returns `False` with a message promptly.
