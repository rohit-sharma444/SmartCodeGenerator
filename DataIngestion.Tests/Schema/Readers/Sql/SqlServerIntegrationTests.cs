using DataIngestion.Schema.Readers.Sql;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers.Sql;

/// <summary>
/// The adapter against a real server.
/// </summary>
/// <remarks>
/// Everything above <see cref="ISqlSchemaSource"/> is covered by unit tests over a fake; this is
/// the only thing that proves the SQL itself runs. It does nothing unless
/// SMARTCODEGEN_SQL_TEST_CONNECTION names a database — CI has no SQL Server, and a test that
/// mocked the driver would assert that a query string equals a query string.
/// <para>
/// A plain <c>[Fact]</c> that returns early rather than a skippable one: expressing the skip
/// properly needs Xunit.SkippableFact, and Microsoft.Data.SqlClient is the only package this
/// branch is permitted to add.
/// </para>
/// </remarks>
public class SqlServerIntegrationTests
{
    private const string Variable = "SMARTCODEGEN_SQL_TEST_CONNECTION";

    private static string? Connection => Environment.GetEnvironmentVariable(Variable);

    [Fact]
    public void Lists_tables_columns_and_keys_from_a_real_database()
    {
        if (string.IsNullOrWhiteSpace(Connection))
            return;   // no server configured — see the class remarks

        var source = new SqlClientSchemaSource();

        var tables = source.Tables(Connection);
        Assert.NotEmpty(tables);

        var first = tables[0];
        Assert.NotEmpty(source.Columns(Connection, first.Schema, first.Name));

        // Not asserted non-empty: a database may legitimately declare no foreign keys. That the
        // query runs at all is the thing being proved.
        _ = source.ForeignKeys(Connection);

        // Reading the first table proves the SELECT and the value rendering, whatever it holds.
        _ = source.Rows(Connection, first.Schema, first.Name);
    }
}
