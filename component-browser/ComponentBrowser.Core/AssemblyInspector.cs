using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ComponentBrowser
{
    /// <summary>
    /// Reflects over the DLLs in a directory to find every public
    /// <see cref="System.ComponentModel.Component"/>-derived type (this suite's own
    /// convention: one such type per component DLL) and its public PMEs.
    /// <para>
    /// Uses <see cref="MetadataLoadContext"/>, not <see cref="Assembly.LoadFrom(string)"/> -
    /// a deliberate safety choice. Plain loading would execute the assembly's static
    /// initializers just by reflecting over it; <see cref="MetadataLoadContext"/> reads only
    /// metadata and never runs any code in the inspected DLLs, which matters here since some
    /// of this suite's own components P/Invoke into Windows APIs on load.
    /// </para>
    /// <para>
    /// Because attribute instances are never constructed under <see cref="MetadataLoadContext"/>,
    /// <c>[Category]</c>/<c>[Description]</c> values are read via <see cref="CustomAttributeData"/>
    /// (its constructor-argument values) rather than <c>GetCustomAttribute&lt;T&gt;()</c>, which
    /// does not work in this context.
    /// </para>
    /// </summary>
    public static class AssemblyInspector
    {
        private const string ComponentBaseTypeFullName = "System.ComponentModel.Component";
        private const string CategoryAttributeFullName = "System.ComponentModel.CategoryAttribute";
        private const string DescriptionAttributeFullName = "System.ComponentModel.DescriptionAttribute";

        /// <summary>
        /// Inspects <c>*.dll</c> files in <paramref name="dllDirectory"/> for a public
        /// <see cref="System.ComponentModel.Component"/>-derived type. When
        /// <paramref name="componentDllFileNamesOnly"/> is given, only those specific file
        /// names are scanned as component candidates - every other DLL in the directory is
        /// still used to satisfy <see cref="MetadataLoadContext"/> type resolution, but is
        /// never itself checked for a Component-derived type. This matters for real release
        /// zips: this suite's own support-package dependencies
        /// (<c>System.Diagnostics.EventLog.dll</c>, <c>System.ServiceProcess.ServiceController.dll</c>)
        /// each define their own unrelated <c>Component</c>-derived BCL type
        /// (<c>System.Diagnostics.EventLog</c>, <c>System.ServiceProcess.ServiceController</c>)
        /// - scanning them as candidates would surface those as spurious fake "components"
        /// alongside this suite's real ones (confirmed by hitting this for real). When
        /// <paramref name="componentDllFileNamesOnly"/> is <c>null</c>, every DLL is scanned -
        /// the original, simpler behavior for a plain directory with no such ambiguity.
        /// A DLL with no public Component-derived type, or that fails to load as managed
        /// metadata, is skipped, not treated as an error.
        /// </summary>
        public static List<ComponentInfo> InspectDirectory(string dllDirectory, IReadOnlyCollection<string> componentDllFileNamesOnly = null)
        {
            if (string.IsNullOrWhiteSpace(dllDirectory) || !Directory.Exists(dllDirectory))
                return new List<ComponentInfo>();

            string[] dllPaths = Directory.GetFiles(dllDirectory, "*.dll");
            if (dllPaths.Length == 0)
                return new List<ComponentInfo>();

            string[] candidatePaths = componentDllFileNamesOnly == null
                ? dllPaths
                : dllPaths.Where(p => componentDllFileNamesOnly.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)).ToArray();

            string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            string[] runtimeDlls = Directory.GetFiles(runtimeDirectory, "*.dll");

            var resolverPaths = dllPaths
                .Concat(runtimeDlls)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string coreAssemblyName = typeof(object).Assembly.GetName().Name;
            var resolver = new PathAssemblyResolver(resolverPaths);

            var results = new List<ComponentInfo>();
            using (var context = new MetadataLoadContext(resolver, coreAssemblyName))
            {
                foreach (string dllPath in candidatePaths)
                {
                    ComponentInfo info;
                    try
                    {
                        info = InspectSingleAssembly(context, dllPath);
                    }
                    catch
                    {
                        // Not one of this suite's components, or not a loadable managed
                        // assembly at all (a native DLL, a mismatched-TFM reference, etc.) -
                        // skip it rather than aborting the whole directory scan.
                        continue;
                    }

                    if (info != null)
                        results.Add(info);
                }
            }

            return results;
        }

        private static ComponentInfo InspectSingleAssembly(MetadataLoadContext context, string dllPath)
        {
            Assembly assembly = context.LoadFromAssemblyPath(dllPath);

            Type componentType = assembly.GetTypes()
                .FirstOrDefault(t => t.IsPublic && t.IsClass && IsComponentDerived(t));

            if (componentType == null)
                return null;

            var info = new ComponentInfo
            {
                AssemblyName = assembly.GetName().Name,
                TypeName = componentType.Name,
                DllPath = dllPath
            };

            const BindingFlags declaredPublicInstance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            foreach (PropertyInfo property in componentType.GetProperties(declaredPublicInstance))
            {
                info.Members.Add(new PmeInfo
                {
                    Kind = PmeKind.Property,
                    Name = property.Name,
                    Signature = FormatPropertySignature(property),
                    Category = GetAttributeStringArgument(property, CategoryAttributeFullName),
                    DescriptionFromAttribute = GetAttributeStringArgument(property, DescriptionAttributeFullName)
                });
            }

            foreach (MethodInfo method in componentType.GetMethods(declaredPublicInstance))
            {
                // Property/event accessors (get_X, add_X, ...) are SpecialName methods - the
                // property/event itself is already listed above.
                if (method.IsSpecialName)
                    continue;

                info.Members.Add(new PmeInfo
                {
                    Kind = PmeKind.Method,
                    Name = method.Name,
                    Signature = FormatMethodSignature(method),
                    Category = GetAttributeStringArgument(method, CategoryAttributeFullName),
                    DescriptionFromAttribute = GetAttributeStringArgument(method, DescriptionAttributeFullName)
                });
            }

            foreach (EventInfo eventInfo in componentType.GetEvents(declaredPublicInstance))
            {
                info.Members.Add(new PmeInfo
                {
                    Kind = PmeKind.Event,
                    Name = eventInfo.Name,
                    Signature = FormatEventSignature(eventInfo),
                    Category = GetAttributeStringArgument(eventInfo, CategoryAttributeFullName),
                    DescriptionFromAttribute = GetAttributeStringArgument(eventInfo, DescriptionAttributeFullName)
                });
            }

            return info;
        }

        /// <summary>
        /// Walks the base-type chain by <see cref="Type.FullName"/>, not <c>==</c> - types
        /// loaded through a <see cref="MetadataLoadContext"/> are never reference-equal to
        /// the running process's own <c>System.ComponentModel.Component</c>, even though they
        /// represent the same type.
        /// </summary>
        private static bool IsComponentDerived(Type type)
        {
            for (Type current = type.BaseType; current != null; current = current.BaseType)
            {
                if (current.FullName == ComponentBaseTypeFullName)
                    return true;
            }
            return false;
        }

        private static string GetAttributeStringArgument(MemberInfo member, string attributeFullName)
        {
            foreach (CustomAttributeData data in CustomAttributeData.GetCustomAttributes(member))
            {
                if (data.AttributeType.FullName != attributeFullName)
                    continue;
                if (data.ConstructorArguments.Count == 0)
                    continue;
                if (data.ConstructorArguments[0].Value is string value)
                    return value;
            }
            return null;
        }

        private static string FormatPropertySignature(PropertyInfo property)
        {
            string accessors = property.CanWrite ? "get; set;" : "get;";
            return $"{FormatTypeName(property.PropertyType)} {property.Name} {{ {accessors} }}";
        }

        private static string FormatEventSignature(EventInfo eventInfo)
        {
            return $"event {FormatTypeName(eventInfo.EventHandlerType)} {eventInfo.Name}";
        }

        private static string FormatMethodSignature(MethodInfo method)
        {
            string parameters = string.Join(", ", method.GetParameters().Select(FormatParameter));
            return $"{FormatTypeName(method.ReturnType)} {method.Name}({parameters})";
        }

        private static string FormatParameter(ParameterInfo parameter)
        {
            Type type = parameter.ParameterType;
            string modifier = string.Empty;

            if (type.IsByRef)
            {
                modifier = parameter.IsOut ? "out " : "ref ";
                type = type.GetElementType();
            }

            string defaultValueText = string.Empty;
            if (parameter.HasDefaultValue)
            {
                // ParameterInfo.DefaultValue throws InvalidOperationException under
                // MetadataLoadContext ("It is illegal to request the default value on a
                // ParameterInfo loaded by a MetadataLoadContext. Use RawDefaultValue instead.")
                // - confirmed for real, and confirmed for real that the exception, uncaught
                // here, silently took down the whole assembly's scan via InspectDirectory's
                // broad catch-and-skip (any component with a defaulted parameter, e.g.
                // EventLogUtils.ListLogNamesDelimited(string delimiter = ","), would vanish
                // from the browser entirely rather than merely mis-format one signature).
                object defaultValue = parameter.RawDefaultValue;
                defaultValueText = defaultValue switch
                {
                    null => " = null",
                    string s => $" = \"{s}\"",
                    bool b => $" = {(b ? "true" : "false")}",
                    _ => " = " + Convert.ToString(defaultValue, CultureInfo.InvariantCulture)
                };
            }

            return $"{modifier}{FormatTypeName(type)} {parameter.Name}{defaultValueText}";
        }

        /// <summary>
        /// A short, readable type name (C# keyword aliases for primitives, angle-bracket
        /// generics) instead of reflection's verbose/assembly-qualified default. Compares by
        /// <see cref="Type.FullName"/>, never <c>typeof(...)</c> equality - the same
        /// cross-context-identity reason as <see cref="IsComponentDerived"/>.
        /// </summary>
        private static string FormatTypeName(Type type)
        {
            if (type == null)
                return "void";

            switch (type.FullName)
            {
                case "System.Void": return "void";
                case "System.String": return "string";
                case "System.Boolean": return "bool";
                case "System.Int32": return "int";
                case "System.Int64": return "long";
                case "System.Double": return "double";
                case "System.Single": return "float";
                case "System.Object": return "object";
            }

            if (type.IsArray)
                return FormatTypeName(type.GetElementType()) + "[]";

            if (type.IsGenericType)
            {
                string name = type.Name;
                int backtick = name.IndexOf('`');
                if (backtick >= 0)
                    name = name.Substring(0, backtick);

                var arguments = type.GetGenericArguments().Select(FormatTypeName);
                return $"{name}<{string.Join(", ", arguments)}>";
            }

            return type.Name;
        }
    }
}
