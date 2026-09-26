using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>
    /// Opt-in measurements of the default-limit workloads (set RECON_MEASURE=1; run one workload per process for a meaningful peak, for example
    /// <c>RECON_MEASURE=1 dotnet test --filter Measure_Matching</c>). Each result line is written to the test output (use <c>--logger "console;verbosity=detailed"</c> to see it) and, when
    /// RECON_MEASURE_OUT names a file, appended to that file. Without the variable each test returns immediately, so normal runs are unaffected.
    /// </summary>
    public sealed class MeasurementTests
    {
        private readonly Xunit.Abstractions.ITestOutputHelper output;

        public MeasurementTests(Xunit.Abstractions.ITestOutputHelper output) { this.output = output; }

        private static bool Enabled => Environment.GetEnvironmentVariable("RECON_MEASURE") == "1";

        private static string Rows(int count, Func<int, string> row)
        {
            var b = new StringBuilder("[");
            for (int i = 0; i < count; i++) { if (i > 0) b.Append(','); b.Append(row(i)); }
            return b.Append(']').ToString();
        }

        private static ReconciliationUtils Component()
        {
            var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.AddTextComparison("Name", "/name", "/name", true, true, ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddDecimalComparison("Amount", "/amount", "/amount", "0.01", ComparisonNullPolicy.RequireValue, out m), m);
            Assert.True(c.AddTextComparisonSimple("Status", "/status", "/status", out m), m);
            return c;
        }

        private void Measure(string workload, string left, string right, bool maximumLimits = false)
        {
            using ReconciliationUtils c = Component();
            if (maximumLimits) Assert.True(c.ConfigureLimits(250000, 32000000, 500000, 500000, out string limitMessage), limitMessage);
            GC.Collect();
            long before = GC.GetTotalMemory(true);
            var clock = Stopwatch.StartNew();
            bool ok = c.ReconcileJson(left, right, out int exceptions, out string message);
            clock.Stop();
            long endOfRun = GC.GetTotalMemory(false);                    // includes garbage from parsing that has not been collected yet
            long retained = GC.GetTotalMemory(true) - before;            // after a full collection; both inputs are alive in both figures, so this is what the results hold
            Process p = Process.GetCurrentProcess();
            Assert.True(c.GetSummaryJson(out string summary, out string summaryMessage), summaryMessage);
            var readClock = Stopwatch.StartNew();
            int read = 0;
            while (true)
            {
                Assert.True(c.TryReadNextException(out bool has, out _, out _, out _, out _, out _, out _, out _, out string readMessage), readMessage);
                if (!has) break;
                read++;
                while (true)
                {
                    Assert.True(c.TryReadNextDifference(out bool more, out _, out _, out _, out _, out _, out string differenceMessage), differenceMessage);
                    if (!more) break;
                }
            }
            readClock.Stop();
            Assert.Equal(exceptions, read);                              // the cursor delivered every exception the run reported
            string line = $"{workload}: ok={ok} message={message} inputChars={left.Length}+{right.Length} exceptions={exceptions} read={read} runMs={clock.ElapsedMilliseconds} readMs={readClock.ElapsedMilliseconds} " +
                          $"inputsHeapMB={before / 1048576} heapAtEndOfRunMB={endOfRun / 1048576} retainedByResultsMB={retained / 1048576} peakWorkingSetMB={p.PeakWorkingSet64 / 1048576} gen2={GC.CollectionCount(2)} " +
                          $"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} cores={Environment.ProcessorCount}";
            string outFile = Environment.GetEnvironmentVariable("RECON_MEASURE_OUT");
            if (outFile != null) File.AppendAllText(outFile, line + Environment.NewLine);
            output.WriteLine(line);          // always in the test output too (show it with: dotnet test --logger "console;verbosity=detailed")
            Assert.True(ok, message);
        }

        // ~120 characters per row, 50,000 rows a side (the default limit), everything matches
        [Fact]
        public void Measure_Matching()
        {
            if (!Enabled) return;
            string Row(int i, string name) => "{\"id\":\"INV-" + i.ToString("D7") + "\",\"name\":\"" + name + i + "\",\"amount\":\"" + (i % 1000) + ".25\",\"status\":\"Open\",\"note\":\"row " + i + "\"}";
            Measure("matching50k", Rows(50000, i => Row(i, "Customer ")), Rows(50000, i => Row(i, "customer ")));
        }

        // every row differs in two fields: 100,000 details, the default limit
        [Fact]
        public void Measure_WorstCaseDifferences()
        {
            if (!Enabled) return;
            Measure("differences50k",
                Rows(50000, i => "{\"id\":\"K" + i + "\",\"name\":\"left " + i + "\",\"amount\":\"1.00\",\"status\":\"Open\"}"),
                Rows(50000, i => "{\"id\":\"K" + i + "\",\"name\":\"right " + i + "\",\"amount\":\"9.00\",\"status\":\"Open\"}"));
        }

        // 50,000 rows a side over only 100 keys: 100 giant duplicate groups
        [Fact]
        public void Measure_DuplicateHeavy()
        {
            if (!Enabled) return;
            string Row(int i) => "{\"id\":\"G" + (i % 100) + "\",\"name\":\"n" + i + "\",\"amount\":\"1\",\"status\":\"Open\"}";
            Measure("duplicates50k", Rows(50000, Row), Rows(50000, Row));
        }

        // no key repeats and nothing matches: 100,000 lone results
        [Fact]
        public void Measure_AllUnmatched()
        {
            if (!Enabled) return;
            Measure("unmatched50k",
                Rows(50000, i => "{\"id\":\"L" + i + "\",\"name\":\"a\",\"amount\":\"1\",\"status\":\"Open\"}"),
                Rows(50000, i => "{\"id\":\"R" + i + "\",\"name\":\"a\",\"amount\":\"1\",\"status\":\"Open\"}"));
        }

        // the documented maximums: 250,000 rows a side, every row differing in two fields (500,000 details)
        [Fact]
        public void Measure_AtMaximumLimits()
        {
            if (!Enabled) return;
            Measure("maximum250k",
                Rows(250000, i => "{\"id\":\"K" + i + "\",\"name\":\"l" + i + "\",\"amount\":\"1\",\"status\":\"Open\"}"),
                Rows(250000, i => "{\"id\":\"K" + i + "\",\"name\":\"r" + i + "\",\"amount\":\"9\",\"status\":\"Open\"}"), maximumLimits: true);
        }

        private void Report(string line)
        {
            string outFile = Environment.GetEnvironmentVariable("RECON_MEASURE_OUT");
            if (outFile != null) File.AppendAllText(outFile, line + Environment.NewLine);
            output.WriteLine(line);
        }

        private static System.Data.DataTable Table(int rows, string side, bool different)
        {
            var t = new System.Data.DataTable();
            t.Columns.Add("id", typeof(string)); t.Columns.Add("name", typeof(string)); t.Columns.Add("amount", typeof(decimal)); t.Columns.Add("status", typeof(string)); t.Columns.Add("note", typeof(string));
            t.BeginLoadData();
            for (int i = 0; i < rows; i++)
                t.LoadDataRow(new object[] { "INV-" + i.ToString("D7"), (different ? side : "Customer ") + i, (i % 1000) + (different && side == "R" ? 9m : 0m) + 0.25m, "Open", "row " + i }, true);
            t.EndLoadData();
            return t;
        }

        private void MeasureTables(string workload, bool different)
        {
            System.Data.DataTable left = Table(50000, "L", different), right = Table(50000, "R", different);
            using ReconciliationUtils c = Component();
            GC.Collect();
            long before = GC.GetTotalMemory(true);
            var clock = Stopwatch.StartNew();
            bool ok = c.ReconcileDataTables(left, right, out int exceptions, out string message);
            clock.Stop();
            long retained = GC.GetTotalMemory(true) - before;
            Process p = Process.GetCurrentProcess();
            Report($"{workload}: ok={ok} message={message} exceptions={exceptions} runMs={clock.ElapsedMilliseconds} retainedByResultsMB={retained / 1048576} peakWorkingSetMB={p.PeakWorkingSet64 / 1048576} runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            Assert.True(ok, message);
        }

        // 50,000 rows a side read from DataTables (the default row limit), every row matching. Run on its own: the peak is per process.
        [Fact]
        public void Measure_DataTablesMatching()
        {
            if (!Enabled) return;
            MeasureTables("tables50k-matching", different: false);
        }

        // the same, every row differing (names and amounts). Run on its own: the peak is per process.
        [Fact]
        public void Measure_DataTablesDiffering()
        {
            if (!Enabled) return;
            MeasureTables("tables50k-differing", different: true);
        }

        // the report for 50,000 differing rows (100,000 differences); first against the default output limit, then under the maximum
        [Fact]
        public void Measure_Export()
        {
            if (!Enabled) return;
            string left = Rows(50000, i => "{\"id\":\"K" + i + "\",\"name\":\"left " + i + "\",\"amount\":\"1.00\",\"status\":\"Open\"}");
            string right = Rows(50000, i => "{\"id\":\"K" + i + "\",\"name\":\"right " + i + "\",\"amount\":\"9.00\",\"status\":\"Open\"}");
            using ReconciliationUtils c = Component();
            Assert.True(c.ReconcileJson(left, right, out _, out string m), m);
            bool fitsDefault = c.ExportResultsJson("measure", out _, out string refused);
            Report("export50k default limit: " + (fitsDefault ? "fits" : refused));
            Assert.False(fitsDefault, "the Limits and Export pages state that this workload is refused under the default output limit; update them if that changed");
            Assert.Contains("ConfigureOutputLimit", refused);
            Assert.True(c.ConfigureOutputLimit(ReconciliationLimits.MaxOutputCharacters, out m), m);        // measure the size itself, under the maximum
            GC.Collect();
            long before = GC.GetTotalMemory(true);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            bool ok = c.ExportResultsJson("measure", out string report, out string message);
            clock.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Process p = Process.GetCurrentProcess();
            Report($"export50k: ok={ok} message={message} reportChars={report?.Length} exportMs={clock.ElapsedMilliseconds} allocatedMB={allocated / 1048576} peakWorkingSetMB={p.PeakWorkingSet64 / 1048576} runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            Assert.True(ok, message);
        }
    }
}
