using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class PostmanCollectionParserTests
    {
        private static ApiSpec ParseCollection() =>
            PostmanCollectionParser.ParseFile(Path.Combine("TestData", "postman-collection.json"));

        private static ApiSpec ParseJson(string json) =>
            PostmanCollectionParser.Parse(JsonDocument.Parse(json).RootElement);

        [Fact]
        public void ParseFile_ExtractsTitle()
        {
            Assert.Equal("PetStore Postman", ParseCollection().Title);
        }

        [Fact]
        public void ParseFile_FolderNesting_ProducesPrefixedUniqueNames()
        {
            var doc = ParseCollection();
            var map = MethodNameMapper.Map(doc.Operations);
            Assert.Contains(doc.Operations, o => o.OperationId == "Pets/Get Pet");
            Assert.Contains(map.Values, n => n == "PetsGetPet");
            Assert.Contains(map.Values, n => n == "PetsCreatePet");
        }

        [Fact]
        public void ParseFile_TemplatedHost_YieldsNullBaseUrl()
        {
            Assert.Null(ParseCollection().DefaultBaseUrl);
        }

        [Fact]
        public void ParseFile_PathVariable_RewrittenAndListedAsPathParam()
        {
            var doc = ParseCollection();
            var getPet = Assert.Single(doc.Operations, o => o.OperationId == "Pets/Get Pet");
            Assert.Equal("/pets/{petId}", getPet.Path);
            Assert.Equal("petId", Assert.Single(getPet.PathParams).Name);
            Assert.True(Assert.Single(getPet.PathParams).IsPath);
        }

        [Fact]
        public void ParseFile_DisabledQueryAndHeaderEntries_AreExcluded()
        {
            var doc = ParseCollection();
            var getPet = Assert.Single(doc.Operations, o => o.OperationId == "Pets/Get Pet");
            Assert.Equal(2, getPet.QueryParams.Count);
            Assert.DoesNotContain(getPet.QueryParams, p => p.Name == "old");
            Assert.Single(getPet.HeaderParams, p => p.Name == "X-Request-Source");
            Assert.DoesNotContain(getPet.HeaderParams, p => p.Name == "X-Disabled");
        }

        [Fact]
        public void ParseFile_RawJsonBody_FlattensIntoTypedSchema()
        {
            var doc = ParseCollection();
            var createPet = Assert.Single(doc.Operations, o => o.OperationId == "Pets/Create Pet");
            Assert.True(createPet.HasBody);
            Assert.NotNull(createPet.BodySchema);
            Assert.Equal("object", createPet.BodySchema!.JsonType);
            Assert.Contains(createPet.BodySchema.Properties, p => p.Name == "name" && p.Schema.JsonType == "string");
            Assert.Contains(createPet.BodySchema.Properties, p => p.Name == "age" && p.Schema.JsonType == "integer");
        }

        [Fact]
        public void ParseFile_FormDataBody_IsSkipped()
        {
            var doc = ParseCollection();
            Assert.DoesNotContain(doc.Operations, o => o.OperationId == "Pets/Upload Photo");
            Assert.Contains("POST /pets/{petId}/photo", doc.Skipped);
        }

        [Fact]
        public void ParseFile_CollectionAndRequestAuth_MapToDeduplicatedKinds()
        {
            var doc = ParseCollection();
            Assert.Contains(doc.SecuritySchemes, s => s.Kind == "bearer");
            Assert.Contains(doc.SecuritySchemes, s => s.Kind == "apiKeyHeader");
        }

        [Fact]
        public void Parse_Oauth2AuthorizationCodeGrant_MapsToUnsupported()
        {
            var doc = ParseJson("""
                {
                  "info": { "name": "Auth Code Api" },
                  "auth": {
                    "type": "oauth2",
                    "oauth2": [ { "key": "grant_type", "value": "authorization_code", "type": "string" } ]
                  },
                  "item": []
                }
                """);
            Assert.Equal("unsupported", Assert.Single(doc.SecuritySchemes).Kind);
        }
    }
}
