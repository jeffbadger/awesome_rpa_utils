using System;
using System.IO;

namespace RestCodeGenerator;

internal static class Program
{
    // Dev tool, not a Robot Studio component: throwing on bad usage is correct here.
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: RestCodeGenerator <swaggerPath> <apiName> <outputDirectory>");
            return 1;
        }
        var (swaggerPath, apiName, outputDirectory) = (args[0], args[1], args[2]);
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
        var rendered = ComponentRenderer.Render(doc, apiName);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, rendered.FileNameBase + ".cs"), rendered.Source);
        File.WriteAllText(Path.Combine(outputDirectory, rendered.FileNameBase + ".csproj"), rendered.Project);
        Console.WriteLine($"Generated {rendered.FileNameBase}.cs and {rendered.FileNameBase}.csproj in {outputDirectory}");
        Console.WriteLine("Primary: paste the .cs into a Robot Studio Script component - the per-endpoint");
        Console.WriteLine("  methods are compiled and persisted right there, and the swagger file is not");
        Console.WriteLine("  needed at runtime.");
        Console.WriteLine("Fallback: dotnet build <outputDirectory>/<apiName>RestUtils.csproj, then load");
        Console.WriteLine("  the built DLL into Robot Studio.");
        return 0;
    }
}