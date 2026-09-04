# Adding work

Avoid building a collection by passing a complete JSON array:

```csharp
queue.AddJsonArray(queuePath, responseArrayJson, out int added, out string message);
```

`AddLines` creates one JSON-string item per non-empty line.
`AddFileReferences` queues paths while leaving source files untouched;
`ImportFiles` copies files into queue-owned storage first. Neither method moves
or deletes source files.

For individual business-sensitive operations, give `AddJson` a stable
`businessKey`. A duplicate active, rejected, or retained completed item returns
success with `duplicate == true` and the original `itemId`.
