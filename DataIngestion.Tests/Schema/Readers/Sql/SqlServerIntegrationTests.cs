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

        // The SMALLEST table for the schema calls, not the first: pointed at a real database the
        // first one alphabetically can be enormous - the one this was first run against had 47
        // million rows in it. An unreadable count sorts last rather than first, since it might be
        // anything.
        var smallest = tables.OrderBy(t => t.Rows ?? long.MaxValue).First();

        Assert.NotEmpty(source.Columns(Connection, smallest.Schema, smallest.Name));

        // Not asserted non-empty: a database may legitimately declare no foreign keys. That the
        // query runs at all is the thing being proved.
        _ = source.ForeignKeys(Connection);

        // Proves the SELECT and the value rendering, whatever the table holds.
        _ = source.Rows(Connection, smallest.Schema, smallest.Name, 10);

        // And the limit, on the LARGEST table: before TOP, this read every row of it. Issue #131.
        var largest = tables.OrderByDescending(t => t.Rows ?? 0).First();

        if (largest.Rows is > 10)
            Assert.Equal(10, source.Rows(Connection, largest.Schema, largest.Name, 10).Count);
    }
}
