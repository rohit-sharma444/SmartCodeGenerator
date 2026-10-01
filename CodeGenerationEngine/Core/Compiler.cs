using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Validates generated C# source code by checking for syntax and semantic errors using Roslyn.
/// </summary>
public static class Compiler
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> _frameworkReferences =
        new(LoadFrameworkReferences);

    /// <summary>The runtime's own assemblies, loaded once: what every compilation here references.</summary>
    internal static IReadOnlyList<MetadataReference> FrameworkReferences => _frameworkReferences.Value;

    /// <summary>
    /// Compiles source code into an in-memory assembly.
    /// </summary>
    public static CompilationResult Compile(string sourceCode,
        IEnumerable<string>? additionalAssemblyPaths = null)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return new CompilationResult
            {
                Success = false,
                Diagnostics = [],
                AssemblyBytes = null
            };
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions);

        var references = BuildReferences(additionalAssemblyPaths);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"GeneratedAssembly_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);

        return new CompilationResult
        {
            Success = emitResult.Success,
            Diagnostics = [.. emitResult.Diagnostics],
            AssemblyBytes = emitResult.Success
                ? stream.ToArray()
                : null
        };
    }

    /// <summary>
    /// Compiles several files as ONE assembly, so a type declared in one file
    /// resolves from another. <see cref="Compile(string, IEnumerable{string})"/>
    /// builds a single-tree compilation and cannot do this.
    /// </summary>
    public static CompilationResult CompileSources(
        IEnumerable<(string Path, string Code)> sources,
        IEnumerable<string>? additionalAssemblyPaths = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        // path: is what puts the file name into each diagnostic's location.
        var syntaxTrees = sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Code))
            .Select(s => CSharpSyntaxTree.ParseText(s.Code, parseOptions, path: s.Path))
            .ToArray();

        if (syntaxTrees.Length == 0)
        {
            return new CompilationResult
            {
                Success = false,
                Diagnostics = [],
                AssemblyBytes = null
            };
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: $"WorkspaceAssembly_{Guid.NewGuid():N}",
            syntaxTrees: syntaxTrees,
            references: BuildReferences(additionalAssemblyPaths),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);

        return new CompilationResult
        {
            Success = emitResult.Success,
            Diagnostics = [.. emitResult.Diagnostics],
            AssemblyBytes = emitResult.Success ? stream.ToArray() : null
        };
    }

    /// <summary>
    /// Builds the complete reference list.
    /// </summary>
    private static IEnumerable<MetadataReference> BuildReferences(
        IEnumerable<string>? additionalAssemblyPaths)
    {
        var references = new List<MetadataReference>(_frameworkReferences.Value);

        if (additionalAssemblyPaths == null)
            return references;

        foreach (var assemblyPath in additionalAssemblyPaths)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                continue;

            if (!File.Exists(assemblyPath))
                continue;

            try
            {
                references.Add(MetadataReference.CreateFromFile(assemblyPath));
            }
            catch
            {
                // Ignore invalid assemblies
            }
        }

        return references;
    }

    /// <summary>
    /// Loads framework assemblies from the current runtime.
    /// Executed only once.
    /// </summary>
    private static IReadOnlyList<MetadataReference> LoadFrameworkReferences()
    {
        try
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

            if (!string.IsNullOrWhiteSpace(trustedAssemblies))
            {
                return [.. trustedAssemblies
                    .Split(Path.PathSeparator)
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(path =>
                    {
                        try
                        {
                            return MetadataReference.CreateFromFile(path);
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(r => r is not null)
                    .Cast<MetadataReference>()];
            }
        }
        catch
        {
            // Fall back to minimal references
        }

        return LoadMinimalReferences();
    }

    /// <summary>
    /// Fallback reference loading for environments where
    /// TRUSTED_PLATFORM_ASSEMBLIES is unavailable.
    /// </summary>
    private static IReadOnlyList<MetadataReference> LoadMinimalReferences()
    {
        var references = new List<MetadataReference>();

        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(Task).Assembly
        };

        foreach (var assembly in assemblies)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(
                            assembly.Location));
                }
            }
            catch
            {
                // Ignore failures
            }
        }

        return references;
    }
}