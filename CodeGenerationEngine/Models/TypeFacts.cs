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

    /// <summary>
    /// Whether the source's own rows hold a MISSING value in this column -- an empty CSV cell, a
    /// JSON null, a SQL NULL -- or whether the source DECLARES the column may hold one (a SQL
    /// column that is not NOT NULL), whatever the rows read happened to hold. A number, flag or
    /// date property with this set is emitted NULLABLE
    /// (<c>int?</c>, <c>DateTime?</c>), so the missing value stays missing: a non-nullable one
    /// silently became 0, False or 01-Jan-0001, which looks like real data. Unlike a NOT NULL
    /// fact, this makes the generated app more permissive, never stricter.
    /// </summary>
    public bool HasMissingValues { get; init; }

    /// <summary>
    /// The value the source's DEFAULT gives a new row, as invariant text ("1", "Active", "-2.5");
    /// null when it declares none, or one <see cref="DefaultFunction"/> names instead.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// The function the source's DEFAULT calls, when it is one a generated app can repeat: "now",
    /// "utcnow" or "newguid". Null otherwise.
    /// </summary>
    public string? DefaultFunction { get; init; }

    /// <summary>The lowest value a CHECK constraint allows, inclusive; null when none says.</summary>
    public decimal? MinValue { get; init; }

    /// <summary>The highest value a CHECK constraint allows, inclusive; null when none says.</summary>
    public decimal? MaxValue { get; init; }

    /// <summary>The only values a CHECK constraint allows (<c>Status IN ('Active', 'Retired')</c>);
    /// null when none lists them.</summary>
    public IReadOnlyList<string>? AllowedValues { get; init; }
}
