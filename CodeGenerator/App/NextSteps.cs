using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.App;

/// <summary>
/// What to tell the user to do with the app that was just written to disk.
/// </summary>
/// <remarks>
/// Generation used to end at "Generated project at &lt;path&gt;", which leaves a first-time user
/// holding a folder and guessing. Each line here is a command they can paste.
/// <para>
/// Pure and tested, like <see cref="BuildReport"/>: which lines a target earns is a decision,
/// and decisions do not belong in the console loop.
/// </para>
/// </remarks>
internal static class NextSteps
{
    /// <summary>Where the scaffolder puts imported and sample rows, relative to the project.</summary>
    private static readonly string SeedFolder = Path.Combine("Data", "Seed");

    /// <summary>Column at which the command in each row starts, so they line up under each other.</summary>
    private const int LabelWidth = -13;

    public static IReadOnlyList<string> Lines(
        ITargetScaffolder target,
        string projectPath,
        string? testProjectPath,
        IReadOnlyList<string> filesWritten)
    {
        var lines = new List<string> { "Next steps:" };

        if (target.ProjectType == DotNetProjectType.ClassLibrary)
        {
            // `dotnet run` on a class library fails with MSB4057. Offering it would send the
            // user to an error we already know they will hit.
            lines.Add(Row("Reference it", $"add a ProjectReference to {projectPath}"));
        }
        else
        {
            lines.Add(Row("Run it", $"dotnet run --project {projectPath}"));

            // The port is picked at startup and cannot be printed here, but a user watching a
            // running API with no address is stuck for no reason.
            if (target.ProjectType == DotNetProjectType.WebApi)
                lines.Add(Row(string.Empty, "the URL it listens on is printed at startup"));
        }

        // Only when rows were actually written: `noseed` with no imported data leaves no such
        // folder, and pointing at a path that does not exist is worse than saying nothing.
        //
        // The folder, separator and all: Data\SeedData.cs is written by every app, and a bare
        // "Data\Seed" match found it, so this pointed at a folder that did not exist.
        var seeds = filesWritten
            .Where(f => f.Contains(SeedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (seeds.Count > 0)
        {
            lines.Add(Row("Seed data", Path.Combine(projectPath, SeedFolder) + Path.DirectorySeparatorChar));
            lines.Add(Row(string.Empty, SeedNote(target, projectPath, seeds, filesWritten)));
        }

        if (testProjectPath is not null)
            lines.Add(Row("Tests", $"dotnet test {testProjectPath}"));

        return lines;
    }

    /// <summary>
    /// What happens to the seed files, said truthfully. The store is seed-if-empty
    /// (<c>SeedData.EnsureSeeded</c>): after the first run it holds data, so editing the seed files
    /// and re-running changes nothing until the store file is deleted. This line used to say
    /// "(edit these, then re-run)", which was true only once.
    /// </summary>
    private static string SeedNote(
        ITargetScaffolder target, string projectPath, IReadOnlyList<string> seeds, IReadOnlyList<string> filesWritten)
    {
        // A class library is never run: its store lives beside whichever app references it.
        if (target.ProjectType == DotNetProjectType.ClassLibrary)
            return "loaded by SeedData.EnsureSeeded() into an empty store, in the app that references it";

        // Where the built app keeps its store: AppContext.BaseDirectory, i.e. the Debug output.
        var windows = target.ProjectType is DotNetProjectType.Wpf or DotNetProjectType.WinForms;
        var output = Path.Combine(projectPath, "bin", "Debug", windows ? "net10.0-windows" : "net10.0");

        // SQLite keeps one database file named after the project; the JSON store one file per class.
        var sqlite = filesWritten.Any(f => string.Equals(
            Path.GetFileName(f), "AppDbContext.cs", StringComparison.OrdinalIgnoreCase));

        var store = sqlite
            ? Path.Combine(output, Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar)) + ".db")
            : seeds.Count == 1
                ? Path.Combine(output, Path.GetFileName(seeds[0]))
                : $"the <Class>.json files in {output}";

        return $"loaded only into an empty store; to reload them after the first run, delete {store}";
    }

    private static string Row(string label, string command)
        => $"  {label,LabelWidth}{command}";
}
