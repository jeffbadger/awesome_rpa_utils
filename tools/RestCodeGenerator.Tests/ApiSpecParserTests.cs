using System.IO;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class ApiSpecParserTests
    {
        [Fact]
        public void Detect_BruExtension_DetectsBruno()
        {
            Assert.Equal(InputFormat.Bruno, ApiSpecParser.Detect(Path.Combine("TestData", "bruno-single.bru")));
        }

        [Fact]
        public void Detect_Directory_DetectsBruno()
        {
            Assert.Equal(InputFormat.Bruno, ApiSpecParser.Detect(Path.Combine("TestData", "bruno-collection")));
        }

        [Fact]
        public void Detect_SwaggerPathsKeys_DetectsOpenApi()
        {
            Assert.Equal(InputFormat.OpenApi, ApiSpecParser.Detect(Path.Combine("TestData", "petstore-minimal.json")));
        }

        [Fact]
        public void Detect_TopLevelItemArray_DetectsPostman()
        {
            Assert.Equal(InputFormat.Postman, ApiSpecParser.Detect(Path.Combine("TestData", "postman-collection.json")));
        }

        [Fact]
        public void Detect_InlineCurlString_DetectsCurl()
        {
            Assert.Equal(InputFormat.Curl, ApiSpecParser.Detect("curl https://api.example.com/users"));
        }

        [Fact]
        public void Detect_CurlTextFile_DetectsCurl()
        {
            Assert.Equal(InputFormat.Curl, ApiSpecParser.Detect(Path.Combine("TestData", "curl-example.txt")));
        }

        [Fact]
        public void Parse_ExplicitFormat_OverridesContentDetection()
        {
            // The Postman fixture's content would auto-detect as Postman; forcing --format
            // curl must still be honored (even though it will produce a degenerate result).
            var doc = ApiSpecParser.Parse("curl https://api.example.com/x", InputFormat.Curl, out var used);
            Assert.Equal(InputFormat.Curl, used);
            Assert.Single(doc.Operations);
        }

        [Fact]
        public void Detect_UnparsableNonCurlInput_ThrowsActionableMessage()
        {
            var ex = Assert.Throws<System.InvalidOperationException>(() => ApiSpecParser.Detect("not a real path or command"));
            Assert.Contains("--format", ex.Message);
        }
    }
}
