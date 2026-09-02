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

    public DiscoveryOutcome Discover(string description)
    {
        var raw = _client.Ask(description);

        if (raw is null)
            return new DiscoveryOutcome(
                new CandidateDomainModel(),
                Label,
                false,
                $"{_modelName} did not answer. Check that Ollama is running and the model is pulled.");

        var model = LlmModelNormalizer.Normalize(raw);

        if (model.Entities.Count == 0)
            return new DiscoveryOutcome(
                model,
                Label,
                false,
                $"{_modelName} proposed no usable classes for that description.");

        return new DiscoveryOutcome(model, Label, true, null);
    }
}
