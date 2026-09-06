using System.IO;
using System.Linq;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class ComponentRendererTests
    {
        private static readonly string Petstore = Path.Combine("TestData", "petstore-minimal.json");
        private static readonly string AuthApi = Path.Combine("TestData", "auth-minimal.json");

        private static ApiSpec Doc(string title, string version, string? description = null) =>
            new(title, version, null,
                System.Array.Empty<ApiOperation>(),
                System.Array.Empty<ApiSecurityScheme>(),
                System.Array.Empty<string>())
            { Description = description };

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
            // BaseUrl/TimeoutSeconds are design-time properties, not never-throw Set* methods:
            Assert.Contains("public string BaseUrl", output.Source);
            Assert.Contains("public int TimeoutSeconds", output.Source);
            Assert.DoesNotContain("SetBaseUrl", output.Source);
            Assert.DoesNotContain("SetTimeoutSeconds", output.Source);
            Assert.Contains("public bool SetBearerAuthentication(string token, out string message)", output.Source);
            Assert.Contains("public bool SetCustomAuthentication(string headerValue, out string message)", output.Source);
            // Petstore declares an apiKey-in-header scheme ("api_key") → its scheme-specific properties are emitted:
            Assert.Contains("public string ApiKeyHeaderName", output.Source);
            Assert.Contains("public string ApiKeyHeaderValue", output.Source);
            // ...but its oauth2 scheme is implicit-flow → "unsupported" → no client-credentials properties:
            Assert.DoesNotContain("OAuthClientId", output.Source);
            // scheme-conditional marker lines must never survive rendering (CRLF-safe replacement):
            Assert.DoesNotContain("//[[", output.Source);
        }

        [Fact]
        public void Render_EmitsQueryApiKeyAndClientCredentialsScheme_Machinery()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(AuthApi), "auth-api");
            Assert.Contains("public string ApiKeyQueryName", output.Source);
            Assert.Contains("public string ApiKeyQueryValue", output.Source);
            Assert.Contains("public string OAuthClientId", output.Source);
            Assert.Contains("public string OAuthClientSecret", output.Source);
            Assert.Contains("public string OAuthTokenUrl", output.Source);
            // setting any OAuth2 config property invalidates the cached access token:
            Assert.Contains("set { _oauthClientId = value ?? \"\"; InvalidateOAuthToken(); }", output.Source);
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
        public void Render_EmitsOAuthScopeProperty_WhenOAuth2SchemeDeclared()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(AuthApi), "auth-api");
            Assert.Contains("public string OAuthScope", output.Source);
            Assert.Contains("set { _oauthScope = value ?? \"\"; InvalidateOAuthToken(); }", output.Source);
        }

        [Fact]
        public void Render_OAuthScope_OmittedFromTokenRequest_WhenBlank()
        {
            // The scope form field is only sent when OAuthScope is non-blank, so existing
            // (non-Entra) client-credentials consumers see byte-identical wire behavior
            // if they never set it.
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(AuthApi), "auth-api");
            Assert.Contains("if (_oauthScope != \"\") formFields.Add(new KeyValuePair<string, string>(\"scope\", _oauthScope));", output.Source);
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
        public void Render_XmlEscapesTitleAndVersion_InClassSummary()
        {
            // hand-built doc with a version (and title) containing '<' — the summary line
            // must escape it (IntelliSense-XML safety) while the Description attribute
            // literal carries it raw (Lit only escapes C# quotes/backslashes).
            var output = ComponentRenderer.Render(Doc("Api<Type>", "2<3"), "esc-api");
            var normalized = output.Source.Replace("\r\n", "\n");
            Assert.Contains("    /// Api&lt;Type&gt; (version 2&lt;3).\n", normalized);
            Assert.Contains("[System.ComponentModel.Description(\"Api<Type> (version 2<3)\")]", output.Source);
        }

        [Fact]
        public void Render_LongDescription_WrapsIntoMultipleSummaryLines_WithoutSplittingWords()
        {
            // 6 joined "lorem ipsum dolor" triples = 107 chars fit in a 110-char line;
            // the 7th triple would overflow, so 12 triples wrap into exactly 2 equal lines.
            var triple = "lorem ipsum dolor";
            var description = string.Join(" ", Enumerable.Repeat(triple, 12));
            var output = ComponentRenderer.Render(Doc("WrapApi", "1.0.0", description), "wrap-api");
            var summaryLines = output.Source.Replace("\r\n", "\n")
                .Split('\n')
                .SkipWhile(l => l != "    /// <summary>")
                .Skip(2)   // drop the <summary> marker line and the title line
                .TakeWhile(l => l != "    /// One method per operation in the source swagger file. Response bodies are")
                .Select(l => l.Substring("    /// ".Length))
                .ToList();
            Assert.Equal(new[] { string.Join(" ", Enumerable.Repeat(triple, 6)),
                                 string.Join(" ", Enumerable.Repeat(triple, 6)) },
                summaryLines);
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