using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>Runs the worked examples from Documentation/ so a page cannot drift from the behavior it describes.</summary>
    public sealed class DocumentationExampleTests
    {
        private const string QuickLeft = "[ { \"invoice\": \"INV-100\", \"amount\": \"10.00\", \"status\": \"Open\" }, { \"invoice\": \"INV-101\", \"amount\": \"20.00\", \"status\": \"Open\" }, { \"invoice\": \"INV-102\", \"amount\": \"5.00\",  \"status\": \"Open\" } ]";
        private const string QuickRight = "[ { \"invoiceId\": \"INV-100\", \"paid\": \"10.00\", \"status\": \"open\" }, { \"invoiceId\": \"INV-101\", \"paid\": \"21.50\", \"status\": \"Open\" }, { \"invoiceId\": \"INV-103\", \"paid\": \"9.00\",  \"status\": \"Open\" } ]";

        [Fact]
        public void QuickStart_ProducesTheDocumentedCountsExceptionsAndDifferences()
        {
            using var recon = new ReconciliationUtils();
            Assert.True(recon.AddKeyMappingSimple("Invoice", "/invoice", "/invoiceId", out string message), message);
            Assert.True(recon.AddDecimalComparisonSimple("Amount", "/amount", "/paid", out message), message);
            Assert.True(recon.AddTextComparison("Status", "/status", "/status", true, true, ComparisonNullPolicy.RequireValue, out message), message);
            Assert.True(recon.ReconcileJson(QuickLeft, QuickRight, out int exceptionCount, out message), message);
            Assert.Equal(3, exceptionCount);

            Assert.True(recon.GetSummary(out int leftRows, out int rightRows, out int matchedPairs, out int exceptions, out message), message);
            Assert.Equal((3, 3, 1, 3), (leftRows, rightRows, matchedPairs, exceptions));

            var seen = new List<string>();
            while (true)
            {
                Assert.True(recon.TryReadNextException(out bool hasItem, out _, out string kind, out string keyJson, out int leftRow, out int rightRow, out _, out int differenceCount, out message), message);
                if (!hasItem) break;
                seen.Add(kind + " " + keyJson + " " + leftRow + "/" + rightRow + " d" + differenceCount);
                while (true)
                {
                    Assert.True(recon.TryReadNextDifference(out bool more, out string rule, out string code, out string leftValue, out string rightValue, out _, out message), message);
                    if (!more) break;
                    seen.Add("  " + rule + " " + code + " " + leftValue + " " + rightValue);
                }
            }
            Assert.Equal(new[]
            {
                "Different [\"INV-101\"] 1/1 d1", "  Amount DecimalMismatch \"20.00\" \"21.50\"",
                "OnlyLeft [\"INV-102\"] 2/-1 d0",
                "OnlyRight [\"INV-103\"] -1/2 d0"
            }, seen);
        }

        [Fact]
        public void TheConfigurationPageDefinition_LoadsValidates_AndRoundTripsThroughTheCanonicalForm()
        {
            const string json = "{ \"schemaVersion\": 1, \"keys\": [ { \"name\": \"Company\", \"leftPointer\": \"/company\", \"rightPointer\": \"/entity\", \"trim\": true, \"ignoreCase\": true }, { \"name\": \"Invoice\", \"leftPointer\": \"/invoiceNumber\", \"rightPointer\": \"/invoiceId\" } ], \"comparisons\": [ { \"name\": \"Amount\", \"kind\": \"Decimal\", \"leftPointer\": \"/amount\", \"rightPointer\": \"/paidAmount\", \"absoluteTolerance\": \"0.01\" }, { \"name\": \"Status\", \"kind\": \"Text\", \"leftPointer\": \"/status\", \"rightPointer\": \"/status\", \"trim\": true, \"ignoreCase\": true } ], \"limits\": { \"maximumRowsPerSide\": 50000, \"maximumInputCharactersPerSide\": 8000000, \"maximumResults\": 100000, \"maximumDifferenceDetails\": 100000 } }";
            using var c = new ReconciliationUtils();
            Assert.True(c.ValidateDefinitionJson(json, out int errors, out _, out string m), m);
            Assert.Equal(0, errors);
            Assert.True(c.LoadDefinitionJson(json, out m), m);
            Assert.True(c.GetDefinitionJson(out string canonical, out m), m);
            using var again = new ReconciliationUtils();
            Assert.True(again.LoadDefinitionJson(canonical, out m), m);
            Assert.True(again.GetDefinitionJson(out string second, out m), m);
            Assert.Equal(canonical, second);
        }

        [Fact]
        public void ADefinitionWithKeysAndNoComparisons_ReportsPresenceOnly()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.AddKeyMappingSimple("Id", "/id", "/id", out string m), m);
            Assert.True(c.ReconcileJson("[{\"id\":\"1\",\"x\":1},{\"id\":\"2\"}]", "[{\"id\":\"1\",\"x\":2}]", out int exceptions, out m), m);
            Assert.Equal(1, exceptions);
            Assert.True(c.GetSummary(out _, out _, out int matched, out _, out _));
            Assert.Equal(1, matched);
        }

        // ---- the helper printed on the QueueHandoff page, verbatim
        private static string ExceptionPayload(string resultId, string kind, string keyJson, int leftRow, int rightRow, string reason)
        {
            using var stream = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("resultId", resultId);
                w.WriteString("kind", kind);
                if (keyJson == null) w.WriteNull("key");
                else { w.WritePropertyName("key"); w.WriteRawValue(keyJson); }
                w.WriteNumber("leftRow", leftRow);
                w.WriteNumber("rightRow", rightRow);
                w.WriteString("reason", reason);
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        [Fact]
        public void QueueHandoff_BuildsAValidPayloadForEveryKindOfException()
        {
            using var recon = new ReconciliationUtils();
            Assert.True(recon.AddKeyMappingSimple("Invoice", "/invoice", "/invoiceId", out string message), message);
            Assert.True(recon.AddDecimalComparisonSimple("Amount", "/amount", "/paid", out message), message);
            const string left = "[{\"invoice\":\"A\",\"amount\":\"1\"},{\"invoice\":\"B\",\"amount\":\"1\"},{\"invoice\":\"B\",\"amount\":\"2\"},{\"invoice\":\"C\",\"amount\":\"1\"},{\"invoice\":\"D\",\"amount\":\"x\"},7]";
            const string right = "[{\"invoiceId\":\"A\",\"paid\":\"2\"},{\"invoiceId\":\"B\",\"paid\":\"1\"},{\"invoiceId\":\"E\",\"paid\":\"1\"},{\"invoiceId\":\"D\",\"paid\":\"1\"}]";
            Assert.True(recon.ReconcileJson(left, right, out int count, out message), message);

            var payloads = new List<JsonElement>();
            while (true)
            {
                Assert.True(recon.TryReadNextException(out bool has, out string id, out string kind, out string key, out int l, out int r, out string reason, out _, out message), message);
                if (!has) break;
                payloads.Add(JsonDocument.Parse(ExceptionPayload(id, kind, key, l, r, reason)).RootElement.Clone());
            }
            Assert.Equal(count, payloads.Count);
            Assert.Equal(new[] { "Different", "DuplicateKey", "OnlyLeft", "InvalidComparison", "InvalidRecord", "OnlyRight" }, payloads.Select(p => p.GetProperty("kind").GetString()));
            Assert.Equal(JsonValueKind.Null, payloads.Last(p => p.GetProperty("kind").GetString() == "InvalidRecord").GetProperty("key").ValueKind);
        }

        [Fact]
        public void TheLimitsPage_TheDocumentedDefaultsAndMaximums_AreTheRealOnes()
        {
            using var c = new ReconciliationUtils();
            Assert.True(c.ConfigureLimits(250000, 32000000, 500000, 500000, out string m), m);          // maximums accepted
            Assert.False(c.ConfigureLimits(250001, 32000000, 500000, 500000, out _));
            Assert.False(c.ConfigureLimits(250000, 32000001, 500000, 500000, out _));
            Assert.False(c.ConfigureLimits(250000, 32000000, 500001, 500000, out _));
            Assert.False(c.ConfigureLimits(250000, 32000000, 500000, 500001, out _));
            Assert.False(c.ConfigureLimits(0, 1, 1, 1, out _));
            Assert.True(c.ClearDefinition(out m), m);
            Assert.True(c.GetDefinitionJson(out string json, out m), m);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement limits = doc.RootElement.GetProperty("limits");
            Assert.Equal(new[] { 50000, 8000000, 100000, 100000 }, new[] { "maximumRowsPerSide", "maximumInputCharactersPerSide", "maximumResults", "maximumDifferenceDetails" }.Select(n => limits.GetProperty(n).GetInt32()));
        }
    }
}
