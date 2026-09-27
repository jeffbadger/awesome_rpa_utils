using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TextExtractAutomation.Tests
{
    /// <summary>
    /// Opt-in measurements (set TEXTEXTRACT_MEASURE=1; run one workload per process for a meaningful peak, for example
    /// <c>TEXTEXTRACT_MEASURE=1 dotnet test -c Release --filter Measure_Email --logger "console;verbosity=detailed"</c>). Each result line is written
    /// to the test output and, when TEXTEXTRACT_MEASURE_OUT names a file, appended to it. Without the variable each test returns immediately.
    /// </summary>
    public sealed class MeasurementTests
    {
        private readonly ITestOutputHelper output;
        public MeasurementTests(ITestOutputHelper output) { this.output = output; }

        private static bool Enabled => Environment.GetEnvironmentVariable("TEXTEXTRACT_MEASURE") == "1";

        private void Report(string line)
        {
            line += " runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " cores=" + Environment.ProcessorCount;
            string file = Environment.GetEnvironmentVariable("TEXTEXTRACT_MEASURE_OUT");
            if (file != null) File.AppendAllText(file, line + Environment.NewLine);
            output.WriteLine(line);
        }

        private void Measure(string workload, TextExtractUtils c, string text, int repetitions, int expectedFound)
        {
            Assert.True(c.ConfigureLimits(TemplateLimits.MaxTextCharacters, out string m), m);
            Assert.True(c.ExtractFromText(text, out int found, out _, out m), m);                // warm up (JIT)
            Assert.Equal(expectedFound, found);
            GC.Collect();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repetitions; i++) Assert.True(c.ExtractFromText(text, out found, out _, out m), m);
            clock.Stop();
            long allocatedPerRun = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / repetitions;
            Assert.True(c.GetResultJson(out string json, out m), m);
            Report($"{workload}: chars={text.Length} fields={c.CurrentTemplate.Fields.Count} found={found} msPerRun={clock.Elapsed.TotalMilliseconds / repetitions:F3} " +
                   $"allocatedKBPerRun={allocatedPerRun / 1024} resultJsonChars={json.Length} peakWorkingSetMB={Process.GetCurrentProcess().PeakWorkingSet64 / 1048576}");
        }

        private static TextExtractUtils Invoice()
        {
            var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("InvoiceNumber", "Invoice Number|Invoice No|Invoice #", FieldType.Code, out string m), m);
            Assert.True(c.AddLabelField("Issued", "Invoice Date|Date", ValuePosition.SameLine, FieldType.Date, DecimalStyle.DotDecimal, "dd/MM/yyyy|yyyy-MM-dd", true, Occurrence.First, out m), m);
            Assert.True(c.AddLabelField("Due", "Due Date|Payment Due", ValuePosition.SameLine, FieldType.Date, DecimalStyle.DotDecimal, "dd/MM/yyyy|yyyy-MM-dd", true, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddLabelField("Total", "Amount Due|Total", ValuePosition.SameLine, FieldType.Amount, DecimalStyle.CommaDecimal, "", true, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddLabelFieldSimple("Iban", "IBAN", FieldType.Iban, out m), m);
            Assert.True(c.AddLabelFieldSimple("Contact", "Contact|Email", FieldType.Email, out m), m);
            Assert.True(c.AddLabelFieldSimple("Supplier", "From|Supplier", FieldType.Text, out m), m);
            Assert.True(c.AddLabelField("Po", "PO Number|Order No", ValuePosition.SameLine, FieldType.Code, DecimalStyle.DotDecimal, "", false, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddLabelField("Vat", "VAT", ValuePosition.SameLine, FieldType.Percentage, DecimalStyle.CommaDecimal, "", false, Occurrence.RequireUnique, out m), m);
            Assert.True(c.AddPatternField("Reference", @"Ref(?:erence)?\s*[:#]?\s*(?<value>[A-Z]{3}-\d{4,})", FieldType.Code, DecimalStyle.DotDecimal, "", out m), m);
            return c;
        }

        private static string Email()
        {
            var b = new StringBuilder();
            b.Append("From: ACME Supplies GmbH\r\nTo: accounts payable\r\nSubject: invoice\r\n\r\n");
            for (int i = 0; i < 60; i++) b.Append("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore.\r\n");
            b.Append("Invoice Number: INV-2026-0042\r\nInvoice Date: 26/09/2026    Due Date: 26/10/2026\r\nAmount Due: EUR 1.234,50\r\nVAT: 19 %\r\n");
            b.Append("IBAN: DE89 3704 0044 0532 0130 00\r\nContact: ap@acme.example\r\nRef: ABC-12345\r\n");
            for (int i = 0; i < 20; i++) b.Append("Kind regards and further text that is not relevant to any field at all.\r\n");
            return b.ToString();
        }

        // a typical invoice email, about 9,000 characters and 10 fields, extracted 2,000 times
        [Fact]
        public void Measure_Email()
        {
            if (!Enabled) return;
            using TextExtractUtils c = Invoice();
            Measure("email9k", c, Email(), 2000, 9);
        }

        // a 10,000-line fixed-width report (about 1 MB) with 50 fields, some found once, most missing
        [Fact]
        public void Measure_Report()
        {
            if (!Enabled) return;
            using var c = new TextExtractUtils();
            for (int i = 0; i < 50; i++)
                Assert.True(c.AddLabelField("F" + i, "Field label " + i + "|Alternative name " + i, ValuePosition.SameLine, FieldType.Decimal, DecimalStyle.DotDecimal, "", false, Occurrence.First, out string m), m);
            var b = new StringBuilder();
            for (int i = 0; i < 10000; i++)
                b.Append((i % 200 == 0 && i / 200 < 50 ? "Field label " + (i / 200) + ": " : "ROW " + i.ToString("D6") + "   ") + "ACCOUNT 4471-993-02   AMOUNT 12,500.00   STATUS OPEN     \n");
            Measure("report10kLines", c, b.ToString(), 20, 50);
        }

        // the maximum template (200 fields x 20 labels) over the maximum text (10,000,000 characters)
        [Fact]
        public void Measure_Maximum()
        {
            if (!Enabled) return;
            using var c = new TextExtractUtils();
            for (int i = 0; i < 200; i++)
                Assert.True(c.AddLabelField("F" + i, string.Join("|", Enumerable.Range(0, 20).Select(j => "Label " + i + " alt " + j)), ValuePosition.SameLine, FieldType.Text, DecimalStyle.DotDecimal, "", false, Occurrence.First, out string m), m);
            var b = new StringBuilder();
            int line = 0;
            while (b.Length < TemplateLimits.MaxTextCharacters - 200)
            {
                b.Append(line % 500 == 0 ? "Label " + (line / 500 % 200) + " alt 3: value " + line : "some ordinary words on an ordinary line of text " + line).Append('\n');
                line++;
            }
            b.Append('x', TemplateLimits.MaxTextCharacters - b.Length);                        // exactly at the text limit
            Assert.Equal(TemplateLimits.MaxTextCharacters, b.Length);
            Measure("maximum10M", c, b.ToString(), 1, 200);
        }
    }
}
