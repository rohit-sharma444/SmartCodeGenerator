using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// A database, as far as the data panel can tell.
/// </summary>
/// <remarks>
/// A copy of the one in DataIngestion.Tests. Duplicated rather than shared because there is no
/// test-utility project here, and a five-line double in two places is cheaper than inventing one.
/// </remarks>
internal sealed class FakeSqlSchemaSource : ISqlSchemaSource
{
    public List<SqlTableInfo> TableList { get; } = [];
    public List<string> RowsFetched { get; } = [];

    public IReadOnlyList<SqlTableInfo> Tables(string connectionString) => TableList;

    public IReadOnlyList<SqlColumnInfo> Columns(string c, string schema, string table) => [];

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => [];

    public IReadOnlyList<string?[]> Rows(string c, string schema, string table)
    {
        RowsFetched.Add(table);
        return [];
    }
}

/// <summary>A server that refuses, so the failure path can be asserted.</summary>
internal sealed class ThrowingSqlSchemaSource(string message) : ISqlSchemaSource
{
    public IReadOnlyList<SqlTableInfo> Tables(string connectionString)
        => throw new InvalidOperationException(message);

    public IReadOnlyList<SqlColumnInfo> Columns(string c, string schema, string table) => [];

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => [];

    public IReadOnlyList<string?[]> Rows(string c, string schema, string table) => [];
}
