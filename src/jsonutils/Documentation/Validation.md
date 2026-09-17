# Validation

## Check whether text is well-formed JSON before doing anything else with it

```csharp
bool ok = json.IsValidJson(candidateText, out string message);
if (!ok)
{
    // message describes the parse failure, e.g. an unexpected token or an unclosed brace.
}
```

Useful as a guard before passing caller-supplied or externally-sourced text
into any other method here — every other method already fails closed
(returns `False` with a message) on malformed JSON, but `IsValidJson` lets
an automation branch on that specifically before committing to further
processing.
