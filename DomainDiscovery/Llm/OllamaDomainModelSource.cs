using DomainDiscovery.Model;

namespace DomainDiscovery.Llm;

/// <summary>Asks a local model for the domain, then repairs what it says.</summary>
public sealed class OllamaDomainModelSource : IDomainModelSource
{
    private readonly IOllamaClient _client;
    private readonly string _modelName;

    public OllamaDomainModelSource(IOllamaClient client, string modelName)
    {
        _client = client;
        _modelName = modelName;
    }

    /// <summary>Named so the Discover panel can say which model produced the classes.</summary>
    public string Label => $"{_modelName} (Ollama)";

    public DiscoveryOutcome Discover(string description, Action<IReadOnlyList<string>>? onProgress = null)
    {
        var answer = _client.Ask(description, onProgress);

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
        // as any others. The warning says the list may be shorter than it would have been, which
        // is the honest thing to tell someone who can simply run it again.
        return new DiscoveryOutcome(model, Label, true, answer.Warning);
    }
}
