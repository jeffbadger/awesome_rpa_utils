using System.IO;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class ComponentRendererTests
    {
        private static readonly string Petstore = Path.Combine("TestData", "petstore-minimal.json");

        [Fact]
        public void Render_EmitsExpectedNamespaceClassAndFile()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("namespace PetStoreRestAutomation", output.Source);
            Assert.Contains("public class PetStoreRestUtils", output.Source);
            Assert.Equal("PetStoreRestUtils", output.FileNameBase);
        }

        [Fact]
        public void Render_EmitsOneMethodPerOperation_WithDesignerShape()
        {
            var output = ComponentRenderer.Render(SwaggerParser.ParseFile(Petstore), "pet-store");
            Assert.Contains("public bool GetPetById(string petId, string status, string apiKey, out string responseJson, out int statusCode, out string message)", output.Source);
            Assert.Contains("public bool PostPetPetId(string petId, string bodyJson, out string responseJson, out int statusCode, out string message)", output.Source);
            Assert.Contains("public bool DeletePetPetId(string petId, string apiKey2, out string responseJson, out int statusCode, out string message)", output.Source);
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