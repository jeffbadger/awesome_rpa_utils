using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TextExtractAutomation.Tests
{
    public sealed class TemplateJsonTests
    {
        // The canonical form escapes < > + as \u003C \u003E \u002B (safe to embed in HTML or script), so the pattern appears that way here.
        private const string Full =
            "{\"schemaVersion\":1,\"fields\":["
            + "{\"name\":\"InvoiceNumber\",\"kind\":\"Label\",\"labels\":[\"Invoice No\",\"Invoice Number\"],\"position\":\"SameLine\",\"type\":\"Code\",\"required\":true,\"occurrence\":\"RequireUnique\"},"
            + "{\"name\":\"Total\",\"kind\":\"Label\",\"labels\":[\"Total\"],\"position\":\"NextLine\",\"type\":\"Amount\",\"decimalStyle\":\"CommaDecimal\",\"required\":false,\"occurrence\":\"Last\"},"
            + "{\"name\":\"Ref\",\"kind\":\"Pattern\",\"pattern\":\"Ref:\\\\s*(?\\u003Cvalue\\u003E\\\\S\\u002B)\",\"type\":\"Code\",\"required\":true,\"occurrence\":\"First\"}"
            + "],\"limits\":{\"maximumTextCharacters\":2000000}}";

        private static List<(string path, string code)> Findings(string json)
        {
            using var c = new TextExtractUtils();
            Assert.True(c.ValidateTemplateJson(json, out int count, out string report, out string m), m);
            using JsonDocument doc = JsonDocument.Parse(report);
            var list = doc.RootElement.GetProperty("errors").EnumerateArray().Select(e => (e.GetProperty("path").GetString(), e.GetProperty("code").GetString())).ToList();
            Assert.Equal(count, doc.RootElement.GetProperty("errorCount").GetInt32());
            Assert.Equal(count == 0, doc.RootElement.GetProperty("valid").GetBoolean());
            return list;
        }

        private static string OneField(string field) => "{\"schemaVersion\":1,\"fields\":[" + field + "]}";

        [Fact]
        public void ACanonicalTemplate_LoadsAndRoundTripsExactly()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.LoadTemplateJson(Full, out string m), m);
            Assert.True(c.GetTemplateJson(out string canonical, out m), m);
            Assert.Equal(Full, canonical);
            Assert.Empty(Findings(Full));
        }

        [Fact]
        public void BuiltAndLoaded_TemplatesProduceIdenticalText()
        {
            using var built = new TextExtractUtils();
            Assert.True(built.AddLabelFieldSimple("InvoiceNumber", "Invoice No|Invoice Number", FieldType.Code, out string m), m);
            Assert.True(built.AddLabelField("Total", "Total", ValuePosition.NextLine, FieldType.Amount, DecimalStyle.CommaDecimal, null, false, Occurrence.Last, out m), m);
            Assert.True(built.AddPatternField("Ref", "Ref:\\s*(?<value>\\S+)", FieldType.Code, DecimalStyle.DotDecimal, null, out m), m);
            Assert.True(built.ConfigureLimits(2000000, out m), m);
            Assert.True(built.GetTemplateJson(out string json, out m), m);
            Assert.Equal(Full.Replace("\"occurrence\":\"First\"", "\"occurrence\":\"RequireUnique\""), json);   // the builder's pattern fields are unique by default
        }

        [Fact]
        public void OmittedOptions_TakeTheirDefaults()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.LoadTemplateJson(OneField("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"Amount\"],\"type\":\"Amount\"}"), out string m), m);
            Assert.True(c.GetTemplateJson(out string json, out m), m);
            Assert.Equal("{\"schemaVersion\":1,\"fields\":[{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"Amount\"],\"position\":\"SameLine\",\"type\":\"Amount\",\"decimalStyle\":\"DotDecimal\",\"required\":true,\"occurrence\":\"RequireUnique\"}],\"limits\":{\"maximumTextCharacters\":1000000}}", json);
            Assert.True(c.LoadTemplateJson("{\"schemaVersion\":1}", out m), m);                 // an empty template is legal
        }

        [Theory]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"type\":\"Text\"}", "fields[0].labels", "MissingProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":\"Total\",\"type\":\"Text\"}", "fields[0].labels", "InvalidType")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[5],\"type\":\"Text\"}", "fields[0].labels[0]", "InvalidType")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[],\"type\":\"Text\"}", "fields[0].labels", "InvalidLabel")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"a|b\"],\"type\":\"Text\"}", "fields[0].labels", "InvalidLabel")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"]}", "fields[0].type", "MissingProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Money\"}", "fields[0].type", "UnknownEnumValue")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":2}", "fields[0].type", "UnknownEnumValue")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"text\"}", "fields[0].type", "UnknownEnumValue")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\",\"position\":\"Above\"}", "fields[0].position", "UnknownEnumValue")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\",\"occurrence\":0}", "fields[0].occurrence", "UnknownEnumValue")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\",\"required\":\"yes\"}", "fields[0].required", "InvalidType")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Date\",\"dateFormats\":[\"yyyy\"]}", "fields[0].dateFormats", "InvalidFormat")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Date\",\"dateFormats\":\"yyyy-MM-dd\"}", "fields[0].dateFormats", "InvalidType")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Date\",\"dateFormats\":[1]}", "fields[0].dateFormats[0]", "InvalidType")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Amount\",\"dateFormats\":[\"yyyy-MM-dd\"]}", "fields[0].dateFormats", "UnknownProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\",\"decimalStyle\":\"DotDecimal\"}", "fields[0].decimalStyle", "UnknownProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Amount\",\"decimalStyle\":\"Comma\"}", "fields[0].decimalStyle", "UnknownEnumValue")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Amount\",\"decimalStyle\":1}", "fields[0].decimalStyle", "UnknownEnumValue")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\",\"format\":\"\"}", "fields[0].format", "UnknownProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\",\"pattern\":\"(?<value>x)\"}", "fields[0].pattern", "UnknownProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Pattern\",\"pattern\":\"(?<value>x)\",\"type\":\"Text\",\"labels\":[\"L\"]}", "fields[0].labels", "UnknownProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Pattern\",\"pattern\":\"(?<value>x)\",\"type\":\"Text\",\"position\":\"SameLine\"}", "fields[0].position", "UnknownProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Pattern\",\"type\":\"Text\"}", "fields[0].pattern", "MissingProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Pattern\",\"pattern\":\"(x)\",\"type\":\"Text\"}", "fields[0].pattern", "InvalidPattern")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Table\",\"type\":\"Text\"}", "fields[0].kind", "UnknownKind")]
        [InlineData("{\"name\":\"A\",\"labels\":[\"L\"],\"type\":\"Text\"}", "fields[0].kind", "MissingProperty")]
        [InlineData("{\"name\":\"A\",\"kind\":1,\"type\":\"Text\"}", "fields[0].kind", "InvalidType")]
        [InlineData("{\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\"}", "fields[0].name", "MissingProperty")]
        [InlineData("{\"name\":\" \",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\"}", "fields[0].name", "InvalidName")]
        [InlineData("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\",\"colour\":\"red\"}", "fields[0].colour", "UnknownProperty")]
        [InlineData("5", "fields[0]", "InvalidType")]
        public void AFieldWithAProblem_IsReportedAtItsPath(string field, string path, string code)
        {
            List<(string path, string code)> findings = Findings(OneField(field));
            Assert.Contains((path, code), findings);
            using var c = new TextExtractUtils();
            Assert.False(c.LoadTemplateJson(OneField(field), out string m));
            Assert.Contains("LoadTemplateJson failed", m);
        }

        [Theory]
        [InlineData("{\"fields\":[]}", "schemaVersion", "MissingProperty")]
        [InlineData("{\"schemaVersion\":2}", "schemaVersion", "UnsupportedVersion")]
        [InlineData("{\"schemaVersion\":\"1\"}", "schemaVersion", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"fields\":{}}", "fields", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"keys\":[]}", "keys", "UnknownProperty")]
        [InlineData("{\"schemaVersion\":1,\"limits\":[]}", "limits", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"limits\":{\"maximumTextCharacters\":0}}", "limits.maximumTextCharacters", "InvalidLimit")]
        [InlineData("{\"schemaVersion\":1,\"limits\":{\"maximumTextCharacters\":10000001}}", "limits.maximumTextCharacters", "InvalidLimit")]
        [InlineData("{\"schemaVersion\":1,\"limits\":{\"maximumTextCharacters\":1.5}}", "limits.maximumTextCharacters", "InvalidType")]
        [InlineData("{\"schemaVersion\":1,\"limits\":{\"maximumRows\":5}}", "limits.maximumRows", "UnknownProperty")]
        [InlineData("[]", "$", "NotAnObject")]
        [InlineData("{", "$", "MalformedJson")]
        [InlineData("{\"schemaVersion\":1,}", "$", "MalformedJson")]
        [InlineData("{\"schemaVersion\":1 /* no */}", "$", "MalformedJson")]
        [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}", "$.schemaVersion", "DuplicateProperty")]
        [InlineData("{\"schemaVersion\":1,\"fields\":[{\"name\":\"A\",\"name\":\"B\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\"}]}", "fields[0].name", "DuplicateProperty")]
        [InlineData("{\"schemaVersion\":1,\"fields\":[{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\"},{\"name\":\"a\",\"kind\":\"Label\",\"labels\":[\"M\"],\"type\":\"Text\"}]}", "fields[1].name", "DuplicateName")]
        [InlineData("{\"schemaVersion\":1,\"fields\":[{\"name\":\"\\uD800\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\"}]}", "fields[0].name", "InvalidText")]
        public void DocumentProblems_AreReportedAtTheirPath(string json, string path, string code)
        {
            Assert.Contains((path, code), Findings(json));
        }

        [Fact]
        public void DeepNesting_IsItsOwnFinding()
        {
            string deep = "{\"schemaVersion\":1,\"x\":" + new string('[', 70) + new string(']', 70) + "}";
            Assert.Equal(new[] { ("$", "DepthLimit") }, Findings(deep));
        }

        [Fact]
        public void EveryProblemIsReported_ButTheReportIsCappedAt100()
        {
            string fields = string.Join(",", Enumerable.Range(0, 150).Select(i => "{\"name\":\"F" + i + "\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Nope\"}"));
            using var c = new TextExtractUtils();
            Assert.True(c.ValidateTemplateJson("{\"schemaVersion\":1,\"fields\":[" + fields + "]}", out int count, out string report, out string m), m);
            Assert.Equal(150, count);
            using JsonDocument doc = JsonDocument.Parse(report);
            Assert.Equal(100, doc.RootElement.GetProperty("errors").GetArrayLength());
            Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
            Assert.False(c.LoadTemplateJson("{\"schemaVersion\":1,\"fields\":[" + fields + "]}", out m));
            Assert.Contains("149 more problem(s)", m);
        }

        [Fact]
        public void MoreThan200Fields_IsReportedOnce()
        {
            string fields = string.Join(",", Enumerable.Range(0, 205).Select(i => "{\"name\":\"F" + i + "\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Text\"}"));
            Assert.Equal(new[] { ("fields", "TooManyFields") }, Findings("{\"schemaVersion\":1,\"fields\":[" + fields + "]}"));
        }

        [Fact]
        public void ARejectedLoad_KeepsThePreviousTemplate_AndValidateNeverChangesIt()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.LoadTemplateJson(Full, out string m), m);
            Assert.False(c.LoadTemplateJson(OneField("{\"name\":\"A\",\"kind\":\"Label\",\"labels\":[\"L\"],\"type\":\"Nope\"}"), out m));
            Assert.True(c.ValidateTemplateJson("{\"schemaVersion\":1}", out int count, out _, out m), m);
            Assert.Equal(0, count);
            Assert.True(c.GetTemplateJson(out string json, out m), m);
            Assert.Equal(Full, json);
        }

        [Fact]
        public void InputOverTheSizeLimit_IsRefusedBeforeParsing_AndNullIsRefused()
        {
            using var c = new TextExtractUtils();
            string big = "{\"schemaVersion\":1,\"x\":\"" + new string('a', 256000) + "\"}";
            Assert.False(c.LoadTemplateJson(big, out string m)); Assert.Contains("longer than 256000", m);
            Assert.False(c.ValidateTemplateJson(big, out int count, out string report, out m)); Assert.Null(report); Assert.Equal(0, count);
            Assert.False(c.LoadTemplateJson(null, out m)); Assert.Contains("templateJson is required", m);
        }

        [Fact]
        public void ACompactTemplateWhoseCanonicalFormIsTooLarge_IsRejected()
        {
            // Written compactly (omitting every default option) the input fits the 256,000-character input check; its canonical form, which spells
            // the defaults out, does not, so it could never be saved and loaded again and is refused.
            string Build(int labelLength) =>
                "{\"schemaVersion\":1,\"fields\":[" + string.Join(",", Enumerable.Range(0, 110).Select(i =>
                    "{\"name\":\"F" + i + "\",\"kind\":\"Label\",\"labels\":[" + string.Join(",", Enumerable.Range(0, 20).Select(j => "\"" + new string('x', labelLength) + j.ToString("D2") + "\"")) + "],\"type\":\"Text\"}")) + "]}";
            int length = 126;
            while (Build(length).Length > 256000) length--;
            string json = Build(length);
            Assert.True(json.Length > 256000 - 2300, "the fixture should sit just under the input limit");
            Assert.Contains(("template", "TemplateTooLarge"), Findings(json));
        }

        [Fact]
        public void NonAsciiIsKeptAsTyped_AndHtmlSensitiveAsciiStaysEscaped()
        {
            using var c = new TextExtractUtils();
            Assert.True(c.AddLabelFieldSimple("Montant <TTC> & 'total'", "Montant dû|合計", FieldType.Amount, out string m), m);
            Assert.True(c.GetTemplateJson(out string json, out m), m);
            Assert.Contains("Montant dû", json);
            Assert.Contains("合計", json);
            foreach (char raw in "<>&'") Assert.DoesNotContain(raw.ToString(), json);
            using var again = new TextExtractUtils();
            Assert.True(again.LoadTemplateJson(json, out m), m);
            Assert.True(again.GetTemplateJson(out string second, out m), m);
            Assert.Equal(json, second);
        }
    }
}
