# Get

Use the getter matching the declared type:

```csharp
bag.TryGetDecimal("Total", out bool found, out decimal total, out string message);
```

A missing name is successful with `found == false`. A present item declared as
another type is a failure with a descriptive message. `TryGetValue` supports
dynamic routing by returning both `DataBagValueType` and invariant text.
