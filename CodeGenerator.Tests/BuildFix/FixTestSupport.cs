using CodeGenerationEngine.Helper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.Tests.BuildFix;

internal static class FixTestSupport
{
    public static ProcessResult Result(string output, int exit = 1)
        => new(exit, output, string.Empty, false);

    /// <summary>A short temp folder: a long one trips MAX_PATH in builds.</summary>
    public static string TempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "cgfix-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string Write(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The error messages Roslyn reports for these sources, compiled together.</summary>
    public static IReadOnlyList<string> CompileErrors(params string[] sources)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "fixcheck",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            trusted,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id + ": " + d.GetMessage())
            .ToList();
    }
}
