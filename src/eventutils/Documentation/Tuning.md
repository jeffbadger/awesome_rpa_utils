# Tuning

Coalesce noisy events, bound queue growth, and inspect recent activity for
diagnostics:

```csharp
events.SetDebounce("WindowShown", 150, out string message);       // dedupe per (hwnd, event name)
events.SetDebounce(EventName.ValueChanged, 50, out message);      // enum overload, no typos

events.SetQueueLimits(1000, "DropOldest", out message);
events.SetQueueLimits(1000, EventOverflowPolicy.DropOldest, out message);

events.DumpRecentEvents(50, out string recent, out message);      // last 50 events as JSON, ring buffer max 500
```

`SetDebounce` defaults to 150 ms for `WindowShown`/`WindowHidden` and 0
otherwise — a Save dialog can fire 4-6 SHOW events in ~200 ms, and debounce
coalesces them to one. `EVENT_OBJECT_VALUECHANGE` is opt-in only (every
keystroke fires it): subscribe to `States` and call `SetDebounce` for
`ValueChanged` explicitly to receive it. `SetQueueLimits`'s `"Block"` policy is
accepted for compatibility but behaves exactly like `"DropNewest"` — the hook
thread must never block, so a full queue always drops the arriving event.
`DumpRecentEvents` works even when nothing was subscribed, since the engine
keeps the ring buffer regardless of subscriptions.
