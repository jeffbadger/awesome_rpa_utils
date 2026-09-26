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
            long afterRun = GC.GetTotalMemory(false);
            Process p = Process.GetCurrentProcess();
            c.GetSummaryJson(out string summary, out _);
            var readClock = Stopwatch.StartNew();
            int read = 0;
            while (c.TryReadNextException(out bool has, out _, out _, out _, out _, out _, out _, out _, out _) && has)
            {
                read++;
                while (c.TryReadNextDifference(out bool more, out _, out _, out _, out _, out _, out _) && more) { }
            }
            readClock.Stop();
            string line = $"{workload}: ok={ok} message={message} inputChars={left.Length}+{right.Length} exceptions={exceptions} read={read} runMs={clock.ElapsedMilliseconds} readMs={readClock.ElapsedMilliseconds} " +
                          $"managedBeforeMB={before / 1048576} managedAfterRunMB={afterRun / 1048576} peakWorkingSetMB={p.PeakWorkingSet64 / 1048576} gen2={GC.CollectionCount(2)} " +
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
    }
}
