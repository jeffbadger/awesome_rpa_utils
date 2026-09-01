# EventLogUtils Documentation

Real-world usage examples for every method on the `EventLogUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Discovery](Discovery.md) — checking logs/sources exist, and registering a new source
- [Query](Query.md) — finding, counting, and dumping matching entries
- [Write](Write.md) — writing an audit/log entry from a bot
- [Wait](Wait.md) — polling for a new entry to appear
- [Export](Export.md) — exporting a filtered log to `.evtx` and querying it back

All examples assume an `EventLogUtils` instance named `evtLog`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var evtLog = new EventLogUtils();`).
