using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ComponentBrowser.Tests
{
    /// <summary>
    /// Reflects over this repo's own real, built component DLLs (not synthetic fixtures) -
    /// requires <c>EventLogUtils</c>/<c>SessionUtils</c> to have been built at least once
    /// (`dotnet build src/eventlogutils/EventLogUtils.csproj`,
    /// `dotnet build src/sessionutils/SessionUtils.csproj`) so `src/bin/&lt;Config&gt;/&lt;TFM&gt;/*.dll`
    /// exists - the same consolidated output location every component in this suite builds
    /// to, per `src/Directory.Build.props`.
    /// </summary>
    public class AssemblyInspectorTests : IDisposable
    {
        private readonly System.Collections.Generic.List<string> _tempDirectoriesToClean = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Finds a build-output directory containing <paramref name="assemblyFileName"/> and
        /// every one of <paramref name="dependencyFileNames"/> together - NOT just any
        /// directory containing the main DLL. This repo's various Debug/Release x
        /// TFM output folders under the shared <c>src/bin/</c> root can be built at different
        /// times with different NuGet-copy outcomes (confirmed for real: a Debug build here
        /// had no <c>System.Diagnostics.EventLog*.dll</c> copied alongside
        /// <c>EventLogAutomation.dll</c> even though a Release build of the same TFM did) - so
        /// a bare "prefer net10.0-windows" substring match can silently pick a directory
        /// missing the very dependency this test needs. Falls back to any directory containing
        /// just the main DLL, matching the old behavior, if no directory has everything.
        /// </summary>
        private static string FindBuiltDllDirectory(string assemblyFileName, params string[] dependencyFileNames)
        {
            string binRoot = RepoPaths.Combine("src", "bin");
            if (!Directory.Exists(binRoot))
            {
                throw new FileNotFoundException(
                    $"'{binRoot}' does not exist. Build the component first, e.g. " +
                    $"'dotnet build src/eventlogutils/EventLogUtils.csproj'.");
            }

            string[] matches = Directory.GetFiles(binRoot, assemblyFileName, SearchOption.AllDirectories);
            if (matches.Length == 0)
            {
                throw new FileNotFoundException(
                    $"No built '{assemblyFileName}' found under '{binRoot}'. Build the component first.");
            }

            var candidateDirectories = matches
                .Select(Path.GetDirectoryName)
                .OrderByDescending(d => d!.Contains("net10.0-windows", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(d => d!.Contains("Release", StringComparison.OrdinalIgnoreCase));

            string bestWithAllDependencies = candidateDirectories.FirstOrDefault(
                d => dependencyFileNames.All(dep => File.Exists(Path.Combine(d!, dep))));

            return bestWithAllDependencies ?? candidateDirectories.First()!;
        }

        /// <summary>
        /// Copies a single built DLL (plus any of its own NuGet-copied dependency DLLs also
        /// found in the shared bin directory, e.g. EventLogAutomation's
        /// <c>System.Diagnostics.EventLog*.dll</c>) into its own fresh temp directory and
        /// returns that directory. Required because this repo's
        /// <c>src/Directory.Build.props</c> consolidates every component's build output into
        /// one shared <c>src/bin/&lt;Config&gt;/&lt;TFM&gt;/</c> directory - inspecting that directory
        /// directly would find every other already-built component alongside the one under
        /// test, not just it, exactly the multi-component shape the "both DLLs" test below
        /// exercises on purpose. Isolating without also carrying a dependency DLL breaks
        /// <see cref="MetadataLoadContext"/> resolution the same way it would for a real
        /// release zip without its nested SupportLibraries.zip extracted alongside it (see
        /// <see cref="ReleaseZipLoader"/>'s own handling of exactly this) - confirmed by
        /// hitting this for real while isolating EventLogAutomation.dll alone.
        /// </summary>
        private string IsolateDll(string assemblyFileName, params string[] dependencyFileNames)
        {
            string sourceDirectory = FindBuiltDllDirectory(assemblyFileName, dependencyFileNames);
            string sourceDll = Path.Combine(sourceDirectory, assemblyFileName);
            string tempDirectory = Path.Combine(Path.GetTempPath(), "ComponentBrowserTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            _tempDirectoriesToClean.Add(tempDirectory);
            File.Copy(sourceDll, Path.Combine(tempDirectory, assemblyFileName));
            foreach (string dependencyFileName in dependencyFileNames)
            {
                // Prefer a direct sibling of the main DLL; some dependency packages (e.g.
                // System.Diagnostics.EventLog's satellite System.Diagnostics.EventLog.Messages.dll)
                // are only ever copied to a nested runtimes/win/lib/<tfm>/ subfolder, never the
                // build-output root - confirmed for real, and confirmed for real that omitting
                // it still breaks MetadataLoadContext resolution of the main DLL, not just a
                // cosmetic gap - so fall back to a recursive search of the whole shared bin tree.
                string directSibling = Path.Combine(sourceDirectory, dependencyFileName);
                string dependencyPath = File.Exists(directSibling)
                    ? directSibling
                    : Directory.GetFiles(RepoPaths.Combine("src", "bin"), dependencyFileName, SearchOption.AllDirectories).FirstOrDefault();

                if (dependencyPath != null)
                    File.Copy(dependencyPath, Path.Combine(tempDirectory, dependencyFileName));
            }
            return tempDirectory;
        }

        private string EventLogUtilsDllDirectory => IsolateDll(
            "EventLogAutomation.dll", "System.Diagnostics.EventLog.dll", "System.Diagnostics.EventLog.Messages.dll");
        private string SessionUtilsDllDirectory => IsolateDll("SessionAutomation.dll");

        public void Dispose()
        {
            foreach (string directory in _tempDirectoriesToClean)
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void InspectDirectory_EventLogUtilsDll_FindsTheEventLogUtilsComponentType()
        {
            var components = AssemblyInspector.InspectDirectory(EventLogUtilsDllDirectory, new[] { "EventLogAutomation.dll" });
            var component = Assert.Single(components);

            Assert.Equal("EventLogAutomation", component.AssemblyName);
            Assert.Equal("EventLogUtils", component.TypeName);
        }

        [Fact]
        public void InspectDirectory_EventLogUtilsDll_WriteEntryMethodHasExpectedCategoryAndDescriptionAttributes()
        {
            var components = AssemblyInspector.InspectDirectory(EventLogUtilsDllDirectory, new[] { "EventLogAutomation.dll" });
            var component = components.Single();

            var writeEntry = component.Members.Single(m => m.Kind == PmeKind.Method && m.Name == "WriteEntry");

            // Read via CustomAttributeData under MetadataLoadContext - if this test passes,
            // the constructor-argument extraction (not GetCustomAttribute<T>(), which does
            // not work in this context) is working correctly.
            Assert.Equal("EventLog - Write", writeEntry.Category);
            Assert.False(string.IsNullOrWhiteSpace(writeEntry.DescriptionFromAttribute));
            Assert.Contains("Writes an entry", writeEntry.DescriptionFromAttribute);
        }

        [Fact]
        public void InspectDirectory_EventLogUtilsDll_MethodSignatureIsFormattedReadably()
        {
            var components = AssemblyInspector.InspectDirectory(EventLogUtilsDllDirectory, new[] { "EventLogAutomation.dll" });
            var component = components.Single();

            var listLogNames = component.Members.Single(m => m.Kind == PmeKind.Method && m.Name == "ListLogNames");

            // Not the raw reflection ToString() shape (which would include
            // "System.Collections.Generic.List`1[System.String]" and assembly-qualified
            // names) - readable C#-style output instead.
            Assert.Equal("List<string> ListLogNames()", listLogNames.Signature);
        }

        [Fact]
        public void InspectDirectory_EventLogUtilsDll_OutParameterMethodFormatsOutModifier()
        {
            var components = AssemblyInspector.InspectDirectory(EventLogUtilsDllDirectory, new[] { "EventLogAutomation.dll" });
            var component = components.Single();

            var method = component.Members.Single(m => m.Kind == PmeKind.Method && m.Name == "DoesLogExistSimple");
            Assert.Equal("bool DoesLogExistSimple(string logName, out string message)", method.Signature);
        }

        [Fact]
        public void InspectDirectory_SessionUtilsDll_FindsTheSessionUtilsComponentType()
        {
            var components = AssemblyInspector.InspectDirectory(SessionUtilsDllDirectory);
            var component = Assert.Single(components);

            Assert.Equal("SessionAutomation", component.AssemblyName);
            Assert.Equal("SessionUtils", component.TypeName);
        }

        [Fact]
        public void InspectDirectory_BothDllsInSameDirectory_FindsBothComponentsIndependently_NotTheSupportLibrarysOwnComponentType()
        {
            // Copy both component DLLs, PLUS EventLogAutomation's own support-library
            // dependency (which itself defines an unrelated System.ComponentModel.Component
            // subclass - the real System.Diagnostics.EventLog BCL class), into one temp
            // directory - the same shape a real extracted release zip has after its nested
            // SupportLibraries.zip is overlaid. Restricting the scan to the two real component
            // file names (as ComponentCatalogBuilder does via LoadedRelease.PrimaryDllFileNames)
            // must find exactly those two, not a spurious third "component" from the support
            // DLL - this is the actual regression test for that bug.
            string tempDirectory = Path.Combine(Path.GetTempPath(), "ComponentBrowserTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                string eventLogDir = FindBuiltDllDirectory(
                    "EventLogAutomation.dll", "System.Diagnostics.EventLog.dll");
                string sessionDir = FindBuiltDllDirectory("SessionAutomation.dll");

                File.Copy(Path.Combine(eventLogDir, "EventLogAutomation.dll"), Path.Combine(tempDirectory, "EventLogAutomation.dll"));
                File.Copy(Path.Combine(eventLogDir, "System.Diagnostics.EventLog.dll"), Path.Combine(tempDirectory, "System.Diagnostics.EventLog.dll"));
                File.Copy(Path.Combine(sessionDir, "SessionAutomation.dll"), Path.Combine(tempDirectory, "SessionAutomation.dll"));

                // Unfiltered: the support DLL's own Component type IS picked up (documents the
                // failure mode this was written to catch, rather than hiding it).
                var unfiltered = AssemblyInspector.InspectDirectory(tempDirectory);
                Assert.Equal(3, unfiltered.Count);
                Assert.Contains(unfiltered, c => c.AssemblyName == "System.Diagnostics.EventLog");

                // Filtered to the real component file names: exactly the two real components.
                var components = AssemblyInspector.InspectDirectory(
                    tempDirectory, new[] { "EventLogAutomation.dll", "SessionAutomation.dll" });

                Assert.Equal(2, components.Count);
                Assert.Contains(components, c => c.AssemblyName == "EventLogAutomation");
                Assert.Contains(components, c => c.AssemblyName == "SessionAutomation");
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void InspectDirectory_NonexistentDirectory_ReturnsEmptyListNotError()
        {
            var components = AssemblyInspector.InspectDirectory(Path.Combine(Path.GetTempPath(), "definitely-does-not-exist-" + Guid.NewGuid()));
            Assert.Empty(components);
        }

        [Fact]
        public void InspectDirectory_NullOrEmptyDirectory_ReturnsEmptyListNotError()
        {
            Assert.Empty(AssemblyInspector.InspectDirectory(null));
            Assert.Empty(AssemblyInspector.InspectDirectory(string.Empty));
        }
    }
}
