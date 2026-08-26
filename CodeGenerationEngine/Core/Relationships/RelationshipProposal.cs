namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// One relationship the tool believes is missing, and why it believes it.
/// </summary>
/// <remarks>
/// <paramref name="Reason"/> is not decoration. The user is being asked to approve a change to
/// their schema, and "because Order.CustomerId is an int and Customer exists" is the difference
/// between a decision and a guess.
/// </remarks>
public sealed record RelationshipProposal(
    string Owner,
    string Target,
    RelationshipKind Kind,
    string Reason)
{
    /// <summary>Identifies the pair, so a proposal the user has already refused is not put to
    /// them again. Keyed on the two names and not on the kind: refusing a one-to-many between
    /// these two is a refusal of the pair.</summary>
    public string Key => $"{Owner}->{Target}";
}
