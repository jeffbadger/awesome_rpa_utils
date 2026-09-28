# Server Bots: the Machine scope

With `LockScope.Machine`, every robot on the machine shares the lock, including Server Bots running as separate RDP sessions under their own
Windows accounts. Each lock is kept as small lease files in a **lock folder**.

## Setting up the lock folder (once per server)

1. **Choose the folder.** The default is `C:\ProgramData\AwesomeRpaUtils\Locks`, created when first used. On hardened servers ProgramData may
   not be writable by robot accounts, and by default one account cannot delete another's files there (locks still work; each robot cleans up
   only its own old files). The recommended setup is a folder an administrator creates, for example `D:\RobotLocks`, with **Modify** rights
   for the robot accounts:

   ```text
   icacls D:\RobotLocks /grant "CONTOSO\RPA-Robots:(OI)(CI)M"
   ```

   The minimum every robot account needs is to **list the folder, read its files and create files** in it. No robot ever needs to delete or
   rename another robot's files; Modify only lets any robot clean up old ones.
2. **Point every robot at it.** Call `ConfigureLockFolder("D:\RobotLocks")` when the automation starts. Every robot that shares a lock must use
   the same folder. Network paths are not supported.
3. **Check it under each robot account.** `ValidateLockFolder` tries every step the Machine scope needs on a probe file and reports:

```json
{
  "folder": "D:\\RobotLocks", "usable": true, "folderCreated": false,
  "canOpenFolder": true, "canList": true, "canCreate": true, "canReadOwn": true, "canRewriteOwn": true, "canDeleteOwn": true,
  "leaseFiles": 12, "unreadableByThisAccount": 0, "damaged": 0, "cleanupOfOtherRobotsFiles": "yes",
  "warnings": []
}
```

   `usable` True means locks can be taken. Each problem comes with a plain-language warning saying what to grant or change.

## What happens when a robot crashes or hangs

- **The robot's process ends** (a crash, a killed runtime, a logged-off session): its locks are free **at once**, without waiting for the lease.
  Each lease file records the holder's machine, process ID and exact process start time, so a later process that reuses the same ID is not
  mistaken for the holder.
- **The robot hangs** but its process is still running: its locks are free when the lease ends. Keep leases short and renew them during long
  work (see [CrossThread](CrossThread.md)).
- A holder this account cannot inspect (another account's process it may not look at) is trusted until its lease ends.

## Good to know

- Lease times are wall-clock UTC, because several processes must agree on them. Do not move a server's clock while robots hold locks.
- A lease file this account cannot read counts as held for up to 24 hours (the longest lease). Give every robot account read access.
- Old lease files are deleted once the file that replaced them is 5 minutes old, so a lock taken very often leaves up to one small file per
  acquisition from the last 5 minutes (about 300 files for a lock taken every second).
- `RenewLock` and `ReleaseLock` work in the folder a lock was taken in, even after `ConfigureLockFolder` changed the folder.
- `GetLocksJson(LockScope.Machine)` shows every held lock with its holder's machine, session and process, for dashboards and troubleshooting:

```json
{
  "scope": "Machine",
  "locks": [
    { "resource": "sap-user-batch01", "kind": "Lock", "capacity": 1,
      "holders": [ { "holder": "MyServer_3", "expiresInSeconds": 241, "machine": "MYSERVER", "sessionId": 4, "processId": 7312 } ] }
  ]
}
```

  Resource names are shown in lower case, as they are stored. Tokens never appear.
