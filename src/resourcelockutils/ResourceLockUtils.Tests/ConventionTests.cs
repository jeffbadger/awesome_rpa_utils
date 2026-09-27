using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ResourceLockAutomation.Tests
{
    /// <summary>Enforces the suite-wide public-surface rules mechanically: never-throws shape, designer attributes, signature uniqueness and the planned surface.</summary>
    public sealed class ConventionTests
    {
        /// <summary>Phase 1 of the plan (project-docs/plans/2026-09-27-resourcelockutils-design.md).</summary>
        private static readonly string[] PlannedMethods =
        {
            "TryAcquireLock", "AcquireLock", "TryAcquireSlot", "AcquireSlot",
            "RenewLock", "ReleaseLock",
            "GetLockStatus", "GetLocksJson",
            "ForceReleaseLock", "ConfigureLockFolder", "ValidateLockFolder"
        };

        internal static MethodInfo[] PublicMethods() =>
            typeof(ResourceLockUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).ToArray();

        [Fact]
        public void ThePublicSurface_IsExactlyThePlannedMethods()
        {
            // A deliberate tripwire: adding, removing or renaming a public method must be a conscious change to this list and to the plan.
            Assert.Equal(PlannedMethods.OrderBy(x => x, StringComparer.Ordinal), PublicMethods().Select(m => m.Name).OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public void EveryPublicMethod_HasCategoryAndDescription_AndTheClassHasADescription()
        {
            foreach (MethodInfo m in PublicMethods())
            {
                Assert.True(m.GetCustomAttribute<CategoryAttribute>() != null, m.Name + " is missing [Category]");
                string description = m.GetCustomAttribute<DescriptionAttribute>()?.Description;
                Assert.True(!string.IsNullOrWhiteSpace(description), m.Name + " is missing [Description]");
                Assert.EndsWith("Never throws.", description);
            }
            Assert.False(string.IsNullOrWhiteSpace(typeof(ResourceLockUtils).GetCustomAttribute<DescriptionAttribute>()?.Description));
        }

        [Fact]
        public void EveryPublicMethod_ReturnsBool_EndsWithOutStringMessage_AndTakesInputsBeforeOutputs()
        {
            foreach (MethodInfo m in PublicMethods())
            {
                Assert.True(m.ReturnType == typeof(bool), m.Name + " must return bool");
                ParameterInfo last = m.GetParameters().Last();
                Assert.True(last.Name == "message" && last.IsOut && last.ParameterType == typeof(string).MakeByRefType(), m.Name + " must end with 'out string message'");
                bool seenOut = false;
                foreach (ParameterInfo p in m.GetParameters())
                {
                    if (p.IsOut) seenOut = true;
                    else Assert.False(seenOut, m.Name + "." + p.Name + " is an input after an output");
                }
            }
        }

        [Fact]
        public void NoOverloads_NoGenerics_NoOptionalParameters()
        {
            Assert.Empty(PublicMethods().GroupBy(m => m.Name).Where(g => g.Count() > 1).Select(g => g.Key));
            Assert.DoesNotContain(PublicMethods(), m => m.IsGenericMethod);
            foreach (MethodInfo m in PublicMethods()) Assert.DoesNotContain(m.GetParameters(), p => p.IsOptional);
        }

        [Fact]
        public void EveryParameter_IsDesignerFriendly()
        {
            foreach (MethodInfo m in PublicMethods())
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    Assert.True(t == typeof(string) || t == typeof(bool) || t == typeof(int) || t.IsEnum, m.Name + "." + p.Name + " is " + t.Name + "; only string, bool, int or an enum are wireable without a proxy");
                }
        }

        [Fact]
        public void NoMethod_HasMoreThanOneBoolOutput()
        {
            foreach (MethodInfo m in PublicMethods())
                Assert.True(m.GetParameters().Count(p => p.IsOut && p.ParameterType == typeof(bool).MakeByRefType()) <= 1, m.Name + " has more than one bool output");
        }

        [Fact]
        public void EveryPublicTypeInTheAssembly_IsTheComponentOrADropDownEnum()
        {
            Type[] expected = { typeof(ResourceLockUtils), typeof(LockScope) };
            Assert.Equal(expected.Select(t => t.Name).OrderBy(n => n), typeof(ResourceLockUtils).Assembly.GetExportedTypes().Select(t => t.Name).OrderBy(n => n));
            Assert.DoesNotContain(typeof(ResourceLockUtils).GetInterfaces(), i => !i.IsPublic);                // no internal type in the component's metadata
        }

        [Fact]
        public void TheScopeEnum_HasTheDocumentedValues_AndTheSafeDefaultFirst()
        {
            // Process first: a lock that stays inside one runtime is the safe default; Shared (network) is appended in phase 2
            Assert.Equal(new[] { "Process", "Machine" }, Enum.GetNames(typeof(LockScope)));
            Assert.Equal(LockScope.Process, default(LockScope));
        }

        [Fact]
        public void TheLockOutcomes_AreOutputsNotFailures()
        {
            // Losing a lease or finding a lock taken is a normal outcome an automation branches on, so each has a bool output; the plan's decision 3
            Assert.Contains(typeof(ResourceLockUtils).GetMethod("ReleaseLock").GetParameters(), p => p.Name == "released" && p.IsOut);
            Assert.Contains(typeof(ResourceLockUtils).GetMethod("RenewLock").GetParameters(), p => p.Name == "renewed" && p.IsOut);
            foreach (string name in new[] { "TryAcquireLock", "AcquireLock", "TryAcquireSlot", "AcquireSlot" })
                Assert.Contains(typeof(ResourceLockUtils).GetMethod(name).GetParameters(), p => p.Name == "acquired" && p.IsOut);
        }
    }
}
