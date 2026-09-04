# Remove

Push cleanup actions as resources are acquired, then pop them to release the
newest resource first. The same pattern supports undo, backtracking,
depth-first traversal, and returning through nested windows.

```csharp
while (stack.TryPop(out bool available, out StackItemKind kind, out string value, out string message) && available)
{
    // Perform the reverse-order action represented by value.
}
```

Use `Clear` when remaining items should simply be discarded.
