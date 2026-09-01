using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class SwaggerParserTests
    {
        private static SwaggerDoc ParsePetstore()
        {
            var path = Path.Combine("TestData", "petstore-minimal.json");
            return SwaggerParser.ParseFile(path);
        }

        [Fact]
        public void ParseFile_ExtractsInfoAndBaseUrl()
        {
            var doc = ParsePetstore();
            Assert.Equal("Petstore", doc.Title);
            Assert.Equal("1.0.0", doc.Version);
            Assert.Equal("https://petstore.example.com/v2", doc.DefaultBaseUrl);
        }

        [Fact]
        public void ParseFile_KeepsFourOperations_AndSkipsMultipartUpload()
        {
            var doc = ParsePetstore();
            Assert.Equal(4, doc.Operations.Count); // uploadImage is skipped, not modeled
            Assert.Contains("POST /pet/{petId}/uploadImage", doc.Skipped); // formData "file" is non-JSON
        }

        [Fact]
        public void ParseFile_ResolvesFileLevelRefs_AndMergesPathAndOperationParams()
        {
            var doc = ParsePetstore();
            var get = Assert.Single(doc.Operations, o => o.OperationId == "getPetById");
            Assert.Equal("GET", get.HttpMethod);
            Assert.Equal("/pet/{petId}", get.Path);
            Assert.Single(get.PathParams, p => p.Name == "petId");
            Assert.Collection(get.QueryParams,
                p => Assert.Equal("status", p.Name),      // arrived via $ref to #/parameters/StatusFilter
                p => Assert.Equal("apiKey", p.Name));     // operation-level
        }

        [Fact]
        public void ParseFile_ExtractsHeaderParameters()
        {
            var doc = ParsePetstore();
            var get = Assert.Single(doc.Operations, o => o.OperationId == "findPetsByStatus");
            Assert.Equal("X-Request-Source", Assert.Single(get.HeaderParams).Name);
        }

        [Fact]
        public void ParseFile_ExtractsSecuritySchemeKinds()
        {
            var doc = ParsePetstore();
            Assert.Equal("apiKeyHeader", Assert.Single(doc.SecuritySchemes, s => s.Name == "api_key").Kind);
            var oauth = Assert.Single(doc.SecuritySchemes, s => s.Name == "petstore_auth");
            Assert.Equal("unsupported", oauth.Kind); // implicit flow — no generated helper
        }

        [Fact]
        public void ParseFile_BodyOpsGetHasBody_NonGetOpsDontCollide()
        {
            var doc = ParsePetstore();
            var post = Assert.Single(doc.Operations, o => o.Path == "/pet/{petId}" && o.HttpMethod == "POST");
            Assert.True(post.HasBody);
            var del = Assert.Single(doc.Operations, o => o.OperationId == "deletePet");
            Assert.False(del.HasBody);
        }

        [Fact]
        public void ParseFile_MissingFile_ThrowsFileNotFoundException()
        {
            // The generator CLI is allowed to throw on bad usage — it is a dev tool,
            // not a Robot Studio component; the never-throws contract applies to the
            // generated component, not to this generator.
            Assert.Throws<FileNotFoundException>(() => SwaggerParser.ParseFile("nope.json"));
        }

        private static SwaggerDoc ParseJson(string json) =>
            SwaggerParser.Parse(JsonDocument.Parse(json).RootElement);

        [Fact]
        public void Parse_Oa3JsonRequestBodyIsModeled_NonJsonRequestBodySkipped()
        {
            var doc = ParseJson("""
                {
                  "openapi": "3.0.0",
                  "info": { "title": "T", "version": "1.0.0" },
                  "paths": {
                    "/json": {
                      "post": {
                        "requestBody": {
                          "content": { "application/json": { "schema": { "type": "object" } } }
                        }
                      }
                    },
                    "/multipart": {
                      "post": {
                        "requestBody": {
                          "content": { "multipart/form-data": { "schema": { "type": "object" } } }
                        }
                      }
                    }
                  }
                }
                """);

            var jsonOp = Assert.Single(doc.Operations, o => o.Path == "/json");
            Assert.True(jsonOp.HasBody);
            Assert.DoesNotContain(doc.Operations, o => o.Path == "/multipart");
            Assert.Contains("POST /multipart", doc.Skipped);
        }

        [Fact]
        public void Parse_TemplatedOa3Server_AndHostless20Spec_YieldNullBaseUrl()
        {
            var templated = ParseJson("""
                {
                  "openapi": "3.0.0",
                  "info": { "title": "T", "version": "1.0.0" },
                  "servers": [ { "url": "https://{host}/v1" } ],
                  "paths": {}
                }
                """);
            Assert.Null(templated.DefaultBaseUrl);

            var hostless20 = ParseJson("""
                {
                  "swagger": "2.0",
                  "info": { "title": "T", "version": "1.0.0" },
                  "paths": {}
                }
                """);
            Assert.Null(hostless20.DefaultBaseUrl);
        }
    }
}