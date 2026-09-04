using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using DataBagAutomation;
using Xunit;

namespace DataBagUtils.Tests
{
    public sealed class DataBagUtilsTests
    {
        private const string Definitions = """
            {"values":[
              {"name":"CustomerId","type":"String","defaultValue":"","mustHaveValue":true},
              {"name":"Total","type":"Decimal","defaultValue":0},
              {"name":"Enabled","type":"Boolean","defaultValue":false},
              {"name":"Payload","type":"Json","defaultValue":{}},
              {"name":"Source","type":"String","defaultValue":"SAP","readOnly":true},
              {"name":"Confirmation","type":"String","defaultValue":null,"writeOnce":true},
              {"name":"Secret","type":"String","defaultValue":"abc","sensitive":true}
            ]}
            """;

        [Fact]
        public void DesignTimeJsonInitializesAndSealsTypedDefinitions()
        {
            using var bag = new DataBagAutomation.DataBagUtils { InitializationSource = DataBagInitializationSource.DesignTimeJson, InitialItemsJson = Definitions };
            Assert.True(bag.Initialize(out int count, out string message)); Assert.Equal(7, count); Assert.Null(message);
            Assert.True(bag.GetState(out DataBagState state, out int stateCount, out _)); Assert.Equal(DataBagState.Ready, state); Assert.Equal(7, stateCount);
            Assert.True(bag.TryGetDecimal("total", out bool found, out decimal total, out _)); Assert.True(found); Assert.Equal(0m, total);
            Assert.False(bag.SetTypedValue("NewName", DataBagValueType.String, "x", out message)); Assert.NotNull(message);
        }

        [Fact]
        public void JsonFileInitializesRelativeToBaseDirectoryAndMissingFileFailsAtomically()
        {
            string name = "databag-" + Guid.NewGuid().ToString("N") + ".json";
            string path = Path.Combine(AppContext.BaseDirectory, name);
            File.WriteAllText(path, Definitions);
            try
            {
                using var bag = new DataBagAutomation.DataBagUtils { InitializationSource = DataBagInitializationSource.JsonFile, InitialItemsFilePath = name };
                Assert.True(bag.Initialize(out int count, out _)); Assert.Equal(7, count);
            }
            finally { File.Delete(path); }

            using var missing = new DataBagAutomation.DataBagUtils { InitializationSource = DataBagInitializationSource.JsonFile, InitialItemsFilePath = name };
            Assert.False(missing.Initialize(out int loaded, out string message)); Assert.Equal(0, loaded); Assert.NotNull(message);
            Assert.True(missing.GetState(out DataBagState state, out int countAfter, out _)); Assert.Equal(DataBagState.NotInitialized, state); Assert.Equal(0, countAfter);
        }

        [Fact]
        public void TemplateActionPopulatesEditableDefinitionJson()
        {
            using var bag = new DataBagAutomation.DataBagUtils();
            Assert.True(bag.PopulateInitialItemsJsonTemplate(out string message)); Assert.Null(message);
            Assert.Equal(DataBagInitializationSource.DesignTimeJson, bag.InitializationSource);
            Assert.Contains("mustHaveValue", bag.InitialItemsJson);
            Assert.False(bag.Initialize(out int loaded, out message)); Assert.Equal(0, loaded); Assert.Contains("name", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ProgrammaticInitializationPublishesAtomicallyAndCanBeCancelled()
        {
            using var bag = new DataBagAutomation.DataBagUtils();
            Assert.True(bag.BeginInitialization(true, out _));
            Assert.True(bag.SetTypedValue("Count", DataBagValueType.Int32, "3", out _));
            Assert.True(bag.CompleteInitialization(out int count, out _)); Assert.Equal(1, count);
            Assert.True(bag.BeginInitialization(false, out _));
            Assert.True(bag.SetTypedValue("Extra", DataBagValueType.String, "x", out _));
            Assert.True(bag.CancelInitialization(out int discarded, out _)); Assert.Equal(2, discarded);
            Assert.True(bag.Contains("Count", out bool exists, out _)); Assert.True(exists);
            Assert.True(bag.Contains("Extra", out exists, out _)); Assert.False(exists);
        }

        [Fact]
        public void UnsealedDesignTimeInitializationRemainsStagedUntilCompleted()
        {
            using var bag = new DataBagAutomation.DataBagUtils
            {
                InitializationSource = DataBagInitializationSource.DesignTimeJson,
                InitialItemsJson = Definitions,
                SealAfterInitialization = false
            };
            Assert.True(bag.Initialize(out int loaded, out _)); Assert.Equal(7, loaded);
            Assert.True(bag.GetState(out DataBagState state, out int activeCount, out _)); Assert.Equal(DataBagState.Initializing, state); Assert.Equal(0, activeCount);
            Assert.True(bag.CompleteInitialization(out int completed, out _)); Assert.Equal(7, completed);
        }

        [Fact]
        public void GenericAndNativeSettersRequireExistingMatchingDefinitions()
        {
            using var bag = ReadyBag();
            Assert.True(bag.SetValue("Total", "1234.50", out _));
            Assert.True(bag.TryGetDecimal("Total", out bool found, out decimal value, out _)); Assert.True(found); Assert.Equal(1234.50m, value);
            Assert.True(bag.SetBoolean("Enabled", true, out _));
            Assert.False(bag.SetString("Enabled", "true", out string message)); Assert.Contains("Boolean", message);
            Assert.False(bag.SetValue("Unknown", "x", out message)); Assert.NotNull(message);
            Assert.False(bag.SetValue("Total", "not-a-number", out message)); Assert.NotNull(message);
        }

        [Fact]
        public void JsonIsValidatedAndRawValueRoundTrips()
        {
            using var bag = ReadyBag();
            Assert.True(bag.SetJson("Payload", " { \"a\": 1 } ", out _));
            Assert.True(bag.TryGetJson("Payload", out bool found, out string json, out _)); Assert.True(found); Assert.Equal(" { \"a\": 1 } ", json);
            Assert.False(bag.SetJson("Payload", "bad", out string message)); Assert.NotNull(message);
            Assert.True(bag.TryGetJson("Payload", out found, out json, out _)); Assert.Equal(" { \"a\": 1 } ", json);
        }

        [Fact]
        public void RuntimeJsonObjectUpdateIsAtomicAndHandlesUnknownNames()
        {
            using var bag = ReadyBag();
            Assert.False(bag.SetFromJsonObject("{\"Total\":12.5,\"Unknown\":1}", DataBagUnknownNamePolicy.Fail, out int updated, out int skipped, out string message));
            Assert.Equal(0, updated); Assert.Equal(0, skipped); Assert.NotNull(message);
            Assert.True(bag.TryGetDecimal("Total", out _, out decimal total, out _)); Assert.Equal(0m, total);
            Assert.True(bag.SetFromJsonObject("{\"Total\":12.5,\"Unknown\":1}", DataBagUnknownNamePolicy.Ignore, out updated, out skipped, out _));
            Assert.Equal(1, updated); Assert.Equal(1, skipped);
        }

        [Fact]
        public void InferredJsonLoadCreatesExpectedTypes()
        {
            using var bag = new DataBagAutomation.DataBagUtils();
            Assert.True(bag.BeginInitialization(true, out _));
            Assert.True(bag.LoadJsonObject("{\"s\":\"x\",\"i\":1,\"l\":2147483648,\"b\":true,\"j\":[1],\"n\":null}", DataBagConflictPolicy.Fail, out int added, out _, out _, out _)); Assert.Equal(6, added);
            Assert.True(bag.CompleteInitialization(out _, out _));
            Assert.True(bag.TryGetValue("j", out _, out DataBagValueType type, out string value, out _)); Assert.Equal(DataBagValueType.Json, type); Assert.Equal("[1]", value);
            Assert.True(bag.TryGetValue("n", out _, out type, out value, out _)); Assert.Equal(DataBagValueType.Null, type); Assert.Null(value);
        }

        [Fact]
        public void DefinitionTableAndBusinessRowAreSupported()
        {
            var definitions = new DataTable(); definitions.Columns.Add("Name"); definitions.Columns.Add("Type"); definitions.Columns.Add("Value");
            definitions.Rows.Add("Code", "String", "initial"); definitions.Rows.Add("Amount", "Decimal", "0");
            using var bag = new DataBagAutomation.DataBagUtils(); Assert.True(bag.BeginInitialization(true, out _));
            Assert.True(bag.LoadDataTable(definitions, DataBagConflictPolicy.Fail, out int added, out _, out _, out _)); Assert.Equal(2, added);
            Assert.True(bag.CompleteInitialization(out _, out _));
            var values = new DataTable(); values.Columns.Add("Code", typeof(string)); values.Columns.Add("Amount", typeof(decimal)); values.Rows.Add("A7", 42.25m);
            Assert.True(bag.SetFromDataRow(values, 0, DataBagUnknownNamePolicy.Fail, out int updated, out int skipped, out _)); Assert.Equal(2, updated); Assert.Equal(0, skipped);
            Assert.True(bag.TryGetString("Code", out _, out string code, out _)); Assert.Equal("A7", code);
        }

        [Fact]
        public void BulkInitializationFailureDoesNotPartiallyMutateStaging()
        {
            using var bag = new DataBagAutomation.DataBagUtils { MaximumItems = 1 };
            Assert.True(bag.BeginInitialization(true, out _));
            Assert.False(bag.LoadJsonObject("{\"a\":1,\"b\":2}", DataBagConflictPolicy.Fail, out int added, out _, out _, out string message)); Assert.Equal(0, added); Assert.NotNull(message);
            Assert.True(bag.CompleteInitialization(out int count, out _)); Assert.Equal(0, count);
        }

        [Fact]
        public void ReadOnlyWriteOnceRequiredResetAndRedactionAreEnforced()
        {
            using var bag = ReadyBag();
            Assert.False(bag.SetString("Source", "Other", out string message)); Assert.Contains("read-only", message);
            Assert.True(bag.SetString("Confirmation", "ABC", out _)); Assert.False(bag.SetString("Confirmation", "DEF", out message)); Assert.Contains("write-once", message);
            Assert.True(bag.ValidateRequiredValuesPresent(out bool ready, out int missing, out string names, out _)); Assert.False(ready); Assert.Equal(1, missing); Assert.Contains("CustomerId", names);
            Assert.True(bag.SetString("CustomerId", "C1", out _)); Assert.True(bag.ValidateRequiredValuesPresent(out ready, out missing, out _, out _)); Assert.True(ready); Assert.Equal(0, missing);
            Assert.True(bag.GetSnapshotJson(out string snapshot, out _)); Assert.DoesNotContain("abc", snapshot); Assert.Contains("***", snapshot);
            Assert.True(bag.ResetValues(out int reset, out _)); Assert.Equal(6, reset); Assert.True(bag.SetString("Confirmation", "XYZ", out _));
        }

        [Fact]
        public async Task ConcurrentUpdatesRemainValid()
        {
            using var bag = new DataBagAutomation.DataBagUtils(); Assert.True(bag.BeginInitialization(true, out _));
            for (int i = 0; i < 100; i++) Assert.True(bag.SetTypedValue("v" + i, DataBagValueType.Int32, "0", out _));
            Assert.True(bag.CompleteInitialization(out _, out _));
            await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => Assert.True(bag.SetInt32("v" + i, i, out _)))));
            for (int i = 0; i < 100; i++) { Assert.True(bag.TryGetInt32("v" + i, out _, out int value, out _)); Assert.Equal(i, value); }
        }

        [Fact]
        public void DisposalClearsAndMethodsFailActionably()
        {
            var bag = ReadyBag(); bag.Dispose();
            Assert.False(bag.GetState(out DataBagState state, out int count, out string message)); Assert.Equal(DataBagState.Disposed, state); Assert.Equal(0, count); Assert.Contains("disposed", message, StringComparison.OrdinalIgnoreCase);
            Assert.False(bag.TryGetString("CustomerId", out bool found, out string value, out message)); Assert.False(found); Assert.Null(value); Assert.Contains("disposed", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PublicAutomationMethodNamesAreUniqueAndOutputsComeLast()
        {
            MethodInfo[] methods = typeof(DataBagAutomation.DataBagUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).ToArray();
            Assert.Equal(methods.Length, methods.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count());
            foreach (MethodInfo method in methods) { Assert.Equal(typeof(bool), method.ReturnType); ParameterInfo[] p = method.GetParameters(); int firstOut = Array.FindIndex(p, x => x.IsOut); if (firstOut >= 0) Assert.All(p.Skip(firstOut), x => Assert.True(x.IsOut)); }
        }

        private static DataBagAutomation.DataBagUtils ReadyBag()
        {
            var bag = new DataBagAutomation.DataBagUtils { InitializationSource = DataBagInitializationSource.DesignTimeJson, InitialItemsJson = Definitions };
            Assert.True(bag.Initialize(out _, out _)); return bag;
        }
    }
}
