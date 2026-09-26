using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ReconciliationAutomation.Tests
{
    /// <summary>
    /// Enforces the suite-wide public-surface rules mechanically so a later change cannot quietly break
    /// them: never-throws shape, designer attributes, signature uniqueness, the Release 1 surface, and the
    /// plan's port rules (Simple forms, at most one bool output besides the result).
    /// </summary>
    public sealed class ConventionTests
    {
        private static readonly string[] Release1Methods =
        {
            "ClearDefinition",
            "AddKeyMappingSimple", "AddKeyMapping",
            "AddTextComparisonSimple", "AddTextComparison",
            "AddDecimalComparisonSimple", "AddDecimalComparison",
            "LoadDefinitionJson", "GetDefinitionJson", "ValidateDefinitionJson", "ConfigureLimits",
            "ReconcileJson", "GetSummary", "GetSummaryJson", "ResetResultCursor",
            "TryReadNextException", "TryReadNextDifference", "GetResultJson", "ClearResults"
        };

        /// <summary>Release 2 is additive; each work package adds its methods here.</summary>
        private static readonly string[] Release2Methods = { "ReconcileDataTables", "ConfigureTableLimits", "AddBooleanComparisonSimple", "AddBooleanComparison", "AddMoneyComparisonSimple", "AddMoneyComparison", "AddCalendarDateComparisonSimple", "AddCalendarDateComparison", "AddInstantComparisonSimple", "AddInstantComparison" };

        internal static MethodInfo[] PublicMethods() =>
            typeof(ReconciliationUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .ToArray();

        [Fact]
        public void ThePublicSurface_IsExactlyTheDocumentedMethods()
        {
            // A deliberate tripwire: adding, removing or renaming a public method must be a conscious change to this list
            // (and to the design plan), never a side effect.
            Assert.Equal(Release1Methods.Concat(Release2Methods).OrderBy(x => x, StringComparer.Ordinal), PublicMethods().Select(m => m.Name).OrderBy(x => x, StringComparer.Ordinal));
        }

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
        public void TheComponentClass_HasADescription()
        {
            Assert.False(string.IsNullOrWhiteSpace(typeof(ReconciliationUtils).GetCustomAttribute<DescriptionAttribute>()?.Description));
        }

        [Fact]
        public void EveryPublicMethod_ReturnsBool_AndEndsWithOutStringMessage()
        {
            foreach (MethodInfo m in PublicMethods())
            {
                Assert.True(m.ReturnType == typeof(bool), m.Name + " must return bool (the never-throws contract)");
                ParameterInfo last = m.GetParameters().Last();
                Assert.True(last.Name == "message" && last.IsOut && last.ParameterType == typeof(string).MakeByRefType(), m.Name + " must end with 'out string message'");
            }
        }

        [Fact]
        public void InputsPrecedeOutputs()
        {
            foreach (MethodInfo m in PublicMethods())
            {
                bool seenOut = false;
                foreach (ParameterInfo p in m.GetParameters())
                {
                    if (p.IsOut) seenOut = true;
                    else Assert.False(seenOut, m.Name + "." + p.Name + " is an input after an output");
                }
            }
        }

        [Fact]
        public void NoTwoPublicMethods_ShareANameAndTheSameNonOutParameterTypes()
        {
            // Robot Studio resolves methods by name plus the ordered non-out parameter types and ignores out parameters.
            var collisions = PublicMethods()
                .GroupBy(m => m.Name + "(" + string.Join(",", m.GetParameters().Where(p => !p.IsOut && !p.ParameterType.IsByRef).Select(p => p.ParameterType.Name)) + ")")
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.True(collisions.Count == 0, "Signature collisions: " + string.Join(", ", collisions));
        }

        [Fact]
        public void NoMethodNameIsOverloaded()
        {
            // The plan is stricter than the suite rule: no overloads at all.
            Assert.Empty(PublicMethods().GroupBy(m => m.Name).Where(g => g.Count() > 1).Select(g => g.Key));
        }

        [Fact]
        public void NoPublicMemberIsGeneric_AndNoOptionalParametersAreUsed()
        {
            Assert.DoesNotContain(PublicMethods(), m => m.IsGenericMethod);
            foreach (MethodInfo m in PublicMethods())
                Assert.DoesNotContain(m.GetParameters(), p => p.IsOptional);
        }

        [Fact]
        public void EveryParameter_IsDesignerFriendly()
        {
            foreach (MethodInfo m in PublicMethods())
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Type t = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    Assert.True(t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(double) || t.IsEnum || (m.Name == "ReconcileDataTables" && t == typeof(System.Data.DataTable)),
                        m.Name + "." + p.Name + " is " + t.Name + "; only string/bool/int/double or an enum are wireable without a proxy object (ReconcileDataTables takes DataTables by design, as DataContractUtils does)");
                }
        }

        [Fact]
        public void NoMethod_HasMoreThanOneBoolOutput_BesidesTheResult()
        {
            // Two bool ports on one block make it awkward on the Robot Studio surface (design review, revision 2).
            // The cursor methods keep exactly one: hasItem.
            foreach (MethodInfo m in PublicMethods())
            {
                int boolOuts = m.GetParameters().Count(p => p.IsOut && p.ParameterType == typeof(bool).MakeByRefType());
                Assert.True(boolOuts <= 1, m.Name + " has " + boolOuts + " bool outputs");
            }
            Assert.Equal(new[] { "TryReadNextDifference", "TryReadNextException" },
                PublicMethods().Where(m => m.GetParameters().Any(p => p.IsOut && p.ParameterType == typeof(bool).MakeByRefType())).Select(m => m.Name).OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public void EveryAddMethod_HasASimpleForm_WithFewerInputs()
        {
            foreach (MethodInfo full in PublicMethods().Where(m => m.Name.StartsWith("Add") && !m.Name.EndsWith("Simple")))
            {
                MethodInfo simple = PublicMethods().SingleOrDefault(m => m.Name == full.Name + "Simple");
                Assert.True(simple != null, full.Name + " has no " + full.Name + "Simple form");
                int Inputs(MethodInfo m) => m.GetParameters().Count(p => !p.IsOut);
                Assert.True(Inputs(simple) < Inputs(full), full.Name + "Simple must take fewer inputs than " + full.Name);
                Assert.Equal(new[] { "name", "leftPointer", "rightPointer" }, simple.GetParameters().Take(3).Select(p => p.Name));
            }
        }

        [Fact]
        public void EveryPublicClassInTheAssembly_IsTheComponentOrANamedEnum()
        {
            foreach (Type t in typeof(ReconciliationUtils).Assembly.GetExportedTypes())
                Assert.True(t == typeof(ReconciliationUtils) || t == typeof(ComparisonNullPolicy), t.Name + " is public but is not part of the planned surface");
        }

        [Fact]
        public void TheNullPolicyEnum_HasTheDocumentedValues_AndRequireValueIsTheDefault()
        {
            Assert.Equal(new[] { "RequireValue", "AllowBothNull" }, Enum.GetNames(typeof(ComparisonNullPolicy)));
            Assert.Equal(ComparisonNullPolicy.RequireValue, default(ComparisonNullPolicy));
        }
    }
}
