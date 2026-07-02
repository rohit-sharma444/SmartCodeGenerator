using Microsoft.CodeAnalysis;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for resolving runtime assembly references for Roslyn compilation.
/// </summary>
public static class RuntimeReferenceResolver
{
    public static IEnumerable<MetadataReference>
        GetRuntimeReferences()
    {
        var assemblies = new List<string>();

        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(tpa))
            assemblies.AddRange(tpa.Split(Path.PathSeparator));

        // Best-effort: ensure System.ComponentModel.DataAnnotations is included for attributes like [Required]
        try
        {
            var dataAnnotationsLocation = typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(dataAnnotationsLocation) && !assemblies.Contains(dataAnnotationsLocation))
                assemblies.Add(dataAnnotationsLocation);
        }
        catch
        {
            // ignore if not available at runtime
        }

        return assemblies
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(a => MetadataReference.CreateFromFile(a));
    }
}