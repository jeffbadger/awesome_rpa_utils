# ServiceUtils Documentation

Real-world usage examples for every method on the `ServiceUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Query](Query.md) — checking whether a service exists, its status, and its startup type
- [Control](Control.md) — starting, stopping, restarting, pausing, and waiting on a service
- [Configuration](Configuration.md) — changing a service's startup type

All examples assume a `ServiceUtils` instance named `svc`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var svc = new ServiceUtils();`).
