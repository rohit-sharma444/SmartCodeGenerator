using Xunit;

namespace DataIngestion.Tests;

public class InlineDataSourceSqlTests
{
    [Fact]
    public void Tables_are_taken_off_the_line_and_not_left_in_the_connection_string()
    {
        var request = InlineDataSource.Parse(
            "order tracking from sql \"Server=.;Database=Northwind\" tables Customers,Orders");

        Assert.NotNull(request.Source);
        Assert.Equal("Server=.;Database=Northwind", request.Source!.Location);
        Assert.Equal(["Customers", "Orders"], request.Source.SqlTables);
        Assert.Equal("order tracking", request.Description);
    }

    [Fact]
    public void Naming_no_tables_leaves_the_list_empty()
    {
        var request = InlineDataSource.Parse(
            "order tracking from sql \"Server=.;Database=Northwind\"");

        Assert.Null(request.Source!.SqlTables);
        Assert.Equal("Server=.;Database=Northwind", request.Source.Location);
    }

    [Fact]
    public void A_quoted_table_name_may_contain_a_space()
    {
        var request = InlineDataSource.Parse(
            "from sql \"Server=.;Database=Northwind\" tables \"Order Details,Orders\"");

        Assert.Equal(["Order Details", "Orders"], request.Source!.SqlTables);
    }

    [Fact]
    public void Normalize_still_works_alongside_tables()
    {
        var request = InlineDataSource.Parse(
            "from sql \"Server=.;Database=X\" tables Orders normalize");

        Assert.True(request.Normalize);
        Assert.Equal(["Orders"], request.Source!.SqlTables);
        Assert.Equal("Server=.;Database=X", request.Source.Location);
    }

    /// <summary>The database names the generated app when no description was given —
    /// BuildAppCommand.AppNameFor already reads this member.</summary>
    [Fact]
    public void The_database_name_is_taken_from_the_connection_string()
    {
        var request = InlineDataSource.Parse(
            "from sql \"Server=.;Initial Catalog=Northwind;Trusted_Connection=True\"");

        Assert.Equal("Northwind", request.Source!.SqlDatabase);
    }

    /// <summary>A path is not a connection string, and must not acquire SQL members.</summary>
    [Fact]
    public void A_csv_is_untouched_by_any_of_this()
    {
        var request = InlineDataSource.Parse(@"orders from csv D:\data\orders.csv");

        Assert.Null(request.Source!.SqlTables);
        Assert.Null(request.Source.SqlDatabase);
        Assert.Equal(@"D:\data\orders.csv", request.Source.Location);
    }

    // "tables" is an ordinary English word and a path may legitimately contain it.
    [Fact]
    public void A_path_containing_the_word_tables_is_not_split_on_it()
    {
        var request = InlineDataSource.Parse(@"from csv D:\my tables\orders.csv");

        Assert.Equal(@"D:\my tables\orders.csv", request.Source!.Location);
    }
}
