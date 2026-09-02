using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComponentBrowser
{
    /// <summary>
    /// Combines <see cref="AssemblyInspector"/>'s reflected PME list (ground truth structure)
    /// with <see cref="ReadmeDocParser"/>'s parsed documentation (rich, human-authored
    /// content) into the detail-enriched catalog the UI displays.
    /// </summary>
    public static class ComponentCatalogBuilder
    {
        public static List<ComponentCatalogEntry> Build(LoadedRelease release)
        {
            List<ComponentInfo> components = AssemblyInspector.InspectDirectory(release.DllDirectory, release.PrimaryDllFileNames);
            Dictionary<string, string> readmeByAssemblyName = IndexReadmesByAssemblyName(release.DocsDirectory);

            var entries = new List<ComponentCatalogEntry>();

            foreach (ComponentInfo component in components)
            {
                ParsedReadme readme = null;
                string componentDocDirectory = null;

                if (readmeByAssemblyName.TryGetValue(component.AssemblyName, out string readmePath))
                {
                    readme = ReadmeDocParser.Parse(readmePath);
                    componentDocDirectory = Path.GetDirectoryName(readmePath);
                }

                var details = new List<PmeDetail>();
                foreach (PmeInfo pme in component.Members)
                {
                    string readmeDescription = MatchDescription(readme, pme);
                    string workedExample = componentDocDirectory != null
                        ? ReadmeDocParser.FindWorkedExamplePath(componentDocDirectory, pme.Category)
                        : null;

                    details.Add(new PmeDetail
                    {
                        Pme = pme,
                        Description = readmeDescription ?? pme.DescriptionFromAttribute,
                        NotesAndCaveats = readme?.NotesAndCaveats,
                        WorkedExamplePath = workedExample
                    });
                }

                entries.Add(new ComponentCatalogEntry { Component = component, Details = details });
            }

            return entries;
        }

        /// <summary>
        /// Scans every extracted <c>README.md</c> under the documentation bundle's <c>src/</c>
        /// tree and keys each by the assembly name in its leading <c># AssemblyName</c>
        /// heading - the join key between a reflected DLL and its shipped README, since the
        /// two don't otherwise share a discoverable path (the DLL is
        /// <c>EventLogAutomation.dll</c>; its README lives at <c>src/eventlogutils/README.md</c>
        /// in the bundle, a folder name reflection has no way to derive).
        /// </summary>
        private static Dictionary<string, string> IndexReadmesByAssemblyName(string docsDirectory)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string srcRoot = Path.Combine(docsDirectory ?? string.Empty, "src");
            if (string.IsNullOrEmpty(docsDirectory) || !Directory.Exists(srcRoot))
                return index;

            foreach (string readmePath in Directory.GetFiles(srcRoot, "README.md", SearchOption.AllDirectories))
            {
                ParsedReadme parsed = ReadmeDocParser.Parse(readmePath);
                if (!string.IsNullOrEmpty(parsed.AssemblyName))
                    index[parsed.AssemblyName] = readmePath;
            }

            return index;
        }

        /// <summary>
        /// Matches a reflected PME to its README row by method name; when a name has more
        /// than one row (overloads), prefers the row whose signature text most closely
        /// matches the reflected signature (whitespace-insensitive), falling back to the
        /// first name match if none matches unambiguously. Deliberately not exact-string
        /// matching only - README signature formatting and reflection's formatting can
        /// legitimately differ in minor ways this shouldn't fail loudly over.
        /// </summary>
        private static string MatchDescription(ParsedReadme readme, PmeInfo pme)
        {
            if (readme == null)
                return null;

            List<ReadmeMethodRow> nameMatches = readme.Categories
                .SelectMany(c => c.Rows)
                .Where(r => r.MethodName == pme.Name)
                .ToList();

            if (nameMatches.Count == 0)
                return null;
            if (nameMatches.Count == 1)
                return nameMatches[0].Description;

            ReadmeMethodRow signatureMatch = nameMatches.FirstOrDefault(r => NormalizeWhitespace(r.Signature) == NormalizeWhitespace(pme.Signature));
            return (signatureMatch ?? nameMatches[0]).Description;
        }

        private static string NormalizeWhitespace(string text) => Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
    }
}
