# TryGet

Use these when the automation needs to branch on whether the read actually
succeeded, rather than silently falling back to a default:

```csharp
if (store.TryGetInt32("Quantity", out int quantity))
{
    // quantity is valid
}

store.TryGetEnum("Status", typeof(OrderStatus).AssemblyQualifiedName,
    out bool found, out object status, out string message);
```

`TryGetString`/`TryGetInt32`/`TryGetInt64`/`TryGetDouble`/`TryGetDecimal`/
`TryGetBoolean`/`TryGetDateTime`/`TryGetGuid` all return `False` for a
missing key, a null value, or a failed conversion — there is no separate
message, since there is nothing more specific to report. `TryGetEnum` is the
exception: it reports `found` and `message` separately, and its
`enumTypeName` should be a trusted, design-time-authored value (the same
trust model as `JsonUtils.TryDeserializeObject`'s `typeName`), not a value
populated from untrusted runtime data.
