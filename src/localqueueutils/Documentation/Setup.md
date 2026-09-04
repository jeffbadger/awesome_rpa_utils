# Setup and lifetime

Create a run-scoped queue using the Robot Manager work ID as `runId`:

```csharp
queue.CreateQueue("invoice-import", QueueLifetime.Run, robotManagerWorkId,
    out string queuePath, out bool resumed, out string message);
```

Calling `CreateQueue` again for the same run reopens its existing disk state.
Use `QueueLifetime.Persistent` and a null `runId` only when later unrelated runs must
continue the same local backlog. Keep the component alive while using the queue;
disposing it releases the exclusive ownership lock.
