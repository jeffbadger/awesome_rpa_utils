using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResourceLockAutomation
{
    /// <summary>
    /// ValidateLockFolder: runs every step the Machine scope needs, harmlessly, and surveys the lease files already there.
    /// <list type="bullet">
    /// <item>Steps, on a probe file (<c>validate-&lt;guid&gt;.probe</c>, which lock operations ignore): create or open the folder, list it, create
    /// a file exclusively, read it back, rewrite it in place, delete it. Usable means locks can be taken: every step but the delete.</item>
    /// <item>Survey: how many lease files this account cannot read (they count as held for up to the longest lease), and how many are damaged
    /// (not a valid lease and older than the grace for half-written files; they are superseded automatically). Damaged files are reported, never
    /// moved or deleted: removing another robot's top generation is exactly what the protocol forbids.</item>
    /// <item>Cleanup of other robots' files: tried on one generation the protocol already allows deleting (below the top, its successor at least
    /// SafeDeleteMinutes old) that another process wrote. "untested" when there is none.</item>
    /// </list>
    /// </summary>
    internal static class FolderCheck
    {
        internal sealed class Report
        {
            internal string Folder;
            internal bool FolderCreated, CanOpen, CanList, CanCreate, CanReadOwn, CanRewriteOwn, CanDeleteOwn;
            internal int LeaseFiles, UnreadableByThisAccount, Damaged;
            internal string CleanupOfOtherRobotsFiles = "untested";
            internal readonly List<string> Warnings = new List<string>();
            internal bool Usable => CanOpen && CanList && CanCreate && CanReadOwn && CanRewriteOwn;
        }

        private static bool Try(Action action)
        {
            try { action(); return true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException) { return false; }
        }

        internal static Report Run(string folder)
        {
            var r = new Report { Folder = folder };
            bool existed = Directory.Exists(folder);
            r.CanOpen = Try(() => Directory.CreateDirectory(folder));
            r.FolderCreated = r.CanOpen && !existed;
            if (!r.CanOpen)
            {
                r.Warnings.Add("The lock folder does not exist and this account cannot create it. Create it (an administrator, once per server) and give every robot account the rights to list it, read and create files; or choose another folder with ConfigureLockFolder.");
                return r;
            }
            r.CanList = Try(() => Directory.EnumerateFiles(folder).Take(1).ToList());

            string probe = Path.Combine(folder, "validate-" + Guid.NewGuid().ToString("N") + ".probe");
            byte[] first = Encoding.UTF8.GetBytes("probe"), second = Encoding.UTF8.GetBytes("probe, rewritten in place");
            r.CanCreate = Try(() => { using var s = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None); s.Write(first, 0, first.Length); });
            if (r.CanCreate)
            {
                r.CanReadOwn = Try(() => { if (File.ReadAllText(probe) != "probe") throw new IOException("read back differs"); });
                r.CanRewriteOwn = Try(() =>
                {
                    using (var s = new FileStream(probe, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { s.SetLength(0); s.Write(second, 0, second.Length); }
                    if (File.ReadAllText(probe) != "probe, rewritten in place") throw new IOException("rewrite differs");
                });
                r.CanDeleteOwn = Try(() => File.Delete(probe));
            }

            if (r.CanList) Survey(folder, r);

            if (!r.CanList) r.Warnings.Add("This account cannot list the lock folder, so it cannot see which locks are held. Give every robot account the right to list the folder.");
            if (!r.CanCreate) r.Warnings.Add("This account cannot create files in the lock folder, so it cannot take Machine-scope locks. Give it the right to create files there, or choose another folder with ConfigureLockFolder.");
            else if (!r.CanReadOwn || !r.CanRewriteOwn) r.Warnings.Add("This account cannot read or rewrite the files it creates in the lock folder, so it cannot renew or release locks. Check the folder's permissions.");
            if (r.CanCreate && !r.CanDeleteOwn) r.Warnings.Add("This account cannot delete the files it creates. Locks still work, but old lease files will accumulate in the folder.");
            if (r.UnreadableByThisAccount > 0)
                r.Warnings.Add(r.UnreadableByThisAccount.ToString(CultureInfo.InvariantCulture) + " lease file(s) cannot be read by this account; each counts as held for up to 24 hours (the longest lease). Give every robot account the right to read the files in the folder.");
            if (r.Damaged > 0)
                r.Warnings.Add(r.Damaged.ToString(CultureInfo.InvariantCulture) + " lease file(s) are damaged (not a valid lease, and too old to be still being written). They are superseded automatically; an administrator may delete them once no robot uses those resources.");
            if (r.CleanupOfOtherRobotsFiles == "no")
                r.Warnings.Add("This account cannot delete other robots' old lease files; each robot cleans up only its own. Optional: an administrator-provisioned folder with Modify rights for all robot accounts lets any robot clean up.");
            return r;
        }

        private static void Survey(string folder, Report r)
        {
            DateTime now = MachineLocks.UtcNow;
            var byChain = new Dictionary<(string Resource, int Slot), List<(long Generation, string Path)>>();
            List<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.lease").ToList(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return; }
            foreach (string path in files)
            {
                Match m = MachineLocks.LeaseName.Match(Path.GetFileName(path));
                if (!m.Success) continue;
                r.LeaseFiles++;
                var key = (m.Groups["resource"].Value, int.Parse(m.Groups["slot"].Value, CultureInfo.InvariantCulture));
                if (!byChain.TryGetValue(key, out var list)) byChain[key] = list = new List<(long, string)>();
                list.Add((long.Parse(m.Groups["gen"].Value, CultureInfo.InvariantCulture), path));
                LeaseRecord record = MachineLocks.ReadRecord(path, out bool denied);
                if (denied) r.UnreadableByThisAccount++;
                else if (record == null && MachineLocks.Present(path) && !MachineLocks.IsYoung(path, now, MachineLocks.UnreadableGraceSeconds)) r.Damaged++;
            }
            // one old generation another process wrote, which the protocol already allows deleting
            foreach (var chain in byChain.Values)
            {
                chain.Sort((a, b) => b.Generation.CompareTo(a.Generation));
                foreach (var (generation, path) in chain.Skip(1))
                {
                    string successor = chain.FirstOrDefault(f => f.Generation == generation + 1).Path;
                    if (successor == null || MachineLocks.IsYoung(successor, now, MachineLocks.SafeDeleteMinutes * 60)) continue;
                    LeaseRecord record = MachineLocks.ReadRecord(path, out _);
                    if (record != null && record.ProcessId == Environment.ProcessId && string.Equals(record.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(path); r.CleanupOfOtherRobotsFiles = "yes"; }
                    catch (UnauthorizedAccessException) { r.CleanupOfOtherRobotsFiles = "no"; }
                    catch (IOException) { continue; }
                    return;
                }
            }
        }

        internal static string ToJson(Report r)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) }))
            {
                w.WriteStartObject();
                w.WriteString("folder", r.Folder);
                w.WriteBoolean("usable", r.Usable);
                w.WriteBoolean("folderCreated", r.FolderCreated);
                w.WriteBoolean("canOpenFolder", r.CanOpen);
                w.WriteBoolean("canList", r.CanList);
                w.WriteBoolean("canCreate", r.CanCreate);
                w.WriteBoolean("canReadOwn", r.CanReadOwn);
                w.WriteBoolean("canRewriteOwn", r.CanRewriteOwn);
                w.WriteBoolean("canDeleteOwn", r.CanDeleteOwn);
                w.WriteNumber("leaseFiles", r.LeaseFiles);
                w.WriteNumber("unreadableByThisAccount", r.UnreadableByThisAccount);
                w.WriteNumber("damaged", r.Damaged);
                w.WriteString("cleanupOfOtherRobotsFiles", r.CleanupOfOtherRobotsFiles);
                w.WriteStartArray("warnings");
                foreach (string warning in r.Warnings) w.WriteStringValue(warning);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
