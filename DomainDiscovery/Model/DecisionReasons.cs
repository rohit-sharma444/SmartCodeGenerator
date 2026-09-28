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

    /// <summary>
    /// The same skip as <see cref="AlreadyAClassFromYourDescription"/>, but for a class an
    /// EARLIER article contributed rather than one the user named. Enrichment runs once per
    /// article and merges into the same model, so from the second article onward the thing a
    /// candidate collides with is often not the user's description at all.
    /// </summary>
    public const string AlreadyAClassFromAnEarlierArticle = "already a class from an earlier article";

    // --- Shaping ---
    public const string DefinedAsADomainClass = "defined as a domain class";
    public const string DefinitionResolvesToAnAbstraction = "its definition resolves to an abstraction";
    public const string YouNamedItNoDefinitionFound = "you named it; no definition found";
    public const string NoDefinitionWeRecognize = "no definition we recognize";

    // --- Data ingestion ---
    public const string ReadFromYourData = "read from your data";
    public const string PrimaryKeyIsGenerated = "a key column is generated, not read";
    public const string ColumnWouldCollideWithItsClassName =
        "a column may not share its class's name";
    public const string ColumnWouldHideAnInheritedMember =
        "a column may not share a member every class already has";
    public const string ColumnNameIsNotAValidIdentifier = "its column name is not a usable name";

    // --- Normalization ---
    public const string ExtractedFromARepeatingGroup = "extracted from a repeating group";
    public const string ExtractedFromANonAtomicColumn = "extracted from a column holding several values";
    public const string ExtractedFromAPartialDependency = "extracted from a partial dependency";
    public const string DroppedAsADerivedColumn = "dropped: it is computed from other columns";

    // --- Model proposal ---
    public const string ProposedByTheModel = "proposed by the model";
    public const string TheModelGaveNoUsableName = "the model gave it no usable class name";

    // --- Review loop ---
    public const string YouRenamedIt = "you renamed it";
    public const string YouRemovedIt = "you removed it";
}
