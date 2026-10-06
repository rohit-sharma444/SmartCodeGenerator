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
/// Compiles the .cs files under a folder, one assembly per project (files under no project
/// together), with each project's implicit global usings and project references, and reports
/// real diagnostics. This replaces a handler that returned the literal string
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

        // One compilation per project, as dotnet build does: compiled as one assembly, two projects
        // each declaring Shop.Customer reported a CS0101 that does not exist. Files under no
        // project are compiled together, as before.
        var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !IsBuildArtifact(root, f))
            .Select(f => new ProjectInfo(f))
            .ToList();

        var groups = files
            .GroupBy(f => OwnerOf(f, projects) ?? Loose)
            .ToDictionary(g => g.Key, g => g.ToList());

        var errors = new List<Diagnostic>();
        var fileCount = 0;
        var built = new Dictionary<string, MetadataReference?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (project, projectFiles) in Ordered(groups))
        {
            var sources = new List<(string Path, string Code)>();

            foreach (var file in projectFiles)
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

            fileCount += sources.Count;
            if (sources.Count == 0)
                continue;

            // The SDK's implicit global usings: dotnet new console writes Console.WriteLine with no
            // using, so without these every fresh console project reported CS0103 'Console'.
            var usings = project.GlobalUsings();
            if (usings.Count > 0)
                sources.Add((ImplicitUsingsPath,
                    string.Concat(usings.Select(u => $"global using global::{u};{Environment.NewLine}"))));

            var references = project.References
                .Select(r => built.TryGetValue(r, out var image) ? image : null)
                .OfType<MetadataReference>()
                .ToList();

            var result = Compiler.CompileSources(sources, extraReferences: references);

            if (project != Loose)
                built[project.Path] = result.AssemblyBytes is { } bytes
                    ? MetadataReference.CreateFromImage(bytes)
                    : null;

            errors.AddRange(result.Diagnostics
                // CS8805/CS5001 are artifacts of compiling as a library: a generated
                // workspace's Program.cs uses top-level statements. Neither is the user's defect.
                .Where(d => d.Severity == DiagnosticSeverity.Error
                            && d.Id is not ("CS8805" or "CS5001")
                            // A global using of a namespace outside the runtime reference set
                            // (ASP.NET Core in a test host) is the reference set's limit, not the
                            // user's code.
                            && d.Location.SourceTree?.FilePath != ImplicitUsingsPath));
        }

        return new ProjectCompilationResult(
            Success: errors.Count == 0,
            FileCount: fileCount,
            Errors: [.. errors.Take(MaxReportedErrors).Select(Format)],
            TotalErrors: errors.Count);
    }

    private const string ImplicitUsingsPath = "ImplicitUsings.g.cs";

    // The files under no project.
    private static readonly ProjectInfo Loose = new(string.Empty);

    // The project whose folder holds the file most closely, or null.
    private static ProjectInfo? OwnerOf(string file, List<ProjectInfo> projects) =>
        projects
            .Where(p => file.StartsWith(p.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Folder.Length)
            .FirstOrDefault();

    // Referenced projects first, so a project compiles against what they built. A cycle (which
    // dotnet build refuses anyway) is broken by taking the rest in name order.
    private static IEnumerable<(ProjectInfo Project, List<string> Files)> Ordered(
        Dictionary<ProjectInfo, List<string>> groups)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = groups.Keys.Where(p => p != Loose).OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();

        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(p => p.References.All(r => done.Contains(r) || !groups.Keys.Any(g => g.Path.Equals(r, StringComparison.OrdinalIgnoreCase))))
                ?? pending[0];
            pending.Remove(next);
            done.Add(next.Path);
            yield return (next, groups[next]);
        }

        if (groups.TryGetValue(Loose, out var loose))
            yield return (Loose, loose);
    }

    /// <summary>What a .csproj says about how its files compile: its SDK, implicit usings and
    /// project references.</summary>
    private sealed class ProjectInfo
    {
        private readonly System.Xml.Linq.XDocument? _xml;

        public ProjectInfo(string path)
        {
            Path = path;
            Folder = string.IsNullOrEmpty(path) ? string.Empty : System.IO.Path.GetDirectoryName(path)!;

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                _xml = System.Xml.Linq.XDocument.Load(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                _xml = null;
            }

            References = Elements("ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>()
                .Select(r => System.IO.Path.GetFullPath(System.IO.Path.Combine(Folder, r.Replace('\\', System.IO.Path.DirectorySeparatorChar))))
                .ToList();
        }

        public string Path { get; }

        public string Folder { get; }

        public IReadOnlyList<string> References { get; } = [];

        private IEnumerable<System.Xml.Linq.XElement> Elements(string name) =>
            _xml?.Descendants().Where(e => e.Name.LocalName == name) ?? [];

        private string? Property(string name) =>
            Elements(name).Select(e => e.Value.Trim()).LastOrDefault();

        private bool IsTrue(string name) =>
            Property(name) is { } value && value.Equals("true", StringComparison.OrdinalIgnoreCase);

        /// <summary>The global usings the SDK adds, as its props files list them, plus the
        /// project's own Using items.</summary>
        public IReadOnlyList<string> GlobalUsings()
        {
            if (_xml is null)
                return [];

            var usings = new List<string>();
            var implicitUsings = Property("ImplicitUsings");

            if (implicitUsings is not null
                && (implicitUsings.Equals("enable", StringComparison.OrdinalIgnoreCase)
                    || implicitUsings.Equals("true", StringComparison.OrdinalIgnoreCase)))
            {
                var sdk = ((string?)_xml.Root?.Attribute("Sdk") ?? string.Empty).Trim();

                usings.AddRange(["System", "System.Collections.Generic", "System.IO", "System.Linq",
                    "System.Net.Http", "System.Threading", "System.Threading.Tasks"]);

                if (sdk.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                    usings.AddRange(["System.Net.Http.Json", "Microsoft.AspNetCore.Builder",
                        "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Routing",
                        "Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection",
                        "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging"]);

                if (sdk.Equals("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
                    usings.AddRange(["Microsoft.Extensions.Configuration",
                        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Hosting",
                        "Microsoft.Extensions.Logging"]);

                if (IsTrue("UseWindowsForms"))
                    usings.AddRange(["System.Drawing", "System.Windows.Forms"]);

                if (IsTrue("UseWPF"))
                    usings.RemoveAll(u => u is "System.IO" or "System.Net.Http");
            }

            foreach (var item in Elements("Using"))
            {
                if ((string?)item.Attribute("Include") is { } include && item.Attribute("Alias") is null
                    && item.Attribute("Static") is null)
                    usings.Add(include.Trim());
                if ((string?)item.Attribute("Remove") is { } remove)
                    usings.RemoveAll(u => u.Equals(remove.Trim(), StringComparison.Ordinal));
            }

            return [.. usings.Distinct(StringComparer.Ordinal)];
        }
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
