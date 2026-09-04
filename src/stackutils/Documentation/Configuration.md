# Configuration

Each dragged component begins empty with capacity 10,000. Set `MaximumItems` in
the Property Grid for design-time configuration. Use the method form for a
runtime value that needs a `bool`/message result:

```csharp
stack.SetMaximumItems(500, out string message);
stack.GetCount(out int count, out message);
stack.GetSnapshotJson(out string snapshotJson, out message);
```

`GetSnapshotJson` is intended for logging and inspection. `Clear` reports how
many items it released. Capacity cannot be reduced below the current count.
