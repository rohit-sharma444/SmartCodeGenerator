namespace CodeGenerationEngine.Models;

/// <summary>
/// What a source declared about a column, beyond the five coarse types the generator emits.
/// </summary>
/// <remarks>
/// Every fact is separately nullable because "not known" and "known to be absent" lead to
/// different rules. A discovered domain has no data and no schema and carries none of these; it
/// must still generate, so no consumer may require a fact to be present.
/// </remarks>
public sealed record TypeFacts
{
    /// <summary>False when the source declares the column NOT NULL. Null when unknown.</summary>
    public bool? IsNullable { get; init; }

    /// <summary>
    /// Declared character length. Null when unknown, and also null when deliberately unbounded —
    /// <see cref="IsUnbounded"/> separates those two.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// True for <c>nvarchar(max)</c> and friends: a KNOWN absence of a bound, as opposed to an
    /// unknown one. Collapsing the two would put a length cap back on a max column.
    /// </summary>
    public bool IsUnbounded { get; init; }

    /// <summary>Total digits for a decimal. Null when unknown.</summary>
    public int? Precision { get; init; }

    /// <summary>Digits after the point for a decimal. Null when unknown.</summary>
    public int? Scale { get; init; }

    /// <summary>
    /// The source's own type name — "tinyint", "smallint", "date". Null for a source that
    /// declares none. This is what <see cref="Readers.SqlTypeMap"/> collapses away, and it is
    /// what integer-range and date-floor rules are derived from.
    /// </summary>
    public string? DeclaredType { get; init; }

    /// <summary>
    /// Whether every value already in the source satisfies the format its column name would make
    /// the generator apply -- <c>[EmailAddress]</c> for a name containing "email", <c>[Phone]</c>
    /// for one containing "phone". False withholds the attribute; null means no data was checked
    /// (a discovered domain has none), and the name alone decides, as it always has.
    /// </summary>
    public bool? NameImpliedFormatHolds { get; init; }
}
