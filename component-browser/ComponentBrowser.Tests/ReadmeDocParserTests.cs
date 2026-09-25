using System.IO;
using System.Linq;
using Xunit;

namespace ComponentBrowser.Tests
{
    /// <summary>
    /// Parses this repo's own real component READMEs - not synthetic markdown - as fixtures,
    /// so a passing test actually proves the parser understands this suite's real doc shape.
    /// </summary>
    public class ReadmeDocParserTests
    {
        private static string EventLogUtilsReadme => RepoPaths.Combine("src", "eventlogutils", "README.md");
        private static string EventLogUtilsDir => RepoPaths.Combine("src", "eventlogutils");
        private static string ScreenCaptureUtilsReadme => RepoPaths.Combine("src", "screencaptureutils", "README.md");
        /// <summary>
        /// A component folder that has a README but no Documentation/ folder, built in a temp directory. It used to be
        /// the real ScreenCaptureUtils, which has since gained a Documentation/ folder, so the no-docs case is now
        /// constructed instead of borrowed from the repository.
        /// </summary>
        private sealed class ComponentWithoutDocumentation : System.IDisposable
        {
            public string Directory { get; } = Path.Combine(Path.GetTempPath(), "cb-nodocs-" + System.Guid.NewGuid().ToString("N"));

            public ComponentWithoutDocumentation()
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(Path.Combine(Directory, "README.md"), "# NoDocsAutomation\n\n## Methods\n\n### Actions\n");
            }

            public void Dispose()
            {
                try { System.IO.Directory.Delete(Directory, recursive: true); } catch (IOException) { /* best effort */ }
            }
        }
        private static string SessionUtilsReadme => RepoPaths.Combine("src", "sessionutils", "README.md");
        private static string ArchiveUtilsReadme => RepoPaths.Combine("src", "archiveutils", "README.md");

        [Fact]
        public void Parse_EventLogUtilsReadme_ReadsAssemblyNameFromLeadingHeading()
        {
            var parsed = ReadmeDocParser.Parse(EventLogUtilsReadme);
            Assert.Equal("EventLogAutomation", parsed.AssemblyName);
        }

        [Fact]
        public void Parse_EventLogUtilsReadme_FindsAllFiveMethodCategories()
        {
            var parsed = ReadmeDocParser.Parse(EventLogUtilsReadme);
            var categoryNames = parsed.Categories.Select(c => c.Name).ToList();
            Assert.Equal(new[] { "Discovery", "Query", "Write", "Wait", "Export" }, categoryNames);
        }

        [Fact]
        public void Parse_EventLogUtilsReadme_DoesNotPickUpTypeHeadingsBeforeMethodsSection()
        {
            // EventLogLevel/EventLogEntryData are `### `-headed under `## Types`, above
            // `## Methods` - they must never be mistaken for method categories.
            var parsed = ReadmeDocParser.Parse(EventLogUtilsReadme);
            Assert.DoesNotContain(parsed.Categories, c => c.Name.Contains("EventLogLevel") || c.Name.Contains("EventLogEntryData"));
        }

        [Fact]
        public void Parse_EventLogUtilsReadme_CreateEventSourceRowHasExpectedSignatureAndDescription()
        {
            var parsed = ReadmeDocParser.Parse(EventLogUtilsReadme);
            var row = parsed.Categories.SelectMany(c => c.Rows).Single(r => r.MethodName == "CreateEventSource");

            Assert.Equal(
                "bool CreateEventSource(string sourceName, string logName, out bool alreadyExisted, out string message)",
                row.Signature);
            Assert.Contains("Registers a new event source", row.Description);
            Assert.Contains("administrator rights", row.Description);
        }

        [Fact]
        public void Parse_EventLogUtilsReadme_WriteEntryRowDescriptionPreservesInlineBackticks()
        {
            // The description column itself contains a backtick-quoted method reference
            // ("call `CreateEventSource` first") - only the Method/Signature columns get
            // their backticks stripped, not Description.
            var parsed = ReadmeDocParser.Parse(EventLogUtilsReadme);
            var row = parsed.Categories.SelectMany(c => c.Rows).Single(r => r.MethodName == "WriteEntry");

            Assert.Contains("`CreateEventSource`", row.Description);
        }

        [Fact]
        public void Parse_EventLogUtilsReadme_NotesAndCaveatsContainsKnownDistinctiveText()
        {
            var parsed = ReadmeDocParser.Parse(EventLogUtilsReadme);
            Assert.False(string.IsNullOrWhiteSpace(parsed.NotesAndCaveats));
            Assert.Contains("Elevation requirements", parsed.NotesAndCaveats);
            Assert.Contains("Local machine only", parsed.NotesAndCaveats);
        }

        [Fact]
        public void FindWorkedExamplePath_EventLogUtils_FindsQueryMdForQueryCategory()
        {
            string path = ReadmeDocParser.FindWorkedExamplePath(EventLogUtilsDir, "Query");
            Assert.NotNull(path);
            Assert.Equal("Query.md", Path.GetFileName(path));
        }

        [Fact]
        public void FindWorkedExamplePath_EventLogUtils_UnknownCategoryReturnsNull()
        {
            string path = ReadmeDocParser.FindWorkedExamplePath(EventLogUtilsDir, "NotARealCategory");
            Assert.Null(path);
        }

        [Fact]
        public void TheNoDocsFixture_ReallyHasNoDocumentationFolder()
        {
            // Ground-truth check before relying on the fixture below: the "no worked example" path must be
            // exercised against a component that truly has no Documentation/ folder.
            using var component = new ComponentWithoutDocumentation();
            Assert.True(File.Exists(Path.Combine(component.Directory, "README.md")));
            Assert.False(Directory.Exists(Path.Combine(component.Directory, "Documentation")));
        }

        [Fact]
        public void FindWorkedExamplePath_AComponentWithNoDocumentationFolder_ReturnsNullNotError()
        {
            using var component = new ComponentWithoutDocumentation();
            string path = ReadmeDocParser.FindWorkedExamplePath(component.Directory, "AnyCategory");
            Assert.Null(path);
        }

        [Fact]
        public void Parse_SessionUtilsReadme_FindsExpectedCategoriesNotTypeHeadings()
        {
            var parsed = ReadmeDocParser.Parse(SessionUtilsReadme);
            var categoryNames = parsed.Categories.Select(c => c.Name).ToList();

            Assert.Contains("Identity", categoryNames);
            Assert.Contains("Wait", categoryNames);
            Assert.Contains("Actions", categoryNames);
            // SessionConnectState/SessionKind/SessionInfoData are `### `-headed under
            // `## Types`, above `## Methods` - must not leak into the category list.
            Assert.DoesNotContain(categoryNames, n => n.StartsWith("Session"));
        }

        [Fact]
        public void Parse_ArchiveUtilsReadme_FindsCategoryWithEmDashInName()
        {
            var parsed = ReadmeDocParser.Parse(ArchiveUtilsReadme);
            Assert.Contains(parsed.Categories, c => c.Name == "Extract — Single File");
        }
    }
}
