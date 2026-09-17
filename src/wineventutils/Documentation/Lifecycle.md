# Lifecycle

`Initialize` starts the background hook thread; `Start`/`StartCategories`
activates the categories to watch and installs the hook:

```csharp
var events = new WinEventUtils();
events.Initialize(out string message);

// A single category — the common case for a simple wait:
events.Start(WinEventCategory.Dialogs, out message);

// ... later, to watch something different, Stop first:
events.Stop(out message);

// More than one category, with one checkbox per category. Every flag is
// nullable, so an unwired port (as on the Pega design surface, where every
// parameter always appears) reads the same as an explicit false:
events.StartCategories(
    windows: true, foreground: true, dialogs: true,
    titles: null, states: null, menus: null, windowOps: null, session: null,
    out message);

events.Stop(out message);   // unhooks; queues are preserved and can still be drained
events.Dispose();           // full teardown — unhooks, stops the thread, clears subscriptions/waiters
```

`Start`/`StartCategories` call `Initialize` internally if it hasn't run yet, so
calling `Initialize` first is optional but recommended for clearer error
reporting. Both fail rather than silently replacing what's being watched:
calling either one again while categories are already active returns `False`
with a message — call `Stop()` first. `StartCategories` also returns `False`
if every flag is `false`/`null` (nothing to watch); `Start` always has exactly
one category, so it can't hit that case. Once `Dispose()` runs, the instance
is final — a later `Initialize()` returns `False`; create a new `WinEventUtils`
instead.
