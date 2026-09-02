namespace DomainDiscovery.Model;

/// <summary>Which stage of discovery reached a verdict about a candidate.</summary>
public enum DecisionStage
{
    /// <summary>Reading the nouns out of the user's own description.</summary>
    SeedExtraction,

    /// <summary>Harvesting and filtering candidates from a Wikipedia article.</summary>
    Enrichment,

    /// <summary>Deciding, from a candidate's own definition, whether it is a domain class.</summary>
    Shaping,

    /// <summary>
    /// Restructuring a data source the user supplied. A normalization split creates a class nobody
    /// named and no article mentioned, so `why` needs a trail for it just as much as for a
    /// harvested noun — arguably more, since the user did not ask for it by name.
    /// </summary>
    Normalization,

    /// <summary>
    /// Asking a language model for the domain's classes outright. Distinct from
    /// <see cref="Enrichment"/>, which mines candidates out of an article's prose.
    /// </summary>
    ModelProposal,

    /// <summary>
    /// The user's own edits in the review loop. Discovery is over by this point, but an edit can
    /// still change what a later `why` has to explain — a rename moves an entity away from the
    /// name its whole trail is filed under.
    /// </summary>
    ReviewLoop
}

/// <summary>What became of a candidate.</summary>
/// <remarks>
/// <see cref="Rejected"/> and <see cref="Skipped"/> are deliberately distinct, and the
/// difference is what the user should do about it. Rejected means the pipeline judged the
/// candidate and said no — <c>add</c> forces it back. Skipped means it never reached
/// judgement at all, which usually means the description needs rewording instead.
/// </remarks>
public enum DecisionOutcome
{
    /// <summary>It is a class in the model.</summary>
    Kept,

    /// <summary>Offered to the user, who can take it with <c>add</c>.</summary>
    Suggested,

    /// <summary>Judged not to be a domain class.</summary>
    Rejected,

    /// <summary>Never reached a verdict.</summary>
    Skipped
}

/// <summary>
/// One decision the discovery pipeline made about one candidate name. Append-only, and never
/// read by scaffolding: this exists so a user can ask why a class is missing and get an answer
/// instead of a guess.
/// </summary>
public sealed class DecisionRecord
{
    /// <summary>The name a user would recognize — the class name for a candidate that became one,
    /// otherwise the text they typed. <see cref="Model.CandidateDomainModel"/> holds both
    /// spellings across a run, and the report matches them canonically rather than forcing one here.</summary>
    public required string Name { get; init; }

    public required DecisionStage Stage { get; init; }

    public required DecisionOutcome Outcome { get; init; }

    /// <summary>A constant from <see cref="DecisionReasons"/>, never an ad-hoc literal — so a
    /// test asserts the same string the pipeline emits rather than a copy of it.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The concrete facts behind the reason — head noun, article title, entity type,
    /// confidence — as one already-composed line, rendered verbatim by the report. A record
    /// may carry several facts here; the reason stays the constant, the specifics stay here.
    /// </summary>
    public string? Evidence { get; init; }
}
