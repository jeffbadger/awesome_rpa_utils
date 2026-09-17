using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using DataContractAutomation;
using Xunit;

namespace DataContractUtils.Tests
{
    public sealed class DataContractUtilsTests
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
            using var contract = new DataContractAutomation.DataContractUtils { InitializationSource = DataContractInitializationSource.DesignTimeJson, InitialItemsJson = Definitions };
            Assert.True(contract.Initialize(out int count, out string message)); Assert.Equal(7, count); Assert.Null(message);
            Assert.True(contract.GetState(out DataContractState state, out int stateCount, out _)); Assert.Equal(DataContractState.Ready, state); Assert.Equal(7, stateCount);
            Assert.True(contract.TryGetDecimal("total", out bool found, out decimal total, out _)); Assert.True(found); Assert.Equal(0m, total);
            Assert.False(contract.SetTypedValue("NewName", DataContractValueType.String, "x", out message)); Assert.NotNull(message);
        }

        [Fact]
        public void JsonFileInitializesRelativeToBaseDirectoryAndMissingFileFailsAtomically()
        {
            string name = "datacontract-" + Guid.NewGuid().ToString("N") + ".json";
            string path = Path.Combine(AppContext.BaseDirectory, name);
            File.WriteAllText(path, Definitions);
            try
            {
                using var contract = new DataContractAutomation.DataContractUtils { InitializationSource = DataContractInitializationSource.JsonFile, InitialItemsFilePath = name };
                Assert.True(contract.Initialize(out int count, out _)); Assert.Equal(7, count);
            }
            finally { File.Delete(path); }

            using var missing = new DataContractAutomation.DataContractUtils { InitializationSource = DataContractInitializationSource.JsonFile, InitialItemsFilePath = name };
            Assert.False(missing.Initialize(out int loaded, out string message)); Assert.Equal(0, loaded); Assert.NotNull(message);
            Assert.True(missing.GetState(out DataContractState state, out int countAfter, out _)); Assert.Equal(DataContractState.NotInitialized, state); Assert.Equal(0, countAfter);
        }

        [Fact]
        public void TemplateActionPopulatesEditableDefinitionJson()
        {
            using var contract = new DataContractAutomation.DataContractUtils();
            Assert.True(contract.PopulateInitialItemsJsonTemplate(out string message)); Assert.Null(message);
            Assert.Equal(DataContractInitializationSource.DesignTimeJson, contract.InitializationSource);
            Assert.Contains("mustHaveValue", contract.InitialItemsJson);
            Assert.False(contract.Initialize(out int loaded, out message)); Assert.Equal(0, loaded); Assert.Contains("name", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ProgrammaticInitializationPublishesAtomicallyAndCanBeCancelled()
        {
            using var contract = new DataContractAutomation.DataContractUtils();
            Assert.True(contract.BeginInitialization(true, out _));
            Assert.True(contract.SetTypedValue("Count", DataContractValueType.Int32, "3", out _));
            Assert.True(contract.CompleteInitialization(out int count, out _)); Assert.Equal(1, count);
            Assert.True(contract.BeginInitialization(false, out _));
            Assert.True(contract.SetTypedValue("Extra", DataContractValueType.String, "x", out _));
            Assert.True(contract.CancelInitialization(out int discarded, out _)); Assert.Equal(2, discarded);
            Assert.True(contract.Contains("Count", out bool exists, out _)); Assert.True(exists);
            Assert.True(contract.Contains("Extra", out exists, out _)); Assert.False(exists);
        }

        [Fact]
        public void UnsealedDesignTimeInitializationRemainsStagedUntilCompleted()
        {
            using var contract = new DataContractAutomation.DataContractUtils
            {
                InitializationSource = DataContractInitializationSource.DesignTimeJson,
                InitialItemsJson = Definitions,
                SealAfterInitialization = false
            };
            Assert.True(contract.Initialize(out int loaded, out _)); Assert.Equal(7, loaded);
            Assert.True(contract.GetState(out DataContractState state, out int activeCount, out _)); Assert.Equal(DataContractState.Initializing, state); Assert.Equal(0, activeCount);
            Assert.True(contract.CompleteInitialization(out int completed, out _)); Assert.Equal(7, completed);
        }

        [Fact]
        public void GenericAndNativeSettersRequireExistingMatchingDefinitions()
        {
            using var contract = ReadyContract();
            Assert.True(contract.SetValue("Total", "1234.50", out _));
            Assert.True(contract.TryGetDecimal("Total", out bool found, out decimal value, out _)); Assert.True(found); Assert.Equal(1234.50m, value);
            Assert.True(contract.SetBoolean("Enabled", true, out _));
            Assert.False(contract.SetString("Enabled", "true", out string message)); Assert.Contains("Boolean", message);
            Assert.False(contract.SetValue("Unknown", "x", out message)); Assert.NotNull(message);
            Assert.False(contract.SetValue("Total", "not-a-number", out message)); Assert.NotNull(message);
        }

        [Fact]
        public void JsonIsValidatedAndRawValueRoundTrips()
        {
            using var contract = ReadyContract();
            Assert.True(contract.SetJson("Payload", " { \"a\": 1 } ", out _));
            Assert.True(contract.TryGetJson("Payload", out bool found, out string json, out _)); Assert.True(found); Assert.Equal(" { \"a\": 1 } ", json);
            Assert.False(contract.SetJson("Payload", "bad", out string message)); Assert.NotNull(message);
            Assert.True(contract.TryGetJson("Payload", out found, out json, out _)); Assert.Equal(" { \"a\": 1 } ", json);
        }

        [Fact]
        public void RuntimeJsonObjectUpdateIsAtomicAndHandlesUnknownNames()
        {
            using var contract = ReadyContract();
            Assert.False(contract.SetFromJsonObject("{\"Total\":12.5,\"Unknown\":1}", DataContractUnknownNamePolicy.Fail, out int updated, out int skipped, out string message));
            Assert.Equal(0, updated); Assert.Equal(0, skipped); Assert.NotNull(message);
            Assert.True(contract.TryGetDecimal("Total", out _, out decimal total, out _)); Assert.Equal(0m, total);
            Assert.True(contract.SetFromJsonObject("{\"Total\":12.5,\"Unknown\":1}", DataContractUnknownNamePolicy.Ignore, out updated, out skipped, out _));
            Assert.Equal(1, updated); Assert.Equal(1, skipped);
        }

        [Fact]
        public void InferredJsonLoadCreatesExpectedTypes()
        {
            using var contract = new DataContractAutomation.DataContractUtils();
            Assert.True(contract.BeginInitialization(true, out _));
            Assert.True(contract.LoadJsonObject("{\"s\":\"x\",\"i\":1,\"l\":2147483648,\"b\":true,\"j\":[1],\"n\":null}", DataContractConflictPolicy.Fail, out int added, out _, out _, out _)); Assert.Equal(6, added);
            Assert.True(contract.CompleteInitialization(out _, out _));
            Assert.True(contract.TryGetValue("j", out _, out DataContractValueType type, out string value, out _)); Assert.Equal(DataContractValueType.Json, type); Assert.Equal("[1]", value);
            Assert.True(contract.TryGetValue("n", out _, out type, out value, out _)); Assert.Equal(DataContractValueType.Null, type); Assert.Null(value);
        }

        [Fact]
        public void DefinitionTableAndBusinessRowAreSupported()
        {
            var definitions = new DataTable(); definitions.Columns.Add("Name"); definitions.Columns.Add("Type"); definitions.Columns.Add("Value");
            definitions.Rows.Add("Code", "String", "initial"); definitions.Rows.Add("Amount", "Decimal", "0");
            using var contract = new DataContractAutomation.DataContractUtils(); Assert.True(contract.BeginInitialization(true, out _));
            Assert.True(contract.LoadDataTable(definitions, DataContractConflictPolicy.Fail, out int added, out _, out _, out _)); Assert.Equal(2, added);
            Assert.True(contract.CompleteInitialization(out _, out _));
            var values = new DataTable(); values.Columns.Add("Code", typeof(string)); values.Columns.Add("Amount", typeof(decimal)); values.Rows.Add("A7", 42.25m);
            Assert.True(contract.SetFromDataRow(values, 0, DataContractUnknownNamePolicy.Fail, out int updated, out int skipped, out _)); Assert.Equal(2, updated); Assert.Equal(0, skipped);
            Assert.True(contract.TryGetString("Code", out _, out string code, out _)); Assert.Equal("A7", code);
        }

        [Fact]
        public void BulkInitializationFailureDoesNotPartiallyMutateStaging()
        {
            using var contract = new DataContractAutomation.DataContractUtils { MaximumItems = 1 };
            Assert.True(contract.BeginInitialization(true, out _));
            Assert.False(contract.LoadJsonObject("{\"a\":1,\"b\":2}", DataContractConflictPolicy.Fail, out int added, out _, out _, out string message)); Assert.Equal(0, added); Assert.NotNull(message);
            Assert.True(contract.CompleteInitialization(out int count, out _)); Assert.Equal(0, count);
        }

        [Fact]
        public void ReadOnlyWriteOnceRequiredResetAndRedactionAreEnforced()
        {
            using var contract = ReadyContract();
            Assert.False(contract.SetString("Source", "Other", out string message)); Assert.Contains("read-only", message);
            Assert.True(contract.SetString("Confirmation", "ABC", out _)); Assert.False(contract.SetString("Confirmation", "DEF", out message)); Assert.Contains("write-once", message);
            Assert.True(contract.ValidateRequiredValuesPresent(out bool ready, out int missing, out string names, out _)); Assert.False(ready); Assert.Equal(1, missing); Assert.Contains("CustomerId", names);
            Assert.True(contract.SetString("CustomerId", "C1", out _)); Assert.True(contract.ValidateRequiredValuesPresent(out ready, out missing, out _, out _)); Assert.True(ready); Assert.Equal(0, missing);
            Assert.True(contract.GetSnapshotJson(out string snapshot, out _)); Assert.DoesNotContain("abc", snapshot); Assert.Contains("***", snapshot);
            Assert.True(contract.ResetValues(out int reset, out _)); Assert.Equal(6, reset); Assert.True(contract.SetString("Confirmation", "XYZ", out _));
        }

        [Fact]
        public async Task ConcurrentUpdatesRemainValid()
        {
            using var contract = new DataContractAutomation.DataContractUtils(); Assert.True(contract.BeginInitialization(true, out _));
            for (int i = 0; i < 100; i++) Assert.True(contract.SetTypedValue("v" + i, DataContractValueType.Int32, "0", out _));
            Assert.True(contract.CompleteInitialization(out _, out _));
            await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => Assert.True(contract.SetInt32("v" + i, i, out _)))));
            for (int i = 0; i < 100; i++) { Assert.True(contract.TryGetInt32("v" + i, out _, out int value, out _)); Assert.Equal(i, value); }
        }

        [Fact]
        public void DisposalClearsAndMethodsFailActionably()
        {
            var contract = ReadyContract(); contract.Dispose();
            Assert.False(contract.GetState(out DataContractState state, out int count, out string message)); Assert.Equal(DataContractState.Disposed, state); Assert.Equal(0, count); Assert.Contains("disposed", message, StringComparison.OrdinalIgnoreCase);
            Assert.False(contract.TryGetString("CustomerId", out bool found, out string value, out message)); Assert.False(found); Assert.Null(value); Assert.Contains("disposed", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PublicAutomationMethodNamesAreUniqueAndOutputsComeLast()
        {
            MethodInfo[] methods = typeof(DataContractAutomation.DataContractUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).ToArray();
            Assert.Equal(methods.Length, methods.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count());
            foreach (MethodInfo method in methods) { Assert.Equal(typeof(bool), method.ReturnType); ParameterInfo[] p = method.GetParameters(); int firstOut = Array.FindIndex(p, x => x.IsOut); if (firstOut >= 0) Assert.All(p.Skip(firstOut), x => Assert.True(x.IsOut)); }
        }

        private static DataContractAutomation.DataContractUtils ReadyContract()
        {
            var contract = new DataContractAutomation.DataContractUtils { InitializationSource = DataContractInitializationSource.DesignTimeJson, InitialItemsJson = Definitions };
            Assert.True(contract.Initialize(out _, out _)); return contract;
        }
    }
}
