using System;
using ValueStoreAutomation;
using Xunit;

namespace ValueStoreAutomation.Tests
{
    /// <summary>
    /// Platform-independent unit tests for ValueStoreUtils. This component is pure in-memory
    /// dictionary/JSON logic with no Win32 dependency, so every test here runs on Linux CI too.
    /// </summary>
    public class ValueStoreUtilsTests
    {
        private readonly ValueStoreUtils _store = new ValueStoreUtils();

        [Fact]
        public void Constructor_DoesNotThrow()
        {
            Assert.NotNull(_store);
        }

        [Fact]
        public void SetValue_ThenTryGetValue_ReturnsStoredValue()
        {
            bool set = _store.SetValue("Name", "Acme", out string setMessage);
            bool got = _store.TryGetValue("Name", out bool found, out object value, out string getMessage);

            Assert.True(set);
            Assert.Null(setMessage);
            Assert.True(got);
            Assert.Null(getMessage);
            Assert.True(found);
            Assert.Equal("Acme", value);
        }

        [Fact]
        public void TryGetValue_MissingKey_ReturnsTrueWithFoundFalse()
        {
            bool got = _store.TryGetValue("Missing", out bool found, out object value, out string message);

            Assert.True(got);
            Assert.False(found);
            Assert.Null(value);
            Assert.Null(message);
        }

        [Fact]
        public void SetValue_NullKey_ReturnsFalseWithMessage()
        {
            bool set = _store.SetValue(null, "x", out string message);

            Assert.False(set);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetValue_WhitespaceKey_ReturnsFalseWithMessage()
        {
            bool set = _store.SetValue("   ", "x", out string message);

            Assert.False(set);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void Remove_ExistingKey_ReturnsTrueWithRemovedTrue()
        {
            _store.SetString("Key", "value", out _);

            bool ok = _store.Remove("Key", out bool removed, out string message);

            Assert.True(ok);
            Assert.True(removed);
            Assert.Null(message);
            Assert.False(_store.ContainsKey("Key", out bool exists, out _) && exists);
        }

        [Fact]
        public void Remove_MissingKey_ReturnsTrueWithRemovedFalse()
        {
            bool ok = _store.Remove("Missing", out bool removed, out string message);

            Assert.True(ok);
            Assert.False(removed);
            Assert.Null(message);
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            _store.SetString("A", "1", out _);
            _store.SetString("B", "2", out _);

            bool ok = _store.Clear(out int removedCount, out string message);
            _store.GetCount(out int count, out _);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Equal(2, removedCount);
            Assert.Equal(0, count);
        }

        [Fact]
        public void GetKeys_ReturnsInsertionOrderedArray()
        {
            _store.SetString("First", "1", out _);
            _store.SetString("Second", "2", out _);

            bool ok = _store.GetKeys(out string[] keys, out string message);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Equal(new[] { "First", "Second" }, keys);
        }

        [Fact]
        public void GetKeys_EmptyStore_ReturnsEmptyArray()
        {
            bool ok = _store.GetKeys(out string[] keys, out string message);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Empty(keys);
        }

        [Fact]
        public void CaseSensitiveKeys_DefaultsToFalse_KeysAreCaseInsensitive()
        {
            _store.SetString("Name", "Acme", out _);

            string value = _store.GetString("name");

            Assert.Equal("Acme", value);
        }

        [Fact]
        public void CaseSensitiveKeys_WhenTrue_KeysAreDistinctByCase()
        {
            _store.CaseSensitiveKeys = true;
            _store.SetString("Name", "Upper", out _);
            _store.SetString("name", "Lower", out _);

            Assert.Equal("Upper", _store.GetString("Name"));
            Assert.Equal("Lower", _store.GetString("name"));
        }

        [Theory]
        [InlineData("42", 42)]
        [InlineData(42, 42)]
        public void GetInt32_ConvertibleValue_ReturnsConvertedValue(object stored, int expected)
        {
            _store.SetValue("Number", stored, out _);

            Assert.Equal(expected, _store.GetInt32("Number"));
        }

        [Fact]
        public void GetInt32_UnconvertibleValue_ReturnsDefault()
        {
            _store.SetString("Number", "not a number", out _);

            Assert.Equal(-1, _store.GetInt32("Number", -1));
        }

        [Fact]
        public void GetInt32_MissingKey_ReturnsDefault()
        {
            Assert.Equal(7, _store.GetInt32("Missing", 7));
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("YES", true)]
        [InlineData("y", true)]
        [InlineData("1", true)]
        [InlineData("false", false)]
        [InlineData("no", false)]
        [InlineData("n", false)]
        [InlineData("0", false)]
        public void GetBoolean_RecognizedText_ParsesExpectedValue(string stored, bool expected)
        {
            _store.SetString("Flag", stored, out _);

            Assert.Equal(expected, _store.GetBoolean("Flag"));
        }

        [Fact]
        public void GetBoolean_UnrecognizedText_ReturnsDefault()
        {
            _store.SetString("Flag", "maybe", out _);

            Assert.True(_store.GetBoolean("Flag", true));
        }

        [Fact]
        public void TryGetInt32_ConvertibleValue_ReturnsTrue()
        {
            _store.SetString("Number", "42", out _);

            bool ok = _store.TryGetInt32("Number", out int value);

            Assert.True(ok);
            Assert.Equal(42, value);
        }

        [Fact]
        public void TryGetInt32_MissingKey_ReturnsFalse()
        {
            bool ok = _store.TryGetInt32("Missing", out int value);

            Assert.False(ok);
            Assert.Equal(0, value);
        }

        [Fact]
        public void SetJson_ObjectValue_IsNavigableViaPath()
        {
            bool ok = _store.SetJson("Customer", "{\"Address\":{\"City\":\"Columbus\"}}", out string message);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Equal("Columbus", _store.GetPathString("Customer.Address.City"));
        }

        [Fact]
        public void SetJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool ok = _store.SetJson("Bad", "{not json", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void SetPath_CreatesIntermediateStructures()
        {
            bool ok = _store.SetPath("Customer.Address.City", "Columbus", out string message);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Equal("Columbus", _store.GetPathString("Customer.Address.City"));
        }

        [Fact]
        public void SetPath_EmptyPath_ReturnsFalseWithMessage()
        {
            bool ok = _store.SetPath("", "value", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void GetPathString_MissingSegment_ReturnsDefault()
        {
            _store.SetPath("Customer.Address.City", "Columbus", out _);

            Assert.Equal("N/A", _store.GetPathString("Customer.Address.Zip", "N/A"));
        }

        [Fact]
        public void TryGetPathValue_MissingSegment_ReturnsTrueWithFoundFalse()
        {
            bool ok = _store.TryGetPathValue("Nowhere.Deep", out bool found, out object value, out string message);

            Assert.True(ok);
            Assert.False(found);
            Assert.Null(value);
            Assert.Null(message);
        }

        [Fact]
        public void TryGetJson_RoundTripsThroughTryLoadFromJson()
        {
            _store.SetString("Name", "Acme", out _);
            _store.SetInt32("Count", 3, out _);

            bool serialized = _store.TryGetJson(out string json, out string serializeMessage);

            var reloaded = new ValueStoreUtils();
            bool loaded = reloaded.TryLoadFromJson(json, true, out int loadedCount, out string loadMessage);

            Assert.True(serialized);
            Assert.Null(serializeMessage);
            Assert.True(loaded);
            Assert.Null(loadMessage);
            Assert.Equal(2, loadedCount);
            Assert.Equal("Acme", reloaded.GetString("Name"));
            Assert.Equal(3, reloaded.GetInt32("Count"));
        }

        [Fact]
        public void TryLoadFromJson_NonObjectRoot_ReturnsFalseWithMessage()
        {
            bool ok = _store.TryLoadFromJson("[1,2,3]", false, out int loadedCount, out string message);

            Assert.False(ok);
            Assert.Equal(0, loadedCount);
            Assert.False(string.IsNullOrEmpty(message));
        }

        [Fact]
        public void TryLoadFromJson_MalformedJson_ReturnsFalseWithMessage()
        {
            bool ok = _store.TryLoadFromJson("{not json", false, out int loadedCount, out string message);

            Assert.False(ok);
            Assert.Equal(0, loadedCount);
            Assert.False(string.IsNullOrEmpty(message));
        }

        private sealed class SamplePerson
        {
            public string Name { get; set; }
            public int Age { get; set; }
        }

        [Fact]
        public void TryLoadFromObject_PopulatesFromPublicProperties()
        {
            bool ok = _store.TryLoadFromObject(new SamplePerson { Name = "Ada", Age = 30 }, true, out int loadedCount, out string message);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Equal(2, loadedCount);
            Assert.Equal("Ada", _store.GetString("Name"));
            Assert.Equal(30, _store.GetInt32("Age"));
        }

        [Fact]
        public void Merge_OverwriteFalse_PreservesExistingValues()
        {
            _store.SetString("Status", "Open", out _);

            bool ok = _store.Merge("{\"Status\":\"Closed\",\"Priority\":1}", false, out int mergedCount, out string message);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Equal(1, mergedCount);
            Assert.Equal("Open", _store.GetString("Status"));
            Assert.Equal(1, _store.GetInt32("Priority"));
        }

        [Fact]
        public void FindKeysJson_WildcardPattern_MatchesExpectedKeys()
        {
            _store.SetString("Cust_Name", "Acme", out _);
            _store.SetString("Cust_Id", "1", out _);
            _store.SetString("Other", "x", out _);

            bool ok = _store.FindKeysJson("Cust_*", out string keysJson, out string message);

            Assert.True(ok);
            Assert.Null(message);
            Assert.Contains("Cust_Name", keysJson);
            Assert.Contains("Cust_Id", keysJson);
            Assert.DoesNotContain("Other", keysJson);
        }

        [Fact]
        public void AddIfMissing_ExistingKey_DoesNotOverwrite()
        {
            _store.SetString("Key", "original", out _);

            bool ok = _store.AddIfMissing("Key", "replacement", out bool added, out string message);

            Assert.True(ok);
            Assert.False(added);
            Assert.Null(message);
            Assert.Equal("original", _store.GetString("Key"));
        }

        [Fact]
        public void RemoveAndGetValue_ExistingKey_ReturnsValueAndRemovesIt()
        {
            _store.SetString("Key", "value", out _);

            bool ok = _store.RemoveAndGetValue("Key", out bool found, out object value, out string message);

            Assert.True(ok);
            Assert.True(found);
            Assert.Equal("value", value);
            Assert.Null(message);
            _store.ContainsKey("Key", out bool exists, out _);
            Assert.False(exists);
        }

        [Fact]
        public void IsEmpty_BlankString_ReturnsTrue()
        {
            _store.SetString("Key", "   ", out _);

            bool ok = _store.IsEmpty("Key", out bool isEmpty, out string message);

            Assert.True(ok);
            Assert.True(isEmpty);
            Assert.Null(message);
        }

        [Fact]
        public void Dispose_ThenSetValue_ReturnsFalseWithMessage()
        {
            _store.Dispose();

            bool ok = _store.SetValue("Key", "value", out string message);

            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(message));
        }
    }
}
