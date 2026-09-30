using DomainDiscovery.Model;

namespace DomainDiscovery.Llm;

/// <summary>
/// Where a model in the language model's JSON shape came from, so `why` does not say "proposed by
/// the model" about a class that shipped with the tool.
/// </summary>
public sealed record ModelOrigin(Provenance Provenance, DecisionStage Stage, string KeptReason)
{
    public static ModelOrigin LanguageModel { get; } =
        new(Provenance.Model, DecisionStage.ModelProposal, DecisionReasons.ProposedByTheModel);

    /// <remarks>
    /// <see cref="Provenance.Data"/>: the tool supplied it, not the user and not a model. A member of
    /// its own would ripple into every switch over Provenance, and nothing downstream needs to tell a
    /// built-in field from an imported one — `why` does it through the stage.
    /// </remarks>
    public static ModelOrigin BuiltIn(string domainName) =>
        new(Provenance.Data, DecisionStage.BuiltInDomain, DecisionReasons.FromTheBuiltInDomain(domainName));
}
