using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// What the user is told their data came from.
/// </summary>
/// <remarks>
/// Both call sites did <c>Path.GetFileName(source.Location)</c>, which for a connection string
/// returns the whole thing — so the discovery and normalization lines printed
/// <c>Read 2 table(s) from Server=.;Database=TRADING;User ID=sa;Password=…</c>. Found by running
/// the command against a real database, not by any test.
/// </remarks>
public class DataSourceLabelTests
{
    [Fact]
    public void A_file_is_labelled_with_its_name_and_not_its_whole_path()
        => Assert.Equal(
            "orders.csv",
            DataSourceLabel.For(new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv")));

    [Fact]
    public void A_database_is_labelled_with_its_name_and_never_the_connection_string()
    {
        var label = DataSourceLabel.For(new DataSourceSpec(
            DataSourceKind.Sql,
            "Server=.;Database=TRADING;User ID=sa;Password=hunter2",
            SqlDatabase: "TRADING"));

        Assert.Equal("TRADING", label);
        Assert.DoesNotContain("hunter2", label);
    }

    /// <summary>The member may be unset on a spec built by hand; the connection string still
    /// names the database, and it still must not be shown whole.</summary>
    [Fact]
    public void The_database_is_recovered_from_the_connection_string_when_unset()
    {
        var label = DataSourceLabel.For(new DataSourceSpec(
            DataSourceKind.Sql, "Server=.;Database=Shop;Password=hunter2"));

        Assert.Equal("Shop", label);
        Assert.DoesNotContain("hunter2", label);
    }

    /// <summary>
    /// A connection string naming no database lands on master. There is nothing to name it with,
    /// and echoing the connection string as a fallback is the one thing that must not happen.
    /// </summary>
    [Fact]
    public void A_database_that_cannot_be_named_is_still_not_the_connection_string()
    {
        var label = DataSourceLabel.For(new DataSourceSpec(
            DataSourceKind.Sql, "Server=.;User ID=sa;Password=hunter2"));

        Assert.DoesNotContain("hunter2", label);
        Assert.DoesNotContain("Server=", label);
        Assert.NotEmpty(label);
    }
}
