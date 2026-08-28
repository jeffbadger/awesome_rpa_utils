# CommandLineUtils Documentation

Real-world usage examples for every method on the `CommandLineUtils`
component, organized by the same categories used in the source code and the
top-level [README](../README.md).

- [Run](Run.md) — running a command directly or through the shell and capturing its output
- [Elevated](Elevated.md) — running a command with a UAC elevation prompt
- [Fire and Forget](FireAndForget.md) — starting a background process without waiting

All examples assume a `CommandLineUtils` instance named `cmd`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var cmd = new CommandLineUtils();`).
