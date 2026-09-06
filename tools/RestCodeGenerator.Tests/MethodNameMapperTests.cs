using System.Collections.Generic;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class MethodNameMapperTests
    {
        private static ApiOperation Op(string method, string path, string? id = null) =>
            new(method, path, id, null,
                new List<ApiParameter>(), new List<ApiParameter>(),
                new List<ApiParameter>(), false);

        [Theory]
        [InlineData("getPetById", "/pet/{petId}", "GET", "GetPetById")]
        [InlineData(null, "/pet/findByStatus", "GET", "GetPetFindByStatus")]
        [InlineData(null, "/pet/{petId}", "POST", "PostPetPetId")]
        [InlineData(null, "/pet/{petId}", "DELETE", "DeletePetPetId")]
        [InlineData("user-login_GET!", "/user/login", "GET", "UserLoginGet")]
        [InlineData("9lives", "/cats", "GET", "X9lives")]
        [InlineData("lastStatusCode", "/y", "GET", "LastStatusCode2")]   // collides with the LastStatusCode property
        [InlineData("headerBuilder", "/y", "GET", "HeaderBuilder2")]     // collides with the private core type
        public void Map_ReturnsExpectedNames(string? id, string path, string method, string expected)
        {
            var map = MethodNameMapper.Map(new[] { Op(method, path, id) });
            Assert.Equal(expected, map[Op(method, path, id)]);
        }

        [Fact]
        public void Map_CollidingNames_GetNumericSuffixes()
        {
            // args follow the helper's (method, path, id) convention, as in the Theory above
            var ops = new[]
            {
                Op("GET", "/a", "doThing"), Op("GET", "/b", "doThing"), Op("GET", "/c", "doThing"),
            };
            var map = MethodNameMapper.Map(ops);
            Assert.Equal("DoThing", map[ops[0]]);
            Assert.Equal("DoThing2", map[ops[1]]);
            Assert.Equal("DoThing3", map[ops[2]]);
        }

        [Fact]
        public void Map_ReservesHelperMethodNames()
        {
            var ops = new[] { Op("GET", "/x", "baseUrl") };
            var map = MethodNameMapper.Map(ops);
            Assert.Equal("BaseUrl2", map[ops[0]]);
        }
    }
}