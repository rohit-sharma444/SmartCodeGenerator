namespace DataIngestion;

/// <summary>
/// Where the data is. <paramref name="Location"/> is a file path for the file formats and a
/// connection string for SQL; the two SQL members stay null for every other kind.
/// </summary>
/// <remarks>
/// Equality is by VALUE for <paramref name="SqlTables"/> too. A record compares a list by
/// reference, and the data stage builds a new list every time it is left, so the same tables
/// counted as a changed source: just opening a SQL build's data node and clicking away asked to
/// discard the model, and "Change anyway" lost it.
/// </remarks>
public sealed record DataSourceSpec(
    DataSourceKind Kind,
    string Location,
    string? SqlDatabase = null,
    IReadOnlyList<string>? SqlTables = null)
{
    public bool Equals(DataSourceSpec? other)
        => other is not null
           && Kind == other.Kind
           && Location == other.Location
           && SqlDatabase == other.SqlDatabase
           && (SqlTables is null
               ? other.SqlTables is null
               : other.SqlTables is not null && SqlTables.SequenceEqual(other.SqlTables));

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Kind);
        hash.Add(Location);
        hash.Add(SqlDatabase);

        foreach (var table in SqlTables ?? [])
            hash.Add(table);

        return hash.ToHashCode();
    }
}
