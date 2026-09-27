using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace ResourceLockAutomation
{
    /// <summary>One lease as written in a lease file.</summary>
    internal sealed class LeaseRecord
    {
        internal string Token;
        internal string Holder;
        internal LockKind Kind;
        internal int Capacity;
        internal DateTime AcquiredUtc;
        internal DateTime ExpiresUtc;
        internal string Machine;
        internal int SessionId;
        internal int ProcessId;
        internal long ProcessStart;               // the process's exact start identity (MachineLocks.StartIdentity)
        internal bool Released;

        internal string ToJson()
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) }))
            {
                w.WriteStartObject();
                w.WriteNumber("formatVersion", 1);
                w.WriteString("token", Token);
                w.WriteString("holder", Holder);
                w.WriteString("kind", Kind.ToString());
                w.WriteNumber("capacity", Capacity);
                w.WriteString("acquiredUtc", AcquiredUtc.ToString("O", CultureInfo.InvariantCulture));
                w.WriteString("expiresUtc", ExpiresUtc.ToString("O", CultureInfo.InvariantCulture));
                w.WriteString("machine", Machine);
                w.WriteNumber("sessionId", SessionId);
                w.WriteNumber("processId", ProcessId);
                w.WriteNumber("processStart", ProcessStart);
                w.WriteBoolean("released", Released);
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>The record in a lease file, or null when the file is not a valid lease (it is then treated as stale and superseded).</summary>
        internal static LeaseRecord Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object || r.GetProperty("formatVersion").GetInt32() != 1) return null;
                var record = new LeaseRecord
                {
                    Token = r.GetProperty("token").GetString(),
                    Holder = r.GetProperty("holder").GetString(),
                    Kind = Enum.Parse<LockKind>(r.GetProperty("kind").GetString(), false),
                    Capacity = r.GetProperty("capacity").GetInt32(),
                    AcquiredUtc = DateTime.Parse(r.GetProperty("acquiredUtc").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    ExpiresUtc = DateTime.Parse(r.GetProperty("expiresUtc").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    Machine = r.GetProperty("machine").GetString(),
                    SessionId = r.GetProperty("sessionId").GetInt32(),
                    ProcessId = r.GetProperty("processId").GetInt32(),
                    ProcessStart = r.GetProperty("processStart").GetInt64(),
                    Released = r.GetProperty("released").GetBoolean()
                };
                if (LockInput.Token(record.Token) != null || string.IsNullOrEmpty(record.Holder) || record.Capacity < 1 || record.ExpiresUtc.Kind != DateTimeKind.Utc) return null;
                return record;
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException || ex is FormatException || ex is ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The Machine scope: locks shared by every robot on the machine (Server Bots in other sessions, under other Windows accounts) through lease
    /// files in one folder. Each robot needs the rights to list the folder, read its files and create files in it; no robot ever needs to delete or
    /// rename another robot's file:
    /// <list type="bullet">
    /// <item>A lock is a chain of generation files <c>&lt;resource&gt;.&lt;slot&gt;.&lt;generation&gt;.lease</c> (a lock is slot 0; a pool of
    /// capacity N uses slots 0 to N-1). The highest generation of a chain decides: it holds the slot when it is not released, not expired and
    /// its process is still running.</item>
    /// <item>Acquiring creates generation n+1 exclusively (create-new: O_EXCL on Linux, CREATE_NEW on Windows), so only one robot can create
    /// it. (A move "without overwrite" is not atomic on Linux: .NET checks, then renames, and nearly every race had several winners.) A new file
    /// can be read before its content is written, so a reader retries briefly, and a young file that still cannot be read counts as held, never
    /// as stale; only an unreadable file older than <see cref="UnreadableGraceSeconds"/> seconds is superseded.</item>
    /// <item>The highest generation is never found by a directory listing alone: a listing is only guaranteed to return files that are neither
    /// created nor deleted while it runs, and a robot that missed the new highest generation and its just-deleted predecessor created a lower
    /// number (two holders; the multi-process stress test found it). The listing gives a starting number; the top is then found by looking up
    /// n+1, n+2, ... by name until one is missing. Holders renew and release by rewriting their own file in place, so the top never leaves the
    /// directory.</item>
    /// <item>Only generations below the top are ever deleted, and only once their successor is at least <see cref="SafeDeleteMinutes"/>
    /// minutes old; an acquire that takes longer than <see cref="MaxDecisionSeconds"/> seconds between reading and creating starts over. So no
    /// generation at or above the top an operation started from can disappear while it runs, the lookups never skip a number, and generation
    /// numbers only grow. Deleting old generations is cleanup (anyone allowed to, at least their creator), never needed for correctness.</item>
    /// <item>A lease is dead when its process has ended, or the process with its ID has another exact start identity (the ID was reused). A
    /// process that cannot be looked at (another user's, or on another machine) is taken to be running; lease expiry then covers it.</item>
    /// <item>A lease file this account may not read is held until it is <see cref="LockLimits.MaxLeaseSeconds"/> seconds old: a live holder
    /// rewrites it on every renewal, and no lease is longer, so it is never superseded while it may be live (ValidateLockFolder reports such a
    /// folder).</item>
    /// </list>
    /// Within this process all Machine-scope operations are serialized, and a disposed component is refused inside that lock, like the Process
    /// scope. Times are UTC wall-clock times, since several processes must agree on them.
    /// </summary>
    internal static class MachineLocks
    {
        private sealed class Holding
        {
            internal string Folder;
            internal string Resource;
            internal string Token;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<object, List<Holding>> Holdings = new Dictionary<object, List<Holding>>(ReferenceEqualityComparer.Instance);
        private static readonly Regex LeaseName = new Regex(@"^(?<resource>.+)\.(?<slot>\d{1,3})\.(?<gen>\d{1,18})\.lease$", RegexOptions.CultureInvariant);

        private static readonly Lazy<(int Id, long StartTicks, int Session)> Self = new Lazy<(int, long, int)>(() =>
        {
            using Process p = Process.GetCurrentProcess();
            return (p.Id, StartIdentity(p), p.SessionId);
        });

        /// <summary>
        /// A process's exact start identity, compared exactly: with its ID it names one process even after the ID is reused. On Windows the
        /// creation time in ticks (from the kernel's creation FILETIME). On Linux the start time in clock ticks since boot from /proc/[pid]/stat,
        /// because .NET's StartTime there is boot time estimated from the wall clock, which moves with clock adjustments (seconds over hours), so
        /// no tolerance would be both safe and exact.
        /// </summary>
        internal static long StartIdentity(Process process)
        {
            if (OperatingSystem.IsLinux())
            {
                string stat = File.ReadAllText("/proc/" + process.Id.ToString(CultureInfo.InvariantCulture) + "/stat");
                string[] fields = stat.Substring(stat.LastIndexOf(')') + 2).Split(' ');   // after "pid (name) ": field 3 (state) is fields[0]
                return long.Parse(fields[22 - 3], CultureInfo.InvariantCulture);          // field 22: starttime
            }
            return process.StartTime.ToUniversalTime().Ticks;
        }

        internal static DateTime UtcNow => DateTime.UtcNow;

        private static string Key(string resource) => resource.ToLowerInvariant();          // names are ASCII (LockInput), so this is exact

        internal static string FileName(string resource, int slot, long generation) =>
            Key(resource) + "." + slot.ToString(CultureInfo.InvariantCulture) + "." + generation.ToString(CultureInfo.InvariantCulture) + ".lease";

        // ------------------------------------------------------------------ reading

        /// <summary>The generation files of one slot, highest first.</summary>
        private sealed class Chain
        {
            internal int Slot;
            internal List<(long Generation, string Path)> Files = new List<(long, string)>();
            internal long Highest => Files.Count == 0 ? 0 : Files[0].Generation;
            internal LeaseRecord Top;             // the highest generation's record; null when there is none or it is not a valid lease
            internal bool Held;                   // the slot is held now (by Top, or by a lease still being written when Top is null)
        }

        /// <summary>Every chain of a resource, by slot, with the state of its highest generation.</summary>
        /// <summary>
        /// Every chain of a resource, by slot, with the state of its top generation. Slots 0 to probeSlots-1 are looked up even when the listing
        /// shows none of their files. The top of each chain is found by name, upwards from the listing's highest number (see the class remarks).
        /// </summary>
        private static SortedDictionary<int, Chain> ReadChains(string folder, string resource, DateTime now, int probeSlots = 1)
        {
            var chains = new SortedDictionary<int, Chain>();
            string key = Key(resource);
            foreach (string path in Directory.EnumerateFiles(folder, key + ".*.lease"))
            {
                if (HideFromListingForTests?.Invoke(path) == true) continue;
                Match m = LeaseName.Match(Path.GetFileName(path));
                if (!m.Success || m.Groups["resource"].Value != key) continue;              // "a.b.0.1.lease" also matches the pattern for "a"
                int slot = int.Parse(m.Groups["slot"].Value, CultureInfo.InvariantCulture);
                if (!chains.TryGetValue(slot, out Chain chain)) chains[slot] = chain = new Chain { Slot = slot };
                chain.Files.Add((long.Parse(m.Groups["gen"].Value, CultureInfo.InvariantCulture), path));
            }
            for (int slot = 0; slot < probeSlots; slot++)
                if (!chains.ContainsKey(slot)) chains[slot] = new Chain { Slot = slot };
            foreach (Chain chain in chains.Values.ToList())
            {
                chain.Files.Sort((a, b) => b.Generation.CompareTo(a.Generation));
                long top = chain.Highest;
                for (int step = 0; step < MaxProbe && Present(Path.Combine(folder, FileName(resource, chain.Slot, top + 1))); step++) top++;
                if (top > chain.Highest) chain.Files.Insert(0, (top, Path.Combine(folder, FileName(resource, chain.Slot, top))));
                if (chain.Files.Count == 0) chains.Remove(chain.Slot);
            }
            foreach (Chain chain in chains.Values)
            {
                string top = chain.Files[0].Path;
                chain.Top = ReadRecord(top, out bool denied);
                for (int attempt = 0; chain.Top == null && !denied && attempt < 10 && Present(top); attempt++)
                {
                    Thread.Sleep(10);                                                       // a new lease whose content is still being written
                    chain.Top = ReadRecord(top, out denied);
                }
                if (chain.Top != null) chain.Held = !chain.Top.Released && now < chain.Top.ExpiresUtc && IsAlive(chain.Top);
                else if (denied) chain.Held = IsYoung(top, now, LockLimits.MaxLeaseSeconds);  // may be a live lease: held as long as any lease could be
                else chain.Held = IsYoung(top, now, UnreadableGraceSeconds);                 // unreadable: held while young, stale once old
            }
            return chains;
        }

        /// <summary>
        /// Whether a file exists, telling a missing file from one this account may not access. File.Exists answers False for both, so an
        /// access-denied lease would look absent: the lookup of the top would stop early and a protected lease would look old.
        /// </summary>
        internal static bool Present(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                // Access denied (or busy) says nothing about existence: in a folder this account may list but not access, every name is denied,
                // and taking that as present made the lookup of the top loop forever. The listing confirms the exact name; it never invents one.
                try { return Directory.EnumerateFiles(Path.GetDirectoryName(path), Path.GetFileName(path)).Any(); }
                catch (Exception inner) when (inner is UnauthorizedAccessException || inner is IOException) { return true; }
            }
        }

        /// <summary>A safety bound on the lookup of the top by name; far above any real chain (old generations are cleaned up).</summary>
        internal const int MaxProbe = 100000;

        /// <summary>How long an unreadable lease file counts as held (being written) before it is treated as stale and superseded.</summary>
        internal const int UnreadableGraceSeconds = 10;

        /// <summary>Whether a present file was written less than the given seconds ago. A timestamp that cannot be read counts as young (held).</summary>
        private static bool IsYoung(string path, DateTime now, int seconds)
        {
            if (!Present(path)) return false;
            try { return (now - File.GetLastWriteTimeUtc(path)).TotalSeconds < seconds; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return true; }
        }

        /// <summary>The holders of the held slots; a lease still being written, or one this account may not read, has no readable holder.</summary>
        private static List<string> HolderNames(IEnumerable<Chain> held) => held.Select(c => c.Top?.Holder ?? "(an unreadable lease)").ToList();

        /// <summary>A lease file's record; null when it is missing or not a valid lease. Opened so that a holder can replace it meanwhile.</summary>
        private static LeaseRecord ReadRecord(string path) => ReadRecord(path, out _);

        private static LeaseRecord ReadRecord(string path, out bool accessDenied)
        {
            accessDenied = false;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return LeaseRecord.Parse(reader.ReadToEnd());
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) { return null; }                                            // opened exclusively by its creator for a moment
            catch (UnauthorizedAccessException) { accessDenied = true; return null; }
        }

        /// <summary>
        /// Whether the lease's process may still be running. Dead only when this machine can tell: no process has the ID, or the process with the
        /// ID started at another time. Another machine's lease, or a process that cannot be looked at, counts as running.
        /// </summary>
        internal static bool IsAlive(LeaseRecord record)
        {
            if (!string.Equals(record.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
            if (record.ProcessId == Self.Value.Id) return record.ProcessStart == Self.Value.StartTicks;
            Process process;
            try { process = Process.GetProcessById(record.ProcessId); }
            catch (ArgumentException) { return false; }                                     // no such process
            catch (InvalidOperationException) { return false; }
            using (process)
            {
                try
                {
                    if (process.HasExited) return false;
                    return StartIdentity(process) == record.ProcessStart;                   // another start: the ID was reused
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    return false;                                                            // /proc entry gone: the process has ended
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is NotSupportedException || ex is UnauthorizedAccessException)
                {
                    return true;                                                             // cannot look: let the lease expire instead
                }
            }
        }

        // ------------------------------------------------------------------ writing

        /// <summary>Creates a lease file exclusively: False when the file already exists (another robot created that generation first).</summary>
        private static bool TryCreate(string path, LeaseRecord record)
        {
            byte[] content = new UTF8Encoding(false).GetBytes(record.ToJson());
            FileStream stream;
            try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) { return false; }                                           // it exists (or appeared and went): read again
            using (stream)
            {
                stream.Write(content, 0, content.Length);
                stream.Flush(true);
            }
            return true;
        }

        /// <summary>
        /// Rewrites this robot's own lease file in place (renew, release), never by renaming a new copy over it, so the file never leaves the
        /// directory listing. A reader may see it empty or partly written for a moment; it retries, and a young unreadable file counts as held.
        /// False when the file no longer exists (it was cleaned up: the lease was lost).
        /// </summary>
        private static bool Overwrite(string path, LeaseRecord record)
        {
            byte[] content = new UTF8Encoding(false).GetBytes(record.ToJson());
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.SetLength(0);
                    stream.Write(content, 0, content.Length);
                    stream.Flush(true);
                    return true;
                }
                catch (FileNotFoundException) { return false; }
                catch (IOException) when (attempt < 5) { Thread.Sleep(20 * attempt); }             // its creator still has it open exclusively
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Tests only: files a listing should miss, as a real listing may while files are created or deleted.</summary>
        internal static Func<string, bool> HideFromListingForTests;

        /// <summary>A generation may be deleted once its successor is this old: longer than any operation's read-to-create window.</summary>
        internal const int SafeDeleteMinutes = 5;

        /// <summary>An acquire that took longer than this between reading the chains and creating a generation starts over.</summary>
        internal const int MaxDecisionSeconds = 60;

        /// <summary>
        /// Cleanup: deletes generations of a chain below the top whose successor is at least SafeDeleteMinutes old, where this robot may (at least
        /// its own). A younger predecessor stays, so a robot that is still deciding from an older reading cannot find a gap and fill it.
        /// </summary>
        private static void DeleteOldGenerations(string folder, string resource, int slot, long highest)
        {
            string key = Key(resource);
            DateTime now = UtcNow;
            foreach (string path in Directory.EnumerateFiles(folder, key + "." + slot.ToString(CultureInfo.InvariantCulture) + ".*.lease"))
            {
                Match m = LeaseName.Match(Path.GetFileName(path));
                if (!m.Success || m.Groups["resource"].Value != key || int.Parse(m.Groups["slot"].Value, CultureInfo.InvariantCulture) != slot) continue;
                long generation = long.Parse(m.Groups["gen"].Value, CultureInfo.InvariantCulture);
                if (generation >= highest) continue;
                string successor = Path.Combine(folder, FileName(resource, slot, generation + 1));
                try
                {
                    if (Present(successor) && !IsYoung(successor, now, SafeDeleteMinutes * 60)) TryDelete(path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }

        private static LeaseRecord NewRecord(string token, string holder, LockKind kind, int capacity, DateTime now, TimeSpan lease) =>
            new LeaseRecord
            {
                Token = token, Holder = holder, Kind = kind, Capacity = capacity, AcquiredUtc = now, ExpiresUtc = now + lease,
                Machine = Environment.MachineName, SessionId = Self.Value.Session, ProcessId = Self.Value.Id, ProcessStart = Self.Value.StartTicks
            };

        // ------------------------------------------------------------------ operations

        private static bool IsClosed(object owner) => owner is ResourceLockUtils component && component.IsClosedForLeases;

        private static int HeldBy(object owner) => Holdings.TryGetValue(owner, out List<Holding> list) ? list.Count : 0;

        private static void Remember(object owner, string folder, string resource, string token)
        {
            if (!Holdings.TryGetValue(owner, out List<Holding> list)) Holdings[owner] = list = new List<Holding>();
            list.Add(new Holding { Folder = folder, Resource = resource, Token = token });
        }

        private static void Forget(object owner, string token)
        {
            if (!Holdings.TryGetValue(owner, out List<Holding> list)) return;
            list.RemoveAll(h => string.Equals(h.Token, token, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0) Holdings.Remove(owner);
        }

        private static void ForgetEverywhere(string token)
        {
            foreach (object owner in Holdings.Keys.ToList()) Forget(owner, token);
        }

        /// <summary>
        /// Stops tracking an owner's leases that are no longer held where they were taken (expired, released, superseded or their file gone),
        /// so leases that ended without being renewed or released do not count against the per-component limit.
        /// </summary>
        private static void PruneLost(object owner)
        {
            if (!Holdings.TryGetValue(owner, out List<Holding> list)) return;
            DateTime now = UtcNow;
            foreach (var group in list.ToList().GroupBy(h => (h.Folder, Resource: Key(h.Resource))))
            {
                SortedDictionary<int, Chain> chains;
                try { chains = Directory.Exists(group.Key.Folder) ? ReadChains(group.Key.Folder, group.First().Resource, now) : new SortedDictionary<int, Chain>(); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }   // cannot tell: keep counting them
                foreach (Holding h in group)
                {
                    var (chain, _) = FindTop(chains, h.Token);
                    if (chain == null || !chain.Held) list.Remove(h);
                }
            }
            if (list.Count == 0) Holdings.Remove(owner);
        }

        /// <summary>Where a token's lease was taken (by any component in this process), or null when this process does not know it.</summary>
        private static Holding FindHolding(string token) =>
            Holdings.Values.SelectMany(l => l).FirstOrDefault(h => string.Equals(h.Token, token, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The folder a token operation works in: the folder its lease was taken in when this process knows the token (so changing the lock
        /// folder cannot strand it). False when the token belongs to another resource: that is the caller's mistake, not a lost lease, so the
        /// lease stays tracked.
        /// </summary>
        private static bool Locate(ref string folder, string resource, string token, out Holding holding)
        {
            holding = FindHolding(token);
            if (holding == null) return true;
            if (!string.Equals(holding.Resource, resource, StringComparison.OrdinalIgnoreCase)) return false;
            folder = holding.Folder;
            return true;
        }

        internal static int HeldByForTests(object owner) { lock (Sync) return HeldBy(owner); }

        /// <summary>Takes a lock (slot 0) or one of capacity slots now, or reports who holds it.</summary>
        internal static AcquireResult TryAcquire(string folder, string resource, LockKind kind, int capacity, string holder, TimeSpan lease, object owner)
        {
            lock (Sync)
            {
                if (IsClosed(owner)) return new AcquireResult(false, null, null, 0, "the component has been disposed");
                if (!EnsureFolder(folder, out string problem)) return new AcquireResult(false, null, null, 0, problem);
                DateTime now = UtcNow;
                var decision = Stopwatch.StartNew();
                SortedDictionary<int, Chain> chains = ReadChains(folder, resource, now, capacity);
                List<LeaseRecord> held = chains.Values.Where(c => c.Held && c.Top != null).Select(c => c.Top).ToList();
                // Best effort, as the check reads before it writes: two robots using one name in two ways at the same moment are not caught.
                if (held.Any(r => r.Kind != kind))
                    return new AcquireResult(false, null, null, 0, kind == LockKind.Lock
                        ? "the resource is in use as a slot pool; use TryAcquireSlot or AcquireSlot, or another resource name"
                        : "the resource is in use as a lock; use TryAcquireLock or AcquireLock, or another resource name");
                LeaseRecord other = held.FirstOrDefault(r => r.Capacity != capacity);
                if (other != null)
                    return new AcquireResult(false, null, null, 0, "the resource is in use with a capacity of " + other.Capacity + "; every caller must give the same capacity");
                // A held slot whose lease cannot be read has an unknown kind and capacity: allocating another slot beside it could mix a lock with
                // a pool. Wait until it can be read (a half-written lease, milliseconds) or ends (an unreadable one, at most the longest lease).
                if (chains.Values.Any(c => c.Held && c.Top == null))
                {
                    List<string> unreadable = HolderNames(chains.Values.Where(c => c.Held));
                    return new AcquireResult(false, null, unreadable.FirstOrDefault(), unreadable.Count, null);
                }
                if (HeldBy(owner) >= LockLimits.MaxHeldPerInstance) PruneLost(owner);           // leases that ended unvisited still count until pruned
                if (HeldBy(owner) >= LockLimits.MaxHeldPerInstance)
                    return new AcquireResult(false, null, null, 0, "this component already holds " + LockLimits.MaxHeldPerInstance + " locks and slots; release some first");
                for (int slot = 0; slot < capacity; slot++)
                {
                    chains.TryGetValue(slot, out Chain chain);
                    if (chain != null && chain.Held) continue;
                    long generation = (chain?.Highest ?? 0) + 1;
                    if (decision.Elapsed.TotalSeconds > MaxDecisionSeconds) break;          // too slow to trust the reading: report not acquired, try again
                    string token = Guid.NewGuid().ToString("N");
                    bool created;
                    try { created = TryCreate(Path.Combine(folder, FileName(resource, slot, generation)), NewRecord(token, holder, kind, capacity, now, lease)); }
                    catch (UnauthorizedAccessException)
                    {
                        return new AcquireResult(false, null, null, 0, "this robot's account cannot create files in the lock folder; grant it access or set another folder with ConfigureLockFolder (ValidateLockFolder checks one)");
                    }
                    if (!created) continue;                                                  // another robot won this slot
                    Remember(owner, folder, resource, token);
                    // The lease exists now: nothing after this may turn it into a failed acquire, so cleanup and the count are best effort.
                    int holderCount = chains.Values.Count(c => c.Held) + 1;
                    try
                    {
                        DeleteOldGenerations(folder, resource, slot, generation);
                        holderCount = ReadChains(folder, resource, UtcNow, capacity).Values.Count(c => c.Held);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                    return new AcquireResult(true, token, null, holderCount, null);
                }
                List<string> holders = HolderNames(ReadChains(folder, resource, UtcNow, capacity).Values.Where(c => c.Held));
                return new AcquireResult(false, null, holders.FirstOrDefault(), holders.Count, null);
            }
        }

        /// <summary>
        /// Waits up to wait for a lock or slot, checking the lease files with growing, jittered pauses (50 ms up to 1 s). Like the Process scope,
        /// every check including the one at the deadline tries to acquire, and a zero wait is a single attempt. The lock is not held while pausing.
        /// </summary>
        internal static AcquireResult Acquire(string folder, string resource, LockKind kind, int capacity, string holder, TimeSpan lease, TimeSpan wait, object owner)
        {
            var clock = Stopwatch.StartNew();
            int pause = 50;
            while (true)
            {
                AcquireResult result = TryAcquire(folder, resource, kind, capacity, holder, lease, owner);
                long left = (long)wait.TotalMilliseconds - clock.ElapsedMilliseconds;
                if (result.Acquired || result.Problem != null || left <= 0) return result;
                int jittered = (int)(pause * (0.75 + Random.Shared.NextDouble() * 0.5));
                Thread.Sleep((int)Math.Max(1, Math.Min(left, jittered)));
                pause = Math.Min(1000, pause * 2);
            }
        }

        /// <summary>The chain and file holding a token, when that file is still the highest generation of its chain.</summary>
        private static (Chain Chain, string Path) FindTop(SortedDictionary<int, Chain> chains, string token) =>
            chains.Values.Where(c => c.Top != null && string.Equals(c.Top.Token, token, StringComparison.OrdinalIgnoreCase))
                  .Select(c => (c, c.Files[0].Path)).FirstOrDefault();

        /// <summary>Extends a live lease; False (not renewed) when it expired, was released or taken over, or its token is not known.</summary>
        internal static bool Renew(string folder, string resource, string token, TimeSpan lease, out TimeSpan expiresIn)
        {
            expiresIn = TimeSpan.Zero;
            lock (Sync)
            {
                if (!Locate(ref folder, resource, token, out Holding holding)) return false;
                if (!Directory.Exists(folder)) { if (holding != null) ForgetEverywhere(token); return false; }
                DateTime now = UtcNow;
                var (chain, path) = FindTop(ReadChains(folder, resource, now), token);
                if (chain == null) { if (holding != null) ForgetEverywhere(token); return false; }  // superseded where it was taken: lost
                if (!chain.Held) { ForgetEverywhere(token); return false; }
                LeaseRecord record = chain.Top;
                record.ExpiresUtc = now + lease;
                if (!Overwrite(path, record)) { ForgetEverywhere(token); return false; }
                // A taker can only supersede a lease it saw expired; this one was live, so the check below only guards against a stale view.
                if (ReadChains(folder, resource, UtcNow).TryGetValue(chain.Slot, out Chain after) && after.Highest != chain.Highest) { ForgetEverywhere(token); return false; }
                expiresIn = lease;
                return true;
            }
        }

        /// <summary>Releases a live lease by rewriting it as released; False (not released) when it had already been lost.</summary>
        internal static bool Release(string folder, string resource, string token)
        {
            lock (Sync)
            {
                if (!Locate(ref folder, resource, token, out Holding holding)) return false;
                if (!Directory.Exists(folder)) { if (holding != null) ForgetEverywhere(token); return false; }
                DateTime now = UtcNow;
                var (chain, path) = FindTop(ReadChains(folder, resource, now), token);
                if (chain == null)
                {
                    if (holding == null) return false;                                       // not a lease of this folder and resource
                    ForgetEverywhere(token);                                                 // superseded where it was taken: lost
                    try { DeleteOwnSuperseded(folder, resource, token); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                    return false;
                }
                ForgetEverywhere(token);
                bool wasHeld = chain.Held;
                if (!chain.Top.Released)
                {
                    LeaseRecord record = chain.Top;
                    record.Released = true;
                    Overwrite(path, record);                                                 // never deleted or renamed: generation numbers only grow
                }
                try { DeleteOldGenerations(folder, resource, chain.Slot, chain.Highest); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                return wasHeld;
            }
        }

        /// <summary>A lost lease's file is below a newer generation; it is cleaned up with the rest of its chain once that is safe.</summary>
        private static void DeleteOwnSuperseded(string folder, string resource, string token)
        {
            foreach (Chain chain in ReadChains(folder, resource, UtcNow).Values)
                if (chain.Files.Skip(1).Any(f => string.Equals(ReadRecord(f.Path)?.Token, token, StringComparison.OrdinalIgnoreCase)))
                    DeleteOldGenerations(folder, resource, chain.Slot, chain.Highest);
        }

        /// <summary>Supersedes every held slot of a resource with a released generation; returns how many holders lost their lease.</summary>
        internal static int ForceRelease(string folder, string resource, string byHolder)
        {
            lock (Sync)
            {
                if (!Directory.Exists(folder)) return 0;
                DateTime now = UtcNow;
                int count = 0;
                foreach (Chain chain in ReadChains(folder, resource, now).Values.Where(c => c.Held))
                {
                    LeaseRecord marker = NewRecord(Guid.NewGuid().ToString("N"), byHolder, chain.Top?.Kind ?? LockKind.Lock, chain.Top?.Capacity ?? 1, now, TimeSpan.Zero);
                    marker.Released = true;
                    if (TryCreate(Path.Combine(folder, FileName(resource, chain.Slot, chain.Highest + 1)), marker)) count++;
                    if (chain.Top != null) ForgetEverywhere(chain.Top.Token);
                }
                return count;
            }
        }

        /// <summary>Releases every lease an owner still holds (it is being disposed and already closed, so it cannot acquire more).</summary>
        internal static void ReleaseAllOwnedBy(object owner)
        {
            List<Holding> list;
            lock (Sync)
            {
                if (!Holdings.TryGetValue(owner, out list)) return;
                list = list.ToList();
            }
            foreach (Holding h in list)
            {
                try { Release(h.Folder, h.Resource, h.Token); }
                catch (Exception ex) when (NeverThrowsGuard.IsRecoverable(ex)) { }           // the lease still expires, or its process ends
            }
        }

        internal static void Status(string folder, string resource, out bool held, out string holders, out int expiresInSeconds, out int holderCount)
        {
            lock (Sync)
            {
                held = false; holders = null; expiresInSeconds = 0; holderCount = 0;
                if (!Directory.Exists(folder)) return;
                DateTime now = UtcNow;
                List<Chain> live = ReadChains(folder, resource, now).Values.Where(c => c.Held).ToList();
                if (live.Count == 0) return;
                held = true;
                holders = string.Join("|", HolderNames(live));
                List<LeaseRecord> known = live.Where(c => c.Top != null).Select(c => c.Top).ToList();
                expiresInSeconds = known.Count == 0 ? 1 : ProcessLocks.Seconds((long)Math.Ceiling((known.Min(r => r.ExpiresUtc) - now).TotalMilliseconds));
                holderCount = live.Count;
            }
        }

        /// <summary>Every held lock and slot in the folder, by resource, with each holder's machine, session and process. Never tokens.</summary>
        internal static string Json(string folder)
        {
            lock (Sync)
            {
                DateTime now = UtcNow;
                var resources = new SortedSet<string>(StringComparer.Ordinal);
                if (Directory.Exists(folder))
                    foreach (string path in Directory.EnumerateFiles(folder, "*.lease"))
                    {
                        Match m = LeaseName.Match(Path.GetFileName(path));
                        if (m.Success) resources.Add(m.Groups["resource"].Value);
                    }
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) }))
                {
                    w.WriteStartObject();
                    w.WriteString("scope", "Machine");
                    w.WriteStartArray("locks");
                    foreach (string resource in resources)
                    {
                        List<Chain> live = ReadChains(folder, resource, now).Values.Where(c => c.Held).ToList();
                        if (live.Count == 0) continue;
                        LeaseRecord known = live.Select(c => c.Top).FirstOrDefault(r => r != null);
                        w.WriteStartObject();
                        w.WriteString("resource", resource);
                        if (known == null) { w.WriteNull("kind"); w.WriteNull("capacity"); }   // only unreadable leases: unknown
                        else { w.WriteString("kind", known.Kind.ToString()); w.WriteNumber("capacity", known.Capacity); }
                        w.WriteStartArray("holders");
                        foreach (Chain c in live)
                        {
                            LeaseRecord r = c.Top;
                            w.WriteStartObject();
                            if (r == null)
                            {
                                w.WriteString("holder", "(an unreadable lease)");            // being written, or not readable by this account
                                w.WriteNull("expiresInSeconds"); w.WriteNull("machine"); w.WriteNull("sessionId"); w.WriteNull("processId");
                            }
                            else
                            {
                                w.WriteString("holder", r.Holder);
                                w.WriteNumber("expiresInSeconds", ProcessLocks.Seconds((long)Math.Ceiling((r.ExpiresUtc - now).TotalMilliseconds)));
                                w.WriteString("machine", r.Machine);
                                w.WriteNumber("sessionId", r.SessionId);
                                w.WriteNumber("processId", r.ProcessId);
                            }
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>Creates the folder when it is missing. A folder that cannot be created or used is a problem for the call, not an exception.</summary>
        private static bool EnsureFolder(string folder, out string problem)
        {
            problem = null;
            try
            {
                Directory.CreateDirectory(folder);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problem = "the lock folder does not exist and cannot be created; set a usable folder with ConfigureLockFolder (ValidateLockFolder checks one)";
                return false;
            }
        }
    }
}
