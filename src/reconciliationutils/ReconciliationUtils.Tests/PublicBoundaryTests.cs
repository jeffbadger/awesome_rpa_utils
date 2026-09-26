using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>
    /// The public boundary, before any behavior exists: every method's failure sentinels, the message contract,
    /// and disposal. These hold for every Release 1 method now and must keep holding as they are implemented.
    /// </summary>
    public sealed class PublicBoundaryTests
    {
        private static object[] DefaultArguments(MethodInfo m) =>
            m.GetParameters().Select(p =>
            {
                Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                if (p.IsOut) return t.IsValueType ? Activator.CreateInstance(t) : null;
                if (t == typeof(string)) return "x";
                if (t == typeof(int)) return 1;
                if (t == typeof(bool)) return true;   // deliberately not the sentinel value, so an untouched output would be visible
                return Activator.CreateInstance(t);
            }).ToArray();

        /// <summary>Calls the method with junk-but-valid-typed inputs; returns (result, message, outputs by name).</summary>
        private static (bool result, string message, System.Collections.Generic.Dictionary<string, object> outs) Call(ReconciliationUtils component, MethodInfo m)
        {
            object[] args = DefaultArguments(m);
            // Poison the outputs so a method that forgets to set one is caught.
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].IsOut)
                {
                    Type t = ps[i].ParameterType.GetElementType();
                    args[i] = t == typeof(string) ? "POISON" : t == typeof(bool) ? (object)true : (object)99;
                }
            bool result = (bool)m.Invoke(component, args);
            var outs = ps.Select((p, i) => (p, i)).Where(x => x.p.IsOut).ToDictionary(x => x.p.Name, x => args[x.i]);
            return (result, (string)outs["message"], outs);
        }

        private static void AssertSentinels(MethodInfo m, System.Collections.Generic.Dictionary<string, object> outs)
        {
            foreach (ParameterInfo p in m.GetParameters().Where(p => p.IsOut && p.Name != "message"))
            {
                Type t = p.ParameterType.GetElementType();
                object expected = t == typeof(string) ? null
                    : t == typeof(bool) ? (object)false
                    : p.Name.EndsWith("RowIndex") ? -1 : 0;
                object actual = outs[p.Name];
                Assert.True(Equals(expected, actual), m.Name + " left output '" + p.Name + "' as " + (actual ?? "null") + " instead of its failure sentinel " + (expected ?? "null"));
            }
        }

        /// <summary>Methods that succeed on a live component even when given junk (they take no meaningful input).</summary>
        private static readonly string[] SucceedsOnAnyInput = { "ClearDefinition", "GetDefinitionJson", "ClearResults" };

        private static object[] BadArguments(MethodInfo m) =>
            m.GetParameters().Select(p =>
            {
                Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                if (p.IsOut) return t.IsValueType ? Activator.CreateInstance(t) : null;
                if (t == typeof(string)) return null;
                if (t == typeof(int)) return int.MinValue;
                if (t == typeof(bool)) return false;
                return Activator.CreateInstance(t);
            }).ToArray();

        [Fact]
        public void EveryMethod_OnFailure_SetsEveryOutputToItsSentinel_AndNamesItselfInTheMessage()
        {
            using var component = new ReconciliationUtils();
            foreach (MethodInfo m in ConventionTests.PublicMethods().Where(m => !SucceedsOnAnyInput.Contains(m.Name)))
            {
                object[] args = BadArguments(m);
                ParameterInfo[] ps = m.GetParameters();
                for (int i = 0; i < ps.Length; i++)   // poison the outputs so a method that forgets to set one is caught
                    if (ps[i].IsOut)
                    {
                        Type t = ps[i].ParameterType.GetElementType();
                        args[i] = t == typeof(string) ? "POISON" : t == typeof(bool) ? (object)true : (object)99;
                    }
                bool result = (bool)m.Invoke(component, args);
                var outs = ps.Select((p, i) => (p, i)).Where(x => x.p.IsOut).ToDictionary(x => x.p.Name, x => args[x.i]);
                string message = (string)outs["message"];
                Assert.False(result, m.Name + " should fail on null/out-of-range input");
                Assert.False(string.IsNullOrWhiteSpace(message), m.Name + " must explain the failure");
                Assert.Contains(m.Name, message);
                AssertSentinels(m, outs);
            }
        }

        [Fact]
        public void EveryMethod_AfterDisposal_FailsCleanly_WithADisposedMessage()
        {
            var component = new ReconciliationUtils();
            component.Dispose();
            foreach (MethodInfo m in ConventionTests.PublicMethods())
            {
                var (result, message, outs) = Call(component, m);
                Assert.False(result);
                Assert.Contains("disposed", message);
                AssertSentinels(m, outs);
            }
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var component = new ReconciliationUtils();
            component.Dispose();
            component.Dispose();
        }

        [Fact]
        public void TheContainerConstructor_AttachesTheComponent_AndToleratesNull()
        {
            using var container = new Container();
            using var attached = new ReconciliationUtils(container);
            Assert.Contains(attached, container.Components.Cast<IComponent>());
            using var detached = new ReconciliationUtils((IContainer)null);
        }

        [Fact]
        public void ADisposedContainer_DisposesItsComponent()
        {
            var container = new Container();
            var attached = new ReconciliationUtils(container);
            container.Dispose();
            Assert.False(attached.ClearResults(out string message));
            Assert.Contains("disposed", message);
        }

        [Fact]
        public void NoMethodThrows_EvenForNullAndOutOfRangeInputs()
        {
            using var component = new ReconciliationUtils();
            foreach (MethodInfo m in ConventionTests.PublicMethods())
            {
                object[] args = m.GetParameters().Select(p =>
                {
                    Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    if (t == typeof(string)) return null;
                    if (t == typeof(int)) return int.MinValue;
                    if (t == typeof(bool)) return false;
                    return Activator.CreateInstance(t);
                }).ToArray();
                var ex = Record.Exception(() => m.Invoke(component, args));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void TheFailureMessage_NeverEchoesExceptionText()
        {
            // The shared guard's raw ex.Message can carry source data; this component's guard must name only the operation and type.
            string text = NeverThrowsGuard.Failure("ReconcileJson", new InvalidOperationException("secret payload 4111-1111"));
            Assert.DoesNotContain("secret", text);
            Assert.DoesNotContain("4111", text);
            Assert.Contains("ReconcileJson", text);
            Assert.Contains("InvalidOperationException", text);
        }

        [Fact]
        public void AnUnrecoverableException_IsNotSwallowedAsAFailure()
        {
            Assert.False(NeverThrowsGuard.IsRecoverable(new OutOfMemoryException()));
            Assert.True(NeverThrowsGuard.IsRecoverable(new InvalidOperationException()));
        }
    }
}
