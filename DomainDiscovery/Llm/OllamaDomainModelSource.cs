using DomainDiscovery.Model;

namespace DomainDiscovery.Llm;

/// <summary>Asks a local model for the domain, then repairs what it says.</summary>
/// <remarks>
/// Two routes, chosen by <see cref="DiscoveryConfig.Phases"/>:
/// <list type="bullet">
/// <item><b>Two phases</b> (the default). Ask which classes exist, show that whole list, then ask
/// what is in them. Measured on a point-of-sale system: the complete list of twelve classes is on
/// screen at 42 seconds, and the finished model arrives at 229. It also degrades far better —
/// every class the roster named survives even if the detail pass is cut short, because
/// <see cref="TwoPhaseModel.Merge"/> treats the roster as the authority on membership.</item>
/// <item><b>One phase.</b> Everything in a single call: ten classes in 158 seconds for the same
/// description. Quicker to a finished model, but nothing complete to show until it is nearly
/// done, and a run cut short loses the classes it had not reached.</item>
/// </list>
/// </remarks>
public sealed class OllamaDomainModelSource : IDomainModelSource
{
    private readonly IOllamaClient _client;
    private readonly string _modelName;
    private readonly int _phases;

    public OllamaDomainModelSource(IOllamaClient client, string modelName, int phases = 2)
    {
        _client = client;
        _modelName = modelName;

        // Anything unrecognised takes the default rather than failing: a hand-edited config must
        // leave the tool working, which is the same rule DiscoveryConfig itself follows.
        _phases = phases == 1 ? 1 : 2;
    }

    /// <summary>Named so the Discover panel can say which model produced the classes.</summary>
    public string Label => $"{_modelName} (Ollama)";

    public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
    {
        var answer = _phases == 1
            ? _client.Ask(description, Reporting(onProgress, "Working out the classes", finished: true))
            : TwoPhases(description, onProgress);

        if (answer.Model is null)
            return new DiscoveryOutcome(
                new CandidateDomainModel(),
                Label,
                false,
                $"{_modelName} did not answer: {answer.Error ?? "no reason was reported."}");

        var model = LlmModelNormalizer.Normalize(answer.Model);

        if (model.Entities.Count == 0)
            return new DiscoveryOutcome(
                model,
                Label,
                false,
                $"{_modelName} proposed no usable classes for that description.");

        // A run that was stopped early still succeeded — it produced classes, and they are as good
        // as any others. The warning says the list may be short, or short of detail, without
        // pretending it is empty.
        return new DiscoveryOutcome(model, Label, true, answer.Warning);
    }

    /// <summary>
    /// Ask which classes exist, then ask what is in them.
    /// </summary>
    /// <remarks>
    /// The detail pass failing is NOT a failure of the discovery: the roster on its own is a real
    /// model — correctly named and classified classes, each of which the normalizer gives a key —
    /// and it is what the user has already been looking at for a couple of minutes. Returning the
    /// roster with a warning is strictly better than discarding it, which is the same judgement
    /// the salvage path in <see cref="OllamaClient"/> makes one level down.
    /// </remarks>
    private OllamaAnswer TwoPhases(string description, Action<DiscoveryProgress>? onProgress)
    {
        var rosterAnswer = _client.AskForClasses(
            description,
            Reporting(onProgress, "Working out which classes there are", finished: false));

        // Nothing to build on. The factory's fallback takes it from here.
        if (rosterAnswer.Model is not { Classes.Count: > 0 } roster)
            return rosterAnswer.Succeeded
                ? OllamaAnswer.Failure($"{_modelName} named no classes for that description.")
                : rosterAnswer;

        var names = roster.Classes.Select(PartialLlmJson.Describe).ToList();

        // The whole list, before a single property has been asked for. This is the point of the
        // split, and it happens here rather than inside the client because only this method knows
        // that the first pass has finished.
        onProgress?.Invoke(new DiscoveryProgress([], names, "Filling in the properties"));

        var detailAnswer = _client.AskForDetail(
            description, roster, found => onProgress?.Invoke(DetailProgress(roster, found)));

        var merged = TwoPhaseModel.Merge(roster, detailAnswer.Model);

        if (detailAnswer.Model is null)
            return OllamaAnswer.Partial(
                merged,
                $"All {merged.Classes.Count} classes were found, but no properties could be added "
                + $"to them: {detailAnswer.Error ?? "the second pass did not answer."}");

        var missing = TwoPhaseModel.WithoutDetail(roster, detailAnswer.Model).Count;

        if (missing > 0)
            return OllamaAnswer.Partial(
                merged,
                $"{missing} of {merged.Classes.Count} class(es) have no properties yet — the pass "
                + "that fills them in did not finish."
                + (detailAnswer.Warning is null ? string.Empty : $" {detailAnswer.Warning}"),
                detailAnswer.DoneReason);

        return detailAnswer.Warning is null
            ? OllamaAnswer.Success(merged, detailAnswer.DoneReason)
            : OllamaAnswer.Partial(merged, detailAnswer.Warning, detailAnswer.DoneReason);
    }

    /// <summary>
    /// What the panel shows during the detail pass: the roster split into the classes that now
    /// have properties and the ones still waiting for them.
    /// </summary>
    private static DiscoveryProgress DetailProgress(
        LlmDomainModel roster, IReadOnlyList<LlmClass> found)
    {
        // Matched by name, not by reference: these came off the wire in the second pass, so they
        // are different objects from the roster's even where they mean the same class — and the
        // model chooses its own casing, as TwoPhaseModel has to allow for too.
        var filled = new Dictionary<string, LlmClass>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in found)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
                filled.TryAdd(c.Name, c);
        }

        var done = new List<string>();
        var pending = new List<string>();

        foreach (var named in roster.Classes)
        {
            if (filled.TryGetValue(named.Name, out var match))
            {
                var count = match.Properties.Count;

                done.Add(
                    $"{PartialLlmJson.Describe(named)} — {count} "
                    + (count == 1 ? "property" : "properties"));
            }
            else
            {
                pending.Add(PartialLlmJson.Describe(named));
            }
        }

        return new DiscoveryProgress(
            done, pending, $"Filling in the properties — {done.Count} of {roster.Classes.Count}");
    }

    /// <summary>
    /// Adapts the client's raw "classes so far" to the panel's shape, for a pass that has no roster
    /// to measure against: the one-shot ask, and the roster pass itself. Neither yet knows what
    /// its own total will be, so one of the two lists is always empty.
    /// </summary>
    /// <param name="finished">
    /// Whether a class arriving from this pass is <em>done</em>. True for the one-shot ask, whose
    /// classes arrive complete with their properties. False for the roster pass, which has only
    /// decided that a class exists — naming it is not finishing it, and the detail pass is about
    /// to list every one of them as still to do. Ticking them here and then unticking them a moment
    /// later is what this distinction exists to prevent: watching a panel's ticks run backwards
    /// reads as something going wrong, which was found by looking at the running page and could
    /// not have been found any other way.
    /// </param>
    private static Action<IReadOnlyList<LlmClass>>? Reporting(
        Action<DiscoveryProgress>? onProgress, string step, bool finished)
    {
        if (onProgress is null)
            return null;

        return found =>
        {
            var named = found.Select(PartialLlmJson.Describe).ToList();

            onProgress(finished
                ? new DiscoveryProgress(named, [], step)
                : new DiscoveryProgress([], named, step));
        };
    }
}
