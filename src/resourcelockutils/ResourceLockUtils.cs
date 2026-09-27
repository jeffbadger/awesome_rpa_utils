using System;
using System.ComponentModel;

namespace ResourceLockAutomation
{
    /// <summary>
    /// Locks on named resources (a legacy login, a license seat, a shared workbook, a rate-limited portal) that any thread can release and that
    /// Server Bots on one machine share. A lock is a lease owned by a token: whoever has the token renews or releases it, from any thread, and a
    /// lease that is not renewed expires, so a crashed or hung holder cannot block everyone forever.
    /// </summary>
    /// <remarks>
    /// Work in progress: every input is validated and <c>ConfigureLockFolder</c> works; acquiring, holding and inspecting locks report that they
    /// are not implemented yet. See project-docs/plans/2026-09-27-resourcelockutils-design.md.
    /// </remarks>
    [Description("Locks on named resources that any thread can release and that Server Bots on one machine share: a lease owned by a token, renewed or released from any thread, that expires if its holder crashes or hangs. Under construction: inputs are validated and ConfigureLockFolder works; locking is not implemented yet. Never throws.")]
    public sealed class ResourceLockUtils : Component
    {
        private readonly object syncRoot = new object();
        private bool disposed;
        private string lockFolder;                  // null: the default folder

        /// <summary>Empty constructor required so Pega Robot Studio can create the component.</summary>
        public ResourceLockUtils() { }

        /// <summary>Standard designer constructor; attaches the component to a container.</summary>
        public ResourceLockUtils(IContainer container) { container?.Add(this); }

        // ------------------------------------------------------------------ acquire

        /// <summary>Takes a lock on a resource now if it is free, without waiting.</summary>
        [Category("Resource Lock - Acquire")]
        [Description("Takes a lock on a resource now if it is free, without waiting. acquired is False when another holder has it, and currentHolder then names who. Keep the token: RenewLock and ReleaseLock need it, from any thread. The lease ends after leaseSeconds unless it is renewed. Never throws.")]
        public bool TryAcquireLock(LockScope scope, string resource, string holder, int leaseSeconds, out bool acquired, out string token, out string currentHolder, out string message)
        {
            acquired = false; token = null; currentHolder = null;
            return NotYetImplemented(nameof(TryAcquireLock), out message,
                LockInput.Scope(scope), LockInput.Resource(resource), LockInput.Holder(holder), LockInput.LeaseSeconds(leaseSeconds));
        }

        /// <summary>Waits up to waitMilliseconds for a lock on a resource and takes it when it is free.</summary>
        [Category("Resource Lock - Acquire")]
        [Description("Waits up to waitMilliseconds for a lock on a resource, checking repeatedly, and takes it when it is free. acquired is False when the wait ended first, and currentHolder then names who has it. Run it on an asynchronous link so the wait does not block the user interface. Never throws.")]
        public bool AcquireLock(LockScope scope, string resource, string holder, int leaseSeconds, int waitMilliseconds, out bool acquired, out string token, out string currentHolder, out string message)
        {
            acquired = false; token = null; currentHolder = null;
            return NotYetImplemented(nameof(AcquireLock), out message,
                LockInput.Scope(scope), LockInput.Resource(resource), LockInput.Holder(holder), LockInput.LeaseSeconds(leaseSeconds), LockInput.WaitMilliseconds(waitMilliseconds));
        }

        /// <summary>Takes one of capacity slots on a resource now if one is free, without waiting.</summary>
        [Category("Resource Lock - Acquire")]
        [Description("Takes one of capacity slots on a resource now if one is free (a pool of licenses, logins or sessions), without waiting. holderCount is how many slots are taken. Every caller must give the same capacity for the resource. The token works with RenewLock and ReleaseLock. Never throws.")]
        public bool TryAcquireSlot(LockScope scope, string resource, int capacity, string holder, int leaseSeconds, out bool acquired, out string token, out int holderCount, out string message)
        {
            acquired = false; token = null; holderCount = 0;
            return NotYetImplemented(nameof(TryAcquireSlot), out message,
                LockInput.Scope(scope), LockInput.Resource(resource), LockInput.Capacity(capacity), LockInput.Holder(holder), LockInput.LeaseSeconds(leaseSeconds));
        }

        /// <summary>Waits up to waitMilliseconds for one of capacity slots on a resource and takes it when one is free.</summary>
        [Category("Resource Lock - Acquire")]
        [Description("Waits up to waitMilliseconds for one of capacity slots on a resource, checking repeatedly, and takes it when one is free. acquired is False when the wait ended first. Run it on an asynchronous link so the wait does not block the user interface. Never throws.")]
        public bool AcquireSlot(LockScope scope, string resource, int capacity, string holder, int leaseSeconds, int waitMilliseconds, out bool acquired, out string token, out int holderCount, out string message)
        {
            acquired = false; token = null; holderCount = 0;
            return NotYetImplemented(nameof(AcquireSlot), out message,
                LockInput.Scope(scope), LockInput.Resource(resource), LockInput.Capacity(capacity), LockInput.Holder(holder), LockInput.LeaseSeconds(leaseSeconds),
                LockInput.WaitMilliseconds(waitMilliseconds));
        }

        // ------------------------------------------------------------------ hold

        /// <summary>Extends a held lock or slot to leaseSeconds from now.</summary>
        [Category("Resource Lock - Hold")]
        [Description("Extends a held lock or slot to leaseSeconds from now, from any thread that has the token. renewed is False when the lease was already lost (it expired or another holder took it over): stop using the resource. Never throws.")]
        public bool RenewLock(LockScope scope, string resource, string token, int leaseSeconds, out bool renewed, out int expiresInSeconds, out string message)
        {
            renewed = false; expiresInSeconds = 0;
            return NotYetImplemented(nameof(RenewLock), out message,
                LockInput.Scope(scope), LockInput.Resource(resource), LockInput.Token(token), LockInput.LeaseSeconds(leaseSeconds));
        }

        /// <summary>Releases a lock or slot, from any thread that has the token.</summary>
        [Category("Resource Lock - Hold")]
        [Description("Releases a lock or slot, from any thread that has the token. released is False when the lease had already been lost (it expired or another holder took it over), so the work may have overlapped with another holder's. Never throws.")]
        public bool ReleaseLock(LockScope scope, string resource, string token, out bool released, out string message)
        {
            released = false;
            return NotYetImplemented(nameof(ReleaseLock), out message, LockInput.Scope(scope), LockInput.Resource(resource), LockInput.Token(token));
        }

        // ------------------------------------------------------------------ inspect

        /// <summary>Reports whether a resource is held, by whom, until when and by how many holders.</summary>
        [Category("Resource Lock - Inspect")]
        [Description("Reports whether a resource is held, by whom (the holders of slots separated by |), in how many seconds the first lease ends, and how many holders it has. Changes nothing. Never throws.")]
        public bool GetLockStatus(LockScope scope, string resource, out bool held, out string holders, out int expiresInSeconds, out int holderCount, out string message)
        {
            held = false; holders = null; expiresInSeconds = 0; holderCount = 0;
            return NotYetImplemented(nameof(GetLockStatus), out message, LockInput.Scope(scope), LockInput.Resource(resource));
        }

        /// <summary>Returns every held lock and slot in the scope as JSON.</summary>
        [Category("Resource Lock - Inspect")]
        [Description("Returns every held lock and slot in the scope as JSON: the resource, holder and lease end, and for the Machine scope the holder's machine, session and process. Changes nothing. Never throws.")]
        public bool GetLocksJson(LockScope scope, out string locksJson, out string message)
        {
            locksJson = null;
            return NotYetImplemented(nameof(GetLocksJson), out message, LockInput.Scope(scope));
        }

        // ------------------------------------------------------------------ setup and operations

        /// <summary>Releases a resource whatever holds it; for an operator who knows the holder is gone.</summary>
        [Category("Resource Lock - Setup")]
        [Description("Releases a resource whatever holds it, for an operator who knows the holder is gone; confirmForceRelease must be True. releasedCount is how many holders lost their lease; each is told so by RenewLock or ReleaseLock. Never throws.")]
        public bool ForceReleaseLock(LockScope scope, string resource, bool confirmForceRelease, out int releasedCount, out string message)
        {
            releasedCount = 0;
            return NotYetImplemented(nameof(ForceReleaseLock), out message, LockInput.Scope(scope), LockInput.Resource(resource),
                confirmForceRelease ? null : "confirmForceRelease must be True to force a release");
        }

        /// <summary>Sets the folder that holds Machine-scope locks for this component.</summary>
        [Category("Resource Lock - Setup")]
        [Description("Sets the folder that holds Machine-scope locks for this component: an absolute local path, or empty for the default (ProgramData\\AwesomeRpaUtils\\Locks). Every robot that shares locks must use the same folder. Never throws.")]
        public bool ConfigureLockFolder(string folderPath, out string message)
        {
            message = null;
            try
            {
                lock (syncRoot)
                {
                    if (disposed) { message = DisposedMessage(nameof(ConfigureLockFolder)); return false; }
                    string problem = LockInput.FolderPath(folderPath);
                    if (problem != null) { message = nameof(ConfigureLockFolder) + " failed: " + problem + "."; return false; }
                    lockFolder = folderPath.Length == 0 ? null : folderPath;
                    return true;
                }
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(nameof(ConfigureLockFolder), ex); return false; }
        }

        /// <summary>Checks whether this robot can use the Machine-scope lock folder.</summary>
        [Category("Resource Lock - Setup")]
        [Description("Checks the Machine-scope lock folder: usable is True when this robot can create lock files there, and reportJson also says whether it can delete its own and other robots' files. Run it once under each robot account when setting up a server. Never throws.")]
        public bool ValidateLockFolder(out bool usable, out string reportJson, out string message)
        {
            usable = false; reportJson = null;
            return NotYetImplemented(nameof(ValidateLockFolder), out message);
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>The folder Machine-scope locks use: the configured one, or the default.</summary>
        internal string LockFolder { get { lock (syncRoot) { return lockFolder ?? LockInput.DefaultFolder; } } }

        private static string DisposedMessage(string operation) => operation + " failed: the component has been disposed.";

        /// <summary>A disposed component fails first; then the first invalid input fails the call; otherwise the operation is not built yet.</summary>
        private bool NotYetImplemented(string operation, out string message, params string[] problems)
        {
            try
            {
                lock (syncRoot)
                {
                    if (disposed) { message = DisposedMessage(operation); return false; }
                }
                foreach (string problem in problems)
                    if (problem != null) { message = operation + " failed: " + problem + "."; return false; }
                message = operation + " is not implemented yet.";
                return false;
            }
            catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { message = NeverThrowsGuard.Failure(operation, ex); return false; }
        }

        /// <summary>Marks the component disposed. Idempotent. (Releasing the locks this instance holds arrives with the scopes.)</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (syncRoot) { disposed = true; }
            }
            base.Dispose(disposing);
        }
    }
}
