using System.IO;
using System.Linq;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class BrunoCollectionParserTests
    {
        private static readonly string SingleFile = Path.Combine("TestData", "bruno-single.bru");
        private static readonly string CollectionDir = Path.Combine("TestData", "bruno-collection");

        [Fact]
        public void ParsePath_SingleFile_ProducesExactlyOneOperation()
        {
            var doc = BrunoCollectionParser.ParsePath(SingleFile);
            Assert.Single(doc.Operations);
        }

        [Fact]
        public void ParsePath_SingleFile_PathVariableRewritten_AndDisabledEntriesExcluded()
        {
            var doc = BrunoCollectionParser.ParsePath(SingleFile);
            var op = doc.Operations[0];
            Assert.Equal("/pets/{petId}", op.Path);
            Assert.Equal("petId", Assert.Single(op.PathParams).Name);
            Assert.Single(op.QueryParams, p => p.Name == "active");
            Assert.DoesNotContain(op.QueryParams, p => p.Name == "old");
            Assert.Single(op.HeaderParams, p => p.Name == "X-Request-Source");
            Assert.DoesNotContain(op.HeaderParams, p => p.Name == "X-Disabled");
        }

        [Fact]
        public void ParsePath_Directory_FindsRequests_SkipsFolderBru()
        {
            var doc = BrunoCollectionParser.ParsePath(CollectionDir);
            Assert.Equal(2, doc.Operations.Count);
            Assert.DoesNotContain(doc.Operations, o => o.OperationId == "Pets");
        }

        [Fact]
        public void ParsePath_Directory_OrdersOperationsDeterministically()
        {
            var names1 = BrunoCollectionParser.ParsePath(CollectionDir).Operations.Select(o => o.OperationId).ToList();
            var names2 = BrunoCollectionParser.ParsePath(CollectionDir).Operations.Select(o => o.OperationId).ToList();
            Assert.Equal(names1, names2);
            // "create-pet.bru" sorts before "get-pet.bru" (c < g) regardless of filesystem order
            Assert.Equal(new[] { "Create Pet", "Get Pet" }, names1);
        }

        [Fact]
        public void ParsePath_Directory_BodyJson_InfersSchema_SameAsPostman()
        {
            var doc = BrunoCollectionParser.ParsePath(CollectionDir);
            var createPet = Assert.Single(doc.Operations, o => o.OperationId == "Create Pet");
            Assert.True(createPet.HasBody);
            Assert.Contains(createPet.BodySchema!.Properties, p => p.Name == "name" && p.Schema.JsonType == "string");
            Assert.Contains(createPet.BodySchema.Properties, p => p.Name == "age" && p.Schema.JsonType == "integer");
        }

        [Fact]
        public void ParsePath_Directory_AuthModeInherit_ContributesNoScheme()
        {
            // get-pet.bru declares "auth: inherit" — folder-tree auth inheritance is out of
            // scope, so it must not surface as any kind of security scheme.
            var doc = BrunoCollectionParser.ParsePath(CollectionDir);
            Assert.DoesNotContain(doc.SecuritySchemes, s => s.Kind is "bearer" or "basic");
        }

        [Fact]
        public void ParsePath_Directory_ApiKeyPlacementQueryParams_MapsToApiKeyQuery()
        {
            // create-pet.bru declares "auth: apikey" with auth:apikey { placement: queryparams }
            var doc = BrunoCollectionParser.ParsePath(CollectionDir);
            Assert.Contains(doc.SecuritySchemes, s => s.Kind == "apiKeyQuery");
        }
    }
}
