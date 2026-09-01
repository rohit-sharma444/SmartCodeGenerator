using Xunit;

namespace DataIngestion.Tests;

public class SqlConnectionInfoTests
{
    /// <summary>BuildAppCommand.AppNameFor names the generated app from this when no description
    /// was given.</summary>
    [Fact]
    public void The_database_is_read_from_initial_catalog()
        => Assert.Equal(
            "Northwind",
            SqlConnectionInfo.DatabaseOf(
                "Server=.;Initial Catalog=Northwind;Trusted_Connection=True"));

    [Fact]
    public void Database_is_accepted_as_the_same_thing()
        => Assert.Equal("Shop", SqlConnectionInfo.DatabaseOf("Server=.;Database=Shop"));

    [Fact]
    public void A_connection_string_naming_no_database_gives_null()
        => Assert.Null(SqlConnectionInfo.DatabaseOf("Server=.;Trusted_Connection=True"));

    /// <summary>A file path is not a connection string, and must not be mined for one.</summary>
    [Fact]
    public void Something_that_is_not_a_connection_string_gives_null()
        => Assert.Null(SqlConnectionInfo.DatabaseOf(@"D:\data\orders.csv"));

    [Fact]
    public void An_empty_string_gives_null()
        => Assert.Null(SqlConnectionInfo.DatabaseOf(string.Empty));
}
