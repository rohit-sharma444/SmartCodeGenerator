using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Targets;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeIntelligence;
using CodeIntelligence.Resolution;
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

    public void Handle(string input, string workspaceRoot, AiLayer? ai = null)
    {
        // Built here rather than at CLI startup: it embeds ~30 phrases, and a session
        // that never runs `build app` should not pay for that. Null whenever AI is off
        // or unavailable, which the review loop treats as "no suggestion".
        var reviewAi = ai?.CreatePhraseResolver(
            ReviewCommandPhrases.All,
            ReviewCommandPhrases.TopK,
            ReviewCommandPhrases.ConfidenceFloor,
            ReviewCommandPhrases.MinTopScore);

        // Options come off the line BEFORE the data source is parsed: a location is taken as the
        // rest of the line, so anything left after it would be swallowed by the path.
        var (remaining, inline) = InlineCustomization.Split(Tail(input), AppCustomization.Default);

        var request = InlineDataSource.Parse(remaining);

        var description = request.Description;
        var source = request.Source;
        var normalize = request.Normalize;
        var customization = inline;
        ITargetScaffolder? target = null;

        // The browser takes the whole interview when there is a person at the console. Redirected
        // stdin never reaches here: existing piped scripts and E2E tests feed the review loop
        // directly, and a page in front of them would break every one of them. They opt in with
        // the inline `from <format> <location>` form instead, and pick a target at `approve`.
        if (UseWebWorkflow(
                Console.IsInputRedirected,
                Environment.GetEnvironmentVariable(NoWebVariable) == "1"))
        {
            var webStart = Merge(CustomizationDefaults.Load(workspaceRoot), inline);

            var session = new Web.Workflow.BuildSession(
                description, workspaceRoot, webStart, source);

            if (Web.BuildAppWebHost.TryStart(session, out var host))
            {
                using (host)
                {
                    Console.WriteLine($"  Opening the build workflow at {host.Url}");

                    if (!WebAppStart.OpenBrowser(host.Url))
                        Console.WriteLine("  No default browser — open that address yourself.");

                    Console.WriteLine("  Ctrl-C here cancels.");
                    Console.WriteLine("");

                    var webOutcome = Web.BuildAppWebHost.Run(session, Interrupt.Token);

                    foreach (var line in webOutcome.Lines)
                        Console.WriteLine(line);

                    Console.WriteLine("");
                }

                // The workflow generated, cancelled or was abandoned in the browser. Either way
                // this command is over — nothing below runs.
                return;
            }

            // Could not bind loopback. Say so and fall through to the console wizard rather than
            // leaving the user with no way to run the command at all.
            Output.Warning(
                "Could not start the build workflow page — using the console wizard instead.");
        }

        if (!Console.IsInputRedirected)
        {
            // The remembered profile is read only here: a redirected-stdin run must not have its
            // output changed by a file it never mentioned, which is the same determinism
            // `normalize` protects. Inline options win over it — they were typed just now.
            var start = Merge(CustomizationDefaults.Load(workspaceRoot), inline);

            var outcome = BuildAppWizard.Run(
                request, Console.In, Console.Out, ApplicationTypeMenu.Choose, start, Menu.Choose);

            if (outcome.Decision == WizardDecision.Cancelled)
            {
                Console.WriteLine("Cancelled. Nothing was generated.");
                return;
            }

            description = outcome.Description;
            target = outcome.Target;
            source = outcome.Source;
            normalize = outcome.Normalize;
            customization = outcome.Customization;

            // Saved even when the step was declined: what the user settled on this time is what
            // the next run should offer, and declining settled it too.
            CustomizationDefaults.Save(workspaceRoot, customization);
        }

        if (source is null)
        {
            HandleFromDescription(description, workspaceRoot, target, customization, reviewAi);
            return;
        }

        HandleFromData(
            description, source, normalize, workspaceRoot, target, customization, reviewAi);
    }

    /// <summary>
    /// The saved profile with anything named on the command line layered over it.
    /// </summary>
    /// <remarks>
    /// A setting counts as named inline when it differs from
    /// <see cref="AppCustomization.Default"/>, which is exact for most of the inline vocabulary:
    /// there is no way to type an empty company or the `light`-less "no theme", and each flag names
    /// the opposite of its default.
    /// <para>
    /// <see cref="AppCustomization.Layout"/> is the exception, and the reason it is nullable:
    /// `layout tabs` is typeable and EQUALS the default, so a differs-from-default comparison would
    /// read it as unmentioned and hand back a remembered layout instead. It layers on null — "not
    /// named" — rather than on inequality. Any future option whose value can equal the default
    /// needs the same treatment.
    /// </para>
    /// </remarks>
    internal static AppCustomization Merge(AppCustomization saved, AppCustomization inline)
    {
        var defaults = AppCustomization.Default;

        return saved with
        {
            Company = Named(inline.Company, defaults.Company, saved.Company),
            Theme = Named(inline.Theme, defaults.Theme, saved.Theme),
            Accent = Named(inline.Accent, defaults.Accent, saved.Accent),
            TitlePattern = Named(inline.TitlePattern, defaults.TitlePattern, saved.TitlePattern),
            IconPath = Named(inline.IconPath, defaults.IconPath, saved.IconPath),
            Validation = Named(inline.Validation, defaults.Validation, saved.Validation),
            Logging = Named(inline.Logging, defaults.Logging, saved.Logging),
            SampleSeedData =
                Named(inline.SampleSeedData, defaults.SampleSeedData, saved.SampleSeedData),
            NamespacePrefix = Named(inline.NamespacePrefix, defaults.NamespacePrefix, saved.NamespacePrefix),

            // Null-coalescing rather than Named: null IS "not named" for a layout, so no comparison
            // against the default is involved — see the remark above.
            Layout = inline.Layout ?? saved.Layout,
        };

        static T Named<T>(T inline, T fallback, T saved)
            => EqualityComparer<T>.Default.Equals(inline, fallback) ? saved : inline;
    }

    /// <summary>The unchanged path: NLE seeds, Wikipedia topic, enrichment, review loop.</summary>
    private static void HandleFromDescription(
        string description, string workspaceRoot, ITargetScaffolder? target,
        AppCustomization customization, PhraseResolver? reviewAi = null)
    {
        if (description.Length == 0)
        {
            Console.WriteLine("Usage: build app: <description of the system you want>");
            Console.WriteLine("   or: build app from csv <path>   (also txt, json, excel)");
            Console.WriteLine("Options (last on the line): company \"<name>\"  theme light|dark  "
                              + "accent #rrggbb  icon <path>  title \"<pattern>\"  "
                              + "novalidation  logging  noseed");
            return;
        }

        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");
        var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDir);

        // The longest silence in the tool: a first fetch for an unseen topic can take many seconds,
        // and an unmoving line reads as hung. Piped runs print the label once, exactly as before.
        DiscoveryResult result;

        Console.WriteLine("\n");
        using (Spinner.Start("Discovering domain (this may fetch from Wikipedia the first time)"))
        {
            result = pipeline.Discover(description);
        }

        if (result.Topic.IsRecognized)
        {
            // Where the rest of the classes came from. Without this a Borrower appearing in a
            // library model looks like a guess, when it was read out of an article by name.
            //if (result.Topic.SupportingTitles.Count > 0)
            //    Console.WriteLine("Also read: " + string.Join(", ", result.Topic.SupportingTitles));
        }
        else
            Console.WriteLine("No topic recognized online — showing seed classes only. " +
                              "You can add classes by hand below.");

        RunReviewLoop(
            result.Model, DomainAppName.FromDescription(description), workspaceRoot,
            ImportedData.None, target, customization, reviewAi);
    }

    /// <summary>
    /// The data path. No Wikipedia call at all: the schema is ground truth, so there is nothing to
    /// recognize, enrich or shape, and the run is fast and offline.
    /// </summary>
    private static void HandleFromData(
        string description, DataSourceSpec source, bool normalize, string workspaceRoot,
        ITargetScaffolder? target, AppCustomization customization,
        PhraseResolver? reviewAi = null)
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

        RunReviewLoop(
            model, AppNameFor(description, source), workspaceRoot, imported, target, customization,
            reviewAi);
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
        ImportedData importedData, ITargetScaffolder? target, AppCustomization customization,
        PhraseResolver? reviewAi = null)
    {
        while (true)
        {
            PrintModel(model);

            // The target was settled before discovery ran, so what `approve` will produce has to
            // be visible here rather than being a surprise.
            if (target is not null)
                Console.WriteLine($"  Target: {ApplicationTypeMenu.LabelFor(target)}");

            // Settled before discovery too, and for the same reason. Prints nothing at all when
            // nothing was customized.
            foreach (var line in CustomizationSummary.Lines(customization))
                Console.WriteLine(line);

            string[] parts;

            // The line as typed, kept for the AI suggestion below. Only the typed path
            // can reach the switch's default case — the picker composes verbs that all
            // have cases — so this stays empty on the picker path and no suggestion is
            // ever attempted there.
            var typedLine = string.Empty;

            if (UseTypedInput)
            {
                // The typed path is unchanged: SelectionPrompt cannot run without a console, and
                // piped scripts drive the loop through exactly these lines.
                Console.WriteLine();

                foreach (var hint in ReviewHints.Short)
                    Console.WriteLine(hint);

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

                // Handled before the switch so it costs no verb and cannot collide with a class
                // name: the short prompt is only defensible if the rest is one keystroke away.
                if (ReviewHints.IsMoreRequest(line))
                {
                    foreach (var hint in ReviewHints.Full)
                        Console.WriteLine(hint);

                    continue;
                }

                typedLine = line;
                parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                var picked = PickAction();

                if (picked is null)   // Esc on the list reads as cancel
                {
                    Console.WriteLine("Cancelled. Nothing was generated.");
                    return;
                }

                // approve is the one action whose arguments are pickers rather than typed lines,
                // and it already holds the values Generate needs — so it never reaches the switch.
                if (picked.Verb == "approve")
                {
                    if (model.Entities.Count == 0)
                    {
                        Console.WriteLine("Nothing to generate — the model is empty.");
                        return;
                    }

                    var approved = ApproveInteractively(target);

                    if (approved is null)   // Esc at a picker: back to the action list
                        continue;

                    WriteLayoutNotice(approved.Value.Target, customization);

                    // Declining the overwrite is not a cancel: the reviewed model is still here,
                    // so go back to the action list rather than discarding the whole interview.
                    if (!Generate(
                        model, appName, workspaceRoot, importedData,
                        approved.Value.Target, approved.Value.Persistence, customization))
                        continue;

                    return;
                }

                var answers = AskArguments(picked);

                if (answers is null)   // blank or EOF: abandon the action
                    continue;

                parts = ReviewActions.Compose(picked, answers);
            }

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

                    WriteLayoutNotice(chosenTarget, customization);

                    // See the picker path above: a declined overwrite returns to the loop.
                    if (!Generate(
                        model, appName, workspaceRoot, importedData,
                        chosenTarget, persistence.Value, customization))
                        break;

                    return;

                case "c":
                case "cancel":
                    Console.WriteLine("Cancelled. Nothing was generated.");
                    return;

                case "add" when parts.Length >= 2:
                    Console.WriteLine(AddLine(model, parts));
                    break;

                case "remove" when parts.Length >= 2:
                    Report(ModelEditor.RemoveEntity(model, parts[1]), $"Removed '{parts[1]}'.", "No such class.");
                    break;

                case "rename" when parts.Length >= 3:
                    Report(ModelEditor.RenameEntity(model, parts[1], parts[2]), $"Renamed to '{parts[2]}'.", "Could not rename (missing or name in use).");
                    break;

                case "field" when parts.Length >= 3:
                    var spec = FieldSpec.Parse(parts[2]);
                    Report(
                        ModelEditor.AddField(model, parts[1], spec.Name, spec.Type),
                        $"Added '{spec.Name}' to {parts[1]}.",
                        "Could not add field. The class must exist; the name must be new, a valid "
                        + "identifier, and neither 'Id' (generated) nor the class's own name; "
                        + "the type must be string, int, decimal, bool or DateTime.");
                    break;

                case "unfield" when parts.Length >= 3:
                    Report(
                        ModelEditor.RemoveField(model, parts[1], parts[2]),
                        $"Removed '{parts[2]}' from {parts[1]}.",
                        "Could not remove field (no such field, or Name, which every class keeps).");
                    break;

                case "why":
                    foreach (var whyLine in WhyLines(model, parts))
                        Console.WriteLine(whyLine);
                    break;

                default:
                    Console.WriteLine("Unrecognized command.");

                    // Suggest, never run. Retrieval recovers the ACTION but not its
                    // operands ("get rid of Reservation" -> remove, with no entity), so
                    // executing would guess at arguments the user did not confirm. It
                    // also cannot prompt: the review loop only reads typed lines when
                    // stdin is redirected, where blocking on a question would break
                    // piped scripts. Printing the verb leaves the next move with the user.
                    foreach (var line in SuggestionLines(reviewAi, typedLine))
                        Console.WriteLine(line);

                    break;
            }
        }
    }

    /// <summary>
    /// The AI's reading of an unrecognized review-loop line, ready to print. Empty when
    /// there is no resolver, nothing was typed, or the two gates declined.
    /// </summary>
    /// <remarks>
    /// Pure, and separated from the loop for the reason <see cref="ReviewActions"/> gives:
    /// the loop reads the console and cannot be driven from a test, so anything decidable
    /// is lifted out of it.
    ///
    /// Usage text is built from <see cref="ReviewActions.All"/> rather than written out
    /// again here, so an action that gains or loses an argument cannot leave this
    /// suggestion telling the user the wrong thing.
    /// </remarks>
    internal static IReadOnlyList<string> SuggestionLines(
        PhraseResolver? reviewAi, string typedLine)
    {
        var suggestion = reviewAi?.Resolve(typedLine);

        if (suggestion is null)
            return [];

        var action = ReviewActions.All.FirstOrDefault(a => a.Verb == suggestion.Label);

        var usage = action is null || action.ArgumentPrompts.Count == 0
            ? suggestion.Label
            : $"{suggestion.Label} "
              + string.Join(' ', action.ArgumentPrompts.Select(p => $"<{p.ToLowerInvariant()}>"));

        return
        [
            $"Did you mean:  {usage}",
            $"  (AI, {suggestion.Confidence:P0} confidence, "
                + $"closest example: \"{suggestion.ClosestText}\")"
        ];
    }

    /// <summary>The environment variable that keeps the console wizard.</summary>
    /// <remarks>
    /// For a headless or SSH session, where there is no browser to open and no display to open
    /// it on. Not a second product path — a way out of one.
    /// </remarks>
    internal const string NoWebVariable = "CG_NO_WEB";

    /// <summary>
    /// Whether this run should open the workflow page.
    /// </summary>
    /// <remarks>
    /// Pure, and taking its two inputs as arguments, for the reason <see cref="UseTypedInput"/>
    /// is separate: the branch it governs reads the console and cannot be driven from a test, so
    /// the decision is lifted out of it.
    /// <para>
    /// Redirected stdin is the hard boundary. A piped run keeps <c>InlineDataSource</c> parsing
    /// and the typed review loop, byte for byte — that is what every scripted E2E test drives,
    /// and none of them should have to change.
    /// </para>
    /// </remarks>
    internal static bool UseWebWorkflow(bool inputRedirected, bool noWeb)
        => !inputRedirected && !noWeb;

    /// <summary>
    /// Whether the review loop must fall back to typed lines. <see cref="SelectionPrompt"/> needs
    /// <c>Console.ReadKey</c> and <c>Console.SetCursorPosition</c>, both of which throw with no
    /// console attached. Matches the guard in <see cref="ApplicationTypeMenu.Choose"/>.
    /// </summary>
    private static bool UseTypedInput
        => Console.IsInputRedirected || Console.IsOutputRedirected;

    /// <summary>The action the user picked, or null if they pressed Esc.</summary>
    private static ReviewAction? PickAction()
    {
        var index = SelectionPrompt.Choose(ReviewActions.Title, ReviewActions.Labels, 0);

        return index is null ? null : ReviewActions.All[index.Value];
    }

    /// <summary>
    /// Collects <paramref name="action"/>'s arguments, one typed line each. Null means the user
    /// abandoned the action — a blank answer, or stdin closing — and the loop returns to the list
    /// (except <c>why</c>, where a blank answer means the whole report).
    /// </summary>
    private static IReadOnlyList<string>? AskArguments(ReviewAction action)
    {
        var answers = new List<string>();

        foreach (var prompt in action.ArgumentPrompts)
        {
            Console.Write($"{prompt}> ");

            var answer = Console.ReadLine();

            if (answer is null)
                return null;

            answer = answer.Trim();

            // `why` is the one action where a blank answer means something — the whole report —
            // so it is the one action a blank does not abandon.
            if (answer.Length == 0 && action.Verb != "why")
                return null;

            answers.Add(answer);
        }

        return answers;
    }

    /// <summary>
    /// Asks for what <c>approve</c> needs through pickers: the application type — always, pre-
    /// selecting whatever the wizard already settled so Enter simply confirms it and ↑↓ can still
    /// switch to something else — and the persistence backend when the newly chosen target can
    /// actually honour SQLite. Null means the user pressed Esc and nothing should be generated.
    /// </summary>
    private static (ITargetScaffolder Target, PersistenceKind Persistence)? ApproveInteractively(
        ITargetScaffolder? preselected)
    {
        var index = preselected is null
            ? ApplicationTypeMenu.Choose()
            : ApplicationTypeMenu.Choose(ApplicationTypeMenu.IndexOf(preselected));

        if (index is null)
            return null;

        var target = ApplicationTypeMenu.Resolve(index.Value);

        // WPF and WinForms read JsonStore directly, so offering a choice here would only lead to
        // the rejection the typed path prints. Skip the question instead. Checked against the
        // target just chosen, not the preselected one — switching away from WPF must unlock the
        // SQLite question, and switching to it must suppress it.
        if (!IsPersistenceAllowed(target, PersistenceKind.Sqlite))
            return (target, PersistenceKind.Json);

        var persistence = SelectionPrompt.Choose("Persistence", ["JSON", "SQLite"], 0);

        return persistence is null
            ? null
            : (target, persistence.Value == 0 ? PersistenceKind.Json : PersistenceKind.Sqlite);
    }

    /// <summary>
    /// Scaffolds the approved model and reports what was written.
    /// </summary>
    /// <remarks>
    /// Extracted from the <c>approve</c> case so the picker path can generate directly: it
    /// already holds a target and a persistence kind from its own prompts, while
    /// <see cref="ResolveTarget"/> and <see cref="ResolvePersistence"/> exist to read those out
    /// of a typed line.
    /// </remarks>
    private static bool Generate(
        CandidateDomainModel model, string appName, string workspaceRoot,
        ImportedData importedData, ITargetScaffolder target, PersistenceKind persistence,
        AppCustomization customization)
    {
        var name = ResolveOverwrite(appName, workspaceRoot, target);

        if (name is null)
        {
            Console.WriteLine("Nothing was written. The model is still here — approve again when ready.");
            Console.WriteLine("");
            return false;
        }

        // The year is read here rather than inside a template: a template reading the clock would
        // turn every golden-output test red on 1 January.
        var scaffold = new AppScaffolder().Scaffold(new ScaffoldRequest(
            model, name, workspaceRoot, target, persistence, importedData,
            customization, DateTime.Now.Year));

        if (scaffold.Success)
        {
            Output.Success($"Generated project at {scaffold.ProjectPath}");
            if (scaffold.TestProjectPath is not null)
                Output.Success($"Generated test project at {scaffold.TestProjectPath}");
            Console.WriteLine("");
            //foreach (var file in scaffold.FilesWritten)
            //    Console.WriteLine($"  {Path.GetRelativePath(scaffold.ProjectPath, file)}");

            BuildAndOfferToRun(scaffold, target);

            // Printed whether or not the build was green: a user whose app does not compile
            // still needs to know where the seed rows are and how to run it once it does.
            foreach (var line in NextSteps.Lines(
                target, scaffold.ProjectPath, scaffold.TestProjectPath, scaffold.FilesWritten))
            {
                Console.WriteLine(line);
            }

            Console.WriteLine("");
        }
        else
        {
            Output.Error($"Generation failed: {scaffold.Message}");
        }

        // A scaffold that failed still counts as attempted: it has reported its own reason and
        // the loop exits exactly as it did before the guard existed. Only a declined overwrite
        // returns false.
        return true;
    }

    /// <summary>
    /// The name to generate under, or null if the user declined to overwrite.
    /// </summary>
    /// <remarks>
    /// Asks only when there is something to lose — <see cref="GeneratedProjectPath.Exists"/> is
    /// the same test <c>AppScaffolder</c> uses to decide whether it is reusing a project, so the
    /// question is put exactly when a regeneration would happen and never on a fresh name.
    /// <para>
    /// Redirected input never prompts and overwrites, which is what every version before this
    /// did, so no script changes behaviour. Interactively the answer is a deliberate one: blank
    /// cancels.
    /// </para>
    /// <para>
    /// Loops rather than asking once, because a rename can collide too — the second name gets
    /// the same question rather than silently overwriting a different project.
    /// </para>
    /// </remarks>
    private static string? ResolveOverwrite(
        string appName, string workspaceRoot, ITargetScaffolder target)
    {
        var name = appName;

        while (GeneratedProjectPath.Exists(workspaceRoot, name, target))
        {
            if (Console.IsInputRedirected)
                return name;

            // A question about losing work: yellow, so it is not skimmed past on the way to the
            // prompt. Not red — nothing has gone wrong, and answering 'o' is a legitimate choice.
            foreach (var line in OverwritePrompt.Warning(
                GeneratedProjectPath.Folder(workspaceRoot, name, target)))
            {
                Output.Warning(line);
            }

            var action = AskOverwriteAction();

            if (action == OverwriteAction.Overwrite)
                return name;

            if (action == OverwriteAction.Cancel)
                return null;

            var renamed = AskNewAppName();

            if (renamed is null)
                return null;

            name = renamed;
        }

        return name;
    }

    /// <summary>Puts the overwrite question until it gets an answer it understands.</summary>
    private static OverwriteAction AskOverwriteAction()
    {
        while (true)
        {
            Console.Write("> ");

            var action = OverwritePrompt.Parse(Console.ReadLine());

            if (action is not null)
                return action.Value;

            Console.WriteLine("  Answer o, r or c — or press Enter to cancel.");
        }
    }

    /// <summary>
    /// A new app name, or null to cancel.
    /// </summary>
    /// <remarks>
    /// Normalized through <see cref="DomainAppName.FromDescription"/>, the same routine that
    /// derived the original name from the description — so "cash book" becomes CashBook here
    /// exactly as it would have there, and a name that cannot be a .NET identifier cannot be
    /// typed in by this route either.
    /// </remarks>
    private static string? AskNewAppName()
    {
        while (true)
        {
            Console.Write("New name (c to cancel)> ");

            var line = Console.ReadLine();

            // EOF is the absence of an answer, not an empty one.
            if (line is null)
                return null;

            var text = line.Trim();

            if (string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            if (text.Length == 0)
                continue;

            var name = DomainAppName.FromDescription(text);

            Console.WriteLine($"  Generating as {name}.");

            return name;
        }
    }

    /// <summary>
    /// How long to wait for the generated app to compile. Longer than the two minutes
    /// <c>DotNetProjectFactory</c> allows <c>dotnet new</c>, because a first WPF or EF Core build
    /// does real work — though restore has already run by this point, so it is rarely close.
    /// </summary>
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Compiles what was just generated, reports the result, and — if it built and there is
    /// something to start — offers to run it.
    /// </summary>
    /// <remarks>
    /// Interactive only, on the same <see cref="Console.IsInputRedirected"/> gate the wizard, the
    /// review loop and the AI resolver use. A piped run sees byte-identical output to before this
    /// existed, so scripted end-to-end runs neither slow down nor change shape.
    /// </remarks>
    private static void BuildAndOfferToRun(ScaffoldResult scaffold, ITargetScaffolder target)
    {
        if (Console.IsInputRedirected)
            return;

        var clock = System.Diagnostics.Stopwatch.StartNew();

        ProcessResult result;

        // Up to three minutes with nothing on screen otherwise. This path is interactive-only
        // already (see the guard above), so the spinner always animates here.
        using (Spinner.Start("Building"))
        {
            try
            {
                result = ProcessRunner.Run("dotnet", "build", scaffold.ProjectPath, BuildTimeout);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No `dotnet` on PATH. The app on disk is still fine, so say what happened and let
                // the next-step lines below tell them how to build it themselves.
                Output.Warning("Could not start `dotnet` — skipping the build check.");
                Console.WriteLine("");
                return;
            }
        }

        clock.Stop();

        // Coloured here rather than inside BuildReport: that stays a pure list of strings, and the
        // outcome is already known at this call site. Only the first line carries the verdict —
        // painting the quoted compiler errors red as well would be noise, not emphasis.
        var report = BuildReport.Lines(result, clock.Elapsed, scaffold.ProjectPath);

        for (var i = 0; i < report.Count; i++)
        {
            if (i > 0)
                Console.WriteLine(report[i]);
            else if (BuildReport.Succeeded(result))
                Output.Success(report[i]);
            else if (result.TimedOut)
                Output.Warning(report[i]);
            else
                Output.Error(report[i]);
        }

        Console.WriteLine("");

        // Nothing to offer for a class library, and nothing worth starting from a red build.
        if (!BuildReport.Succeeded(result)
            || target.ProjectType == DotNetProjectType.ClassLibrary)
            return;

        Console.Write("Run it now? [y/n]: ");

        var answer = Console.ReadLine()?.Trim() ?? string.Empty;

        if (!answer.StartsWith('y') && !answer.StartsWith('Y'))
            return;

        var isWebApp = target.ProjectType == DotNetProjectType.BlazorServer;

        var launched = AppLauncher.Launch(
            scaffold.ProjectPath, result.StandardOutput, fromProjectFolder: isWebApp);

        Console.WriteLine(launched is null
            ? "Could not start the app — run it yourself with the command below."
            : $"Started {launched}");

        if (launched is not null && isWebApp)
            OpenWhenListening(scaffold.ProjectPath);

        Console.WriteLine("");
    }

    /// <summary>
    /// Waits for the web application that was just started, then puts it on screen.
    /// </summary>
    /// <remarks>
    /// Every other target shows itself — a window, or a console. A web application shows nothing
    /// and waits to be visited, so without this the whole of "Run it now? y" is the word "Started"
    /// and an address, and it reads as an app that did not run. It was reported exactly that way.
    /// <para>
    /// The wait is the part that matters. <c>dotnet run</c> restores and builds before Kestrel
    /// binds anything, so for the first several seconds the address is dead and opening it produces
    /// a browser that cannot connect — which looks worse than printing nothing. Opening it only
    /// once something answers is what makes the offer honest.
    /// </para>
    /// </remarks>
    private static void OpenWhenListening(string projectPath)
    {
        var url = LaunchSettingsReader.HttpUrl(projectPath);

        if (url is null)
        {
            Console.WriteLine("It is a web application — open the address it prints in its own window.");
            return;
        }

        bool listening;

        // Short enough to stay inside the spinner's eraser; the address is printed on its own line
        // either way.
        using (Spinner.Start("Waiting for it to start"))
        {
            listening = WebAppStart.Wait(url);
        }

        if (!listening)
        {
            Output.Warning(
                $"It has not answered on {url} yet — open that once its own window says it is listening.");
            return;
        }

        if (WebAppStart.OpenBrowser(url))
            Output.Success($"Opened {url} in your browser.");
        else
            Console.WriteLine($"It is listening at {url} — open that in your browser.");
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

    /// <summary>
    /// What to tell the user when the target they approved cannot use the layout they named, or
    /// null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Internal and pure for the same reason <see cref="WhyLines"/> is. The check belongs at
    /// `approve` rather than only in the wizard because the target can still be changed there — the
    /// same reason <see cref="IsPersistenceAllowed"/> is re-checked at that point. A notice rather
    /// than a refusal: the layout is decoration a windowless target simply has no use for, and
    /// failing the build over it would be out of proportion.
    /// </remarks>
    /// <summary>Prints <see cref="LayoutNotice"/> when there is one. Both approve paths settle a
    /// target of their own, so both call this.</summary>
    private static void WriteLayoutNotice(ITargetScaffolder target, AppCustomization customization)
    {
        var notice = LayoutNotice(target, customization);

        if (notice is not null)
            Console.WriteLine(notice);
    }

    internal static string? LayoutNotice(ITargetScaffolder target, AppCustomization customization)
    {
        if (customization.Layout is null || AppLayouts.Supports(target.Key))
            return null;

        return $"The {AppLayoutCatalog.KeyFor(customization.Layout.Value)} layout applies to the "
             + $"wpf and winforms targets; the {target.Key} target has no window, so it is ignored.";
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
        //Console.WriteLine();

        //var lines = new List<string>
        //{
        //    $"Candidate model ({model.Entities.Count} classes):",
        //};

        //foreach (var entity in model.Entities)
        //{
        //    var fields = entity.Fields.Count == 0
        //        ? string.Empty
        //        : " { " + string.Join(", ", entity.Fields.Select(f => $"{f.Name}:{f.Type}")) + " }";

        //    // A class with no mined relationships prints exactly as it always has; one with
        //    // any gets its targets appended, so the review loop is where a user first sees
        //    // that RelationshipMiner connected two of their classes.
        //    var relationships = entity.Relationships.Count == 0
        //        ? string.Empty
        //        : "  -> " + string.Join(", ", entity.Relationships.Select(r => r.Target));

        //    lines.Add($"  - {entity.Name} [{entity.Provenance}]{fields}{relationships}");
        //}

        // Paged: a rich domain can run to dozens of classes with their fields, and the model is
        // reprinted after every action — so scrolling it away is the loop's most repeated annoyance.
        //Pager.Write(lines);

        // Every entity is scaffolded with a typed repository exposing these CRUD functions, but
        // spelling that out here was judged noise for the review loop, so the describing line
        // stays deliberately commented out below — only the blank-line spacer still runs.
        if (model.Entities.Count > 0)
        {
            Console.WriteLine();
            //Console.WriteLine(
            //    "Each entity also gets a <Entity>Repository with CRUD: "
            //    + "GetAll, GetById, Create, Update, Delete");
        }

        // Rejections, demoted suggestions and skipped nouns, grouped by the reason each was
        // given, were judged noise here too and are deliberately suppressed from this printout.
        // DecisionReport.All is still reachable through the review loop's `why` action.
    //    foreach (var line in DecisionReport.Summary(model))
    //        Console.WriteLine(line);
    }
}
