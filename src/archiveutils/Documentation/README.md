# ArchiveUtils Documentation

Real-world usage examples for every method on the `ArchiveUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [CreateExtract](CreateExtract.md) — creating a ZIP archive and extracting one back
- [Inspect](Inspect.md) — listing contents and reading summary metadata before extracting
- [Validate](Validate.md) — CRC-32 verification and encrypted-entry detection
- [DiagnosticBundles](DiagnosticBundles.md) — zipping up files for a failure/diagnostic bundle

All examples assume an `ArchiveUtils` instance named `az`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var az = new ArchiveUtils();`).
