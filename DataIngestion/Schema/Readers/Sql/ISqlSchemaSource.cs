namespace DataIngestion.Schema.Readers.Sql;

/// <summary>One table as the picker needs it.</summary>
/// <param name="Rows">
/// Null when the row count could not be read — see <see cref="ISqlSchemaSource"/>. Null rather
/// than zero: "I do not know" and "there is nothing in it" are different answers, and a picker
/// showing 0 for a full table would be a lie.
/// </param>
public sealed record SqlTableInfo(string Schema, string Name, int Columns, long? Rows);

/// <summary>
/// One column as INFORMATION_SCHEMA describes it. The trailing ones are optional so every
/// existing construction and every test fake keeps compiling; a source that cannot supply them
/// leaves them null, which reads as "not known". <paramref name="IsPrimaryKey"/> is true for the
/// one column of a single-column primary key, and false for every column of a composite one.
/// </summary>
/// <param name="ComputedAs">The expression a computed column is computed by, as the database
/// stores it ("([UnitPrice]*(1.2))"); null for a column that holds stored data.</param>
/// <param name="Default">The column's DEFAULT definition as the database stores it ("((1))",
/// "(getdate())"); null when it has none.</param>
public sealed record SqlColumnInfo(
    string Name,
    string SqlType,
    bool Nullable,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    bool IsPrimaryKey = false,
    string? ComputedAs = null,
    string? Default = null);

/// <summary>
/// The whole of this feature's contact with ADO.NET.
/// </summary>
/// <remarks>
/// One thin adapter implements this over <c>Microsoft.Data.SqlClient</c>; everything above it is
/// tested against a fake. CI has no SQL Server, and a mock asserting that a query string equals a
/// query string would be a test that passes while proving nothing — so the seam is drawn here,
/// kept narrow enough that the adapter can be read in one sitting, and the adapter is covered by
/// an opt-in integration test instead.
/// </remarks>
public interface ISqlSchemaSource
{
    /// <summary>Base tables only, with column counts and near-exact row counts. Reads no data.</summary>
    IReadOnlyList<SqlTableInfo> Tables(string connectionString);

    IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table);

    /// <summary>Every declared single-column foreign key, in the shape the model builder wants.</summary>
    IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString);

    /// <summary>At most <paramref name="limit"/> rows of one table, each value already rendered
    /// invariantly to a string.</summary>
    IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table, int limit);
}
