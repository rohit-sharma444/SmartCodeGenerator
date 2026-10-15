using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;

namespace DataIngestion.Tests.Schema.Readers.Sql;

/// <summary>
/// A database, as far as anything above the seam can tell.
/// </summary>
/// <remarks>
/// Everything of substance in this feature is tested against this. Only the SqlClient adapter is
/// not, and that limit is stated in the design rather than papered over with a mock that asserts
/// a query string equals a query string.
/// </remarks>
internal sealed class FakeSqlSchemaSource : ISqlSchemaSource
{
    public List<SqlTableInfo> TableList { get; } = [];
    public Dictionary<string, List<SqlColumnInfo>> ColumnsByTable { get; } = [];
    public Dictionary<string, List<string?[]>> RowsByTable { get; } = [];
    public List<TabularForeignKey> Keys { get; } = [];

    /// <summary>Every table whose rows were actually fetched, in order, so a test can assert that
    /// nothing was read before it was asked for.</summary>
    public List<string> RowsFetched { get; } = [];

    public IReadOnlyList<SqlTableInfo> Tables(string connectionString) => TableList;

    /// <summary>Columns and rows are filed under "schema.table" when two schemas share a table
    /// name, else under the table name alone.</summary>
    public IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table)
        => ColumnsByTable.TryGetValue($"{schema}.{table}", out var qualified) ? qualified
            : ColumnsByTable.TryGetValue(table, out var columns) ? columns : [];

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => Keys;

    /// <summary>The limit the last Rows call asked for.</summary>
    public int? LastLimit { get; private set; }

    public IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table, int limit)
    {
        RowsFetched.Add(table);
        LastLimit = limit;
        return RowsByTable.TryGetValue($"{schema}.{table}", out var qualified) ? [.. qualified.Take(limit)]
            : RowsByTable.TryGetValue(table, out var rows) ? [.. rows.Take(limit)] : [];
    }
}
