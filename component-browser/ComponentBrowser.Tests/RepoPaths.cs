using System;
using System.IO;

namespace ComponentBrowser.Tests
{
    /// <summary>
    /// Locates the repo root from the test assembly's own output directory, independent of
    /// whatever the test runner's current working directory happens to be, so fixtures can
    /// reference this repo's real files (READMEs, built component DLLs) rather than
    /// synthetic ones with no connection to what actually ships.
    /// </summary>
    internal static class RepoPaths
    {
        internal static string RepoRoot { get; } = FindRepoRoot();

        internal static string Combine(params string[] segments)
        {
            string path = RepoRoot;
            foreach (string segment in segments)
                path = Path.Combine(path, segment);
            return path;
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                bool looksLikeRepoRoot =
                    Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                    File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md"));

                if (looksLikeRepoRoot)
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the awesome_rpa_utils repo root above '{AppContext.BaseDirectory}'.");
        }
    }
}
