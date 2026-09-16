# Configuration

Each dragged component begins empty with case-insensitive keys. Set
`CaseSensitiveKeys` in the Property Grid for design-time configuration, or
assign it at runtime before adding data that could collide:

```csharp
var store = new ValueStoreUtils { CaseSensitiveKeys = true };
store.SetString("Name", "Upper", out string message);
store.SetString("name", "Lower", out message);
// Both entries exist independently.
```

Changing `CaseSensitiveKeys` after entries exist rebuilds the internal map
under the new comparer, preserving existing entries. Switching to
case-insensitive after adding keys that differ only by case collides
silently — the later write wins. Set this once, before adding data that
could collide.
