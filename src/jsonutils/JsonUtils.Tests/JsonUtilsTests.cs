using System.Collections.Generic;
using JsonAutomation;
using Newtonsoft.Json;
using Xunit;

namespace JsonAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for JsonUtils. This component is pure JSON/string
    /// logic over Newtonsoft.Json with no Win32 dependency, so unlike most of this suite's
    /// Windows-flavored components, every test here runs on Linux CI too.
    /// </summary>
    public class JsonUtilsTests
    {
        private readonly JsonUtils _json = new JsonUtils();

        private sealed class SamplePerson
        {
            public string Name { get; set; }
            public int Age { get; set; }
        }

        [Fact]
        public void Constructor_DoesNotThrow()
        {
            Assert.NotNull(_json);
        }

        [Fact]
        public void TryDeserializeObject_ValidJson_ReturnsPopulatedObject()
        {
            bool succeeded = _json.TryDeserializeObject("{\"Name\":\"Ada\",\"Age\":30}", out SamplePerson person, out string message);

            Assert.True(succeeded);
            Assert.Null(message);
            Assert.Equal("Ada", person.Name);
            Assert.Equal(30, person.Age);
        }

        [Fact]
        public void TryDeserializeObject_MalformedJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryDeserializeObject("{not json", out SamplePerson person, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TrySerializeObject_SimpleObject_ReturnsJson()
        {
            bool succeeded = _json.TrySerializeObject(new SamplePerson { Name = "Ada", Age = 30 }, out string json, out string message);

            Assert.True(succeeded);
            Assert.Null(message);
            Assert.Equal("Ada", (string)Newtonsoft.Json.Linq.JObject.Parse(json)["Name"]);
        }

        [Fact]
        public void TryGetValueFromJson_ExistingPath_ReturnsValue()
        {
            bool succeeded = _json.TryGetValueFromJson("{\"order\":{\"items\":[{\"sku\":\"ABC\"}]}}", "order.items[0].sku", out string value, out string message);

            Assert.True(succeeded);
            Assert.Null(message);
            Assert.Equal("ABC", value);
        }

        [Fact]
        public void TryGetValueFromJson_PathNotFound_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetValueFromJson("{\"order\":{}}", "order.missing", out string value, out string message);

            Assert.False(succeeded);
            Assert.Null(value);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetValueFromJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetValueFromJson("{not json", "a", out string value, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TrySetValueInJson_ExistingPath_ReturnsUpdatedJson()
        {
            bool succeeded = _json.TrySetValueInJson("{\"order\":{\"status\":\"open\"}}", "order.status", "closed", out string updatedJson, out string message);

            Assert.True(succeeded);
            Assert.Null(message);
            Assert.Equal("closed", (string)Newtonsoft.Json.Linq.JObject.Parse(updatedJson)["order"]["status"]);
        }

        [Fact]
        public void TrySetValueInJson_PathNotFound_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TrySetValueInJson("{\"order\":{}}", "order.missing", "x", out string updatedJson, out string message);

            Assert.False(succeeded);
            Assert.Null(updatedJson);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
