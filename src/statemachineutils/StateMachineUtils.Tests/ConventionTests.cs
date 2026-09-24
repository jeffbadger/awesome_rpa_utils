using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace StateMachineAutomation.Tests
{
    /// <summary>
    /// Enforces the suite-wide public-surface rules mechanically, so a later addition cannot quietly
    /// break them: never-throws shape, designer attributes, and signature uniqueness.
    /// </summary>
    public sealed class ConventionTests
    {
        private static MethodInfo[] PublicMethods() =>
            typeof(StateMachineUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName) // property accessors and event add/remove
                .ToArray();

        [Fact]
        public void EveryPublicMethod_HasCategoryAndDescription()
        {
            foreach (MethodInfo m in PublicMethods())
            {
                Assert.True(m.GetCustomAttribute<CategoryAttribute>() != null, m.Name + " is missing [Category]");
                Assert.True(!string.IsNullOrWhiteSpace(m.GetCustomAttribute<DescriptionAttribute>()?.Description), m.Name + " is missing [Description]");
            }
        }

        [Fact]
        public void EveryPublicPropertyAndEvent_HasCategoryAndDescription()
        {
            foreach (PropertyInfo p in typeof(StateMachineUtils).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.True(p.GetCustomAttribute<CategoryAttribute>() != null, p.Name + " is missing [Category]");
                Assert.True(p.GetCustomAttribute<DescriptionAttribute>() != null, p.Name + " is missing [Description]");
            }
            foreach (EventInfo e in typeof(StateMachineUtils).GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.True(e.GetCustomAttribute<CategoryAttribute>() != null, e.Name + " is missing [Category]");
                Assert.True(e.GetCustomAttribute<DescriptionAttribute>() != null, e.Name + " is missing [Description]");
            }
        }

        [Fact]
        public void EveryPublicMethod_ReturnsBoolAndHasAnOutStringMessage()
        {
            foreach (MethodInfo m in PublicMethods())
            {
                Assert.True(m.ReturnType == typeof(bool), m.Name + " must return bool (the never-throws contract)");
                ParameterInfo message = m.GetParameters().FirstOrDefault(p => p.Name == "message");
                Assert.True(message != null && message.IsOut && message.ParameterType == typeof(string).MakeByRefType(), m.Name + " must have 'out string message'");
            }
        }

        [Fact]
        public void NoTwoPublicMethods_ShareANameAndTheSameNonOutParameterTypes()
        {
            // Robot Studio resolves methods by name plus the ordered non-out parameter types and ignores
            // out parameters, so two such overloads would be indistinguishable in the designer.
            var collisions = PublicMethods()
                .GroupBy(m => m.Name + "(" + string.Join(",", m.GetParameters().Where(p => !p.IsOut && !p.ParameterType.IsByRef).Select(p => p.ParameterType.Name)) + ")")
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.True(collisions.Count == 0, "Signature collisions: " + string.Join(", ", collisions));
        }

        [Fact]
        public void NoPublicMemberIsGeneric_BecauseTheRobotStudioDesignerCannotBindOne()
        {
            Assert.DoesNotContain(PublicMethods(), m => m.IsGenericMethod);
        }

        [Fact]
        public void EveryPublicMethod_ParametersAreDesignerFriendly()
        {
            foreach (MethodInfo m in PublicMethods())
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    Assert.True(t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(double),
                        m.Name + "." + p.Name + " is " + t.Name + "; only string/bool/int/double are wireable without a proxy object");
                }
        }

        [Fact]
        public void EveryPublicClassInTheAssembly_IsEitherTheComponentOrAnEventArgs()
        {
            foreach (Type t in typeof(StateMachineUtils).Assembly.GetExportedTypes())
                Assert.True(t == typeof(StateMachineUtils) || typeof(EventArgs).IsAssignableFrom(t), t.Name + " is public but is neither the component nor an EventArgs");
        }
    }
}
