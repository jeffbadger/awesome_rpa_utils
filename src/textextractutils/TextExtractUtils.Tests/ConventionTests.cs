using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace TextExtractAutomation.Tests
{
    /// <summary>Enforces the suite-wide public-surface rules mechanically: never-throws shape, designer attributes, signature uniqueness and the planned surface.</summary>
    public sealed class ConventionTests
    {
        /// <summary>Phase 1 of the plan (project-docs/plans/2026-09-26-textextractutils-design.md). Tables arrive in phase 2.</summary>
        private static readonly string[] Phase1Methods =
        {
            "ClearTemplate", "AddLabelFieldSimple", "AddLabelField", "AddPatternField",
            "LoadTemplateJson", "GetTemplateJson", "ValidateTemplateJson", "ConfigureLimits",
            "ExtractFromText",
            "GetField", "GetResultJson", "ResetFieldCursor", "TryReadNextField", "ClearResults"
        };

        internal static MethodInfo[] PublicMethods() =>
            typeof(TextExtractUtils).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).ToArray();

        [Fact]
        public void ThePublicSurface_IsExactlyThePlannedMethods()
        {
            // A deliberate tripwire: adding, removing or renaming a public method must be a conscious change to this list and to the plan.
            Assert.Equal(Phase1Methods.OrderBy(x => x, StringComparer.Ordinal), PublicMethods().Select(m => m.Name).OrderBy(x => x, StringComparer.Ordinal));
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
            Assert.False(string.IsNullOrWhiteSpace(typeof(TextExtractUtils).GetCustomAttribute<DescriptionAttribute>()?.Description));
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
            Type[] expected = { typeof(TextExtractUtils), typeof(ValuePosition), typeof(FieldType), typeof(Occurrence), typeof(DecimalStyle) };
            Assert.Equal(expected.Select(t => t.Name).OrderBy(n => n), typeof(TextExtractUtils).Assembly.GetExportedTypes().Select(t => t.Name).OrderBy(n => n));
        }

        [Fact]
        public void TheEnums_HaveTheDocumentedValues_AndTheSafeDefaultFirst()
        {
            Assert.Equal(new[] { "SameLine", "NextLine", "Below" }, Enum.GetNames(typeof(ValuePosition)));
            Assert.Equal(new[] { "Text", "Code", "Integer", "Decimal", "Amount", "Date", "Email", "Iban", "Percentage" }, Enum.GetNames(typeof(FieldType)));
            Assert.Equal(new[] { "RequireUnique", "First", "Last" }, Enum.GetNames(typeof(Occurrence)));
            Assert.Equal(new[] { "DotDecimal", "CommaDecimal" }, Enum.GetNames(typeof(DecimalStyle)));
            Assert.Equal(DecimalStyle.DotDecimal, default(DecimalStyle));
            Assert.Equal(Occurrence.RequireUnique, default(Occurrence));
            Assert.Equal(ValuePosition.SameLine, default(ValuePosition));
        }
    }
}
