using Microsoft.CodeAnalysis;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for resolving and providing default metadata references for the code generation process.
/// </summary>
public static class ReferenceResolver
{
    public static IEnumerable<MetadataReference>
        GetDefaultReferences()
    {
        return
        [
            MetadataReference.CreateFromFile(
                typeof(object).Assembly.Location),

            MetadataReference.CreateFromFile(
                typeof(Console).Assembly.Location),

            MetadataReference.CreateFromFile(
                typeof(Enumerable).Assembly.Location),

            MetadataReference.CreateFromFile(
                typeof(List<>).Assembly.Location)
        ];
    }
}