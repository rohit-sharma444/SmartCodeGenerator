using DataIngestion.Schema.Readers.Sql;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers.Sql;

public class SqlServerCatalogTests
{
    private const string Connection = "Server=.;Database=Northwind;Trusted_Connection=True";

    [Fact]
    public void Lists_what_the_database_holds()
    {
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Customers", 11, 91));
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, 830));

        var listed = new SqlServerCatalog(source).List(Connection);

        Assert.Equal(2, listed.Count);
        Assert.Equal("Customers", listed[0].Name);
        Assert.Equal(91, listed[0].Rows);
    }

    /// <summary>
    /// The whole point of the phase: it runs before the user has chosen anything, so it must not
    /// touch a single row.
    /// </summary>
    [Fact]
    public void Reads_no_data_at_all()
    {
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, 830));

        new SqlServerCatalog(source).List(Connection);

        Assert.Empty(source.RowsFetched);
    }

    [Fact]
    public void A_database_with_no_tables_is_refused_with_a_reason()
    {
        var source = new FakeSqlSchemaSource();

        var error = Assert.Throws<InvalidDataException>(
            () => new SqlServerCatalog(source).List(Connection));

        Assert.Contains("no tables", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A low-privilege login can read tables without holding VIEW DATABASE STATE, which is what
    /// sys.dm_db_partition_stats needs. Losing the count must not lose the picker.
    /// </summary>
    [Fact]
    public void A_table_whose_row_count_could_not_be_read_is_still_listed()
    {
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, null));

        var listed = new SqlServerCatalog(source).List(Connection);

        Assert.Single(listed);
        Assert.Null(listed[0].Rows);
    }
}
