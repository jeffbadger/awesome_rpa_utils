# Read

Peek when routing depends on the next item's kind, then pop when processing can
begin:

```csharp
stack.TryPeek(out bool available, out StackItemKind kind, out string value, out string message);
stack.TryPop(out available, out kind, out value, out message);
```

Unlike a durable queue, pop removes the item immediately. There is no lease,
acknowledgement, or retry. An empty result is successful and is distinguished
with `available == false`.
