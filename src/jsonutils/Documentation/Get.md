# Get

## Extract a value as a specific .NET type instead of raw text

```csharp
string document = "{\"invoiceTotal\":1042.50,\"lineCount\":3,\"approved\":true,\"receivedUtc\":\"2026-02-20T08:30:00Z\"}";

json.TryGetDoubleValue(document, "invoiceTotal", out double total, out string message);   // total == 1042.50
json.TryGetIntValue(document, "lineCount", out int lineCount, out message);               // lineCount == 3
json.TryGetBoolValue(document, "approved", out bool approved, out message);               // approved == true
json.TryGetDateTimeValue(document, "receivedUtc", out DateTime receivedUtc, out message);  // parsed to a DateTime
```

Each of these fails (returns `False` with a message) rather than silently
coercing when the resolved value genuinely can't convert to the requested
type:

```csharp
json.TryGetIntValue("{\"age\":\"thirty\"}", "age", out int age, out message);
// False - "thirty" isn't a parseable number
```

Unlike `TryGetValueFromJson` (see [Core](Core.md)), which always returns raw
JSON text, these convert to the actual .NET type so an automation can use
the value directly in arithmetic, a date comparison, or a Boolean branch
without a separate parse step.
