using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Models;

/// <summary>What an entity-scoped proposal would become.</summary>
public enum EntityRuleProposalKind
{
    /// <summary>One column never exceeds another, across every row.</summary>
    CrossField,

    /// <summary>One column always equals an arithmetic identity over two others.</summary>
    Calculation
}

/// <summary>
/// A rule spanning more than one property of the same entity, the data supports, offered to
/// the user and enforced only if they take it.
/// </summary>
/// <remarks>
/// The entity-scoped counterpart to <see cref="RuleProposal"/>, which names exactly one
/// property. Separate rather than a widened <see cref="RuleProposal"/> for the same reason
/// phase 3 added <c>EntityStageRules</c> beside <c>EmittableRules</c> instead of editing it.
/// <para>
/// <see cref="Rule"/> is already the real <see cref="IValidationRule"/> the scan built —
/// unlike <see cref="RuleProposal.Rule"/>'s <see cref="PropertyRule"/>, there is no further
/// conversion step once this is accepted: an accepted proposal and a hand-typed rule are the
/// identical object.
/// </para>
/// </remarks>
public sealed class EntityRuleProposal
{
    public required EntityRuleProposalKind Kind { get; init; }

    /// <summary>Every property this proposal names, for the review card's evidence line.</summary>
    public required IReadOnlyList<string> Properties { get; init; }

    /// <summary>The rule this becomes when accepted — already built, ready to add to a class's
    /// business rules.</summary>
    public required IValidationRule Rule { get; init; }

    /// <summary>What makes this arguable rather than mysterious: "holds in all 40 rows".</summary>
    public required string Evidence { get; init; }

    /// <summary>False until a user picks it.</summary>
    public bool Accepted { get; set; }
}
