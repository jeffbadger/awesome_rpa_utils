using System.IO;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class ComponentRendererTests
    {
        private static readonly string Petstore = Path.Combine("TestData", "petstore-minimal.json");
        private static readonly string AuthApi = Path.Combine("TestData", "auth-minimal.json");

        [Fact]
        public void Render_EmitsExpectedNamespaceClassAndFile()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("namespace PetStoreRestAutomation", output.Source);
            Assert.Contains("public class PetStoreRestUtils", output.Source);
            // Default (no designerComponent flag) must stay plain Script-component output:
            Assert.DoesNotContain(": System.ComponentModel.Component", output.Source);
            Assert.Equal("PetStoreRestUtils", output.FileNameBase);
        }

        [Fact]
        public void Render_DesignerComponent_InheritsSystemComponent()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store", designerComponent: true);
            Assert.Contains("public class PetStoreRestUtils : System.ComponentModel.Component", output.Source);
            // instance-level client + standard Dispose pattern (Component teardown releases it):
            Assert.Contains("private readonly HttpClient _httpClient = new HttpClient();", output.Source);
            Assert.Contains("protected override void Dispose(bool disposing)", output.Source);
            Assert.Contains("_httpClient.Dispose();", output.Source);
            // the static client must be fully replaced in designer mode:
            Assert.DoesNotContain("static readonly HttpClient", output.Source);
        }

        [Fact]
        public void Render_Default_DoesNotInheritSystemComponent()
        {
            // Explicit default-off guard: Render(doc, apiName) stays plain Script-component output.
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("public class PetStoreRestUtils", output.Source);
            Assert.DoesNotContain(": System.ComponentModel.Component", output.Source);
            Assert.DoesNotContain("Dispose(bool disposing)", output.Source);
            // default keeps the shared static client (byte-identical to pre-flag output):
            Assert.DoesNotContain("_httpClient", output.Source);
            Assert.Contains("private static readonly HttpClient _client = new HttpClient();", output.Source);
        }

        [Fact]
        public void Render_EmitsOneMethodPerOperation_WithDesignerShape()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("public bool GetPetById(string petId, string status, string apiKey, out string responseJson, out int statusCode, out string message)", output.Source);
            Assert.Contains("public bool PostPetPetId(string petId, string bodyJson, out string responseJson, out int statusCode, out string message)", output.Source);
            // "DeletePet" not "DeletePetPetId": the fixture's delete op has operationId "deletePet",
            // and the Task 3 mapper contract (operationId first) governs the rendered name —
            // the plan's DeletePetPetId expectation was an authoring bug (corrected per review).
            Assert.Contains("public bool DeletePet(string petId, string apiKey2, out string responseJson, out int statusCode, out string message)", output.Source);
            Assert.Contains("public bool FindPetsByStatus(string xRequestSource, out string responseJson, out int statusCode, out string message)", output.Source);
        }

        [Fact]
        public void Render_ListsSkippedOperations_InHeaderComment()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("POST /pet/{petId}/uploadImage", output.Source);
            Assert.Contains("Skipped", output.Source);
        }

        [Fact]
        public void Render_EmitsAlwaysPresentHelpers_AndNeverThrowsContract()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("public bool SetBaseUrl(string url, out string message)", output.Source);
            Assert.Contains("public bool SetBearerAuthentication(string token, out string message)", output.Source);
            Assert.Contains("public bool SetCustomAuthentication(string headerValue, out string message)", output.Source);
            // Petstore declares an apiKey-in-header scheme ("api_key") → its scheme-specific helper is emitted:
            Assert.Contains("public bool SetApiKeyAuthentication(string name, string value, out string message)", output.Source);
            // ...but its oauth2 scheme is implicit-flow → "unsupported" → no client-credentials helper:
            Assert.DoesNotContain("SetOAuth2ClientCredentials", output.Source);
            // scheme-conditional marker lines must never survive rendering (CRLF-safe replacement):
            Assert.DoesNotContain("//[[", output.Source);
        }

        [Fact]
        public void Render_EmitsQueryApiKeyAndClientCredentialsScheme_Machinery()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(AuthApi), "auth-api");
            Assert.Contains("public bool SetApiKeyQueryAuthentication(string name, string value, out string message)", output.Source);
            Assert.Contains("public bool SetOAuth2ClientCredentials(string clientId, string clientSecret, string tokenUrl, out string message)", output.Source);
            Assert.Contains("private bool TryRefreshOAuth2Token(out string message)", output.Source);
            Assert.Contains(
                "if (ok && statusCode == 401 && _oauthTokenUrl != \"\" && TryRefreshOAuth2Token(out _))",
                output.Source);
            // ClearAuthentication clears the cached token (marker-replaced), after the api-key-query lines:
            var normalized = output.Source.Replace("\r\n", "\n");
            Assert.Contains(
                "_apiKeyQueryName = \"\"; _apiKeyQueryValue = \"\";\n            _oauthAccessToken = \"\";",
                normalized);
            Assert.DoesNotContain("//[[", output.Source);
        }

        [Fact]
        public void Render_MultiLineSummary_IsFlatInDocCommentAndDescription()
        {
            // auth-minimal.json's getCat summary spans multiple lines (with \n and \r\n):
            // both the /// doc comment line and the Description attribute must be one flat line.
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(AuthApi), "auth-api");
            var normalized = output.Source.Replace("\r\n", "\n");
            Assert.Contains(
                "/// <summary>Gets a cat. Includes details spread across lines." +
                " Returns true if the HTTP call completed; check statusCode for 4xx/5xx. Never throws.</summary>",
                normalized);
            Assert.Contains(
                "[System.ComponentModel.Description(\"Gets a cat. Includes details spread across lines. (GET /cats/{catId})\")]",
                output.Source);
            // the raw multi-line summary text must never reach the emitted docs:
            Assert.DoesNotContain("Gets a cat.\n", normalized);
        }

        [Fact]
        public void Render_EmitsApiMetadataOnClass_InBothModes()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            var normalized = output.Source.Replace("\r\n", "\n");
            // summary carries title + version and the description flattened to one line:
            var summary =
                "    /// <summary>\n" +
                "    /// Petstore (version 1.0.0).\n" +
                "    /// This is a sample petstore server spec with doubled spaces and tabs.\n";
            Assert.Contains(summary, normalized);
            // remarks carry TOS / contact (all three parts, XML-escaped <email>) / license:
            var remarks =
                "    /// <remarks>\n" +
                "    /// <para>Terms of service: https://petstore.example.com/terms</para>\n" +
                "    /// <para>Contact: Support &lt;support@petstore.example.com&gt; (https://petstore.example.com/support)</para>\n" +
                "    /// <para>License: MIT (https://petstore.example.com/license)</para>\n" +
                "    /// </remarks>\n";
            Assert.Contains(remarks, normalized);
            // class-level designer description: title + version + flattened description:
            var attribute = "[System.ComponentModel.Description(\"Petstore (version 1.0.0) - " +
                            "This is a sample petstore server spec with doubled spaces and tabs.\")]";
            Assert.Contains(attribute, output.Source);

            // designer mode emits identical metadata (class docs/attribute are mode-independent):
            var designer = ComponentRenderer.Render(
                SwaggerParser.ParseFile(Petstore), "pet-store", designerComponent: true)
                .Source.Replace("\r\n", "\n");
            Assert.Contains("    /// Petstore (version 1.0.0).\n", designer);
            Assert.Contains("    /// <para>License: MIT (https://petstore.example.com/license)</para>\n", designer);
            Assert.Contains(attribute, designer);
        }

        [Fact]
        public void Render_MissingMetadataEntities_OmitsLinesAndRemarksBlock()
        {
            // auth-minimal.json declares no TOS/contact/license: those lines must be omitted
            // entirely (no empty <remarks> block, no placeholders), and the class attribute
            // falls back to title + version.
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(AuthApi), "auth-api");
            var normalized = output.Source.Replace("\r\n", "\n");
            Assert.Contains("    /// AuthApi (version 2.0.0).\n", normalized);
            Assert.DoesNotContain("<remarks>", normalized);
            Assert.DoesNotContain("Terms of service:", normalized);
            Assert.DoesNotContain("<para>Contact:", normalized);
            Assert.DoesNotContain("<para>License:", normalized);
            Assert.Contains("[System.ComponentModel.Description(\"AuthApi (version 2.0.0)\")]", output.Source);
        }

        [Fact]
        public void Render_CsprojFallbackIsMultiTargetedStandalone()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("<TargetFrameworks>net8.0-windows;net10.0-windows</TargetFrameworks>", output.Project);
            Assert.Contains("<AssemblyName>PetStoreRestAutomation</AssemblyName>", output.Project);
            Assert.DoesNotContain("PackageReference", output.Project); // zero packages — pure BCL
        }
    }
}