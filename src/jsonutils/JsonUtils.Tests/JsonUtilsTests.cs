using System;
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
        public void TryDeserializeObject_ValidJsonAndTypeName_ReturnsPopulatedObject()
        {
            bool succeeded = _json.TryDeserializeObject("{\"Name\":\"Ada\",\"Age\":30}", typeof(SamplePerson).AssemblyQualifiedName, out object result, out string message);

            Assert.True(succeeded);
            Assert.Null(message);
            SamplePerson person = Assert.IsType<SamplePerson>(result);
            Assert.Equal("Ada", person.Name);
            Assert.Equal(30, person.Age);
        }

        [Fact]
        public void TryDeserializeObject_MalformedJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryDeserializeObject("{not json", typeof(SamplePerson).AssemblyQualifiedName, out object result, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryDeserializeObject_UnresolvableTypeName_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryDeserializeObject("{\"Name\":\"Ada\"}", "NoSuch.Type, NoSuchAssembly", out object result, out string message);

            Assert.False(succeeded);
            Assert.Null(result);
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
        public void TryGetValueFromJson_NullLiteralPath_ReturnsTrueWithNullValue()
        {
            bool succeeded = _json.TryGetValueFromJson("{\"a\":null}", "a", out string value, out string message);

            Assert.True(succeeded);
            Assert.Null(value);
            Assert.Null(message);
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

        [Theory]
        [InlineData("{\"a\":1}")]
        [InlineData("[1,2,3]")]
        [InlineData("\"just a string\"")]
        public void IsValidJson_ValidJson_ReturnsTrue(string json)
        {
            bool valid = _json.IsValidJson(json, out string message);

            Assert.True(valid);
            Assert.Null(message);
        }

        [Fact]
        public void IsValidJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool valid = _json.IsValidJson("{not json", out string message);

            Assert.False(valid);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetStringValue_StringPath_ReturnsValue()
        {
            bool succeeded = _json.TryGetStringValue("{\"name\":\"Ada\"}", "name", out string value, out string message);

            Assert.True(succeeded);
            Assert.Equal("Ada", value);
        }

        [Fact]
        public void TryGetIntValue_IntegerPath_ReturnsValue()
        {
            bool succeeded = _json.TryGetIntValue("{\"age\":30}", "age", out int value, out string message);

            Assert.True(succeeded);
            Assert.Equal(30, value);
        }

        [Fact]
        public void TryGetIntValue_NonNumericPath_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetIntValue("{\"age\":\"thirty\"}", "age", out int value, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetBoolValue_BooleanPath_ReturnsValue()
        {
            bool succeeded = _json.TryGetBoolValue("{\"active\":true}", "active", out bool value, out string message);

            Assert.True(succeeded);
            Assert.True(value);
        }

        [Fact]
        public void TryGetBoolValue_NonBooleanPath_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetBoolValue("{\"active\":\"nope\"}", "active", out bool value, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetDoubleValue_NumberPath_ReturnsValue()
        {
            bool succeeded = _json.TryGetDoubleValue("{\"price\":19.99}", "price", out double value, out string message);

            Assert.True(succeeded);
            Assert.Equal(19.99, value);
        }

        [Fact]
        public void TryGetDoubleValue_NonNumericPath_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetDoubleValue("{\"price\":\"abc\"}", "price", out double value, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetDateTimeValue_DateStringPath_ReturnsValue()
        {
            bool succeeded = _json.TryGetDateTimeValue("{\"created\":\"2026-01-15T00:00:00\"}", "created", out DateTime value, out string message);

            Assert.True(succeeded);
            Assert.Equal(new DateTime(2026, 1, 15), value);
        }

        [Fact]
        public void TryGetDateTimeValue_NonDatePath_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetDateTimeValue("{\"created\":\"not a date\"}", "created", out DateTime value, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetStringValue_PathNotFound_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetStringValue("{}", "missing", out string value, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetValuesFromJson_WildcardPath_ReturnsDelimitedValues()
        {
            bool succeeded = _json.TryGetValuesFromJson("{\"items\":[{\"sku\":\"A\"},{\"sku\":\"B\"}]}", "items[*].sku", ",", out string delimitedValues, out string message);

            Assert.True(succeeded);
            Assert.Equal("A,B", delimitedValues);
        }

        [Fact]
        public void TryGetValuesFromJson_NoMatches_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetValuesFromJson("{\"items\":[]}", "items[*].sku", ",", out string delimitedValues, out string message);

            Assert.False(succeeded);
            Assert.Null(delimitedValues);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Theory]
        [InlineData("{\"a\":\"x\"}", "a", JsonValueKind.String)]
        [InlineData("{\"a\":1}", "a", JsonValueKind.Number)]
        [InlineData("{\"a\":true}", "a", JsonValueKind.Boolean)]
        [InlineData("{\"a\":null}", "a", JsonValueKind.Null)]
        [InlineData("{\"a\":[1,2]}", "a", JsonValueKind.Array)]
        [InlineData("{\"a\":{\"b\":1}}", "a", JsonValueKind.Object)]
        public void TryGetValueType_VariousTypes_ReturnsExpectedKind(string json, string path, JsonValueKind expectedKind)
        {
            bool succeeded = _json.TryGetValueType(json, path, out JsonValueKind kind, out string message);

            Assert.True(succeeded);
            Assert.Equal(expectedKind, kind);
        }

        [Fact]
        public void TryGetValueType_PathNotFound_ReturnsFalseWithNotFoundKind()
        {
            bool succeeded = _json.TryGetValueType("{}", "missing", out JsonValueKind kind, out string message);

            Assert.False(succeeded);
            Assert.Equal(JsonValueKind.NotFound, kind);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetValueFromJson_DateLikeStringPath_ReturnsExactOriginalText()
        {
            bool succeeded = _json.TryGetValueFromJson("{\"created\":\"2026-02-20T08:30:00Z\"}", "created", out string value, out string message);

            Assert.True(succeeded);
            Assert.Equal("2026-02-20T08:30:00Z", value);
        }

        [Fact]
        public void TryGetValuesFromJson_MatchIncludesNull_ReturnsEmptyStringForThatMatch()
        {
            bool succeeded = _json.TryGetValuesFromJson("{\"items\":[{\"sku\":\"A\"},{\"sku\":null},{\"sku\":\"B\"}]}", "items[*].sku", ",", out string delimitedValues, out string message);

            Assert.True(succeeded);
            Assert.Equal("A,,B", delimitedValues);
        }
    }
}
