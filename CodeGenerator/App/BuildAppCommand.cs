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
using DomainDiscovery.Bundled;
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
        var options = InlineCustomization.Read(Tail(input), AppCustomization.Default);
        var (remaining, inline) = (options.Remaining, options.Customization);

        // Said before anything runs: an ignored value is a setting the user thinks they made.
        foreach (var note in options.Notes)
            Output.Warning("  " + note);

        var request = InlineDataSource.Parse(remaining);

        // A relative data file is the workspace's, not the current directory's: piped with
        // PROJECTPATH from any other folder, `from csv books.csv` said "No file at books.csv".
        // Resolved once here, so the web workflow, the console wizard and the piped run all get it.
        if (request.Source is { } typed)
            request = request with { Source = DataSourcePath.Resolve(typed, workspaceRoot) };

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
            var saved = CustomizationDefaults.Load(workspaceRoot);
            var webStart = Merge(saved, inline);

            // Asked here, in the console, and before the page opens. Safe because this
            // command is dispatched OUTSIDE CommandColour.Run - deliberately, since it
            // prints an address and then blocks - which is why OverwritePrompt is visible
            // too. A prompt inside a buffered command would not be.
            var resumed = OfferToResume(workspaceRoot, out var keptAside);

            // Before the page opens, so the layout the workflow will start pre-filled with is one
            // the user has been told about rather than one they have to notice. Skipped for a
            // resumed build: its layout came from the snapshot, not from the profile.
            if (resumed is null)
                WriteRememberedLayoutNotice(workspaceRoot, saved, inline.Layout);

            var session = resumed ?? new Web.Workflow.BuildSession(
                description, workspaceRoot, webStart, source)
            {
                NormalizeAll = normalize,
            };

            // Attached AFTER any restore, so replaying a snapshot does not write a dozen
            // partial ones over the good one.
            //
            // Not attached at all when an unfinished build was kept aside with "start fresh":
            // there is one slot, and this run saving into it would overwrite the build the offer
            // had just promised to keep - its first answer did exactly that - and finishing it
            // would delete it.
            if (!keptAside)
            {
                session.Autosave = s => Resume.BuildSnapshotStore.Save(
                    workspaceRoot, Resume.BuildSnapshot.Of(s));

                session.OnFinished = _ => Resume.BuildSnapshotStore.Clear(workspaceRoot);
            }

            if (Web.BuildAppWebHost.TryStart(session, out var host))
            {
                using (host)
                {
                    Console.WriteLine($"  Opening the build workflow at {host.Url}");

                    if (!WebAppStart.OpenBrowser(host.Url))
                        Console.WriteLine($"  {WebAppStart.NotOpenedLine()}");

                    Console.WriteLine("  Ctrl-C here cancels.");
                    Console.WriteLine("");

                    // The host that is already running, not a second one: the tabs have to be
                    // counted on the Kestrel the browser was pointed at.
                    var webOutcome = host.Run(session, Interrupt.Token);

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
            var saved = CustomizationDefaults.Load(workspaceRoot);
            var start = Merge(saved, inline);

            // Before the wizard, because its Layout step arrives pre-filled with the remembered
            // answer: Enter keeps it, and nothing else would say where it came from.
            WriteRememberedLayoutNotice(workspaceRoot, saved, inline.Layout);

            var outcome = BuildAppWizard.Run(
                request, Console.In, Console.Out, ApplicationTypeMenu.Choose, start, Menu.Choose,
                workspaceRoot);

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
            // Null-coalescing rather than Named: "namespace none" sets this to string.Empty, which
            // EQUALS the default, so a differs-from-default comparison would read "cleared" as
            // "not mentioned" and hand back the remembered prefix instead. Null IS "not mentioned"
            // here, the same reasoning as Layout below.
            NamespacePrefix = inline.NamespacePrefix ?? saved.NamespacePrefix,

            // These four were missing, so the profile quietly won over what was typed:
            // `build app: shop nologin` generated a login screen whenever the profile had one.
            // CustomizationMergeTests.Every_typed_setting_overrides_the_profile now holds every
            // setting to this.
            Login = Named(inline.Login, defaults.Login, saved.Login),
            Transactions = Named(inline.Transactions, defaults.Transactions, saved.Transactions),
            Masters = Named(inline.Masters, defaults.Masters, saved.Masters),

            // Null-coalescing rather than Named: null IS "not named" for a layout or an entry
            // template, so no comparison against the default is involved — see the remark above.
            Layout = inline.Layout ?? saved.Layout,
            EntryTemplate = inline.EntryTemplate ?? saved.EntryTemplate,
        };

        static T Named<T>(T inline, T fallback, T saved)
            => EqualityComparer<T>.Default.Equals(inline, fallback) ? saved : inline;
    }

    /// <summary>
    /// The options line the usage message prints.
    /// </summary>
    /// <remarks>
    /// A constant rather than a literal inside the usage message, so a test can hold it to
    /// listing every option the parser accepts. A flag the help does not mention is a flag
    /// nobody finds.
    /// </remarks>
    /// <para>
    /// Static rather than const so the layout and entry-template words come from their catalogs.
    /// It used to leave out namespace, layout and entry-template, although every one is accepted;
    /// <c>InlineOptionsHelpTests</c> now holds it to the parser's own list.
    /// </para>
    internal static readonly string InlineOptionsHelp =
        "Options (last on the line): company \"<name>\"  theme light|dark  "
        + "accent #rrggbb  icon <path>  title \"<pattern>\"  namespace <prefix>|none  "
        + $"layout {AppLayoutCatalog.HelpOptions}  "
        + $"entry-template {AppScaffolding.Screens.EntryTemplateCatalog.HelpOptions}  "
        + "transactions <A,B>  masters <A,B>  "
        + "novalidation  logging  noseed  nologin";

    /// <summary>The description path: resolves a discovery source (Ollama, or Wikipedia — directly
    /// or as its fallback), then the usual NLE seeds, topic enrichment and review loop.</summary>
    private static void HandleFromDescription(
        string description, string workspaceRoot, ITargetScaffolder? target,
        AppCustomization customization, PhraseResolver? reviewAi = null)
    {
        if (description.Length == 0)
        {
            Console.WriteLine("Usage: build app: <description of the system you want>");
            Console.WriteLine("   or: build app from csv <path>   (also txt, json, excel, sql)");
            Console.WriteLine(InlineOptionsHelp);
            return;
        }

        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");
        // The store, if one is installed, is tried before the built-in domains; the online API after.
        var config = DiscoveryConfig.Load();
        var storePath = WikipediaStoreLocation.Resolve(config);
        var source = DomainModelSourceFactory.Create(
            config,
            () => new WikipediaDomainModelSource(DomainDiscoveryPipeline.CreateDefault(cacheDir, storePath)),
            bundled: BundledDomainSource.CreateDefault,
            wikipediaIsLocal: File.Exists(storePath));

        // The longest silence in the tool, and a genuinely long one: a local model on a CPU
        // measured two and a half minutes for an ordinary domain, and a first Wikipedia fetch is
        // no quicker. The spinner says it is alive; the class count says it is getting somewhere,
        // which is what stops a real wait from reading as a hang.
        DiscoveryOutcome outcome;

        Console.WriteLine("\n");
        using (var spinner = Spinner.Start("Discovering the domain (this can take a few minutes)"))
        {
            outcome = source.Discover(
                description,
                progress => Spinner.Describe(spinner, ConsoleProgress(progress)));
        }

        // Which source answered, always — the command line says exactly what the web panel says.
        Console.WriteLine(outcome.Describe());

        RunReviewLoop(
            outcome.Model, DomainAppName.FromDescription(description), workspaceRoot,
            ImportedData.None, target, customization, reviewAi);
    }

    /// <summary>
    /// One line for the spinner. The console has a single line to say this on, so the class names
    /// the web panel lists cannot go here — the count and the step do.
    /// </summary>
    private static string ConsoleProgress(DiscoveryProgress progress)
    {
        var step = progress.Step ?? "Discovering the domain";

        return progress.Total == 0
            ? step
            : $"{step} ({progress.Done.Count} of {progress.Total} class(es))";
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
        var fileLabel = DataSourceLabel.For(source);

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
    /// name goes through the same <see cref="DomainAppName"/> rules (as a typed name, so it is never
    /// cut at "for" or "with"), rather than a second rule invented for this path.
    /// </summary>
    internal static string AppNameFor(string description, DataSourceSpec? source)
    {
        if (description.Length > 0)
            return DomainAppName.FromDescription(description);

        if (source is null)
            return DomainAppName.FromDescription(string.Empty);

        // A file or database name is a name the user chose, not a sentence to shorten.
        return DomainAppName.FromTypedName(
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
            foreach (var line in CustomizationSummary.Lines(customization, target))
                Console.WriteLine(line);

            if (ProposalReport.Summary(model) is { } proposals)
                Console.WriteLine(proposals);

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
                    var renamed = ModelEditor.RenameEntity(model, parts[1], parts[2], out var renamedTo);
                    Report(renamed, $"Renamed to '{renamedTo}'.", "Could not rename (missing, name in use, or not a valid class name).");
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
                        ModelEditor.RemoveField(model, parts[1], parts[2], out var dropped),
                        $"Removed '{parts[2]}' from {parts[1]}.{DroppedRulesNote(dropped)}",
                        "Could not remove field (no such field, or Name, which every class keeps).");
                    break;

                // The three edits the page has had and the console had not. `unfield` then `field`
                // was the only way round the first two, and it threw away the field's proposed
                // rules and its provenance with it.
                // The loop splits into three parts at most, so the field and its new name or
                // type arrive together in the third and are split apart here.
                case "renamefield" when parts.Length >= 3
                    && TwoWords(parts[2], out var oldField, out var newField):
                    Report(
                        ModelEditor.RenameField(model, parts[1], oldField, newField),
                        $"Renamed {parts[1]}.{oldField} to {newField}.",
                        "Could not rename the field. The class and field must exist, and the new "
                        + "name must be a new, valid identifier - not 'Id' or the class's own name.");
                    break;

                case "retype" when parts.Length >= 3
                    && TwoWords(parts[2], out var retyped, out var newType):
                    Report(
                        ModelEditor.ChangeFieldType(model, parts[1], retyped, newType),
                        $"{parts[1]}.{retyped} is now {newType}.",
                        "Could not change the type. The class and field must exist, and the type "
                        + "must be string, int, decimal, bool or DateTime.");
                    break;

                case "unaccept" when parts.Length >= 2:
                    Console.WriteLine(ProposalReport.Unaccept(model, parts[1]));
                    break;

                case "why":
                    foreach (var whyLine in WhyLines(model, parts))
                        Console.WriteLine(whyLine);
                    break;

                case "rules":
                    foreach (var line in ProposalReport.Lines(model))
                        Console.WriteLine(line);
                    break;

                case "accept" when parts.Length >= 2:
                    Console.WriteLine(ProposalReport.Accept(model, parts[1]));
                    break;

                case "order" when parts.Length >= 2
                    && string.Equals(parts[1], "status", StringComparison.OrdinalIgnoreCase):
                    Console.WriteLine(OrderStatusLine(model, parts));
                    break;

                case "final" when parts.Length >= 2
                    && string.Equals(parts[1], "status", StringComparison.OrdinalIgnoreCase):
                    Console.WriteLine(FinalStatusLine(model, parts));
                    break;

                default:
                    Console.WriteLine(Refused("Unrecognized command."));

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
        var outcome = ResolveOverwrite(model, appName, workspaceRoot, target);

        if (outcome.Name is not { } name)
        {
            Console.WriteLine("Nothing was written. The model is still here — approve again when ready.");
            Console.WriteLine("");
            return false;
        }

        var request = new ScaffoldRequest(
            model, name, workspaceRoot, target, persistence, importedData,
            customization, DateTime.Now.Year);

        if (outcome.AddOnly)
            return AddOnly(request, workspaceRoot, name, target);

        // The year is read here rather than inside a template: a template reading the clock would
        // turn every golden-output test red on 1 January.
        var scaffold = new AppScaffolder().Scaffold(request);

        if (scaffold.Success)
        {
            Output.Success($"Generated project at {scaffold.ProjectPath}");
            if (scaffold.TestProjectPath is not null)
                Output.Success($"Generated test project at {scaffold.TestProjectPath}");

            // Warning and not Error, for the reason the stub note below is: the app was written.
            // What is missing is its tests, and that used to go unsaid for every desktop app.
            if (scaffold.TestProjectProblem is { } testProblem)
                Output.Warning("  " + testProblem);

            // How the model was split into screens, or which layout means there is no such split.
            // A misclassification is otherwise only discoverable by launching the app and noticing
            // a screen is missing.
            if (ScreensLine(target, customization, scaffold.ScreenSummary) is { } screens)
                Console.WriteLine(screens);

            // An app generated before the validation pipeline keeps a .Custom.cs stub declaring a
            // hook the regenerated half no longer has, and the pair does not compile. Said here
            // rather than left to the build error, which names CS0759 and CS8795 and not the
            // reason: the write-once file is the one thing regeneration is not allowed to fix.
            // Warning and not Error: the generate itself succeeded, and calling it an error would
            // say the files are not there. What failed is the pair, and only after the next build.
            foreach (var stub in scaffold.OutdatedValidatorStubs)
                Output.Warning("  " + stub + ".");

            // The user's own values, missing from the app: said, not left for them to notice.
            foreach (var unreadable in scaffold.UnreadableSeedValues)
                Output.Warning("  " + unreadable + ".");

            // What an overwrite removed (or kept) of the files an earlier generate wrote for
            // classes the model no longer has.
            foreach (var stale in scaffold.StaleFiles)
                Console.WriteLine("  " + stale + ".");

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
    /// <summary>
    /// Writes only the classes this app does not have, and says what it left alone.
    /// </summary>
    /// <remarks>
    /// No build and no offer to run: the project was already built and may already be
    /// running, and rebuilding it here would be a side effect nobody asked for. The report
    /// names every file written and how many were not.
    /// </remarks>
    private static bool AddOnly(
        ScaffoldRequest request, string workspaceRoot, string name, ITargetScaffolder target)
    {
        var folder = GeneratedProjectPath.Folder(workspaceRoot, name, target);

        var added = AppScaffolding.Incremental.IncrementalScaffolder.Add(
            request,
            AppScaffolding.Incremental.ModelDelta.NewNames(
                DeltaAgainst(request.Model, folder)));

        foreach (var line in AddReport.Lines(added, folder))
            Console.WriteLine(line);

        Console.WriteLine("");

        return added.Success;
    }

    /// <summary>
    /// What the overwrite question settled on: the name to write under, and whether to write
    /// only what is new.
    /// </summary>
    /// <remarks>
    /// A record rather than the bare <c>string?</c> this returned before, because "add" is a
    /// third outcome that a name alone cannot express. <c>Name</c> null still means cancel.
    /// </remarks>
    private sealed record OverwriteOutcome(string? Name, bool AddOnly = false);

    private static OverwriteOutcome ResolveOverwrite(
        CandidateDomainModel model, string appName, string workspaceRoot,
        ITargetScaffolder target)
    {
        var name = appName;

        while (GeneratedProjectPath.Exists(workspaceRoot, name, target))
        {
            // A piped run overwrites in silence, as it always has - and so can never reach
            // the add branch, for the same reason it never reads a profile.
            if (Console.IsInputRedirected)
            {
                // A script never replaces an app: it may have been edited by hand since, and the
                // replay would throw that away without a word.
                if (Scripts.ScriptBuildApp.IsStep)
                {
                    Console.WriteLine(Scripts.ScriptBuildApp.RefusedMarker
                        + $"{GeneratedProjectPath.Folder(workspaceRoot, name, target)} already exists, and a script never "
                        + "replaces an app. Delete it, or build it under another name.");
                    return new OverwriteOutcome(null);
                }

                return new OverwriteOutcome(name);
            }

            var folder = GeneratedProjectPath.Folder(workspaceRoot, name, target);
            var delta = DeltaAgainst(model, folder);

            // A question about losing work: yellow, so it is not skimmed past on the way to the
            // prompt. Not red — nothing has gone wrong, and answering 'o' is a legitimate choice.
            foreach (var line in OverwritePrompt.Warning(folder, delta))
            {
                Output.Warning(line);
            }

            var action = AskOverwriteAction();

            if (action == OverwriteAction.Overwrite)
                return new OverwriteOutcome(name);

            if (action == OverwriteAction.Add)
                return new OverwriteOutcome(name, AddOnly: true);

            if (action == OverwriteAction.Cancel)
                return new OverwriteOutcome(null);

            var renamed = AskNewAppName();

            if (renamed is null)
                return new OverwriteOutcome(null);

            name = renamed;
        }

        return new OverwriteOutcome(name);
    }

    /// <summary>
    /// What this model has that the app in <paramref name="projectFolder"/> does not.
    /// </summary>
    /// <remarks>
    /// Scoped to the Models folder, not the workspace: <c>ClassSnapshots.Read</c> recurses
    /// whatever root it is given, and a workspace may hold several generated apps - reading
    /// all of them would diff this app against everybody's classes.
    /// </remarks>
    internal static IReadOnlyList<AppScaffolding.Incremental.ClassDelta> DeltaAgainst(
        CandidateDomainModel model, string projectFolder)
        => AppScaffolding.Incremental.ModelDelta.Between(
            CodeGenerationEngine.Core.Relationships.ClassSnapshots.Read(
                Path.Combine(projectFolder, "Models")),
            DomainModelMapper.ToClassDefinitions(model));

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
    /// Normalized through <see cref="DomainAppName.FromTypedName"/> — so "cash book" becomes
    /// CashBook here exactly as it would from a description, and a name that cannot be a .NET
    /// identifier cannot be typed in by this route either; but a name the user typed is never
    /// shortened to a description's subject.
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

            var name = DomainAppName.FromTypedName(text);

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

        // A red build the user can act on: offer to fix it, and carry on with the rebuilt result.
        // Everything below (run offer, the app's output) reads `result`, so it must be the rebuild.
        result = Rebuilt(result, ShouldOfferFix(result) ? OfferToFix(scaffold, result) : null);

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

    /// <summary>A failed build that finished (a timed-out one has no errors worth reading) is worth offering a fix for.</summary>
    internal static bool ShouldOfferFix(ProcessResult result)
        => !BuildReport.Succeeded(result) && !result.TimedOut;

    /// <summary>The build later steps must use: the rebuild when a fix ran, else the one they had.</summary>
    internal static ProcessResult Rebuilt(ProcessResult current, ProcessResult? rebuilt)
        => rebuilt ?? current;

    /// <summary>
    /// Asks whether to fix a failed build, and if so fixes it and rebuilds. Returns the last build, or
    /// null when the user said no, there was nothing parsable to fix, or something went wrong.
    /// </summary>
    /// <remarks>
    /// <paramref name="choose"/> and <paramref name="fixer"/> are seams: the console loop around this
    /// cannot be driven from a test. It never throws: a failure here must not end the whole
    /// <c>build app</c> command, so it says one plain line and returns null (the failed build stands).
    /// Runs outside <c>CommandColour.Run</c> like the rest of <c>build app</c>, so the prompt is visible.
    /// </remarks>
    internal static ProcessResult? OfferToFix(
        ScaffoldResult scaffold,
        ProcessResult failed,
        Func<string, IReadOnlyList<string>, int, int?>? choose = null,
        CodeGenerator.BuildFix.IBuildFixer? fixer = null)
    {
        try
        {
            var errors = CodeGenerator.BuildFix.BuildDiagnostics.Parse(failed, scaffold.ProjectPath).Count;

            if (errors == 0)
                return null;

            choose ??= SelectionPrompt.Choose;

            var choice = choose(
                CodeGenerator.BuildFix.BuildFixReport.Title(errors),
                CodeGenerator.BuildFix.BuildFixReport.Options,
                CodeGenerator.BuildFix.BuildFixReport.DefaultIndex);

            if (!CodeGenerator.BuildFix.BuildFixReport.Accepted(choice))
                return null;

            fixer ??= new CodeGenerator.BuildFix.BuildFixer();

            CodeGenerator.BuildFix.FixOutcome outcome;

            // Spinner.Start already prints one plain line instead of animating when the console is
            // redirected, so no extra gate is needed. Disposed on an exception by the using.
            using (Spinner.Start("Fixing the build errors"))
            {
                // Ctrl+C ends the wait: the round being checked is undone and the report still prints.
                outcome = fixer.Fix(
                    scaffold.ProjectPath, failed,
                    () => ProcessRunner.Run("dotnet", "build", scaffold.ProjectPath, BuildTimeout),
                    _ => { },
                    Interrupt.Token);
            }

            var lines = CodeGenerator.BuildFix.BuildFixReport.Lines(outcome, scaffold.ProjectPath);

            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    Console.WriteLine(lines[i]);
                else if (outcome.Green)
                    Output.Success(lines[i]);
                else
                    Output.Error(lines[i]);
            }

            Console.WriteLine("");

            // A fix that stopped with no build to trust leaves the failed build as it was.
            return outcome.BuildChecked ? outcome.LastBuild : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Output.Warning($"The fix could not be offered ({ex.GetType().Name}); the build is left as it was.");
            Console.WriteLine("");

            return null;
        }
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
    /// <summary>Prints the remembered-layout notice when there is one.</summary>
    /// <summary>
    /// Offers an unfinished build, and returns the session to carry on with - or null to
    /// start fresh.
    /// </summary>
    /// <remarks>
    /// Interactive only. A redirected-stdin run neither writes a snapshot nor reads one: a
    /// scripted build must not have its output changed by a file it never mentioned, which is
    /// the same determinism <c>CustomizationDefaults</c> and <c>normalize</c> already protect.
    /// </remarks>
    /// <param name="keptAside">
    /// True when the user chose to start fresh, leaving the unfinished build on disk. The caller
    /// must then not let this run save over it - see where it attaches the autosave.
    /// </param>
    private static Web.Workflow.BuildSession? OfferToResume(
        string workspaceRoot, out bool keptAside)
    {
        keptAside = false;

        if (Console.IsInputRedirected)
            return null;

        var snapshot = Resume.BuildSnapshotStore.Load(workspaceRoot);

        if (snapshot is null)
            return null;

        while (true)
        {
            foreach (var line in Resume.ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow))
                Console.WriteLine(line);

            Console.Write("> ");

            switch (Resume.ResumeOffer.Parse(Console.ReadLine()))
            {
                case Resume.ResumeChoice.Resume:
                    var session = Resume.ResumedSession.From(
                        snapshot, workspaceRoot, out var notice);

                    if (notice is not null)
                        Output.Warning("  " + notice);

                    return session;

                case Resume.ResumeChoice.Fresh:
                    // Left on disk deliberately: starting fresh is not a decision to throw
                    // the old work away, and the next run can still offer it.
                    keptAside = true;
                    Console.WriteLine(Resume.ResumeOffer.KeptAsideNotice);
                    return null;

                case Resume.ResumeChoice.Discard:
                    Resume.BuildSnapshotStore.Clear(workspaceRoot);
                    Console.WriteLine("  Discarded.");
                    return null;

                default:
                    Console.WriteLine("  Answer r, f or d.");
                    continue;
            }
        }
    }

    private static void WriteRememberedLayoutNotice(
        string workspaceRoot, AppCustomization saved, AppLayout? namedOnTheLine)
    {
        var notice = CustomizationDefaults.RememberedLayoutNotice(
            workspaceRoot, saved, namedOnTheLine);

        if (notice is not null)
            Console.WriteLine(notice);
    }

    /// <summary>Prints <see cref="LayoutNotice"/> when there is one. Both approve paths settle a
    /// target of their own, so both call this.</summary>
    private static void WriteLayoutNotice(ITargetScaffolder target, AppCustomization customization)
    {
        var notice = LayoutNotice(target, customization);

        if (notice is not null)
            Console.WriteLine(notice);
    }

    /// <summary>
    /// The finished build's line about its screens: the master / transaction split where the
    /// screens were built from it, the layout in force where they were not, and nothing for a
    /// target that has no screens.
    /// </summary>
    /// <remarks>
    /// Pure and internal for the reason <see cref="LayoutNotice"/> is: the printing is one line at
    /// the call site and the decision is what needs covering. The decision matters because the
    /// summary used to print on every target and every layout — a `layout dashboard` run
    /// announced "8 masters, 0 transactions" over a window holding neither, which is what made a
    /// shipped business shell look to its user as though it had never arrived.
    /// </remarks>
    internal static string? ScreensLine(
        ITargetScaffolder target, AppCustomization customization, string screenSummary)
    {
        // Nothing was classified, so there is nothing to describe and nothing to correct. The
        // guard the printed version already had, kept for the same reason: "Screens: " alone is
        // worse than silence.
        if (screenSummary.Length == 0)
            return null;

        if (AppLayouts.UsesRoleScreens(target.Key, customization.EffectiveLayout))
            return $"  Screens: {screenSummary}";

        // Only where a layout could have been business and was not. A windowless target already
        // hears about a named layout from LayoutNotice, and has no screens to explain.
        if (!AppLayouts.Supports(target.Key))
            return null;

        var key = AppLayoutCatalog.KeyFor(customization.EffectiveLayout);

        return $"  Layout: {key} — no master or transaction screens; "
             + $"the {AppLayoutCatalog.KeyFor(AppLayout.Business)} layout has them.";
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
    /// What to add to a field's removal report for the accepted rules that went with it, or
    /// nothing when none did. Shared by the console and the Review page, so both say it.
    /// </summary>
    internal static string DroppedRulesNote(IReadOnlyList<string> dropped)
        => dropped.Count == 0
            ? string.Empty
            : $" Also dropped {(dropped.Count == 1 ? "the rule" : "the rules")} that named it: "
              + string.Join("; ", dropped.Select(rule => $"'{rule}'")) + ".";

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

    /// <summary>
    /// The line an <c>order status &lt;values&gt; in &lt;Class&gt;</c> line produces. Internal
    /// and pure for the same reason <see cref="AddLine"/> is.
    /// </summary>
    /// <remarks>
    /// The loop's <c>line.Split(' ', 3, ...)</c> hands this at most three parts, so
    /// <c>order status Draft,Approved,Shipped in Order</c> arrives as
    /// <c>["order", "status", "Draft,Approved,Shipped in Order"]</c> — the value list and the
    /// class name share that third token and have to be split apart here, on the last " in ".
    /// </remarks>
    internal static string OrderStatusLine(CandidateDomainModel model, string[] parts)
        => RunStatusCommand(model, parts, WorkflowCommands.OrderStatus);

    /// <summary>
    /// The two words <paramref name="text"/> holds, or false when it does not hold exactly two.
    /// </summary>
    internal static bool TwoWords(string text, out string first, out string second)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        (first, second) = words.Length == 2 ? (words[0], words[1]) : (string.Empty, string.Empty);

        return words.Length == 2;
    }

    /// <summary>The <c>final status &lt;values&gt; in &lt;Class&gt;</c> counterpart of
    /// <see cref="OrderStatusLine"/>.</summary>
    internal static string FinalStatusLine(CandidateDomainModel model, string[] parts)
        => RunStatusCommand(model, parts, WorkflowCommands.FinalStatus);

    private static string RunStatusCommand(
        CandidateDomainModel model, string[] parts, Func<CandidateDomainModel, string, string, string> action)
    {
        var verb = parts.Length > 0 ? parts[0] : "order";
        var usage = $"Usage: {verb} status <values> in <Class> "
            + $"(e.g. '{verb} status Draft,Approved,Shipped in Order').";

        if (parts.Length < 3)
            return usage;

        var tail = parts[2];
        var splitAt = tail.LastIndexOf(" in ", StringComparison.OrdinalIgnoreCase);
        if (splitAt < 0)
            return usage;

        var valuesText = tail[..splitAt];
        var className = tail[(splitAt + 4)..].Trim();

        return action(model, className, valuesText);
    }

    // In a script's build step a refused answer is marked, so the script fails rather than build an
    // app other than the one its lines describe.
    private static void Report(bool changed, string ok, string fail)
        => Console.WriteLine(changed ? ok : Refused(fail));

    private static string Refused(string text) =>
        Scripts.ScriptBuildApp.IsStep ? Scripts.ScriptBuildApp.RefusedMarker + text : text;

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
