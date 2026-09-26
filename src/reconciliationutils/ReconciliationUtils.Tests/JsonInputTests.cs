using System;
using System.Text;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    public sealed class JsonInputTests
    {
        private static ReconciliationLimits Limits(int rows = 1000, int chars = 1_000_000) =>
            new ReconciliationLimits { MaximumRowsPerSide = rows, MaximumInputCharactersPerSide = chars };

        private static bool TryParse(string json, out JsonInput input, out InputFailure failure, ReconciliationLimits limits = null) =>
            JsonInput.TryParse(json, "Left", limits ?? Limits(), out input, out failure);

        [Fact]
        public void AnArrayOfObjects_IsParsed_InSourceOrder()
        {
            Assert.True(TryParse("[{\"k\":\"a\"},{\"k\":\"b\"},{\"k\":\"c\"}]", out JsonInput input, out _));
            using (input)
            {
                Assert.Equal(3, input.RowCount);
                var key = new[] { "k" };
                Assert.Equal("a", input.RowAt(0).Read(key).Text);
                Assert.Equal("b", input.RowAt(1).Read(key).Text);
                Assert.Equal("c", input.RowAt(2).Read(key).Text);
            }
        }

        [Theory]
        [InlineData("[]")]
        [InlineData(" [ ] ")]
        public void AnEmptyArray_IsValid(string json)
        {
            Assert.True(TryParse(json, out JsonInput input, out _));
            using (input) Assert.Equal(0, input.RowCount);
        }

        [Fact]
        public void AnItemThatIsNotAnObject_IsKeptAsARow_AtItsIndex()
        {
            Assert.True(TryParse("[{\"k\":1},5,\"x\",null,[1],{\"k\":2}]", out JsonInput input, out _));
            using (input)
            {
                Assert.Equal(6, input.RowCount);
                Assert.True(input.RowAt(0).IsObject);
                for (int i = 1; i <= 4; i++) Assert.False(input.RowAt(i).IsObject);
                Assert.True(input.RowAt(5).IsObject);
            }
        }

        [Theory]
        [InlineData("{}", "NotAnArray")]
        [InlineData("5", "NotAnArray")]
        [InlineData("\"text\"", "NotAnArray")]
        [InlineData("null", "NotAnArray")]
        [InlineData("", "MalformedJson")]
        [InlineData("   ", "MalformedJson")]
        [InlineData("[", "MalformedJson")]
        [InlineData("[{\"a\":1}", "MalformedJson")]
        [InlineData("[{\"a\":1},]", "MalformedJson")]                 // a trailing comma
        [InlineData("[{\"a\":1,}]", "MalformedJson")]
        [InlineData("[{\"a\":1}] x", "MalformedJson")]
        [InlineData("[{a:1}]", "MalformedJson")]
        [InlineData("[// note\n{\"a\":1}]", "MalformedJson")]          // comments are not allowed
        [InlineData("[/* note */{\"a\":1}]", "MalformedJson")]
        public void ABadDocument_IsRejected_WithAStableCode(string json, string code)
        {
            Assert.False(TryParse(json, out JsonInput input, out InputFailure failure));
            Assert.Null(input);
            Assert.Equal(code, failure.Code);
            Assert.StartsWith("Left input", failure.Message);
        }

        [Fact]
        public void ANullInput_IsRejected()
        {
            Assert.False(TryParse(null, out _, out InputFailure failure));
            Assert.Equal("MissingInput", failure.Code);
        }

        [Fact]
        public void TheSideIsNamedInTheMessage()
        {
            JsonInput.TryParse("{}", "Right", Limits(), out _, out InputFailure failure);
            Assert.StartsWith("Right input", failure.Message);
        }

        [Theory]
        [InlineData("[{\"a\":1,\"a\":2}]")]                            // in a row
        [InlineData("[{\"a\":{\"b\":1,\"b\":2}}]")]                    // nested
        [InlineData("[{\"a\":1},{\"b\":1,\"b\":1}]")]                  // in a later row, even when the values agree
        [InlineData("[{\"a\":[{\"x\":1,\"x\":2}]}]")]                  // inside an array inside a row
        [InlineData("[{\"a\":1,\"\\u0061\":2}]")]                      // the same name written with an escape
        public void ARepeatedPropertyName_AnywhereInTheInput_IsRejected(string json)
        {
            Assert.False(TryParse(json, out _, out InputFailure failure));
            Assert.Equal("DuplicateProperty", failure.Code);
        }

        [Fact]
        public void PropertyNamesThatDifferOnlyByCase_AreNotDuplicates()
        {
            Assert.True(TryParse("[{\"a\":1,\"A\":2}]", out JsonInput input, out _));
            input.Dispose();
        }

        [Fact]
        public void TheSamePropertyNameInSeparateObjects_IsFine()
        {
            Assert.True(TryParse("[{\"a\":1},{\"a\":2},{\"n\":{\"a\":1},\"m\":{\"a\":2}}]", out JsonInput input, out _));
            input.Dispose();
        }

        [Fact]
        public void TheRowLimit_IsEnforcedAtItsBoundary()
        {
            string rows(int n) => "[" + string.Join(",", System.Linq.Enumerable.Repeat("{}", n)) + "]";
            Assert.True(TryParse(rows(5), out JsonInput ok, out _, Limits(rows: 5)));
            ok.Dispose();
            Assert.False(TryParse(rows(6), out _, out InputFailure failure, Limits(rows: 5)));
            Assert.Equal("TooManyRows", failure.Code);
            Assert.Contains("limit of 5", failure.Message);
        }

        [Fact]
        public void TheRowLimit_CountsItemsThatAreNotObjects_AndNotNestedItems()
        {
            // 3 top-level items, one of which holds many nested values: only the top-level items are rows.
            Assert.True(TryParse("[1,2,{\"a\":[1,2,3,4,5,6,7,8,9]}]", out JsonInput input, out _, Limits(rows: 3)));
            input.Dispose();
            Assert.False(TryParse("[1,2,3,4]", out _, out InputFailure failure, Limits(rows: 3)));
            Assert.Equal("TooManyRows", failure.Code);
        }

        [Fact]
        public void TheCharacterLimit_IsCheckedBeforeAnythingIsParsed()
        {
            string json = "[{\"a\":\"" + new string('x', 100) + "\"}]";
            Assert.True(TryParse(json, out JsonInput ok, out _, Limits(chars: json.Length)));
            ok.Dispose();
            Assert.False(TryParse(json, out _, out InputFailure failure, Limits(chars: json.Length - 1)));
            Assert.Equal("InputTooLarge", failure.Code);
        }

        [Fact]
        public void TheDepthLimit_IsEnforcedAtItsBoundary()
        {
            // depth counts every array/object level: the top-level array is level 1
            string nested(int levels) => new string('[', levels) + new string(']', levels);
            Assert.True(TryParse(nested(JsonInput.MaxDepth), out JsonInput ok, out _));
            ok.Dispose();
            Assert.False(TryParse(nested(JsonInput.MaxDepth + 1), out _, out InputFailure failure));
            Assert.Equal("DepthLimit", failure.Code);
        }

        [Fact]
        public void TheDepthLimit_AppliesToObjectsToo()
        {
            string json = "[" + string.Concat(System.Linq.Enumerable.Repeat("{\"a\":", JsonInput.MaxDepth)) + "1" + new string('}', JsonInput.MaxDepth) + "]";
            Assert.False(TryParse(json, out _, out InputFailure failure));
            Assert.Equal("DepthLimit", failure.Code);
        }

        [Fact]
        public void ANumberToken_IsBoundedAt256Characters()
        {
            Assert.True(TryParse("[{\"k\":" + new string('1', 256) + "}]", out JsonInput ok, out _));
            ok.Dispose();
            Assert.False(TryParse("[{\"k\":" + new string('1', 257) + "}]", out _, out InputFailure failure));
            Assert.Equal("NumberTooLong", failure.Code);
            Assert.Contains("256", failure.Message);
            Assert.StartsWith("Left input", failure.Message);
        }

        [Theory]
        [InlineData("-{0}")]                                        // the sign counts
        [InlineData("{0}.5")]                                       // so does a fraction
        [InlineData("1e{0}")]                                       // and an exponent
        public void TheNumberBound_CountsTheWholeToken(string shape)
        {
            // the digits are sized so that the whole token is 257 characters
            string Token(int digits) => string.Format(shape, new string('1', digits));
            int extra = Token(1).Length - 1;
            string over = Token(257 - extra);
            Assert.Equal(257, over.Length);
            Assert.False(TryParse("[{\"k\":" + over + "}]", out _, out InputFailure failure));
            Assert.Equal("NumberTooLong", failure.Code);
            Assert.True(TryParse("[{\"k\":" + Token(256 - extra) + "}]", out JsonInput ok, out _));
            ok.Dispose();
        }

        [Fact]
        public void ALongNumber_AnywhereInTheInput_IsCaught_EvenNestedOrInAnArray()
        {
            string longNumber = new string('9', 300);
            Assert.False(TryParse("[{\"a\":{\"b\":[1," + longNumber + "]}}]", out _, out InputFailure nested));
            Assert.Equal("NumberTooLong", nested.Code);
            Assert.False(TryParse("[" + longNumber + "]", out _, out InputFailure topLevel));
            Assert.Equal("NumberTooLong", topLevel.Code);
        }

        [Fact]
        public void ALongNumber_IsRejectedBeforeAnyTreeIsBuilt_WithoutEchoingIt()
        {
            string longNumber = "8" + new string('7', 400);
            Assert.False(TryParse("[{\"k\":" + longNumber + "}]", out JsonInput input, out InputFailure failure));
            Assert.Null(input);
            Assert.DoesNotContain("7777", failure.Message);
        }

        [Theory]
        [InlineData("[{\"secret\":\"4111-1111-1111-1111\",]")]
        [InlineData("[{\"secret\":\"4111-1111-1111-1111\"} \"SSN 078-05-1120\"]")]
        [InlineData("[{\"secret\":\"4111-1111-1111-1111\",\"secret\":\"SSN 078-05-1120\"}]")]
        public void AFailureMessage_NeverEchoesTheData(string json)
        {
            Assert.False(TryParse(json, out _, out InputFailure failure));
            Assert.DoesNotContain("4111", failure.Message);
            Assert.DoesNotContain("078", failure.Message);
            Assert.DoesNotContain("secret", failure.Message);
        }

        [Fact]
        public void AMalformedDocument_ReportsAPosition_ToHelpFindIt()
        {
            Assert.False(TryParse("[\n{\"a\":1},\n{\"a\":}\n]", out _, out InputFailure failure));
            Assert.Contains("line 3", failure.Message);
        }

        [Fact]
        public void APropertyNameThatIsNotValidText_RejectsTheDocument_WithoutThrowing()
        {
            Assert.False(TryParse("[{\"\\uD800\":1}]", out _, out InputFailure failure));
            Assert.Equal("InvalidText", failure.Code);
            Assert.StartsWith("Left input", failure.Message);
        }

        [Theory]
        [InlineData("[{\"k\":\"\\uD800\"}]")]
        [InlineData("[{\"k\":\"a\\uDC00b\"}]")]
        [InlineData("[{\"k\":\"\\uDBFF\"}]")]
        public void AStringValueThatIsNotValidText_IsUnsupportedData_NeverAnException(string json)
        {
            Assert.True(TryParse(json, out JsonInput input, out _));   // legal JSON syntax
            using (input)
            {
                FieldValue value = input.RowAt(0).Read(new[] { "k" });  // must not throw
                Assert.Equal(FieldKind.Unsupported, value.Kind);
            }
        }

        [Fact]
        public void AProperSurrogatePair_InAValue_IsAString()
        {
            Assert.True(TryParse("[{\"k\":\"\\uD83C\\uDF89\"}]", out JsonInput input, out _));
            using (input)
            {
                FieldValue value = input.RowAt(0).Read(new[] { "k" });
                Assert.Equal(FieldKind.String, value.Kind);
                Assert.Equal("\U0001F389", value.Text);
            }
        }

        [Fact]
        public void Unicode_IsPreserved()
        {
            Assert.True(TryParse("[{\"k\":\"日本語 ünï 🎉\"}]", out JsonInput input, out _));
            using (input) Assert.Equal("日本語 ünï 🎉", input.RowAt(0).Read(new[] { "k" }).Text);
        }
    }
}
