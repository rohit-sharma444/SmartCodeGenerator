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

    public IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table)
        => ColumnsByTable.TryGetValue(table, out var columns) ? columns : [];

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => Keys;

    public IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table)
    {
        RowsFetched.Add(table);
        return RowsByTable.TryGetValue(table, out var rows) ? rows : [];
    }
}
