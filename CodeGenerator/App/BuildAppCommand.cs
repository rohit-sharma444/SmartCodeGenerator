using AppScaffolding;
using AppScaffolding.Targets;
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
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
    private const string Command = "build app";

    /// <summary>
    /// Whether <paramref name="input"/> is a build-app line. Both <c>build app: &lt;description&gt;</c>
    /// and the bare <c>build app</c> count, the latter so a user with data need not invent a
    /// description first.
    /// </summary>
    /// <remarks>
    /// Deliberately not a bare prefix test: "build apple tracker" and "build application" both start
    /// with "build app". The character after the command must be a colon, whitespace, or nothing.
    /// <para>
    /// Widening this is safe because "build app" appears in no recognition source — not
    /// <c>LiteralCommandTable</c>, not a <c>CommandFront</c> guard, not <c>NlpCommandMapper</c> —
    /// only in the <c>CommandDictionary</c> help catalog. <c>CodeGeneratorStarter</c> tests this
    /// before the pipeline runs, so nothing downstream competes for the text.
    /// </para>
    /// </remarks>
    public static bool IsBuildApp(string input)
    {
        var text = (input ?? string.Empty).TrimStart();

        if (!text.StartsWith(Command, StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Length == Command.Length)
            return true;

        var next = text[Command.Length];
        return next == ':' || char.IsWhiteSpace(next);
    }

    /// <summary>Everything after the command word, with an optional colon removed.</summary>
    internal static string Tail(string input)
    {
        var text = (input ?? string.Empty).TrimStart();
        if (text.Length <= Command.Length)
            return string.Empty;

        var rest = text[Command.Length..];
        return (rest.StartsWith(':') ? rest[1..] : rest).Trim();
    }

    public void Handle(string input, string workspaceRoot)
    {
        var request = InlineDataSource.Parse(Tail(input));

        var description = request.Description;
        var source = request.Source;
        var normalize = request.Normalize;
        ITargetScaffolder? target = null;

        // Redirected stdin never prompts: existing piped scripts and E2E tests feed the review
        // loop directly, and a question at the front would break every one of them. They opt in
        // with the inline `from <format> <location>` form instead, and pick a target at `approve`.
        if (!Console.IsInputRedirected)
        {
            var outcome = BuildAppWizard.Run(
                request, Console.In, Console.Out, ApplicationTypeMenu.Choose);

            if (outcome.Decision == WizardDecision.Cancelled)
            {
                Console.WriteLine("Cancelled. Nothing was generated.");
                return;
            }

            description = outcome.Description;
            target = outcome.Target;
            source = outcome.Source;
            normalize = outcome.Normalize;
        }

        if (source is null)
        {
            HandleFromDescription(description, workspaceRoot, target);
            return;
        }

        HandleFromData(description, source, normalize, workspaceRoot, target);
    }

    /// <summary>The unchanged path: NLE seeds, Wikipedia topic, enrichment, review loop.</summary>
    private static void HandleFromDescription(
        string description, string workspaceRoot, ITargetScaffolder? target)
    {
        if (description.Length == 0)
        {
            Console.WriteLine("Usage: build app: <description of the system you want>");
            Console.WriteLine("   or: build app from csv <path>   (also txt, json, excel)");
            return;
        }

        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");
        var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDir);

        Console.WriteLine("Discovering domain (this may fetch from Wikipedia the first time)...");
        DiscoveryResult result = pipeline.Discover(description);

        if (result.Topic.IsRecognized)
        {
            // Where the rest of the classes came from. Without this a Borrower appearing in a
            // library model looks like a guess, when it was read out of an article by name.
            if (result.Topic.SupportingTitles.Count > 0)
                Console.WriteLine("Also read: " + string.Join(", ", result.Topic.SupportingTitles));
        }
        else
            Console.WriteLine("No topic recognized online — showing seed entities only. " +
                              "You can add entities by hand below.");

        RunReviewLoop(
            result.Model, DomainAppName.FromDescription(description), workspaceRoot,
            ImportedData.None, target);
    }

    /// <summary>
    /// The data path. No Wikipedia call at all: the schema is ground truth, so there is nothing to
    /// recognize, enrich or shape, and the run is fast and offline.
    /// </summary>
    private static void HandleFromData(
        string description, DataSourceSpec source, bool normalize, string workspaceRoot,
        ITargetScaffolder? target)
    {
        if (!DataSourceReaders.TryCreate(source.Kind, out var reader))
        {
            Console.WriteLine(DataSourceReaders.NotSupportedMessage(source.Kind));
            return;
        }

        IReadOnlyList<TabularTable> tables;
        try
        {
            tables = reader.Read(source);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            // The reader's message already says what was wrong in the user's own terms.
            Console.WriteLine(ex.Message);
            return;
        }

        // The file, not tables[0].Label: a workbook labels each table with its own sheet
        // ("shop.xlsx [Orders]"), and a check that covers every sheet must not claim to be about
        // the first one. Each finding still carries the sheet it came from.
        var fileLabel = Path.GetFileName(source.Location);

        Console.WriteLine(
            $"Read {tables.Count} table(s) from {fileLabel}: "
            + string.Join(", ", tables.Select(t => $"{t.Name} ({t.Rows.Count} rows)")));

        var report = CombineReports(tables);

        foreach (var line in NormalizationPrompt.Lines(
                     report, fileLabel, tables.Sum(t => t.Rows.Count)))
        {
            Console.WriteLine(line);
        }

        // A scripted run applies everything or nothing, so it stays deterministic; `normalize`
        // inline is how it opts in.
        var chosen = Console.IsInputRedirected
            ? (normalize ? report.Findings : [])
            : NormalizationPrompt.Choose(report, Console.In, Console.Out);

        if (chosen.Count > 0)
        {
            tables = NormalizationPlan.Apply(tables, chosen);
            Console.WriteLine($"Applied {chosen.Count} normalization change(s).");
        }

        // `chosen`, not nothing: every applied split gets a DecisionRecord, so `why OrderItem`
        // in the review loop can answer for a class the user never named.
        var model = DataModelBuilder.Build(tables, chosen);
        var imported = TabularImport.ToImportedData(tables);

        // Said out loud: a cap the user is not told about reads as "all your data is in there".
        foreach (var note in imported.Notes)
            Console.WriteLine(note);

        RunReviewLoop(model, AppNameFor(description, source), workspaceRoot, imported, target);
    }

    /// <summary>
    /// The findings of every table in one numbered list, so `n 2` means the same thing whichever
    /// table the finding came from. Notes are de-duplicated: the same skipped check reported once
    /// per table would be noise.
    /// </summary>
    internal static NormalizationReport CombineReports(IReadOnlyList<TabularTable> tables)
    {
        var findings = new List<NormalizationFinding>();
        var notes = new List<string>();

        foreach (var table in tables)
        {
            var report = NormalizationAnalyzer.Analyze(table);
            findings.AddRange(report.Findings);

            foreach (var note in report.Notes)
                if (!notes.Contains(note, StringComparer.Ordinal))
                    notes.Add(note);
        }

        return new NormalizationReport(findings, notes);
    }

    /// <summary>
    /// The project name. A description names the app when there is one; otherwise the source's base
    /// name goes through the SAME <see cref="DomainAppName.FromDescription"/> rule, so there is one
    /// naming rule in the product rather than a second one invented for this path.
    /// </summary>
    internal static string AppNameFor(string description, DataSourceSpec? source)
    {
        if (description.Length > 0)
            return DomainAppName.FromDescription(description);

        if (source is null)
            return DomainAppName.FromDescription(string.Empty);

        return DomainAppName.FromDescription(
            source.Kind == DataSourceKind.Sql
                ? source.SqlDatabase ?? string.Empty
                : Path.GetFileNameWithoutExtension(source.Location));
    }

    private static void RunReviewLoop(
        CandidateDomainModel model, string appName, string workspaceRoot,
        ImportedData importedData, ITargetScaffolder? target)
    {
        while (true)
        {
            PrintModel(model);

            // The target was settled before discovery ran, so what `approve` will produce has to
            // be visible here rather than being a surprise.
            if (target is not null)
                Console.WriteLine($"  Target: {ApplicationTypeMenu.LabelFor(target)}");

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

                    var chosenTarget = ResolveTarget(
                        parts, Console.IsInputRedirected, Console.In, target);
                    if (chosenTarget is null)
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

                    if (!IsPersistenceAllowed(chosenTarget, persistence.Value))
                    {
                        Console.WriteLine(
                            "SQLite persistence isn't supported for the WPF or Windows Forms "
                            + "targets yet — they stay JSON.");
                        break;
                    }

                    var scaffold = new AppScaffolder()
                        .Scaffold(model, appName, workspaceRoot, chosenTarget, persistence.Value,
                                  importedData);
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
                    Console.WriteLine(AddLine(model, parts));
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
    /// <c>approve &lt;target&gt;</c> wins (null when the key is unknown); a
    /// <paramref name="preselected"/> target — the one the wizard asked for — is next. With
    /// neither, redirected input defaults to WPF (so piped scripts are unchanged) and interactive
    /// input is prompted, blank meaning WPF, re-prompting on an unknown key.
    /// </summary>
    internal static ITargetScaffolder? ResolveTarget(
        string[] parts, bool inputRedirected, TextReader reader,
        ITargetScaffolder? preselected = null)
    {
        // A lone json/sqlite is persistence, not a target: `approve sqlite` is only meaningful
        // because the target was already chosen, and reading it as a target key is what used to
        // reject it as unknown.
        if (parts.Length >= 2 && !IsPersistenceToken(parts[1]))
            return TargetScaffolders.TryResolve(parts[1], out var inline) ? inline : null;

        if (preselected is not null)
            return preselected;

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

    /// <summary>Whether <paramref name="token"/> names a persistence backend rather than a
    /// target.</summary>
    internal static bool IsPersistenceToken(string token)
        => string.Equals(token, "sqlite", StringComparison.OrdinalIgnoreCase)
           || string.Equals(token, "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>The persistence backend an <c>approve</c> line selects. No token means JSON;
    /// <c>sqlite</c>/<c>json</c> select explicitly, in either the second or third position (the
    /// second is how <c>approve sqlite</c> reads once the wizard has settled the target); any
    /// other token is invalid (returns null).</summary>
    internal static PersistenceKind? ResolvePersistence(string[] parts)
    {
        var token = parts.Length >= 3
            ? parts[2]
            : parts.Length == 2 && IsPersistenceToken(parts[1]) ? parts[1] : null;

        if (token is null)
            return PersistenceKind.Json;

        return token.ToLowerInvariant() switch
        {
            "sqlite" => PersistenceKind.Sqlite,
            "json" => PersistenceKind.Json,
            _ => null,
        };
    }

    /// <summary>The targets whose view models read JsonStore directly, off the shared store
    /// seam, and so cannot honour a SQLite backend.</summary>
    private static readonly string[] JsonOnlyTargets = ["wpf", "winforms"];

    /// <summary>SQLite is available for every target except the two desktop ones, whose shared
    /// view models read JsonStore directly (off the shared seam).</summary>
    internal static bool IsPersistenceAllowed(ITargetScaffolder target, PersistenceKind persistence)
        => persistence != PersistenceKind.Sqlite
           || !JsonOnlyTargets.Contains(target.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds the entity an <c>add &lt;name&gt;</c> line asks for, and returns what to report.
    /// Internal and pure for the same reason <see cref="WhyLines"/> is.
    /// </summary>
    /// <remarks>
    /// Two things the previous inline version got wrong. It passed only <c>parts[1]</c>, so the
    /// loop's three-way split silently reduced <c>add Cash register</c> to "Cash"; and it echoed
    /// the typed text, which hid any difference between what was asked for and what was created.
    /// </remarks>
    internal static string AddLine(CandidateDomainModel model, string[] parts)
    {
        var name = string.Join(' ', parts.Skip(1)).Trim();

        return ModelEditor.AddEntity(model, name, out var added)
            ? $"Added '{added}'."
            : "Could not add (duplicate or invalid).";
    }

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
