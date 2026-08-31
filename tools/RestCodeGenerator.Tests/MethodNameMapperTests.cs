using System.Collections.Generic;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class MethodNameMapperTests
    {
        private static SwaggerOperation Op(string method, string path, string? id = null) =>
            new(method, path, id, null,
                new List<SwaggerParameter>(), new List<SwaggerParameter>(),
                new List<SwaggerParameter>(), false);

        [Theory]
        [InlineData("getPetById", "/pet/{petId}", "GET", "GetPetById")]
        [InlineData(null, "/pet/findByStatus", "GET", "GetPetFindByStatus")]
        [InlineData(null, "/pet/{petId}", "POST", "PostPetPetId")]
        [InlineData(null, "/pet/{petId}", "DELETE", "DeletePetPetId")]
        [InlineData("user-login_GET!", "/user/login", "GET", "UserLoginGet")]
        public void Map_ReturnsExpectedNames(string? id, string path, string method, string expected)
        {
            var map = MethodNameMapper.Map(new[] { Op(method, path, id) });
            Assert.Equal(expected, map[Op(method, path, id)]);
        }

        [Fact]
        public void Map_CollidingNames_GetNumericSuffixes()
        {
            // args follow the helper's (method, path, id) convention, as in the Theory above
            var ops = new[] { Op("GET", "/a", "doThing"), Op("GET", "/b", "doThing") };
            var map = MethodNameMapper.Map(ops);
            Assert.Equal("DoThing", map[ops[0]]);
            Assert.Equal("DoThing2", map[ops[1]]);
        }

        [Fact]
        public void Map_ReservesHelperMethodNames()
        {
            var ops = new[] { Op("GET", "/x", "setBaseUrl") };
            var map = MethodNameMapper.Map(ops);
            Assert.Equal("SetBaseUrl2", map[ops[0]]);
        }
    }
}