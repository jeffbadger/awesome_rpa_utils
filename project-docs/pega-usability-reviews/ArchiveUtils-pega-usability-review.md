# ArchiveUtils Pega API Usability Review

## Summary

Like `EventLogUtils`'s, `SessionUtils`'s, and `FileWatchUtils`'s review
docs, this one is written **up front**, alongside the initial
implementation, rather than as a post-hoc pass over an already-shipped
surface - there is no legacy API to retrofit here. It records the
signature-uniqueness decisions made before any code was written, per the
[Signature Uniqueness Standard](../coding-standards/signature-uniqueness-standard.md).

All method ports are scalars, strings, ints, longs, doubles, Booleans, or
JSON strings - directly usable in Pega. The repository-owned
`ArchiveEntryInfo` type is JSON-only (never a public method return type),
consistent with this suite's preference for JSON over complex objects on
the Pega boundary.

**Notable finding: this component has no mandatory `Simple`-suffix
collision anywhere.** Every prior component in this suite had at least one
pair of overloads sharing an identical non-`out` parameter list, forcing a
`Simple` split. Here, every similarly-shaped pair differs in arity
(`ExtractArchive`/`ExtractArchiveSimple`,
`CreateDiagnosticBundle`/`CreateDiagnosticBundleSimple`), and every
same-shaped-input pair uses distinct method names instead of an ambiguous
overload (`ValidateArchiveCrc`/`ValidateArchiveCrcJson`,
`TryGetArchiveMetadata`/`ListArchiveContentsJson`) - a deliberate design
choice made while drafting the method surface, not a coincidence.

## Method review

| Method or group | Rating | Assessment |
|---|---|---|
| `CreateArchive` | Direct | All scalar in/out; atomic publish and timestamp handling are entirely internal, never crossing the Pega boundary. |
| `ExtractArchive`/`ExtractArchiveSimple` | Direct, distinct arity | The `Simple` overload has strictly fewer parameters (defaulted limits), not an identical non-`out` list - a genuine arity difference, not a signature-uniqueness necessity. |
| `ExtractSingleFile` | Direct | Scalar entry name in, scalar path out. |
| `ExtractFirstMatchingFile` | Direct, distinct name | A glob-pattern sibling of `ExtractSingleFile` kept as its own method (not an overload) so a caller's intent - "this exact name" vs. "match this pattern" - is unambiguous at the call site, not inferred from string content. |
| `ListArchiveContentsJson` | Direct, JSON | Returns a JSON array string, the pre-flight inspection method. |
| `TryGetArchiveMetadata` | Direct | All scalar outputs - entry count, both size totals, and an encryption flag. |
| `ValidateArchiveCrc`/`ValidateArchiveCrcJson` | Direct, distinct names | Share an identical `(string)` input list but are different method names - not a violation, same precedent as `TryGetFileMetadata`/`GetFileMetadataJson` in `FileWatchUtils`. |
| `HasEncryptedEntries` | Direct | Scalar Boolean output, a fast short-circuit scan distinct from `ListArchiveContentsJson`'s full per-entry detail. |
| `CreateDiagnosticBundle`/`CreateDiagnosticBundleSimple` | Direct, distinct arity | The `Simple` overload's `out createdArchivePath` and auto-naming behavior give it a genuinely different shape and purpose, not just a missing disambiguating output. |

## Signature-uniqueness decisions made up front

- `ExtractArchive(string, string, bool, long, double, bool, out string)` vs. `ExtractArchiveSimple(string, string, bool, out string)` - different arity (7 vs. 4 non-`out` params), so `Simple` here is a naming-consistency choice, not a uniqueness requirement.
- `CreateDiagnosticBundle(string, string, bool, string, out string)` vs. `CreateDiagnosticBundleSimple(string, string, out string, out string)` - different arity and a different `out` shape (`createdArchivePath` vs. none); same reasoning.
- `ExtractSingleFile` vs. `ExtractFirstMatchingFile` - deliberately distinct method names rather than a single method accepting "an exact name or a pattern," so the designer surface never requires guessing which interpretation a given string gets.
- `ValidateArchiveCrc(string, out bool, out string)` vs. `ValidateArchiveCrcJson(string, out string, out string)` - identical non-`out` parameter list (`(string)`), but distinct names, so no `Simple` suffix is needed or applied - the standard's collision rule only governs same-named overloads.
- `TryGetArchiveMetadata` vs. `ListArchiveContentsJson` - same reasoning; both take `(string)` but are named distinctly for their different output shapes (scalar summary vs. JSON detail).
- No `As<Type>`-suffixed overloads exist in this component - there is no case here where two overloads return the same logical value via a different type (unlike `ServiceUtils`'s `ServiceControllerStatus`/`ServiceStatus` pair).

## Operational concerns

- **Zip-slip protection is non-optional, by design.** Every extraction
  method routes through the same internal safety guard rather than
  exposing it as a separate "validate this path" call a caller could
  forget to invoke - mirrors `FileWatchUtils.ClaimFile`'s design philosophy
  of making the safe path the only path.
- **Zip-bomb limits are checked against declared archive metadata before
  any extraction begins** - `ExtractArchive`/`ExtractSingleFile`/
  `ExtractFirstMatchingFile` all fail closed, extracting nothing, if a
  limit would be exceeded. This trusts the central directory's declared
  sizes; a maliciously hand-crafted header lying about its own declared
  size is a documented, out-of-scope limitation, not a gap this review
  claims to close.
- **CRC validation is genuinely independent of the archive's own claims.**
  .NET performs no automatic CRC check on read at all - `ValidateArchiveCrc`/
  `Json` decompress and hash every non-encrypted entry themselves via a
  hand-rolled CRC-32 (no new NuGet dependency).
- **Encrypted entries are never opened.** `System.IO.Compression` cannot
  decrypt an entry under any circumstance; `ValidateArchiveCrcJson` reports
  such entries as `"SkippedEncrypted"` rather than risking an unexpected
  exception or a misleading "corrupted" result from attempting to read
  ciphertext as if it were the real content.
- **The ZIP/DOS timestamp format has no time-zone field** - only wall-clock
  date/time survives a round-trip through an entry's `LastWriteTime`.
  `CreateArchive` timestamps entries using each source file's local
  last-write time (not UTC) specifically because of this - a UTC-based
  implementation would silently shift wall-clock times on any machine not
  running in UTC. Discovered during implementation via a failing test, not
  anticipated in the original design.

## Recommended changes

None outstanding - this is the initial design pass, not a retrofit.
