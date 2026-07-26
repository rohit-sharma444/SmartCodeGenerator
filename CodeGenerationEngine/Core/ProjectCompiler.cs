using Microsoft.CodeAnalysis;

namespace CodeGenerationEngine.Core;

/// <summary>Outcome of compiling a whole workspace.</summary>
/// <param name="Success">Whether the compilation ran AND produced no errors.</param>
/// <param name="FileCount">How many .cs files were compiled.</param>
/// <param name="Errors">Formatted error lines, capped at <see cref="ProjectCompiler.MaxReportedErrors"/>.</param>
/// <param name="TotalErrors">How many errors there were in total.</param>
public sealed record ProjectCompilationResult(
    bool Success,
    int FileCount,
    IReadOnlyList<string> Errors,
    int TotalErrors)
{
    public string Report()
    {
        if (FileCount == 0)
        {
            return Success
                ? "No C# files found to validate."
                : "No C# files found to validate: the folder does not exist.";
        }

        if (Success)
            return $"{FileCount} file(s) compile against the runtime reference set.";

        var lines = new List<string>
        {
            $"{TotalErrors} error(s) in {FileCount} file(s):"
        };

        lines.AddRange(Errors);

        if (TotalErrors > Errors.Count)
            lines.Add($"... and {TotalErrors - Errors.Count} more.");

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Compiles every .cs file under a folder as one assembly and reports real
/// diagnostics. This replaces a handler that returned the literal string
/// "Project compilation successful (stub)."
/// </summary>
/// <remarks>
/// References come from the runtime reference set, not the project's
/// PackageReferences, so a workspace using EF Core or WPF types will report
/// unresolved-type errors that `dotnet build` would not. That is why the report
/// says "compile against the runtime reference set" rather than "builds".
/// </remarks>
public static class ProjectCompiler
{
    public const int MaxReportedErrors = 20;

    public static ProjectCompilationResult CompileWorkspace(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new ProjectCompilationResult(false, 0, [], 0);

        var files = Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildArtifact(root, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return new ProjectCompilationResult(true, 0, [], 0);

        var sources = new List<(string Path, string Code)>();

        foreach (var file in files)
        {
            try
            {
                sources.Add((file, File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable file is not a compile error; skip it rather than
                // failing the whole validation.
            }
        }

        var result = Compiler.CompileSources(sources);

        var errors = result.Diagnostics
            // CS8805/CS5001 are artifacts of compiling as a library: a generated
            // workspace's Program.cs uses top-level statements. Neither is the user's defect.
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        && d.Id is not ("CS8805" or "CS5001"))
            .ToList();

        return new ProjectCompilationResult(
            Success: errors.Count == 0,
            FileCount: sources.Count,
            Errors: [.. errors.Take(MaxReportedErrors).Select(Format)],
            TotalErrors: errors.Count);
    }

    private static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var name = string.IsNullOrEmpty(span.Path) ? "(unknown)" : Path.GetFileName(span.Path);

        return $"{name}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): "
            + $"error {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    // bin/ and obj/ hold generated copies of the same types, which would report as
    // CS0101 duplicates against the user's own files. DeletedClasses holds files the
    // user deliberately moved out of the way via `delete class`, which does not rename
    // the type inside them -- so a live class of the same name would otherwise collide
    // with its own deleted predecessor as a CS0101. (ClassValidator.cs and
    // ProjectManager.cs exclude the same folder for the same reason.)
    //
    // bin/obj are tested against the path RELATIVE to root, not the whole path, so a
    // workspace whose own root happens to sit under a directory literally named "bin"
    // or "obj" does not have every one of its own files excluded. Both '\' and '/' are
    // accepted as separators since '/' is legal in a Windows path too.
    private static bool IsBuildArtifact(string root, string path)
    {
        if (path.Contains("DeletedClasses", StringComparison.OrdinalIgnoreCase))
            return true;

        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(s =>
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
