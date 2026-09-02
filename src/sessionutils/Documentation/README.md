# SessionUtils Documentation

Real-world usage examples for every method on the `SessionUtils` component,
organized by the same categories used in the source code and the top-level
[README](../README.md).

- [Identity](Identity.md) — who am I: session ID, console vs. RDP vs. service
- [State](State.md) — connect state, logged-on user, enumerating every session
- [DesktopAndLock](DesktopAndLock.md) — is the workstation locked, is the desktop available, is the session interactive
- [IdleTime](IdleTime.md) — milliseconds since the last local input
- [Wait](Wait.md) — polling for a session/desktop state to change
- [Actions](Actions.md) — locking the workstation, disconnecting a session

All examples assume a `SessionUtils` instance named `session`, as it would
appear dropped onto a Pega Robot Studio automation's design surface (or
instantiated directly: `var session = new SessionUtils();`).
