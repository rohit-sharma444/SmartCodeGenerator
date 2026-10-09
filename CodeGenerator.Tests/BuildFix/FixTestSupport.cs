using CodeGenerationEngine.Helper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.Tests.BuildFix;

internal static class FixTestSupport
{
    public static ProcessResult Result(string output, int exit = 1)
        => new(exit, output, string.Empty, false);

    private static readonly System.Collections.Concurrent.ConcurrentBag<string> Created = [];

    /// <summary>A short temp folder: a long one trips MAX_PATH in builds. Deleted when the test run ends.</summary>
    public static string TempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "cgfix-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        Created.Add(root);
        return root;
    }

    /// <summary>Deletes every folder <see cref="TempProject"/> made. Never throws: a lock only leaves a folder behind.</summary>
    public static void CleanUpTempProjects()
    {
        while (Created.TryTake(out var root))
            DeleteTree(root);
    }

    /// <summary>
    /// Deletes a folder and what is in it. A junction or symlink inside is removed as a link and is
    /// never followed, so what it points at is left alone.
    /// </summary>
    public static void DeleteTree(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);

            if (!info.Exists)
                return;

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                info.Delete(false);
                return;
            }

            foreach (var file in info.EnumerateFiles())
            {
                file.Attributes = FileAttributes.Normal;
                file.Delete();
            }

            foreach (var dir in info.EnumerateDirectories())
                DeleteTree(dir.FullName);

            info.Delete(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A lock on a temp folder: leave it for the OS temp cleaner.
        }
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
