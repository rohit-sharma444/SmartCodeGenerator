namespace CodeGenerationEngine.Models;

/// <summary>What a proposal would become.</summary>
public enum RuleProposalKind
{
    /// <summary>A small fixed set of values, which becomes an allowed-values rule.</summary>
    Status,

    /// <summary>No value repeats.</summary>
    Unique,

    /// <summary>Every value matches a format the rule catalog already knows.</summary>
    Pattern
}

/// <summary>
/// A rule the data supports, offered to the user and enforced only if they take it.
/// </summary>
/// <remarks>
/// Separate from <see cref="PropertyRule"/>, which is a rule the app WILL enforce. A rule the
/// source DECLARED — a NOT NULL column, an nvarchar(50) — lands as a PropertyRule silently,
/// because it is a fact the source stated. A rule inferred from the CONTENTS of rows is a guess
/// about the future however many rows support it, so it arrives here instead, unaccepted, and
/// reaches a generated app only once someone has picked it.
/// </remarks>
public sealed class RuleProposal
{
    public required RuleProposalKind Kind { get; init; }

    /// <summary>The rule this becomes when accepted, ready to be added to a property's Rules.
    /// </summary>
    public required PropertyRule Rule { get; init; }

    /// <summary>What makes this arguable rather than mysterious: "holds in all 12,431 rows".
    /// </summary>
    public required string Evidence { get; init; }

    /// <summary>False until a user picks it. Settable for the same reason
    /// <see cref="PropertyRule"/>'s members are: the review surfaces set it in place.</summary>
    public bool Accepted { get; set; }
}
