namespace CodeGenerator.App;

/// <summary>
/// How people actually ask for each review-loop action, for the AI phrase index.
/// </summary>
/// <remarks>
/// These are paraphrases, NOT the verbs themselves — the verbs already match exactly,
/// and a phrase index that only knows "remove" cannot help someone who typed "get rid
/// of Reservation".
///
/// Verb-only by design. The review verbs take arguments (<c>add &lt;name&gt;</c>,
/// <c>rename &lt;old&gt; &lt;new&gt;</c>) and retrieval recovers the ACTION, never the
/// operands, so a suggestion is printed for the user to type rather than executed. That
/// also keeps the loop safe under a redirected stdin, where the review loop lives and
/// where nothing may block on a prompt.
///
/// <c>add</c>/<c>field</c> and <c>remove</c>/<c>unfield</c> are deliberately close in
/// meaning. When retrieval cannot separate them the vote splits, confidence falls below
/// the floor, and no suggestion is offered — which is the intended outcome, not a gap.
/// </remarks>
internal static class ReviewCommandPhrases
{
    /// <summary>Calibrated for THIS catalog, not borrowed from AiConfig.
    ///
    /// Measured over the committed model with the gates open: genuine paraphrases score
    /// 0.62-0.86 against their verb, while nonsense tops out at 0.21 ("zzzz qqqq" 0.212,
    /// "the weather in Paris is cold" 0.136). 0.45 sits well clear of both.
    ///
    /// This is the gate that does the work. Share-of-vote does NOT discriminate here —
    /// "the weather in Paris is cold" votes `field` with 100% confidence, because with
    /// only eight well-separated labels the neighbours agree even on garbage. That is
    /// precisely the case the absolute-score gate exists for.</summary>
    public const double MinTopScore = 0.45;

    /// <summary>Just above 0.5, so an exact two-way tie still declines (the floor is an
    /// inclusive minimum) without rejecting real matches. AiConfig's 0.55 was measured
    /// wrongly rejecting "get rid of that entity", a correct `remove` at 0.54.</summary>
    public const double ConfidenceFloor = 0.51;

    /// <summary>2, for the reason AiConfig.DefaultTopK gives: 1 makes every vote
    /// unanimous and switches the floor off entirely.</summary>
    public const int TopK = 2;

    public static IReadOnlyList<(string Text, string Label)> All { get; } =
    [
        ("generate the app", "approve"),
        ("build it now", "approve"),
        ("go ahead and create the application", "approve"),
        ("looks good, make it", "approve"),
        ("that model is fine, generate", "approve"),

        ("stop without generating anything", "cancel"),
        ("never mind, forget it", "cancel"),
        ("abort this", "cancel"),
        ("quit the review", "cancel"),

        ("add an entity to the model", "add"),
        ("i need another class", "add"),
        ("put in a new model", "add"),
        ("create one more entity", "add"),

        ("delete an entity from the model", "remove"),
        ("get rid of a class", "remove"),
        ("take that entity out", "remove"),
        ("drop one of the models", "remove"),

        ("rename an entity", "rename"),
        ("change the name of a class", "rename"),
        ("call that model something else", "rename"),

        ("add a field to an entity", "field"),
        ("give a class another property", "field"),
        ("put an attribute on a model", "field"),
        ("this entity is missing a column", "field"),
        ("put a total field on that entity", "field"),
        ("add a price field to a class", "field"),

        ("remove a field from an entity", "unfield"),
        ("delete a property off a class", "unfield"),
        ("that model has a field it should not", "unfield"),
        ("take the total field off that entity", "unfield"),

        ("explain how the model was decided", "why"),
        ("where did these entities come from", "why"),
        ("show me the reasoning", "why"),
        ("justify this class", "why"),
        ("why is this entity in the model", "why"),
        ("why did you include that class", "why"),

        ("what rules were suggested from my data", "rules"),
        ("show me the proposed validation rules", "rules"),
        ("what constraints did the scan find", "rules"),
        ("list the rules you inferred", "rules"),

        ("apply that suggested rule", "accept"),
        ("take the rule you proposed", "accept"),
        ("use the validation rule you found", "accept"),
        ("go with the suggested constraint", "accept"),
    ];
}
