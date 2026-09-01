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
            // the fixture's description carries a "\n" and doubled spaces — parsed raw
            // (flattening is a renderer concern); TOS is stored as-is (a URL per spec):
            Assert.Equal("This is a  sample petstore\nserver spec with  doubled spaces and\ttabs.", doc.Description);
            Assert.Equal("https://petstore.example.com/terms", doc.TermsOfService);
            Assert.Equal("Support", doc.ContactName);
            Assert.Equal("support@petstore.example.com", doc.ContactEmail);
            Assert.Equal("https://petstore.example.com/support", doc.ContactUrl);
            Assert.Equal("MIT", doc.LicenseName);
            Assert.Equal("https://petstore.example.com/license", doc.LicenseUrl);
        }

        [Fact]
        public void Parse_MissingOrMalformedInfoMetadata_YieldsNullsAndNeverThrows()
        {
            // No info object at all: title/version fall back, every metadata field is null.
            var noInfo = ParseJson(""" { "swagger": "2.0", "paths": {} } """);
            Assert.Equal("Api", noInfo.Title);
            Assert.Equal("1.0.0", noInfo.Version);
            Assert.Null(noInfo.Description);
            Assert.Null(noInfo.TermsOfService);
            Assert.Null(noInfo.ContactName);
            Assert.Null(noInfo.ContactEmail);
            Assert.Null(noInfo.ContactUrl);
            Assert.Null(noInfo.LicenseName);
            Assert.Null(noInfo.LicenseUrl);

            // Present-but-scalar contact/license sub-objects (malformed shape) → nulls, no throw;
            // partial contact (only email) and license-without-url keep their real values.
            var partial = ParseJson("""
                {
                  "openapi": "3.0.0",
                  "info": {
                    "title": "T",
                    "version": "1.0.0",
                    "contact": "not-an-object",
                    "license": { "name": "MIT" }
                  },
                  "paths": {}
                }
                """);
            Assert.Null(partial.ContactName);
            Assert.Null(partial.ContactEmail);
            Assert.Null(partial.ContactUrl);
            Assert.Equal("MIT", partial.LicenseName);
            Assert.Null(partial.LicenseUrl);
            Assert.Null(partial.TermsOfService);
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
        public void Parse_WrongTypedScalars_YieldNullsAndNeverThrow()
        {
            // GetString() throws InvalidOperationException on non-string JSON values;
            // every info field must instead degrade to null (title/version to their
            // fallbacks) when its value has the wrong JSON type.
            var doc = ParseJson("""
                {
                  "swagger": "2.0",
                  "info": {
                    "title": 5,
                    "version": { "x": 1 },
                    "description": [ "nope" ],
                    "termsOfService": [],
                    "contact": { "email": 5, "name": { "full": "nope" } },
                    "license": { "url": true }
                  },
                  "paths": {}
                }
                """);
            Assert.Equal("Api", doc.Title);
            Assert.Equal("1.0.0", doc.Version);
            Assert.Null(doc.Description);
            Assert.Null(doc.TermsOfService);
            Assert.Null(doc.ContactName);
            Assert.Null(doc.ContactEmail);
            Assert.Null(doc.ContactUrl);
            Assert.Null(doc.LicenseName);   // absent
            Assert.Null(doc.LicenseUrl);    // present but boolean
        }

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

        // ---- the official swagger-api OpenAPI 3.0 petstore spec, end-to-end ----

        private static SwaggerDoc ParseOfficialPetstore() =>
            SwaggerParser.ParseFile(Path.Combine("TestData", "petstore-openapi.json"));

        [Fact]
        public void OfficialPetstore_ExtractsInfoMetadata()
        {
            var doc = ParseOfficialPetstore();
            Assert.Equal("Swagger Petstore - OpenAPI 3.0", doc.Title);
            Assert.Equal("1.0.27", doc.Version);
            Assert.Equal("https://swagger.io/terms/", doc.TermsOfService);
            // contact is email-only in the real spec — the other parts stay null
            Assert.Null(doc.ContactName);
            Assert.Equal("apiteam@swagger.io", doc.ContactEmail);
            Assert.Null(doc.ContactUrl);
            Assert.Equal("Apache 2.0", doc.LicenseName);
            Assert.Equal("https://www.apache.org/licenses/LICENSE-2.0.html", doc.LicenseUrl);
        }

        [Fact]
        public void OfficialPetstore_RelativeServerUrl_YieldsNullBaseUrl()
        {
            // servers[0].url is "/api/v3" — no origin, so it cannot be a default base URL
            // (a relative DefaultBaseUrl would produce un-requestable URIs downstream).
            Assert.Null(ParseOfficialPetstore().DefaultBaseUrl);
        }

        [Fact]
        public void Parse_TemplatedAbsoluteServerUrl_YieldsNullBaseUrl_AbsoluteWithPathIsKept()
        {
            // Uri.TryCreate accepts braces in the path, so a templated absolute URL would
            // otherwise leak through as a bogus DefaultBaseUrl; the '{' guard keeps it null.
            // (The templated-host case was already covered by Parse_TemplatedOa3Server_….)
            var templated = ParseJson("""
                {
                  "openapi": "3.0.0",
                  "info": { "title": "T", "version": "1.0.0" },
                  "servers": [ { "url": "https://api.example.com/v{version}" } ],
                  "paths": {}
                }
                """);
            Assert.Null(templated.DefaultBaseUrl);

            // an absolute http(s) URL — even with a path — is the one usable shape
            var absolute = ParseJson("""
                {
                  "openapi": "3.0.0",
                  "info": { "title": "T", "version": "1.0.0" },
                  "servers": [ { "url": "https://petstore.example/api/v3" } ],
                  "paths": {}
                }
                """);
            Assert.Equal("https://petstore.example/api/v3", absolute.DefaultBaseUrl);
        }

        [Fact]
        public void OfficialPetstore_SecuritySchemes_ApiKeyHeader_ImplicitOauth2Unsupported()
        {
            var doc = ParseOfficialPetstore();
            Assert.Equal(2, doc.SecuritySchemes.Count);
            Assert.Equal("apiKeyHeader", Assert.Single(doc.SecuritySchemes, s => s.Name == "api_key").Kind);
            // implicit oauth2 flow — parsed but not given a generated helper
            Assert.Equal("unsupported", Assert.Single(doc.SecuritySchemes, s => s.Name == "petstore_auth").Kind);
        }

        [Fact]
        public void OfficialPetstore_Models18Ops_AddPetBody_PutPetNotSkipped()
        {
            var doc = ParseOfficialPetstore();   // 19 operations, all but one JSON-capable
            Assert.Equal(18, doc.Operations.Count);
            // addPet offers json+xml+form-urlencoded; JSON is present, so it's modeled
            // (json preferred; the alternative content types don't demote it to a skip)
            var addPet = Assert.Single(doc.Operations, o => o.OperationId == "addPet");
            Assert.False(doc.Skipped.Contains("POST /pet"));
            Assert.True(addPet.HasBody);
            // updatePet (PUT /pet) also offers json+xml+form — likewise modeled with a body
            var updatePet = Assert.Single(doc.Operations, o => o.OperationId == "updatePet");
            Assert.True(updatePet.HasBody);
        }

        [Fact]
        public void OfficialPetstore_OctetStreamUploadImage_IsSkipped()
        {
            var doc = ParseOfficialPetstore();
            var skipped = Assert.Single(doc.SkippedOperations);
            Assert.Equal("POST /pet/{petId}/uploadImage", skipped);
            Assert.DoesNotContain(doc.Operations, o => o.OperationId == "uploadFile");
        }

        [Fact]
        public void OfficialPetstore_BucketsPathAndQueryParams()
        {
            var doc = ParseOfficialPetstore();
            var getPetById = Assert.Single(doc.Operations, o => o.OperationId == "getPetById");
            Assert.Equal("petId", Assert.Single(getPetById.PathParams).Name);
            Assert.True(Assert.Single(getPetById.PathParams).IsPath);
            var findByStatus = Assert.Single(doc.Operations, o => o.OperationId == "findPetsByStatus");
            Assert.Equal("status", Assert.Single(findByStatus.QueryParams).Name);
        }

        [Fact]
        public void OfficialPetstore_MethodNames_FromOperationIds()
        {
            var doc = ParseOfficialPetstore();
            var map = MethodNameMapper.Map(doc.Operations);
            Assert.Equal("AddPet", map[Assert.Single(doc.Operations, o => o.OperationId == "addPet")]);
            Assert.Equal("UpdatePet", map[Assert.Single(doc.Operations, o => o.OperationId == "updatePet")]);
            Assert.Equal("GetPetById", map[Assert.Single(doc.Operations, o => o.OperationId == "getPetById")]);
        }
    }
}