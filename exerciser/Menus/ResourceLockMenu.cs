using System;
using ResourceLockAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// ResourceLockUtils' menu. Process scope needs no setup at all; Machine
    /// scope needs a lock folder ACL'd for every robot account first - see the
    /// Setup/Cleanup menu's ResourceLockUtils reminder, which this component
    /// can't grant itself (that's an ACL/account-provisioning step, not
    /// something any single process can do). TESTING.md's own note applies
    /// here too: never put a resource name, holder, or token you care about
    /// keeping private into a message you'd paste elsewhere - use a
    /// recognizable-but-disposable marker instead.
    /// </summary>
    internal static class ResourceLockMenu
    {
        internal static MenuItem[] Build(ResourceLockUtils resourceLock)
        {
            return new[]
            {
                new MenuItem("TryAcquireLock", "Non-blocking. A second attempt against the same resource/scope while still held returns acquired=false with currentHolder set.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestResource");
                    string holder = Prompt.String("Holder", "ZZTestHolder");
                    int leaseSeconds = Prompt.Int("Lease seconds", 30);
                    bool ok = resourceLock.TryAcquireLock(scope, resource, holder, leaseSeconds, out bool acquired, out string token, out string currentHolder, out string message);
                    Report.Result(ok, message, ("acquired", acquired), ("token", token), ("currentHolder", currentHolder));
                }),
                new MenuItem("AcquireLock", "Blocking - this menu calls it synchronously, so the exerciser itself is unresponsive for up to waitMilliseconds while it waits for a lock held by another holder to free up. (In real Robot Studio usage on an asynchronous link, the wait itself doesn't busy-loop and the UI stays responsive - it's specifically this synchronous menu call that blocks, not the underlying wait.)", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestResource");
                    string holder = Prompt.String("Holder", "ZZTestHolder");
                    int leaseSeconds = Prompt.Int("Lease seconds", 30);
                    int waitMilliseconds = Prompt.Int("Wait ms", 5000);
                    bool ok = resourceLock.AcquireLock(scope, resource, holder, leaseSeconds, waitMilliseconds, out bool acquired, out string token, out string currentHolder, out string message);
                    Report.Result(ok, message, ("acquired", acquired), ("token", token), ("currentHolder", currentHolder));
                }),
                new MenuItem("TryAcquireSlot", "Non-blocking pool lock - up to capacity holders at once. Run this from a few different exerciser sessions against the same resource/capacity to see holderCount climb.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestSlotPool");
                    int capacity = Prompt.Int("Capacity", 3);
                    string holder = Prompt.String("Holder", "ZZTestHolder");
                    int leaseSeconds = Prompt.Int("Lease seconds", 30);
                    bool ok = resourceLock.TryAcquireSlot(scope, resource, capacity, holder, leaseSeconds, out bool acquired, out string token, out int holderCount, out string message);
                    Report.Result(ok, message, ("acquired", acquired), ("token", token), ("holderCount", holderCount));
                }),
                new MenuItem("AcquireSlot", "Blocking pool lock - waits up to waitMilliseconds for a free slot in the pool.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestSlotPool");
                    int capacity = Prompt.Int("Capacity", 3);
                    string holder = Prompt.String("Holder", "ZZTestHolder");
                    int leaseSeconds = Prompt.Int("Lease seconds", 30);
                    int waitMilliseconds = Prompt.Int("Wait ms", 5000);
                    bool ok = resourceLock.AcquireSlot(scope, resource, capacity, holder, leaseSeconds, waitMilliseconds, out bool acquired, out string token, out int holderCount, out string message);
                    Report.Result(ok, message, ("acquired", acquired), ("token", token), ("holderCount", holderCount));
                }),
                new MenuItem("RenewLock", "Extend a lock/slot's lease with the token from Try/AcquireLock or Try/AcquireSlot above. Fails once the lease has already expired and someone else holds it.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestResource");
                    string token = Prompt.String("Token", "");
                    int leaseSeconds = Prompt.Int("Lease seconds", 30);
                    bool ok = resourceLock.RenewLock(scope, resource, token, leaseSeconds, out bool renewed, out int expiresInSeconds, out string message);
                    Report.Result(ok, message, ("renewed", renewed), ("expiresInSeconds", expiresInSeconds));
                }),
                new MenuItem("ReleaseLock", "Release with the token from Try/AcquireLock or Try/AcquireSlot above. Wire this on both the success path and the Catch path of a Try/Catch in real usage.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestResource");
                    string token = Prompt.String("Token", "");
                    bool ok = resourceLock.ReleaseLock(scope, resource, token, out bool released, out string message);
                    Report.Result(ok, message, ("released", released));
                }),
                new MenuItem("GetLockStatus", "Read-only status check - does not itself acquire or release anything.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestResource");
                    bool ok = resourceLock.GetLockStatus(scope, resource, out bool held, out string holders, out int expiresInSeconds, out int holderCount, out string message);
                    Report.Result(ok, message, ("held", held), ("holders", holders), ("expiresInSeconds", expiresInSeconds), ("holderCount", holderCount));
                }),
                new MenuItem("GetLocksJson", "Dumps every currently-held lock/slot in the given scope as JSON - for Machine scope, shows each holder's machine, session, and process.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    bool ok = resourceLock.GetLocksJson(scope, out string locksJson, out string message);
                    Report.Result(ok, message, ("locksJson", locksJson));
                }),
                new MenuItem("ForceReleaseLock", "DISRUPTIVE - forcibly releases a lock/slot another holder may still be actively relying on. confirmForceRelease must be explicitly true; releasedCount reports how many entries (a slot pool can have more than one) were cleared.", () =>
                {
                    LockScope scope = Prompt.Enum<LockScope>("Scope");
                    string resource = Prompt.String("Resource", "ZZTestResource");
                    if (!Prompt.Bool("This forcibly releases the lock even if another holder relies on it. Continue?", false))
                    {
                        Console.WriteLine("Cancelled.");
                        return;
                    }
                    bool ok = resourceLock.ForceReleaseLock(scope, resource, confirmForceRelease: true, out int releasedCount, out string message);
                    Report.Result(ok, message, ("releasedCount", releasedCount));
                }),
                new MenuItem("ConfigureLockFolder", "Machine scope only - points this component at the lock folder. Blank resets to the component's own default (ProgramData\\AwesomeRpaUtils\\Locks) instead of hard-coding that path here, so this can also undo a previously configured custom folder. See the Setup/Cleanup menu for the ACL prerequisite this doesn't grant itself.", () =>
                {
                    string folderPath = Prompt.String("Folder path (blank = component default)", "");
                    bool ok = resourceLock.ConfigureLockFolder(folderPath, out string message);
                    Report.Result(ok, message);
                }),
                new MenuItem("ValidateLockFolder", "Machine scope only - checks the configured (or default) lock folder is actually usable from this account: list/read/create/write rights, and whether other robots' old files can be cleaned up.", () =>
                {
                    bool ok = resourceLock.ValidateLockFolder(out bool usable, out string reportJson, out string message);
                    Report.Result(ok, message, ("usable", usable), ("reportJson", reportJson));
                })
            };
        }
    }
}
