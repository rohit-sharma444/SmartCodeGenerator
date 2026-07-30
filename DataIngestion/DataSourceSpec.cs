namespace DataIngestion;

/// <summary>
/// Where the data is. <paramref name="Location"/> is a file path for the file formats and a
/// connection string for SQL; the two SQL members stay null for every other kind.
/// </summary>
public sealed record DataSourceSpec(
    DataSourceKind Kind,
    string Location,
    string? SqlDatabase = null,
    IReadOnlyList<string>? SqlTables = null);
