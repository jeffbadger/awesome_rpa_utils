# Filters

Every `WaitForX`/`Subscribe` call takes a filter as a JSON string. Prefer
`BuildFilterJson` over hand-authoring that JSON yourself — a typo'd key name
is silently ignored (an unfiltered match-all result) rather than rejected:

```csharp
// Don't:
events.Subscribe(WinEventCategory.Windows, "{\"process\":\"notepad\",\"class\":\"#32770\",\"titleContains\":\"Save\"}", "sub", out string message);

// Do — same result, no JSON to author or escape:
string filterJson = events.BuildFilterJson(process: "notepad", className: "#32770", titleContains: "Save");
events.Subscribe(WinEventCategory.Windows, filterJson, "sub", out message);
```

Every field is its own named, optional parameter — omit whichever ones you
don't need:

```csharp
string byProcess = events.BuildFilterJson(process: "notepad");
string byClassAndTitle = events.BuildFilterJson(className: "#32770", titleContains: "Save As");
```

For the common case of filtering on exactly one field, pass an
`WinEventFilterField` instead of naming a parameter — most useful when the
field itself is chosen at runtime (a dropdown, a config value) rather than
fixed at design time:

```csharp
string viaEnum = events.BuildFilterJson(WinEventFilterField.TitleContains, "Save As");
// Identical JSON to events.BuildFilterJson(titleContains: "Save As")
```

The enum overload also takes an optional third `hasButtonChildren` parameter,
since that field is rarely useful as a filter by itself (most windows have a
button somewhere) but is common paired with one other field:

```csharp
string filterJson = events.BuildFilterJson(WinEventFilterField.Process, "myapp", hasButtonChildren: true);
```

Use the full `BuildFilterJson` overload instead when combining more than one
non-Boolean field, or for `excludeSelf`, which the single-field overload
doesn't cover.

.NET callers with a compile-time-known filter can also skip JSON entirely via
the fluent builder (not available from Pega Robot Studio, which can only
wire scalar inputs):

```csharp
WinEventFilter.Create().Process("saplogon").Class("#32770").TitleContains("Save As");
```

All fields are optional — an unset field is a wildcard. `BuildFilterJson`
never throws, returning `"{}"` (match-all) if every parameter is omitted.
Invalid input is rejected, not silently ignored: malformed filter JSON or an
uncompilable `titleMatches` regex makes `Subscribe`/`WaitForX` return `False`
with a message; a fluent `TitleMatches("[Bad")` fails closed instead — the
filter matches nothing rather than throwing at construction time.
