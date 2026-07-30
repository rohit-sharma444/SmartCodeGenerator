namespace DataIngestion.Normalization;

/// <summary>Which normal form a finding is about.</summary>
public enum NormalizationFindingKind
{
    /// <summary>1NF: several columns holding the same kind of thing, numbered.</summary>
    RepeatingGroup,

    /// <summary>1NF: several values packed into one cell.</summary>
    NonAtomicValues,

    /// <summary>2NF/3NF: columns determined by a non-key column.</summary>
    FunctionalDependency,

    /// <summary>3NF: a column computed from others.</summary>
    DerivedColumn
}

/// <summary>
/// One normalization problem found in one table, with the split it proposes and the rows that
/// justify it.
/// </summary>
/// <remarks>
/// <see cref="Evidence"/> is not decoration. A finding the user cannot inspect is a finding they
/// cannot trust, so the `d &lt;n&gt;` verb prints these lines verbatim — the same reason the
/// discovery pipeline carries <c>DecisionRecord.Evidence</c> for its own verdicts.
/// </remarks>
public sealed class NormalizationFinding
{
    public required NormalizationFindingKind Kind { get; init; }

    public required string TableName { get; init; }

    /// <summary>What to call the source in a message ("orders.csv"). Carried here so the audit trail
    /// can name it without <see cref="DataIngestion.DataModelBuilder"/> having to re-find the table
    /// a finding came from.</summary>
    public required string SourceLabel { get; init; }

    /// <summary>The columns this finding is about.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>The entity a split would create. Null for <see cref="NormalizationFindingKind.DerivedColumn"/>,
    /// which removes a column rather than extracting one.</summary>
    public string? ProposedEntity { get; init; }

    /// <summary>One line naming the problem: "Repeating group: Item1, Item2, Item3".</summary>
    public required string Headline { get; init; }

    /// <summary>One line naming the remedy: "split into OrderItem (many per Order)".</summary>
    public required string Proposal { get; init; }

    /// <summary>Rows and values behind the claim, printed by `d &lt;n&gt;`.</summary>
    public required IReadOnlyList<string> Evidence { get; init; }

    /// <summary>The character packing values into one cell. Only set for
    /// <see cref="NormalizationFindingKind.NonAtomicValues"/>, whose split has to re-split on it.</summary>
    public char? Separator { get; init; }
}
