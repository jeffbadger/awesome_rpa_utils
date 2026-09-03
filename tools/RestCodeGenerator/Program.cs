using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RestCodeGenerator;

internal static class Program
{
    private const string Usage = "Usage: RestCodeGenerator <swaggerPath> <apiName> <outputDirectory> [--component] [--build]";

    // Dev tool, not a Robot Studio component: throwing on bad usage is correct here.
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }
        var (swaggerPath, apiName, outputDirectory) = (args[0], args[1], args[2]);
        var designerComponent = false;
        var build = false;
        foreach (var flag in args.Skip(3))
        {
            switch (flag)
            {
                case "--component": designerComponent = true; break;
                case "--build": build = true; break;
                default:
                    Console.Error.WriteLine($"Unrecognized flag: {flag}");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        SwaggerDoc doc;
        try
        {
            doc = SwaggerParser.ParseFile(swaggerPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse {swaggerPath}: {ex.Message}");
            return 1;
        }
        if (doc.Operations.Count == 0)
        {
            Console.Error.WriteLine($"No usable operations found in {swaggerPath}.");
            if (doc.Skipped.Count > 0)
                Console.Error.WriteLine($"(all {doc.Skipped.Count} operations had non-JSON bodies and were skipped)");
            return 1;
        }
        var rendered = ComponentRenderer.Render(doc, apiName, designerComponent);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, rendered.FileNameBase + ".cs"), rendered.Source);
        var csprojPath = Path.Combine(outputDirectory, rendered.FileNameBase + ".csproj");
        File.WriteAllText(csprojPath, rendered.Project);
        Console.WriteLine($"Generated {rendered.FileNameBase}.cs and {rendered.FileNameBase}.csproj in {outputDirectory}");
        Console.WriteLine("Primary: paste the .cs into a Robot Studio Script component - the per-endpoint");
        Console.WriteLine("  methods are compiled and persisted right there, and the swagger file is not");
        Console.WriteLine("  needed at runtime.");

        if (!build)
        {
            Console.WriteLine("Fallback: dotnet build " + csprojPath + ", then load the");
            Console.WriteLine("  built DLL into Robot Studio.");
            return 0;
        }

        Console.WriteLine("Fallback: building " + csprojPath + " (--build)...");
        var buildExitCode = RunDotnetBuild(csprojPath);
        if (buildExitCode != 0)
        {
            Console.Error.WriteLine($"dotnet build failed with exit code {buildExitCode}.");
            return 1;
        }
        Console.WriteLine($"Build succeeded — load the built DLL into Robot Studio from {outputDirectory}/bin/<Configuration>/<framework>/.");
        return 0;
    }

    /// <summary>Shells out to the `dotnet` CLI already required to run this tool, streaming its
    /// output straight through so build errors are visible without re-capturing/re-printing them.</summary>
    private static int RunDotnetBuild(string csprojPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "build", csprojPath },
                UseShellExecute = false,
            });
            process!.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to run 'dotnet build': {ex.Message}");
            return 1;
        }
    }
}