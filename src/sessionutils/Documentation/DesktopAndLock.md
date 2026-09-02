# DesktopAndLock

These three checks answer genuinely different questions. Read all three
before picking one - they are not interchangeable, and a UAC prompt can make
`IsWorkstationLocked` and `IsInputDesktopAvailable` disagree with each other
at the same instant, correctly.

## Is the workstation locked?

The authoritative check - not affected by a transient secure-desktop switch
(a UAC prompt, Ctrl+Alt+Del):

```csharp
if (session.IsWorkstationLockedSimple(out string message))
{
    // Locked - any UI automation attempted now will fail. Wait it out
    // (see Wait.md) or abort with a clear diagnosis.
}
```

## Is the input desktop available?

A narrower, different question: is the *default* desktop currently
receiving input. A UAC consent prompt or the Ctrl+Alt+Del screen makes this
report "unavailable" **even though the workstation is not locked** - that is
correct behavior for what this method answers, not a bug:

```csharp
if (!session.IsInputDesktopAvailableSimple(out string message))
{
    // Could be locked, could be a UAC prompt, could be no desktop at all
    // (a service session). Check IsWorkstationLockedSimple/IsRunningAsServiceSession
    // separately to tell these apart if the distinction matters to your flow.
}
```

## Is the session interactive?

Whether the session has a visible window station at all - false for a plain
Windows service (Session 0), independent of lock/desktop state:

```csharp
bool interactive = session.IsSessionInteractiveSimple(out string message);
```

## querySucceeded means "the check ran," not "the answer was true"

```csharp
bool locked = session.IsWorkstationLocked(out bool querySucceeded, out string message);
// querySucceeded is true even when locked is false (a legitimate "not locked" answer),
// and even when IsInputDesktopAvailable reports unavailable because a service
// session has no desktop at all. It is false only for a genuine unexpected failure.
```
