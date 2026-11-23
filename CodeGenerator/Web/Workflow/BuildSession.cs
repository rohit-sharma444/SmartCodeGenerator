using AppScaffolding;
using CodeGenerator.Web;
using DataIngestion;
using DataIngestion.Normalization;
using DomainDiscovery;
using DomainDiscovery.Model;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Every decision a `build app` run makes, and the rules for changing one's mind.
/// </summary>
/// <remarks>
/// Deliberately free of Razor, Kestrel and the console: the components read these properties and
/// call these methods, and the whole workflow is therefore assertable as plain C#. This is the
/// same split <c>SelectionState</c> / <c>SelectionPrompt</c> already uses, one layer up.
/// <para>
/// Registered as a singleton for the host's lifetime, so a second browser tab attaches to the
/// running workflow rather than starting a rival one against the same workspace.
/// </para>
/// <para>
/// Public, along with the rest of <c>CodeGenerator.Web.Workflow</c>, only because Razor generates
/// every component as a public class and a <c>[Parameter]</c> may not be less accessible than the
/// component holding it. Nothing outside this executable references any of it.
/// </para>
/// </remarks>
public sealed class BuildSession
{
    private readonly Dictionary<BuildStage, StageState> _states = new();

    /// <summary>Whether "no data" was chosen, which a null <see cref="Source"/> cannot say on its
    /// own — it also means nobody has answered that node yet.</summary>
    private bool _noDataChosen;

    /// <summary>Whether the look node was answered. The customization always HAS a value, so this
    /// is the only way to tell a decision from a default.</summary>
    private bool _lookAnswered;

    /// <param name="description">
    /// What followed `build app:`, or empty. A description typed inline has already answered
    /// node 1, so the workflow opens at node 2.
    /// </param>
    /// <param name="start">
    /// The remembered profile with anything typed on the command line layered over it —
    /// <c>BuildAppCommand.Merge(CustomizationDefaults.Load(root), inline)</c>.
    /// </param>
    /// <param name="source">
    /// A `from &lt;format&gt; &lt;location&gt;` typed inline, which has answered node 3.
    /// </param>
    public BuildSession(
        string description, string workspaceRoot, AppCustomization start,
        DataSourceSpec? source = null)
    {
        WorkspaceRoot = workspaceRoot;
        Description = description ?? string.Empty;
        Customization = start;
        Source = source;

        foreach (var node in StageGraph.Nodes)
            _states[node.Stage] = StageState.Pending;

        // Inline answers arrive already made, so the nodes that hold them open green and the
        // workflow parks on the first thing it does not know.
        if (Description.Length > 0)
            _states[BuildStage.Describe] = StageState.Done;

        if (source is not null)
            _states[BuildStage.Data] = StageState.Done;

        Current = FirstUnanswered();
        _states[Current] = StageState.Active;
    }

    /// <summary>
    /// Raised after anything here moves.
    /// </summary>
    /// <remarks>
    /// One workflow can have more than one page looking at it — the session is a singleton so
    /// that a second tab attaches rather than starting a rival run against the same workspace —
    /// and a Blazor circuit only repaints when it is told to. Without this, the tab nobody
    /// touched goes on drawing a node the workflow has left, with a live Continue button on it.
    /// <para>
    /// Raised on whichever circuit's thread made the change, which is not the subscriber's: a
    /// page handling this must marshal onto its own renderer before touching its state.
    /// </para>
    /// </remarks>
    public event Action? Changed;

    /// <summary>
    /// Invoked whenever this session has changed in a way worth persisting.
    /// </summary>
    /// <remarks>
    /// A callback rather than a call to a store, for two reasons. This type is in
    /// <c>Web.Workflow</c> and knows nothing about disk - the seam keeps it that way. And
    /// every existing test constructs a session; null-by-default means none of them starts
    /// writing files.
    /// </remarks>
    public Action<BuildSession>? Autosave { get; set; }

    /// <summary>
    /// Says that the model has been edited in place, so the session can be saved.
    /// </summary>
    /// <remarks>
    /// <see cref="Change"/> cannot see these. <c>ReviewStage</c> calls <c>ModelEditor</c>
    /// directly - rename, remove, add field, promote - and every one of them mutates the
    /// model object this session already holds. Without this method an autosave hooked to
    /// answers alone would save every stage transition and lose every edit, which is
    /// precisely the work worth keeping.
    /// <para>
    /// A real edit raises <see cref="Changed"/>, because nothing else repaints a second tab: it
    /// went on listing a removed field until an edit there was refused against it.
    /// </para>
    /// </remarks>
    /// <param name="changed">Whether the editor took the edit. A refused one leaves the model as
    /// it was, so it is saved but discards nothing: it used to drop the generated app.</param>
    public void ModelEdited(bool changed = true)
    {
        if (changed)
        {
            Revision++;

            // A review edit changes what Generate would write, as a changed answer on an earlier
            // node does - but it never goes through Change, so it has to discard the written app
            // itself. Without this the old app stayed on the Generate node with no way to write
            // the new one.
            if (Generated is not null || Started)
            {
                foreach (var stage in StageGraph.Invalidates(BuildStage.Review))
                    if (_states[stage] != StageState.Active)
                        _states[stage] = StageState.Pending;

                Generated = null;
                Started = false;
            }

            Changed?.Invoke();
        }

        Autosave?.Invoke(this);
    }

    /// <summary>
    /// Invoked when this build is over for good - generated, or cancelled.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT invoked by <see cref="Abandon"/>. A closed tab is the case a saved
    /// snapshot exists for; clearing on abandon would delete the work at exactly the moment
    /// it becomes worth keeping.
    /// </remarks>
    public Action<BuildSession>? OnFinished { get; set; }

    public string WorkspaceRoot { get; }

    public BuildStage Current { get; private set; }

    public SessionPhase Phase { get; private set; } = SessionPhase.Running;

    public string Description { get; private set; }

    public ITargetScaffolder? Target { get; private set; }

    public DataSourceSpec? Source { get; private set; }

    /// <summary>Whether node 3 was answered "I have a file". Distinct from
    /// <c>Source is not null</c> only in intent, and read by node 5 to choose its shape.</summary>
    public bool HasData => Source is not null;

    public AppCustomization Customization { get; private set; }

    public CandidateDomainModel? Model { get; private set; }

    /// <summary>The rows read from the user's file, or <see cref="ImportedData.None"/> on the
    /// discovery path. Read by the generate step, and its notes — the row caps — are said out
    /// loud rather than swallowed.</summary>
    public ImportedData ImportedData { get; private set; } = ImportedData.None;

    /// <summary>The normalization findings that were ticked when <see cref="ImportedData"/> was
    /// read.</summary>
    /// <remarks>
    /// Kept so a resumed build can read its rows again the same way: the model was built from the
    /// normalized tables, and raw rows would not match its classes.
    /// </remarks>
    public IReadOnlyList<NormalizationFinding> Normalized { get; private set; } = [];

    /// <summary>Whether the generated application was actually started. Distinct from a green
    /// build: compiling is not the same as having seen it run.</summary>
    public bool Started { get; private set; }

    /// <summary>What the generate stage wrote, once it has. Null until then, and again whenever a
    /// changed answer invalidates it — the run stage must not offer to start an app that no longer
    /// matches the answers on screen.</summary>
    public GeneratedApp? Generated { get; private set; }

    private readonly List<string> _written = [];

    /// <summary>
    /// Every project folder this run has written, in order, including any whose
    /// <see cref="Generated"/> a changed answer has since dropped.
    /// </summary>
    /// <remarks>
    /// Dropping <see cref="Generated"/> does not delete the folder. Changing the application type
    /// after generating left a project on disk that nothing else remembered, so a cancel said
    /// "nothing was generated" over it.
    /// </remarks>
    public IReadOnlyList<string> Written => _written;

    /// <summary>Why the node that failed, failed. Null whenever nothing has.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>What the console prints, set by the generate step. Null until it runs.</summary>
    public BuildOutcome? Outcome { get; private set; }

    /// <summary>
    /// The question on screen, when a changed answer is waiting to be confirmed. Null the rest of
    /// the time, which is nearly always.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a flag, because the page has nothing else to say it with and the
    /// count is the part that matters: "the model" is an abstraction, and "14 classes" is the
    /// afternoon the user is about to lose.
    /// </remarks>
    public string? PendingDiscard { get; private set; }

    /// <summary>The change <see cref="PendingDiscard"/> is asking about, held unapplied.</summary>
    private Action? _pending;

    public StageState StateOf(BuildStage stage) => _states[stage];

    public bool SetDescription(string text)
    {
        var value = (text ?? string.Empty).Trim();

        return Change(BuildStage.Describe, value != Description, () => Description = value);
    }

    public bool SetTarget(ITargetScaffolder target)
        => Change(
            BuildStage.AppType,
            !string.Equals(target.Key, Target?.Key, StringComparison.OrdinalIgnoreCase),
            () => Target = target);

    public bool SetSource(DataSourceSpec source)
        => Change(BuildStage.Data, source != Source, () =>
        {
            Source = source;
            _noDataChosen = false;
        });

    public bool SetNoData()
        => Change(BuildStage.Data, Source is not null, () =>
        {
            Source = null;
            _noDataChosen = true;
        });

    /// <remarks>
    /// Called even when the user changed nothing — the look panel applies its answer either way —
    /// so this is what records that the node was answered at all.
    /// </remarks>
    public bool SetCustomization(AppCustomization customization)
        => Change(BuildStage.Look, customization != Customization, () =>
        {
            Customization = customization;
            _lookAnswered = true;
        });

    /// <summary>
    /// Which source produced <see cref="Model"/> — "qwen2.5:3b (Ollama)", "Wikipedia", or a
    /// fallback note. Empty when nothing said.
    /// </summary>
    /// <remarks>
    /// It lives here rather than on the Discover panel because that panel does not survive its
    /// own success: discovery ends with <see cref="Advance"/>, and BuildWorkflow renders one
    /// stage at a time, so the component is torn down before anything it set could be painted.
    /// Review is the first surface the user actually sees afterwards.
    /// </remarks>
    public string ModelSource { get; private set; } = string.Empty;

    /// <param name="imported">The rows the model was built from, on the data path. Applied with
    /// the model, so a change held for the discard question holds the rows too.</param>
    /// <param name="normalized">The normalization findings those rows were read with.</param>
    public bool SetModel(
        CandidateDomainModel model, string? sourceLabel = null,
        ImportedData? imported = null, IReadOnlyList<NormalizationFinding>? normalized = null)
        => Change(BuildStage.Discover, changed: true, () =>
        {
            Model = model;
            ModelSource = sourceLabel ?? string.Empty;

            if (imported is not null)
            {
                ImportedData = imported;
                Normalized = normalized ?? [];
            }
        });

    /// <summary>
    /// Whether <c>normalize</c> was typed on the line: every normalization finding starts ticked.
    /// </summary>
    /// <remarks>
    /// Before this the page ignored it and opened every finding unticked, although the manual says
    /// the option applies them all.
    /// </remarks>
    public bool NormalizeAll { get; init; }

    /// <summary>Guards <see cref="_discoveryInFlight"/> against two threads landing on it at the
    /// same instant — a real possibility, not a theoretical one: Razor's default prerendering
    /// runs a fresh top-level navigation's <c>OnInitializedAsync</c> as part of handling that HTTP
    /// request, on whatever thread pool thread Kestrel gave it, so a reload or a second tab can
    /// call <see cref="JoinDiscovery"/> from a genuinely different thread than the call already in
    /// flight.</summary>
    private readonly object _discoveryGate = new();

    /// <summary>The discovery call currently running, when one is. Held here — not on the panel
    /// that started it — because this session is the singleton every circuit shares, which is
    /// what lets a second panel find it at all.</summary>
    private Task<DiscoveryOutcome>? _discoveryInFlight;

    /// <summary>
    /// The classes the running discovery has found so far, for the panel to draw while it waits.
    /// </summary>
    /// <remarks>
    /// On the session for exactly the reason <see cref="_discoveryInFlight"/> is: a reload or a
    /// second tab JOINS the call already in flight rather than starting one, so a panel that kept
    /// this to itself would leave every joiner watching a blank list for the rest of a generation
    /// it is genuinely waiting on. Whole-reference assignment under the same gate, since the
    /// discovery reports progress from a background thread while circuits render on others.
    /// </remarks>
    private DiscoveryProgress _discovered = DiscoveryProgress.None;

    /// <summary>What the running discovery has done and still has to do. Empty when none is
    /// running.</summary>
    public DiscoveryProgress Discovered
    {
        get { lock (_discoveryGate) return _discovered; }
    }

    /// <summary>Records how the running discovery is getting on, for every tab watching it.</summary>
    public void RecordDiscoveryProgress(DiscoveryProgress progress)
    {
        lock (_discoveryGate)
            _discovered = progress;
    }

    /// <summary>
    /// Starts <paramref name="discover"/>, or, if a call is already running, hands back that same
    /// task instead of starting a second one.
    /// </summary>
    /// <remarks>
    /// This is what stops a reload or a second tab landing on <see cref="BuildStage.Discover"/>
    /// from launching a real, independent second call to the local model — wasted minutes, and
    /// contention on the one model instance — while the first is still in flight. It is also half
    /// of what stops the two calls' outcomes from racing each other into <see cref="SetModel"/>:
    /// only the <em>owner</em> — the caller this returns <c>true</c> for — should ever act on the
    /// outcome. A caller that joined has nothing left to do once the shared task completes; acting
    /// on it too is exactly the double completion that used to overwrite a good model with a stale
    /// or empty one and advance <see cref="Current"/> twice for a node it never showed.
    /// </remarks>
    /// <returns>The task to await, and whether this caller is the one that started it.</returns>
    public (Task<DiscoveryOutcome> Task, bool Owner) JoinDiscovery(
        Func<Task<DiscoveryOutcome>> discover)
        => JoinDiscovery(_ => discover());

    /// <summary>
    /// As <see cref="JoinDiscovery(Func{Task{DiscoveryOutcome}})"/>, handing the call the token
    /// <see cref="StopDiscovery"/> cancels.
    /// </summary>
    /// <remarks>
    /// The task handed back is cancelled by <see cref="StopDiscovery"/> even when the call itself
    /// cannot stop: every page waiting on it - the owner and every joiner - stops waiting at once.
    /// </remarks>
    public (Task<DiscoveryOutcome> Task, bool Owner) JoinDiscovery(
        Func<CancellationToken, Task<DiscoveryOutcome>> discover)
    {
        lock (_discoveryGate)
        {
            if (_discoveryInFlight is { } running)
                return (running, false);

            // Not disposed when the call ends: the call may still be reading the token.
            var stop = new CancellationTokenSource();
            var task = discover(stop.Token).WaitAsync(stop.Token);
            _discoveryStop = stop;
            _discoveryInFlight = task;

            return (task, true);
        }
    }

    /// <summary>Cancels <see cref="_discoveryInFlight"/>; null when none is running.</summary>
    private CancellationTokenSource? _discoveryStop;

    /// <summary>
    /// Stops the running discovery, so every page waiting on it stops waiting and nothing is applied.
    /// </summary>
    /// <remarks>
    /// The busy view had no way out: a user who saw the wrong description going in could only wait
    /// minutes for a model they did not want.
    /// </remarks>
    public void StopDiscovery()
    {
        lock (_discoveryGate)
            _discoveryStop?.Cancel();
    }

    /// <summary>
    /// Clears the in-flight discovery call, so the next <see cref="JoinDiscovery"/> starts a
    /// fresh one rather than joining a call that has already finished.
    /// </summary>
    /// <remarks>
    /// Only the owner <see cref="JoinDiscovery"/> returned should call this — it is what makes
    /// "try again" from a failed discovery actually try again, rather than replaying the same
    /// finished (or faulted) task forever.
    /// </remarks>
    public void DiscoveryFinished()
    {
        lock (_discoveryGate)
        {
            _discoveryInFlight = null;
            _discoveryStop = null;

            // Cleared with the call itself: the next run starts from nothing found, so a retry
            // after a failure cannot show the previous attempt's classes as though they were this
            // one's.
            _discovered = DiscoveryProgress.None;
        }
    }

    public void SetImportedData(
        ImportedData imported, IReadOnlyList<NormalizationFinding>? normalized = null)
    {
        ImportedData = imported;
        Normalized = normalized ?? [];

        Changed?.Invoke();
    }

    /// <summary>Records what the generate stage produced, for the run stage to start.</summary>
    public void SetGenerated(GeneratedApp app)
    {
        Generated = app;
        Started = false;

        RecordWritten(app.Scaffold.ProjectPath);

        Changed?.Invoke();
    }

    /// <summary>
    /// Records a project folder this run wrote, without offering it to the run stage.
    /// </summary>
    /// <remarks>
    /// For a write whose answers changed while it built: the app no longer matches them, so it is
    /// not <see cref="Generated"/>, but the folder is on disk and a cancel must still name it.
    /// </remarks>
    public void RecordWritten(string? path)
    {
        if (path is { Length: > 0 } && !_written.Contains(path, StringComparer.OrdinalIgnoreCase))
            _written.Add(path);
    }

    /// <summary>
    /// Goes up by one on every change to an answer or to the model.
    /// </summary>
    /// <remarks>
    /// Read by the generate stage before a write and again after its build: a build takes a
    /// minute, and an answer changed meanwhile means the app it built is not the one the session
    /// now wants. It recorded that app anyway, so Generate and Run described a WPF app under a
    /// Console target.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>
    /// The process the run stage started, while it is still ours to stop. Null before a run, and
    /// again once it has been stopped.
    /// </summary>
    /// <remarks>
    /// On the session rather than the panel because a reload builds a new panel, and an app the
    /// user can no longer stop from the page is an app holding a <c>bin</c> folder that the next
    /// build needs.
    /// </remarks>
    public int? StartedProcessId { get; private set; }

    /// <summary>Records that the run stage started it, and what it started.</summary>
    public void MarkStarted(int? processId = null)
    {
        Started = true;
        StartedProcessId = processId is > 0 ? processId : null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Forgets the process, the app having been stopped.
    /// </summary>
    /// <remarks>
    /// <see cref="Started"/> stays true: the run happened, and the node holds that answer. What is
    /// gone is only the offer to stop something that is no longer there.
    /// </remarks>
    public void MarkStopped()
    {
        StartedProcessId = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Whether <paramref name="stage"/> holds something: an answer given, or work done.
    /// </summary>
    /// <remarks>
    /// This is what <see cref="GoTo"/> uses to decide whether the node being left has been
    /// answered, and it is asked of the session rather than inferred from the node's state because
    /// a state can lie. Marking a node Done merely because the user passed through it is how a
    /// diagram ends up claiming a project was generated when nothing was written.
    /// </remarks>
    public bool HoldsAnAnswer(BuildStage stage)
        => stage switch
        {
            BuildStage.Describe => Description.Length > 0,
            BuildStage.AppType => Target is not null,
            BuildStage.Data => Source is not null || _noDataChosen,
            BuildStage.Look => _lookAnswered,

            // The review node holds the model it reviews, so it stays answered for as long as
            // there is one — even though its own step is an action rather than an answer.
            BuildStage.Discover => Model is not null,
            BuildStage.Review => Model is not null,

            // Same rule as Review, and for the same reason: this node describes the model it is
            // shown beside, so it stays answered for as long as there is one.
            BuildStage.Output => Model is not null,
            BuildStage.Generate => Generated is not null,
            BuildStage.Run => Started,
            _ => false,
        };

    /// <summary>
    /// Applies an answer, and discards whatever it invalidated — or holds it, and asks first.
    /// </summary>
    /// <remarks>
    /// <paramref name="changed"/> is what makes clicking back into a node harmless: re-answering
    /// with the same value is not a change, so looking at an answer and pressing Next does not
    /// throw away a Wikipedia fetch.
    /// <para>
    /// A change that would cost the model is not applied at all until it is confirmed. Holding it
    /// rather than applying-and-offering-undo is what keeps this honest: while the question is on
    /// screen the session still holds the model, so a user who closes the tab, or answers the
    /// question with the browser's Back button, loses nothing.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Whether the answer was applied. False means it is held and <see cref="PendingDiscard"/> is
    /// the question to put on screen — which is also why the panel must not advance.
    /// </returns>
    private bool Change(BuildStage stage, bool changed, Action apply)
    {
        RequireRunning();

        if (changed && WouldDiscardModel(stage))
        {
            _pending = () => Apply(stage, changed: true, apply);
            PendingDiscard = DiscardWarning();

            Changed?.Invoke();

            return false;
        }

        Apply(stage, changed, apply);

        Changed?.Invoke();

        return true;
    }

    /// <summary>
    /// A change is only worth a question when there is a model to lose and this answer is what
    /// discovery was run from. The table in <see cref="StageGraph.Invalidates"/> is the authority
    /// — asking anywhere else would be caution the rules do not support, and users who are asked
    /// needlessly stop reading the question.
    /// </summary>
    private bool WouldDiscardModel(BuildStage stage)
        => Model is not null && StageGraph.Invalidates(stage).Contains(BuildStage.Discover);

    private string DiscardWarning()
    {
        var count = Model!.Entities.Count;

        return $"This discards the {count} {(count == 1 ? "class" : "classes")}, "
            + "and discovery will run again.";
    }

    /// <summary>Applies a held change. The page calls this when the user says to go ahead.</summary>
    /// <exception cref="InvalidOperationException">Nothing is held. The page drew a question the
    /// session was not asking, which is a wiring bug rather than something to shrug at.</exception>
    public void ConfirmPending()
    {
        RequireRunning();

        var pending = _pending
            ?? throw new InvalidOperationException("No change is waiting to be confirmed.");

        _pending = null;
        PendingDiscard = null;

        pending();

        Changed?.Invoke();
    }

    /// <summary>Drops a held change, model intact. The answer on screen stays as it was.</summary>
    public void CancelPending()
    {
        _pending = null;
        PendingDiscard = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// The question on screen when a cancel is waiting to be answered. Null the rest of the time.
    /// </summary>
    /// <remarks>
    /// On the session rather than the page for the same reason <see cref="PendingDiscard"/> is:
    /// the workflow is one run with possibly two tabs looking at it, and the tab that did not
    /// click Cancel must not go on offering a step forward through a run that is being ended.
    /// </remarks>
    public string? PendingCancel { get; private set; }

    /// <summary>
    /// Ends the workflow — asking first, when there is anything to lose.
    /// </summary>
    /// <remarks>
    /// Cancel is the most destructive control on the page and it was the only unguarded one, while
    /// re-typing a description had been asking since the round before. Nothing is held here the
    /// way <see cref="Change"/> holds a change, because nothing needs to be: cancelling is not
    /// applied until it is confirmed, so waiting costs the user nothing.
    /// </remarks>
    /// <returns>Whether the workflow was cancelled outright, there being nothing to ask about.</returns>
    public bool RequestCancel()
    {
        RequireRunning();

        // An empty first page has nothing to confirm losing, and a wizard that asks anyway teaches
        // the user that these questions are noise to be clicked away.
        if (!StageGraph.Nodes.Any(node => HoldsAnAnswer(node.Stage)))
        {
            Cancel();

            return true;
        }

        PendingCancel = CancelWarning();

        Changed?.Invoke();

        return false;
    }

    /// <summary>
    /// What cancelling would actually cost, which is not the same at every node.
    /// </summary>
    /// <remarks>
    /// A written project is not deleted by cancelling, so a warning that implied it was would be
    /// overstating the stakes — the same failure as understating them, and the faster way to teach
    /// someone to ignore the question.
    /// </remarks>
    private string CancelWarning()
    {
        if (Generated is not null)
            return $"The project at {Generated.Scaffold.ProjectPath} stays on disk, "
                + "but the workflow ends here and the console reports it cancelled.";

        // Written by this run, then dropped by a changed answer: the folder is still there.
        if (Written.Count > 0)
        {
            var places = string.Join(" and ", Written);

            return Written.Count == 1
                ? $"The project this run wrote at {places} stays on disk, "
                    + "but the workflow ends here and the console reports it cancelled."
                : $"The projects this run wrote at {places} stay on disk, "
                    + "but the workflow ends here and the console reports it cancelled.";
        }

        if (Model is not null)
        {
            var count = Model.Entities.Count;

            if (EarlierProject() is { } earlier)
                return $"This discards the {count} {(count == 1 ? "class" : "classes")} discovered "
                    + $"and generates nothing more. The project already at {earlier} stays on disk "
                    + "as it is.";

            return $"This discards the {count} {(count == 1 ? "class" : "classes")} discovered, "
                + "and nothing will be generated.";
        }

        return "This discards the answers so far, and nothing will be generated.";
    }

    /// <summary>
    /// The project folder an earlier run already wrote where this one would write, when this run
    /// has written nothing itself; otherwise null.
    /// </summary>
    /// <remarks>
    /// A resumed build has no <see cref="Generated"/> of its own, yet the session it resumes may
    /// have written the project. Saying "nothing will be generated" over it read as though there
    /// were nothing on disk. Keyed on the project file, as the overwrite question is.
    /// </remarks>
    public string? EarlierProject()
    {
        if (Generated is not null || Target is null)
            return null;

        var name = GenerateStep.NameFor(this);

        return GenerateStep.Collides(this, name, Target) ? GenerateStep.FolderFor(this, name, Target) : null;
    }

    /// <summary>Ends the workflow, the question having been answered.</summary>
    /// <exception cref="InvalidOperationException">Nothing was asked. As with
    /// <see cref="ConfirmPending"/>, the page drew a question the session was not asking.</exception>
    public void ConfirmCancel()
    {
        RequireRunning();

        if (PendingCancel is null)
            throw new InvalidOperationException("No cancel is waiting to be confirmed.");

        PendingCancel = null;

        Cancel();
    }

    /// <summary>Drops the question. Every answer stays exactly as it was.</summary>
    public void KeepBuilding()
    {
        PendingCancel = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Writes the answer in, and clears out what it invalidated.
    /// </summary>
    /// <remarks>
    /// The value is written back even when nothing changed — that is how the look node records
    /// that it was answered at all — but only a real change costs anything downstream.
    /// </remarks>
    private void Apply(BuildStage stage, bool changed, Action apply)
    {
        apply();

        if (!changed)
            return;

        Revision++;
        FailureMessage = null;

        foreach (var invalidated in StageGraph.Invalidates(stage))
        {
            // A stage never invalidates itself: `apply()` just wrote the new answer, and
            // Discover's self-entry in StageGraph.Invalidates exists purely so a SECOND SetModel
            // call is caught by WouldDiscardModel before Apply ever runs — not so this, the first
            // (and only genuinely applied) call, immediately deletes the model it just set. That
            // would otherwise happen for any caller who calls SetModel before Current has reached
            // Discover — every real one does, since the panel that calls it IS the Discover node,
            // but a couple of tests call it directly without walking there first, and both are
            // right to expect the model to stick.
            if (invalidated == stage)
                continue;

            if (_states[invalidated] == StageState.Active)
                continue;

            _states[invalidated] = StageState.Pending;

            if (invalidated == BuildStage.Discover)
            {
                Model = null;
                ImportedData = ImportedData.None;
                Normalized = [];
            }

            if (invalidated == BuildStage.Generate)
                Generated = null;

            // The run described the app that is gone. The process id is kept on purpose: that app
            // may still be running, it holds its own bin folder so the next build of the project
            // fails until it closes, and the Stop button is how the user closes it.
            if (invalidated == BuildStage.Run)
                Started = false;
        }

        // Here rather than in Change, which returns early while it is holding a pending
        // discard - a snapshot taken then would record an answer this session has not
        // accepted yet.
        Autosave?.Invoke(this);
    }

    /// <summary>Marks the open node answered and opens the next one.</summary>
    public void Advance()
    {
        RequireRunning();

        _states[Current] = StageState.Done;

        var next = StageGraph.Next(Current);

        if (next is null)
            return;

        Current = next.Value;
        _states[Current] = StageState.Active;

        Changed?.Invoke();

        // Moving on is progress as well. Without this the snapshot recorded the stage of the
        // last ANSWER, so a build left on Output after an unedited Review resumed on Discover.
        Autosave?.Invoke(this);
    }

    /// <summary>Reopens an answered node. This is what a click on a green node does.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="stage"/> has not been reached. A Pending node has nothing to show and no
    /// answer to change, so a click on one is a wiring bug rather than user input.
    /// </exception>
    public void GoTo(BuildStage stage)
    {
        RequireRunning();

        if (_states[stage] == StageState.Pending)
            throw new InvalidOperationException($"{stage} has not been reached yet.");

        // The node being left is recorded for what it actually holds. Done where it was answered,
        // so it stays green and can be clicked back into; Pending where it was only passed
        // through — walking into the generate node and leaving again writes nothing, and a green
        // tick there would say a project exists.
        if (_states[Current] == StageState.Active)
            _states[Current] = HoldsAnAnswer(Current) ? StageState.Done : StageState.Pending;

        Current = stage;
        _states[stage] = StageState.Active;

        Changed?.Invoke();

        // As Advance: the node someone went back to is where they left off.
        Autosave?.Invoke(this);
    }

    /// <summary>Records that the open node could not do its work. Not terminal: the panel offers
    /// a way back, because a mistyped path is not a reason to abandon an interview.</summary>
    public void Fail(string message)
    {
        RequireRunning();

        FailureMessage = message;
        _states[Current] = StageState.Failed;

        Changed?.Invoke();
    }

    public void Cancel()
    {
        Phase = SessionPhase.Cancelled;

        Changed?.Invoke();
        OnFinished?.Invoke(this);
    }

    /// <summary>The tab was closed. Distinct from <see cref="Cancel"/>: nothing was decided.</summary>
    /// <remarks>
    /// Deliberately does NOT invoke <see cref="OnFinished"/>, and its absence is the point:
    /// a closed tab is what a saved snapshot exists for, so clearing here would delete the
    /// work at the one moment it is worth keeping. Cancel and Complete both invoke it.
    /// </remarks>
    public void Abandon()
    {
        Phase = SessionPhase.Abandoned;

        Changed?.Invoke();
    }

    /// <summary>
    /// Ends the workflow with what the console should print.
    /// </summary>
    /// <remarks>
    /// Marks whichever node it was called from, not <see cref="BuildStage.Run"/>: a generation
    /// that failed finishes on <see cref="BuildStage.Generate"/>, and claiming the app was run
    /// would be a lie the diagram tells.
    /// </remarks>
    public void Complete(BuildOutcome outcome)
    {
        Outcome = outcome;
        _states[Current] = StageState.Done;
        Phase = SessionPhase.Generated;

        Changed?.Invoke();
        OnFinished?.Invoke(this);
    }

    private void RequireRunning()
    {
        if (Phase != SessionPhase.Running)
            throw new InvalidOperationException($"The workflow has already ended ({Phase}).");
    }

    /// <summary>The first node with no answer, which is where an inline-answered run opens.</summary>
    private BuildStage FirstUnanswered()
    {
        foreach (var node in StageGraph.Nodes)
            if (_states[node.Stage] != StageState.Done)
                return node.Stage;

        return BuildStage.Generate;
    }
}
