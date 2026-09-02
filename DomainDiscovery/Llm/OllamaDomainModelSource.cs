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
        var answer = _client.Ask(description);

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

        return new DiscoveryOutcome(model, Label, true, null);
    }
}
