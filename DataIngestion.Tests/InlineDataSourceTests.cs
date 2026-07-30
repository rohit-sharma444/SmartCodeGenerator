using Xunit;

namespace DataIngestion.Tests;

public class InlineDataSourceTests
{
    [Fact]
    public void SplitsTheDescriptionFromTheSource()
    {
        var request = InlineDataSource.Parse(@"order tracking from csv D:\data\orders.csv");

        Assert.Equal("order tracking", request.Description);
        Assert.NotNull(request.Source);
        Assert.Equal(DataSourceKind.Csv, request.Source!.Kind);
        Assert.Equal(@"D:\data\orders.csv", request.Source.Location);
        Assert.False(request.Normalize);
    }

    [Fact]
    public void ADescriptionIsOptional()
    {
        var request = InlineDataSource.Parse(@"from csv D:\orders.csv");

        Assert.Equal(string.Empty, request.Description);
        Assert.Equal(DataSourceKind.Csv, request.Source!.Kind);
    }

    /// <summary>
    /// The LAST `from` followed by a format keyword wins, so a description that legitimately
    /// contains the word "from" is not truncated. This is the case that makes naive splitting
    /// on the first "from" wrong.
    /// </summary>
    [Fact]
    public void ADescriptionMayContainTheWordFrom()
    {
        var request = InlineDataSource.Parse(@"track orders from suppliers from csv D:\orders.csv");

        Assert.Equal("track orders from suppliers", request.Description);
        Assert.Equal(@"D:\orders.csv", request.Source!.Location);
    }

    /// <summary>`from` followed by something that is not a format is just prose.</summary>
    [Fact]
    public void FromSomethingThatIsNotAFormatIsNotASource()
    {
        var request = InlineDataSource.Parse("a system built from scratch");

        Assert.Null(request.Source);
        Assert.Equal("a system built from scratch", request.Description);
    }

    [Fact]
    public void NoFromAtAllIsJustADescription()
    {
        var request = InlineDataSource.Parse("a library that lends books to members");

        Assert.Null(request.Source);
        Assert.Equal("a library that lends books to members", request.Description);
    }

    [Fact]
    public void TrailingNormalizeIsAFlag_NotPartOfThePath()
    {
        var request = InlineDataSource.Parse(@"orders from csv D:\orders.csv normalize");

        Assert.True(request.Normalize);
        Assert.Equal(@"D:\orders.csv", request.Source!.Location);
    }

    /// <summary>Windows paths contain spaces, so the location is the rest of the line, not one token.</summary>
    [Fact]
    public void APathWithSpacesIsKeptWhole()
    {
        var request = InlineDataSource.Parse(@"orders from csv C:\My Data\orders 2026.csv");

        Assert.Equal(@"C:\My Data\orders 2026.csv", request.Source!.Location);
    }

    [Fact]
    public void AQuotedPathLosesItsQuotes()
    {
        var request = InlineDataSource.Parse(@"orders from csv ""C:\My Data\orders.csv"" normalize");

        Assert.Equal(@"C:\My Data\orders.csv", request.Source!.Location);
        Assert.True(request.Normalize);
    }

    [Theory]
    [InlineData("csv", DataSourceKind.Csv)]
    [InlineData("txt", DataSourceKind.Txt)]
    [InlineData("json", DataSourceKind.Json)]
    [InlineData("excel", DataSourceKind.Excel)]
    [InlineData("sql", DataSourceKind.Sql)]
    public void EveryFormatKeywordIsRecognized(string keyword, DataSourceKind expected)
    {
        var request = InlineDataSource.Parse($@"orders from {keyword} D:\x");

        Assert.Equal(expected, request.Source!.Kind);
    }

    /// <summary>A connection string contains '=' and ';' and no spaces around them; it is the
    /// location, kept verbatim.</summary>
    [Fact]
    public void ASqlConnectionStringIsTheLocation()
    {
        var request = InlineDataSource.Parse(
            "shop from sql Server=.;Database=Shop;Trusted_Connection=True");

        Assert.Equal(DataSourceKind.Sql, request.Source!.Kind);
        Assert.Equal("Server=.;Database=Shop;Trusted_Connection=True", request.Source.Location);
    }

    /// <summary>`from csv` with nothing after it names no file, so it is not a source. Treating it
    /// as one would produce a spec with an empty path and a confusing not-found error.</summary>
    [Fact]
    public void AFormatWithNoLocationIsNotASource()
    {
        var request = InlineDataSource.Parse("orders from csv");

        Assert.Null(request.Source);
        Assert.Equal("orders from csv", request.Description);
    }

    [Fact]
    public void EmptyInputIsAnEmptyDescription()
    {
        var request = InlineDataSource.Parse("   ");

        Assert.Null(request.Source);
        Assert.Equal(string.Empty, request.Description);
    }
}
