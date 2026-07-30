using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery;
using DomainDiscovery.Model;
using DomainDiscovery.Review;

namespace CodeGenerator.App;

/// <summary>
/// Handles `build app: &lt;description&gt;`. Runs domain discovery, prints the
/// recognized topic and candidate model, and drives the review loop. Code
/// generation from the approved model is added in a later phase.
/// </summary>
internal sealed class BuildAppCommand
{
    private const string Prefix = "build app:";

    public static bool IsBuildApp(string input)
        => input.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public void Handle(string input, string workspaceRoot)
    {
        var description = input.TrimStart()[Prefix.Length..].Trim();
        if (description.Length == 0)
        {
            Console.WriteLine("Usage: build app: <description of the system you want>");
            return;
        }

        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");
        var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDir);

        Console.WriteLine("Discovering domain (this may fetch from Wikipedia the first time)...");
        DiscoveryResult result = pipeline.Discover(description);

        if (result.Topic.IsRecognized)
        {
            //Console.WriteLine($"Recognized topic: {result.Topic.Title} " +
            //                  $"(confidence {result.Topic.Confidence:P0}) — {result.Topic.ArticleUrl}");

            // Where the rest of the classes came from. Without this a Borrower appearing in a
            // library model looks like a guess, when it was read out of an article by name.
            if (result.Topic.SupportingTitles.Count > 0)
                Console.WriteLine("Also read: " + string.Join(", ", result.Topic.SupportingTitles));
        }
        else
            Console.WriteLine("No topic recognized online — showing seed entities only. " +
                              "You can add entities by hand below.");

        var appName = DomainAppName.FromDescription(description);
        RunReviewLoop(result.Model, appName, workspaceRoot);
    }

    private static void RunReviewLoop(CandidateDomainModel model, string appName, string workspaceRoot)
    {
        while (true)
        {
            PrintModel(model);
            Console.WriteLine("  [a]pprove [target] [sqlite]   add <name>   remove <name>   rename <old> <new>");
            Console.WriteLine("  field <entity> <name>[:<type>]   unfield <entity> <name>   why [name]   [c]ancel");
            Console.Write("> ");
            var input = Console.ReadLine();
            if (input is null)   // stdin closed / EOF — treat as cancel, never busy-loop
            {
                Console.WriteLine("Input closed. Cancelled — nothing was generated.");
                return;
            }

            var line = input.Trim();

            if (line.Length == 0)
                continue;

            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts[0].ToLowerInvariant();

            switch (verb)
            {
                case "a":
                case "approve":
                    if (model.Entities.Count == 0)
                    {
                        Console.WriteLine("Nothing to generate — the model is empty.");
                        return;
                    }

                    var target = ResolveTarget(parts, Console.IsInputRedirected, Console.In);
                    if (target is null)
                    {
                        Console.WriteLine(
                            $"Unknown target '{parts[1]}'. Valid: {string.Join(", ", TargetScaffolders.Keys)}");
                        break;
                    }

                    var persistence = ResolvePersistence(parts);
                    if (persistence is null)
                    {
                        Console.WriteLine("Unknown persistence option. Valid: json, sqlite.");
                        break;
                    }

                    if (!IsPersistenceAllowed(target, persistence.Value))
                    {
                        Console.WriteLine(
                            "SQLite persistence isn't supported for the WPF target yet — it stays JSON.");
                        break;
                    }

                    var scaffold = new AppScaffolder()
                        .Scaffold(model, appName, workspaceRoot, target, persistence.Value);
                    if (scaffold.Success)
                    {
                        Console.WriteLine($"Generated project at {scaffold.ProjectPath}");
                        if (scaffold.TestProjectPath is not null)
                            Console.WriteLine($"Generated test project at {scaffold.TestProjectPath} — run with `dotnet test`.");
                        Console.WriteLine("");
                        //foreach (var file in scaffold.FilesWritten)
                        //    Console.WriteLine($"  {Path.GetRelativePath(scaffold.ProjectPath, file)}");
                    }
                    else
                    {
                        Console.WriteLine($"Generation failed: {scaffold.Message}");
                    }
                    return;

                case "c":
                case "cancel":
                    Console.WriteLine("Cancelled. Nothing was generated.");
                    return;

                case "add" when parts.Length >= 2:
                    Report(ModelEditor.AddEntity(model, parts[1]), $"Added '{parts[1]}'.", "Could not add (duplicate or invalid).");
                    break;

                case "remove" when parts.Length >= 2:
                    Report(ModelEditor.RemoveEntity(model, parts[1]), $"Removed '{parts[1]}'.", "No such entity.");
                    break;

                case "rename" when parts.Length >= 3:
                    Report(ModelEditor.RenameEntity(model, parts[1], parts[2]), $"Renamed to '{parts[2]}'.", "Could not rename (missing or name in use).");
                    break;

                case "field" when parts.Length >= 3:
                    var spec = FieldSpec.Parse(parts[2]);
                    Report(
                        ModelEditor.AddField(model, parts[1], spec.Name, spec.Type),
                        $"Added '{spec.Name}' to {parts[1]}.",
                        "Could not add field. The entity must exist; the name must be new, a valid "
                        + "identifier, and neither 'Id' (generated) nor the entity's own name; "
                        + "the type must be string, int, decimal, bool or DateTime.");
                    break;

                case "unfield" when parts.Length >= 3:
                    Report(
                        ModelEditor.RemoveField(model, parts[1], parts[2]),
                        $"Removed '{parts[2]}' from {parts[1]}.",
                        "Could not remove field (no such field, or Name, which every entity keeps).");
                    break;

                case "why":
                    foreach (var whyLine in WhyLines(model, parts))
                        Console.WriteLine(whyLine);
                    break;

                default:
                    Console.WriteLine("Unrecognized command.");
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves the output target for an <c>approve</c> command. An inline
    /// <c>approve &lt;target&gt;</c> wins (null when the key is unknown). With no inline key,
    /// redirected input never prompts and defaults to WPF (so piped scripts are unchanged);
    /// interactive input is prompted, blank meaning WPF, and re-prompts on an unknown key.
    /// </summary>
    internal static ITargetScaffolder? ResolveTarget(string[] parts, bool inputRedirected, TextReader reader)
    {
        if (parts.Length >= 2)
            return TargetScaffolders.TryResolve(parts[1], out var inline) ? inline : null;

        if (inputRedirected)
            return TargetScaffolders.Default;

        while (true)
        {
            Console.Write($"Target [{string.Join("/", TargetScaffolders.Keys)}] (default wpf): ");
            var line = reader.ReadLine()?.Trim() ?? string.Empty;

            if (line.Length == 0)
                return TargetScaffolders.Default;

            if (TargetScaffolders.TryResolve(line, out var chosen))
                return chosen;

            Console.WriteLine("Unknown target. Valid: " + string.Join(", ", TargetScaffolders.Keys));
        }
    }

    /// <summary>The persistence backend an <c>approve</c> line selects. No third token means JSON;
    /// <c>sqlite</c>/<c>json</c> (case-insensitively) select explicitly; any other token is invalid
    /// (returns null).</summary>
    internal static PersistenceKind? ResolvePersistence(string[] parts)
    {
        if (parts.Length < 3)
            return PersistenceKind.Json;

        return parts[2].ToLowerInvariant() switch
        {
            "sqlite" => PersistenceKind.Sqlite,
            "json" => PersistenceKind.Json,
            _ => null,
        };
    }

    /// <summary>SQLite is available for every target except WPF, whose view models read JsonStore
    /// directly (off the shared seam).</summary>
    internal static bool IsPersistenceAllowed(ITargetScaffolder target, PersistenceKind persistence)
        => !(persistence == PersistenceKind.Sqlite
             && string.Equals(target.Key, "wpf", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The lines a <c>why [name]</c> line produces: the whole report when no name is given,
    /// that name's trail otherwise. Internal and pure for the same reason
    /// <see cref="ResolveTarget"/> is — the review loop itself reads the console and cannot be
    /// driven from a test, so the decidable part is lifted out of it.
    /// </summary>
    /// <remarks>
    /// The loop splits its input into at most three parts, so a two-word name arrives in
    /// pieces and has to be put back together before it can be matched.
    /// </remarks>
    internal static IReadOnlyList<string> WhyLines(CandidateDomainModel model, string[] parts)
    {
        var name = string.Join(' ', parts.Skip(1)).Trim();

        return name.Length == 0
            ? DecisionReport.All(model)
            : DecisionReport.ForName(model, name);
    }

    private static void Report(bool changed, string ok, string fail)
        => Console.WriteLine(changed ? ok : fail);

    private static void PrintModel(CandidateDomainModel model)
    {
        Console.WriteLine();
        Console.WriteLine($"Candidate model ({model.Entities.Count} entities):");
        foreach (var entity in model.Entities)
        {
            var fields = entity.Fields.Count == 0
                ? string.Empty
                : " { " + string.Join(", ", entity.Fields.Select(f => $"{f.Name}:{f.Type}")) + " }";

            // A class with no mined relationships prints exactly as it always has; one with
            // any gets its targets appended, so the review loop is where a user first sees
            // that RelationshipMiner connected two of their classes.
            var relationships = entity.Relationships.Count == 0
                ? string.Empty
                : "  -> " + string.Join(", ", entity.Relationships.Select(r => r.Target));

            Console.WriteLine($"  - {entity.Name} [{entity.Provenance}]{fields}{relationships}");
        }

        // Every entity is scaffolded with a typed repository exposing these CRUD functions,
        // so the review loop shows the full shape of what approval will generate.
        if (model.Entities.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Each entity also gets a <Entity>Repository with CRUD: "
                + "GetAll, GetById, Create, Update, Delete");
        }

        // Rejections, demoted suggestions and skipped nouns, grouped by the reason each was
        // given. This replaces a bare list of rejected names that said nothing about why.
        foreach (var line in DecisionReport.Summary(model))
            Console.WriteLine(line);
    }
}
