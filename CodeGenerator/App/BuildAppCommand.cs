using AppScaffolding;
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
            Console.WriteLine($"Recognized topic: {result.Topic.Title} " +
                              $"(confidence {result.Topic.Confidence:P0}) — {result.Topic.ArticleUrl}");
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
            Console.WriteLine("  [a]pprove   add <name>   remove <name>   rename <old> <new>");
            Console.WriteLine("  field <entity> <name>[:<type>]   unfield <entity> <name>   [c]ancel");
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

                    var scaffold = new AppScaffolder().Scaffold(model, appName, workspaceRoot);
                    if (scaffold.Success)
                    {
                        Console.WriteLine($"Generated project at {scaffold.ProjectPath}");
                        foreach (var file in scaffold.FilesWritten)
                            Console.WriteLine($"  {Path.GetRelativePath(scaffold.ProjectPath, file)}");
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

                default:
                    Console.WriteLine("Unrecognized command.");
                    break;
            }
        }
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
            Console.WriteLine($"  - {entity.Name} [{entity.Provenance}]{fields}");
        }

        if (model.Suggestions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Also found - 'add <name>' to include: "
                + string.Join(", ", model.Suggestions.Select(e => e.Name)));
        }

        if (model.Rejected.Count > 0)
        {
            Console.WriteLine("Not classes: " + string.Join(", ", model.Rejected));
        }
    }
}
