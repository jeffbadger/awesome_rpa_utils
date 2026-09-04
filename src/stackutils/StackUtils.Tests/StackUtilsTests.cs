using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using StackAutomation;
using Xunit;

namespace StackUtils.Tests
{
    public sealed class StackUtilsTests
    {
        [Fact]
        public void MixedItemsAreStrictLifoAndPeekDoesNotRemove()
        {
            using var stack = new StackAutomation.StackUtils();
            string file = Path.GetTempFileName();
            try
            {
                Assert.True(stack.PushText("first", out _));
                Assert.True(stack.PushJson(" { \"n\": 1 } ", out _));
                Assert.True(stack.PushFileReference(file, out string full, out _));
                Assert.Equal(Path.GetFullPath(file), full);

                Assert.True(stack.TryPeek(out bool available, out StackItemKind kind, out string value, out string message));
                Assert.True(available); Assert.Equal(StackItemKind.FileReference, kind); Assert.Equal(full, value); Assert.Null(message);
                Assert.True(stack.GetCount(out int count, out _)); Assert.Equal(3, count);

                AssertPop(stack, StackItemKind.FileReference, full);
                AssertPop(stack, StackItemKind.Json, " { \"n\": 1 } ");
                AssertPop(stack, StackItemKind.Text, "first");
                Assert.True(stack.TryPop(out available, out kind, out value, out message));
                Assert.False(available); Assert.Equal(default, kind); Assert.Null(value); Assert.Null(message);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void CapacityChangesAndClearBehaveAsDocumented()
        {
            using var stack = new StackAutomation.StackUtils();
            Assert.Equal(10000, stack.MaximumItems);
            stack.MaximumItems = 3;
            Assert.Equal(3, stack.MaximumItems);
            Assert.True(stack.GetMaximumItems(out int maximum, out _)); Assert.Equal(3, maximum);
            Assert.Throws<ArgumentOutOfRangeException>(() => stack.MaximumItems = 0);
            Assert.False(stack.SetMaximumItems(0, out string message)); Assert.NotNull(message);
            Assert.False(stack.SetMaximumItems(1000001, out message)); Assert.NotNull(message);
            Assert.True(stack.PushLines("a\nb", out int pushed, out _)); Assert.Equal(2, pushed);
            Assert.Throws<InvalidOperationException>(() => stack.MaximumItems = 1);
            Assert.False(stack.SetMaximumItems(1, out message)); Assert.NotNull(message);
            Assert.True(stack.SetMaximumItems(2, out _));
            Assert.False(stack.PushText("c", out message)); Assert.NotNull(message);
            Assert.True(stack.Clear(out int removed, out _)); Assert.Equal(2, removed);
            Assert.True(stack.SetMaximumItems(1, out _));
        }

        [Fact]
        public void JsonValuesRoundTripAndArrayUsesStackOrder()
        {
            using var stack = new StackAutomation.StackUtils();
            Assert.True(stack.PushJsonArray("[\"A\", {\"b\":2}, null, true, 4]", out int pushed, out _));
            Assert.Equal(5, pushed);
            AssertPop(stack, StackItemKind.Json, "4");
            AssertPop(stack, StackItemKind.Json, "true");
            AssertPop(stack, StackItemKind.Json, "null");
            AssertPop(stack, StackItemKind.Json, "{\"b\":2}");
            AssertPop(stack, StackItemKind.Json, "\"A\"");
            Assert.True(stack.PushJson("[ 1, 2 ]", out _));
            AssertPop(stack, StackItemKind.Json, "[ 1, 2 ]");
        }

        [Fact]
        public void InvalidJsonAndFailedBulkPushAreAtomic()
        {
            using var stack = new StackAutomation.StackUtils();
            Assert.True(stack.SetMaximumItems(2, out _));
            Assert.True(stack.PushText("existing", out _));
            Assert.False(stack.PushJson("not json", out string message)); Assert.NotNull(message);
            Assert.False(stack.PushJsonArray("[1,", out int pushed, out message)); Assert.Equal(0, pushed); Assert.NotNull(message);
            Assert.False(stack.PushJsonArray("[1,2]", out pushed, out message)); Assert.Equal(0, pushed); Assert.NotNull(message);
            Assert.True(stack.GetCount(out int count, out _)); Assert.Equal(1, count);
            AssertPop(stack, StackItemKind.Text, "existing");
        }

        [Fact]
        public void LinesIgnoreOnlyEmptyLinesAndPreserveContent()
        {
            using var stack = new StackAutomation.StackUtils();
            Assert.True(stack.PushLines("one\r\n\r\n  \rthree\n", out int pushed, out _));
            Assert.Equal(3, pushed);
            AssertPop(stack, StackItemKind.Text, "three");
            AssertPop(stack, StackItemKind.Text, "  ");
            AssertPop(stack, StackItemKind.Text, "one");
            Assert.False(stack.PushLines(null, out pushed, out string message)); Assert.Equal(0, pushed); Assert.NotNull(message);
        }

        [Fact]
        public void FileBulkPushIsNormalizedSortedAndNonRecursiveWhenRequested()
        {
            string directory = Path.Combine(Path.GetTempPath(), "stack-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "child"));
            try
            {
                File.WriteAllText(Path.Combine(directory, "b.txt"), "b");
                File.WriteAllText(Path.Combine(directory, "a.txt"), "a");
                File.WriteAllText(Path.Combine(directory, "child", "c.txt"), "c");
                using var stack = new StackAutomation.StackUtils();
                Assert.True(stack.PushFileReferences(directory, "*.txt", false, out int pushed, out _)); Assert.Equal(2, pushed);
                AssertPop(stack, StackItemKind.FileReference, Path.GetFullPath(Path.Combine(directory, "b.txt")));
                AssertPop(stack, StackItemKind.FileReference, Path.GetFullPath(Path.Combine(directory, "a.txt")));
                Assert.True(stack.PushFileReferences(directory, "*.txt", true, out pushed, out _)); Assert.Equal(3, pushed);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Fact]
        public void FileFailuresDoNotModifyStack()
        {
            using var stack = new StackAutomation.StackUtils();
            Assert.False(stack.PushFileReference("missing-" + Guid.NewGuid(), out string path, out string message)); Assert.Null(path); Assert.NotNull(message);
            Assert.False(stack.PushFileReferences("missing-" + Guid.NewGuid(), "*", false, out int pushed, out message)); Assert.Equal(0, pushed); Assert.NotNull(message);
            Assert.False(stack.PushFileReferences(Path.GetTempPath(), "", false, out pushed, out message)); Assert.Equal(0, pushed); Assert.NotNull(message);
            Assert.False(stack.PushFileReferences(Path.GetTempPath(), "bad\0pattern", false, out pushed, out message)); Assert.Equal(0, pushed); Assert.NotNull(message);
            Assert.True(stack.GetCount(out int count, out _)); Assert.Equal(0, count);
        }

        [Fact]
        public void SnapshotIsNextToPopOrderWithKindAndValue()
        {
            using var stack = new StackAutomation.StackUtils();
            Assert.True(stack.PushText("A", out _)); Assert.True(stack.PushJson("2", out _));
            Assert.True(stack.GetSnapshotJson(out string json, out _));
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement[] values = document.RootElement.EnumerateArray().ToArray();
            Assert.Equal("Json", values[0].GetProperty("kind").GetString()); Assert.Equal("2", values[0].GetProperty("value").GetString());
            Assert.Equal("Text", values[1].GetProperty("kind").GetString()); Assert.Equal("A", values[1].GetProperty("value").GetString());
        }

        [Fact]
        public async Task ConcurrentPushesAndPopsPreserveEveryItemExactlyOnce()
        {
            using var stack = new StackAutomation.StackUtils();
            Assert.True(stack.SetMaximumItems(2000, out _));
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() => Assert.True(stack.PushText(i.ToString(), out _)))));
            var popped = new ConcurrentBag<string>();
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(ignored => Task.Run(() =>
            {
                Assert.True(stack.TryPop(out bool available, out _, out string value, out _));
                Assert.True(available); popped.Add(value);
            })));
            Assert.Equal(1000, popped.Distinct().Count());
            Assert.True(stack.GetCount(out int count, out _)); Assert.Equal(0, count);
        }

        [Fact]
        public void DisposeClearsAndAllCallsFailActionably()
        {
            var stack = new StackAutomation.StackUtils();
            Assert.True(stack.PushText("secret", out _));
            stack.Dispose();
            Assert.False(stack.GetCount(out int count, out string message)); Assert.Equal(0, count); Assert.Contains("disposed", message, StringComparison.OrdinalIgnoreCase);
            Assert.False(stack.TryPop(out bool available, out StackItemKind kind, out string value, out message));
            Assert.False(available); Assert.Equal(default, kind); Assert.Null(value); Assert.Contains("disposed", message, StringComparison.OrdinalIgnoreCase);
            Assert.False(stack.PushText("x", out message)); Assert.Contains("disposed", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PublicMethodNamesAreUniqueAndOutputsAreLast()
        {
            MethodInfo[] methods = typeof(StackAutomation.StackUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName).ToArray();
            Assert.Equal(methods.Length, methods.Select(method => method.Name).Distinct(StringComparer.Ordinal).Count());
            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                int firstOutput = Array.FindIndex(parameters, parameter => parameter.IsOut);
                if (firstOutput >= 0) Assert.All(parameters.Skip(firstOutput), parameter => Assert.True(parameter.IsOut));
                Assert.Equal(typeof(bool), method.ReturnType);
            }
        }

        private static void AssertPop(StackAutomation.StackUtils stack, StackItemKind expectedKind, string expectedValue)
        {
            Assert.True(stack.TryPop(out bool available, out StackItemKind kind, out string value, out string message));
            Assert.True(available); Assert.Equal(expectedKind, kind); Assert.Equal(expectedValue, value); Assert.Null(message);
        }
    }
}
