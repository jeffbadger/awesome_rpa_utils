using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace TextExtractAutomation.Tests
{
    /// <summary>The public boundary: every method's failure sentinels, the message contract, disposal and the never-throws guard.</summary>
    public sealed class PublicBoundaryTests
    {
        private static object Poison(Type t) => t == typeof(string) ? "POISON" : t == typeof(bool) ? (object)true : t.IsEnum ? Enum.GetValues(t).GetValue(1) : 99;

        private static (bool result, string message, Dictionary<string, object> outs) Invoke(TextExtractUtils component, MethodInfo m, Func<Type, object> input)
        {
            ParameterInfo[] ps = m.GetParameters();
            object[] args = ps.Select(p => p.IsOut ? Poison(p.ParameterType.GetElementType()) : input(p.ParameterType)).ToArray();   // poisoned outputs catch a method that forgets one
            bool result = (bool)m.Invoke(component, args);
            var outs = ps.Select((p, i) => (p, i)).Where(x => x.p.IsOut).ToDictionary(x => x.p.Name, x => args[x.i]);
            return (result, (string)outs["message"], outs);
        }

        private static object Valid(Type t) => t == typeof(string) ? "x" : t == typeof(int) ? 1 : t == typeof(bool) ? true : Activator.CreateInstance(t);

        private static object Bad(Type t) => t == typeof(string) ? null : t == typeof(int) ? int.MinValue : t == typeof(bool) ? false : Enum.ToObject(t, 77);

        private static void AssertSentinels(MethodInfo m, Dictionary<string, object> outs)
        {
            foreach (ParameterInfo p in m.GetParameters().Where(p => p.IsOut && p.Name != "message"))
            {
                Type t = p.ParameterType.GetElementType();
                object expected = t == typeof(string) ? null : t == typeof(bool) ? (object)false : 0;
                Assert.True(Equals(expected, outs[p.Name]), m.Name + " left '" + p.Name + "' as " + (outs[p.Name] ?? "null") + " instead of its failure sentinel");
            }
        }

        private static readonly string[] SucceedsOnAnyInput = { "ClearTemplate", "GetTemplateJson" };

        [Fact]
        public void EveryMethod_OnBadInput_FailsWithSentinels_AndNamesItself()
        {
            using var component = new TextExtractUtils();
            foreach (MethodInfo m in ConventionTests.PublicMethods().Where(m => !SucceedsOnAnyInput.Contains(m.Name)))
            {
                var (result, message, outs) = Invoke(component, m, Bad);
                Assert.False(result, m.Name + " should fail on null or out-of-range input");
                Assert.False(string.IsNullOrWhiteSpace(message), m.Name + " must explain the failure");
                Assert.Contains(m.Name, message);
                AssertSentinels(m, outs);
            }
        }

        [Fact]
        public void EveryMethod_AfterDisposal_FailsCleanly_WithADisposedMessage()
        {
            var component = new TextExtractUtils();
            component.Dispose();
            component.Dispose();                                                   // idempotent
            foreach (MethodInfo m in ConventionTests.PublicMethods())
            {
                var (result, message, outs) = Invoke(component, m, Valid);
                Assert.False(result, m.Name);
                Assert.Contains("disposed", message);
                AssertSentinels(m, outs);
            }
        }

        [Fact]
        public void NoMethodThrows_ForNullAndOutOfRangeInputs()
        {
            using var component = new TextExtractUtils();
            foreach (MethodInfo m in ConventionTests.PublicMethods())
                Assert.Null(Record.Exception(() => Invoke(component, m, Bad)));
        }

        [Fact]
        public void TheContainerConstructor_AttachesTheComponent_ToleratesNull_AndDisposalFlowsThrough()
        {
            var container = new Container();
            var attached = new TextExtractUtils(container);
            Assert.Contains(attached, container.Components.Cast<IComponent>());
            using var detached = new TextExtractUtils((IContainer)null);
            container.Dispose();
            Assert.False(attached.GetTemplateJson(out _, out string message));
            Assert.Contains("disposed", message);
        }

        [Fact]
        public void TheGuard_NamesOnlyTheOperationAndType_AndDoesNotSwallowFatalExceptions()
        {
            string text = NeverThrowsGuard.Failure("ExtractFromText", new InvalidOperationException("secret payload 4111-1111"));
            Assert.DoesNotContain("secret", text);
            Assert.DoesNotContain("4111", text);
            Assert.Contains("ExtractFromText", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.False(NeverThrowsGuard.IsRecoverable(new OutOfMemoryException()));
            Assert.True(NeverThrowsGuard.IsRecoverable(new InvalidOperationException()));
        }

        [Fact]
        public void TheRunAndResultMethods_ReportThatTheyAreNotImplementedYet()
        {
            using var component = new TextExtractUtils();
            string[] stubbed = { "ExtractFromText", "GetField", "GetResultJson", "ResetFieldCursor", "TryReadNextField", "ClearResults" };
            foreach (MethodInfo m in ConventionTests.PublicMethods().Where(m => stubbed.Contains(m.Name)))
            {
                var (result, message, outs) = Invoke(component, m, Valid);
                Assert.False(result);
                Assert.Equal(m.Name + " is not implemented yet.", message);
                AssertSentinels(m, outs);
            }
        }
    }
}
