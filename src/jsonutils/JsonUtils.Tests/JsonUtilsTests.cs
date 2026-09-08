using System;
using System.Collections.Generic;
using System.Linq;
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

        [Fact]
        public void TryRemoveValueFromJson_ExistingPath_ReturnsUpdatedJson()
        {
            bool succeeded = _json.TryRemoveValueFromJson("{\"a\":1,\"b\":2}", "b", out string updatedJson, out string message);

            Assert.True(succeeded);
            Assert.False(Newtonsoft.Json.Linq.JObject.Parse(updatedJson).ContainsKey("b"));
        }

        [Fact]
        public void TryRemoveValueFromJson_ArrayElementPath_ReturnsUpdatedJson()
        {
            bool succeeded = _json.TryRemoveValueFromJson("{\"items\":[1,2,3]}", "items[1]", out string updatedJson, out string message);

            Assert.True(succeeded);
            Assert.Equal(new[] { 1, 3 }, Newtonsoft.Json.Linq.JObject.Parse(updatedJson)["items"].ToObject<int[]>());
        }

        [Fact]
        public void TryRemoveValueFromJson_PathNotFound_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryRemoveValueFromJson("{\"a\":1}", "missing", out string updatedJson, out string message);

            Assert.False(succeeded);
            Assert.Null(updatedJson);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryGetArrayLength_ArrayPath_ReturnsCount()
        {
            bool succeeded = _json.TryGetArrayLength("{\"items\":[1,2,3]}", "items", out int length, out string message);

            Assert.True(succeeded);
            Assert.Equal(3, length);
        }

        [Fact]
        public void TryGetArrayLength_NonArrayPath_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryGetArrayLength("{\"items\":1}", "items", out int length, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryAppendToJsonArray_ArrayPath_ReturnsUpdatedJsonWithNewElement()
        {
            bool succeeded = _json.TryAppendToJsonArray("{\"items\":[1,2]}", "items", "3", out string updatedJson, out string message);

            Assert.True(succeeded);
            Assert.Equal(3, Newtonsoft.Json.Linq.JObject.Parse(updatedJson)["items"].Count());
        }

        [Fact]
        public void TryAppendToJsonArray_NonArrayPath_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryAppendToJsonArray("{\"items\":1}", "items", "3", out string updatedJson, out string message);

            Assert.False(succeeded);
            Assert.Null(updatedJson);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryAppendToJsonArray_MalformedElementJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryAppendToJsonArray("{\"items\":[1]}", "items", "{not json", out string updatedJson, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryPrettyPrintJson_ValidJson_ReturnsIndentedText()
        {
            bool succeeded = _json.TryPrettyPrintJson("{\"a\":1}", out string formattedJson, out string message);

            Assert.True(succeeded);
            Assert.Contains("\n", formattedJson);
        }

        [Fact]
        public void TryPrettyPrintJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryPrettyPrintJson("{not json", out string formattedJson, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryMinifyJson_IndentedJson_ReturnsCompactText()
        {
            bool succeeded = _json.TryMinifyJson("{\n  \"a\": 1\n}", out string minifiedJson, out string message);

            Assert.True(succeeded);
            Assert.Equal("{\"a\":1}", minifiedJson);
        }

        [Fact]
        public void TryMinifyJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryMinifyJson("{not json", out string minifiedJson, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryPrettyPrintJson_OffsetDateLikeStringValue_PreservesExactText()
        {
            bool succeeded = _json.TryPrettyPrintJson("{\"created\":\"2026-02-20T08:30:00+05:30\"}", out string formattedJson, out string message);

            Assert.True(succeeded);
            Assert.Contains("\"2026-02-20T08:30:00+05:30\"", formattedJson);
        }

        [Fact]
        public void TryMinifyJson_OffsetDateLikeStringValue_PreservesExactText()
        {
            bool succeeded = _json.TryMinifyJson("{\"created\":\"2026-02-20T08:30:00+05:30\"}", out string minifiedJson, out string message);

            Assert.True(succeeded);
            Assert.Equal("{\"created\":\"2026-02-20T08:30:00+05:30\"}", minifiedJson);
        }

        [Fact]
        public void Workflow_SetPrettyPrintThenGet_RoundTripsCorrectly()
        {
            bool setSucceeded = _json.TrySetValueInJson("{\"order\":{\"status\":\"open\"}}", "order.status", "closed", out string updatedJson, out string setMessage);
            Assert.True(setSucceeded);

            bool prettySucceeded = _json.TryPrettyPrintJson(updatedJson, out string prettyJson, out string prettyMessage);
            Assert.True(prettySucceeded);

            bool getSucceeded = _json.TryGetValueFromJson(prettyJson, "order.status", out string value, out string getMessage);
            Assert.True(getSucceeded);
            Assert.Equal("closed", value);
        }

        [Fact]
        public void Workflow_AppendMinifyThenGetArrayLength_RoundTripsCorrectly()
        {
            bool appendSucceeded = _json.TryAppendToJsonArray("{\"items\":[1,2]}", "items", "3", out string updatedJson, out string appendMessage);
            Assert.True(appendSucceeded);

            bool minifySucceeded = _json.TryMinifyJson(updatedJson, out string minifiedJson, out string minifyMessage);
            Assert.True(minifySucceeded);

            bool lengthSucceeded = _json.TryGetArrayLength(minifiedJson, "items", out int length, out string lengthMessage);
            Assert.True(lengthSucceeded);
            Assert.Equal(3, length);
        }

        [Fact]
        public void TryMergeJson_ScalarConflict_SecondDocumentWins()
        {
            bool succeeded = _json.TryMergeJson("{\"a\":1,\"b\":2}", "{\"b\":3}", out string mergedJson, out string message);

            Assert.True(succeeded);
            Assert.Null(message);
            Assert.Equal(3, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["b"]);
            Assert.Equal(1, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["a"]);
        }

        [Fact]
        public void TryMergeJson_ArrayValues_Concatenates()
        {
            bool succeeded = _json.TryMergeJson("{\"items\":[1,2]}", "{\"items\":[3]}", out string mergedJson, out string message);

            Assert.True(succeeded);
            Assert.Equal(new[] { 1, 2, 3 }, Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["items"].ToObject<int[]>());
        }

        [Fact]
        public void TryMergeJson_BaseNotObject_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryMergeJson("[1,2]", "{\"a\":1}", out string mergedJson, out string message);

            Assert.False(succeeded);
            Assert.Null(mergedJson);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryMergeJson_OverrideNotObject_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryMergeJson("{\"a\":1}", "[1,2]", out string mergedJson, out string message);

            Assert.False(succeeded);
            Assert.Null(mergedJson);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryMergeJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryMergeJson("{not json", "{\"a\":1}", out string mergedJson, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryMergeJson_KeyOnlyInOverride_IsAdded()
        {
            bool succeeded = _json.TryMergeJson("{\"a\":1}", "{\"c\":2}", out string mergedJson, out string message);

            Assert.True(succeeded);
            Assert.Equal(1, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["a"]);
            Assert.Equal(2, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["c"]);
        }

        [Fact]
        public void TryMergeJson_NestedObjects_RecursesRatherThanReplacing()
        {
            bool succeeded = _json.TryMergeJson("{\"nested\":{\"x\":1,\"y\":2}}", "{\"nested\":{\"y\":9,\"z\":3}}", out string mergedJson, out string message);

            Assert.True(succeeded);
            Newtonsoft.Json.Linq.JObject nested = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["nested"];
            Assert.Equal(1, (int)nested["x"]);
            Assert.Equal(9, (int)nested["y"]);
            Assert.Equal(3, (int)nested["z"]);
        }

        [Fact]
        public void TryMergeJson_ExplicitNullInOverride_DoesNotClearBaseValue()
        {
            bool succeeded = _json.TryMergeJson("{\"a\":1}", "{\"a\":null}", out string mergedJson, out string message);

            Assert.True(succeeded);
            Assert.Equal(1, (int)Newtonsoft.Json.Linq.JObject.Parse(mergedJson)["a"]);
        }

        [Fact]
        public void TryDiffJson_EqualDocuments_ReturnsTrueWithEmptyPaths()
        {
            bool succeeded = _json.TryDiffJson("{\"a\":1,\"b\":2}", "{\"a\":1,\"b\":2}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.True(areEqual);
            Assert.Equal(string.Empty, differingPaths);
        }

        [Fact]
        public void TryDiffJson_ChangedScalar_ReturnsPathToScalar()
        {
            bool succeeded = _json.TryDiffJson("{\"a\":1}", "{\"a\":2}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("a", differingPaths);
        }

        [Fact]
        public void TryDiffJson_AddedKey_ReturnsPathToKey()
        {
            bool succeeded = _json.TryDiffJson("{\"a\":1}", "{\"a\":1,\"b\":2}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("b", differingPaths);
        }

        [Fact]
        public void TryDiffJson_ChangedArrayElement_ReturnsIndexedPath()
        {
            bool succeeded = _json.TryDiffJson("{\"items\":[1,2,3]}", "{\"items\":[1,9,3]}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("items[1]", differingPaths);
        }

        [Fact]
        public void TryDiffJson_ArrayLengthDifference_ReturnsPathForExtraElement()
        {
            bool succeeded = _json.TryDiffJson("{\"items\":[1,2]}", "{\"items\":[1,2,3]}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("items[2]", differingPaths);
        }

        [Fact]
        public void TryDiffJson_NestedPathDifference_ReturnsFullDottedPath()
        {
            bool succeeded = _json.TryDiffJson("{\"order\":{\"items\":[{\"sku\":\"A\"}]}}", "{\"order\":{\"items\":[{\"sku\":\"B\"}]}}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("order.items[0].sku", differingPaths);
        }

        [Fact]
        public void TryDiffJson_MultipleDifferences_ReturnsAllPathsDelimited()
        {
            bool succeeded = _json.TryDiffJson("{\"a\":1,\"b\":2}", "{\"a\":9,\"b\":9}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("a,b", differingPaths);
        }

        [Fact]
        public void TryDiffJson_EntirelyDifferentRootTypes_ReturnsDollarSign()
        {
            bool succeeded = _json.TryDiffJson("{\"a\":1}", "[1,2,3]", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("$", differingPaths);
        }

        [Fact]
        public void TryDiffJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool succeeded = _json.TryDiffJson("{not json", "{\"a\":1}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.False(succeeded);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryDiffJson_NestedNullVsValue_ReturnsPathNotConflatedWithAbsentKey()
        {
            bool succeeded = _json.TryDiffJson("{\"a\":{\"b\":null}}", "{\"a\":{\"b\":1}}", ",", out bool areEqual, out string differingPaths, out string message);

            Assert.True(succeeded);
            Assert.False(areEqual);
            Assert.Equal("a.b", differingPaths);
        }
    }
}
