using System.Text.Json;
using Xunit;

namespace RestCodeGenerator.Tests
{
    public class ExampleSchemaInferenceTests
    {
        private static ApiSchema Infer(string json) =>
            ExampleSchemaInference.Infer(JsonDocument.Parse(json).RootElement);

        [Fact]
        public void Infer_Object_ReturnsNamedProperties()
        {
            var schema = Infer("""{ "name": "Rex", "age": 3 }""");
            Assert.Equal("object", schema.JsonType);
            Assert.Collection(schema.Properties,
                p => { Assert.Equal("name", p.Name); Assert.Equal("string", p.Schema.JsonType); },
                p => { Assert.Equal("age", p.Name); Assert.Equal("integer", p.Schema.JsonType); });
        }

        [Fact]
        public void Infer_Array_MaxItemsIsActualElementCount()
        {
            var schema = Infer("""[1, 2, 3, 4, 5]""");
            Assert.Equal("array", schema.JsonType);
            Assert.Equal(5, schema.MaxItems);
            Assert.Equal("integer", schema.Items!.JsonType);
        }

        [Fact]
        public void Infer_EmptyObject_HasNoProperties()
        {
            var schema = Infer("{}");
            Assert.Equal("object", schema.JsonType);
            Assert.Empty(schema.Properties);
        }

        [Fact]
        public void Infer_EmptyArray_MaxItemsIsOne_ItemsFallsBackToString()
        {
            var schema = Infer("[]");
            Assert.Equal("array", schema.JsonType);
            Assert.Equal(1, schema.MaxItems);
            Assert.Equal("string", schema.Items!.JsonType);
        }

        [Fact]
        public void Infer_DistinguishesIntegerFromNumber()
        {
            Assert.Equal("integer", Infer("5").JsonType);
            Assert.Equal("number", Infer("5.5").JsonType);
        }

        [Fact]
        public void Infer_BooleanAndString_PassThrough()
        {
            Assert.Equal("boolean", Infer("true").JsonType);
            Assert.Equal("string", Infer("\"hello\"").JsonType);
            Assert.Equal("string", Infer("null").JsonType);
        }

        [Fact]
        public void Infer_DeeplyNestedObject_CollapsesToStringLeafPastDepthGuard()
        {
            // 12 nested single-property objects around a leaf string — well past the
            // depth-8 guard, so a node encountered at depth 9 must collapse to a string
            // leaf even though the actual JSON at that position is still an object.
            var json = "\"leaf\"";
            for (var i = 0; i < 12; i++)
                json = "{\"n\":" + json + "}";
            var current = Infer(json);

            for (var i = 0; i < 9; i++)
            {
                Assert.Equal("object", current.JsonType);
                current = current.Properties[0].Schema;
            }
            Assert.Equal("string", current.JsonType);
            Assert.Empty(current.Properties);
        }
    }
}
