# FileWatchUtils Documentation

Real-world usage examples for every method on the `FileWatchUtils`
component, organized by the same categories used in the source code and the
top-level [README](../README.md).

- [Wait](Wait.md) — existence/deletion/change, stability, lock, and directory-pattern polling
- [Watch](Watch.md) — blocking on a native filesystem change event
- [Actions](Actions.md) — atomically moving/replacing a file, and claiming a work file
- [Hash](Hash.md) — hashing files to detect duplicate or unchanged inputs
- [Metadata](Metadata.md) — reading file/directory metadata as scalars or JSON

All examples assume a `FileWatchUtils` instance named `fw`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var fw = new FileWatchUtils();`).
