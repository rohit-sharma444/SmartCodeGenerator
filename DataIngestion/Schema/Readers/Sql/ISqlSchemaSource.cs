namespace DataIngestion.Schema.Readers.Sql;

/// <summary>One table as the picker needs it.</summary>
/// <param name="Rows">
/// Null when the row count could not be read — see <see cref="ISqlSchemaSource"/>. Null rather
/// than zero: "I do not know" and "there is nothing in it" are different answers, and a picker
/// showing 0 for a full table would be a lie.
/// </param>
public sealed record SqlTableInfo(string Schema, string Name, int Columns, long? Rows);

/// <summary>One column, with SQL Server's own type name for it.</summary>
public sealed record SqlColumnInfo(string Name, string SqlType, bool Nullable);

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

    /// <summary>Every row of one table, each value already rendered invariantly to a string.</summary>
    IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table);
}
