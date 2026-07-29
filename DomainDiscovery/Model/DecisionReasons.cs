namespace DomainDiscovery.Model;

/// <summary>
/// Every reason the pipeline can give, in one place. Call sites use these constants rather
/// than literals so the phrasing can be changed once, and so a test asserts the same string
/// the pipeline emits instead of a copy that can drift out of step with it.
/// </summary>
public static class DecisionReasons
{
    // --- Seed extraction ---
    public const string NotRecognizedAsAThing = "not recognized as a thing";
    public const string NameNormalizedToNothing = "its name normalized to nothing";
    public const string AlreadyNamed = "already named by another candidate";
    public const string YouNamedIt = "you named it";

    // --- Enrichment ---
    public const string NotAUsableClassName = "not a usable class name";
    public const string LeadDoesNotMentionIt = "the article's lead does not mention it";
    public const string AlreadyAClassFromYourDescription = "already a class from your description";

    // --- Shaping ---
    public const string DefinedAsADomainClass = "defined as a domain class";
    public const string DefinitionResolvesToAnAbstraction = "its definition resolves to an abstraction";
    public const string YouNamedItNoDefinitionFound = "you named it; no definition found";
    public const string NoDefinitionWeRecognize = "no definition we recognize";
}
