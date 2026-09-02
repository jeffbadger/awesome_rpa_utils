# Validate

## Confirm intake wasn't corrupted in transit

```csharp
az.ValidateArchiveCrc(intakeZipPath, out bool allEntriesValid, out string message);

if (!allEntriesValid)
{
    // At least one entry's actual content doesn't match its declared
    // checksum - .NET never checks this automatically, so this call is the
    // only thing standing between a silently-corrupted export and the
    // automation treating it as good data.
}
```

## Find out exactly which entry failed

```csharp
az.ValidateArchiveCrcJson(intakeZipPath, out string json, out string message);

// Parse json (an array of {FullName, DeclaredCrc32, ComputedCrc32, IsValid,
// Status}) - Status is "Valid", "Mismatch", or "SkippedEncrypted". An
// encrypted entry is never opened, so it's reported separately rather than
// as a false "Mismatch".
```

## Reject an intake you can't read at all

```csharp
az.HasEncryptedEntries(intakeZipPath, out bool hasEncryptedEntries, out string message);

if (hasEncryptedEntries)
{
    // Detection only - there's no way for this component to decrypt or
    // extract a password-protected entry. Route to manual handling.
}
```
