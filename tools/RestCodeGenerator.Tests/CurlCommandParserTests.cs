using System.IO;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class CurlCommandParserTests
    {
        [Fact]
        public void Parse_NoFlags_IsGet_NoParams()
        {
            var doc = CurlCommandParser.Parse("curl https://api.example.com/users");
            var op = Assert.Single(doc.Operations);
            Assert.Equal("GET", op.HttpMethod);
            Assert.Equal("https://api.example.com", doc.DefaultBaseUrl);
            Assert.Empty(op.QueryParams);
            Assert.Empty(op.HeaderParams);
            Assert.False(op.HasBody);
        }

        [Fact]
        public void Parse_PostWithJsonBody_FlattensIntoTypedParams()
        {
            var doc = CurlCommandParser.Parse(
                "curl -X POST -H \"Content-Type: application/json\" -d '{\"name\":\"x\"}' https://api.example.com/items");
            var op = Assert.Single(doc.Operations);
            Assert.Equal("POST", op.HttpMethod);
            Assert.True(op.HasBody);
            Assert.NotNull(op.BodySchema);
            Assert.Contains(op.BodySchema!.Properties, p => p.Name == "name");
        }

        [Fact]
        public void Parse_RepeatedHeaders_AuthorizationDropped_ValueNeverAppearsAnywhere()
        {
            var doc = CurlCommandParser.Parse(
                "curl -H \"X-One: a\" -H \"X-Two: b\" -H \"Authorization: Bearer secret123\" https://api.example.com/x");
            var op = Assert.Single(doc.Operations);
            Assert.Equal(2, op.HeaderParams.Count);
            Assert.DoesNotContain(op.HeaderParams, h => string.Equals(h.Name, "Authorization", System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(doc.Operations, o => o.OperationId != null && o.OperationId.Contains("secret123"));
            var serialized = System.Text.Json.JsonSerializer.Serialize(doc);
            Assert.DoesNotContain("secret123", serialized);
        }

        [Fact]
        public void Parse_UserFlag_DroppedEntirely_NoSchemeInferred()
        {
            var doc = CurlCommandParser.Parse("curl -u alice:hunter2 https://api.example.com/secure");
            var op = Assert.Single(doc.Operations);
            Assert.Empty(op.HeaderParams);
            Assert.False(op.HasBody);
            Assert.Empty(doc.SecuritySchemes);
            var serialized = System.Text.Json.JsonSerializer.Serialize(doc);
            Assert.DoesNotContain("hunter2", serialized);
        }

        [Fact]
        public void Parse_QueryString_BecomesQueryParams()
        {
            var doc = CurlCommandParser.Parse("curl 'https://api.example.com/pets?status=open&limit=5'");
            var op = Assert.Single(doc.Operations);
            Assert.Equal(2, op.QueryParams.Count);
            Assert.Contains(op.QueryParams, p => p.Name == "status");
            Assert.Contains(op.QueryParams, p => p.Name == "limit");
        }

        [Fact]
        public void Parse_NonJsonBody_FallsBackToRawBodyJsonParameter()
        {
            var doc = CurlCommandParser.Parse("curl -d 'name=Rex&age=3' https://api.example.com/pets");
            var op = Assert.Single(doc.Operations);
            Assert.True(op.HasBody);
            Assert.Null(op.BodySchema);
        }

        [Fact]
        public void Parse_NoExplicitMethod_ButDataPresent_InfersPost()
        {
            var doc = CurlCommandParser.Parse("curl -d '{}' https://api.example.com/pets");
            Assert.Equal("POST", Assert.Single(doc.Operations).HttpMethod);
        }

        [Fact]
        public void ParseFile_MultiLineContinuedCommand_MatchesInlineEquivalent()
        {
            var fromFile = CurlCommandParser.ParseFile(Path.Combine("TestData", "curl-example.txt"));
            var inline = CurlCommandParser.Parse(
                "curl --location 'https://api.example.com/pets?status=open&limit=5' " +
                "--header 'Content-Type: application/json' --header 'Authorization: Bearer secret123' " +
                "--data '{\"name\":\"Rex\",\"age\":3}'");

            var fileOp = Assert.Single(fromFile.Operations);
            var inlineOp = Assert.Single(inline.Operations);
            Assert.Equal(inlineOp.HttpMethod, fileOp.HttpMethod);
            Assert.Equal(inlineOp.Path, fileOp.Path);
            Assert.Equal(inlineOp.QueryParams.Count, fileOp.QueryParams.Count);
            Assert.Equal(inlineOp.HeaderParams.Count, fileOp.HeaderParams.Count);
            Assert.Equal(fromFile.DefaultBaseUrl, inline.DefaultBaseUrl);
            Assert.Equal("POST", fileOp.HttpMethod);   // no -X, inferred from --data
        }
    }
}
