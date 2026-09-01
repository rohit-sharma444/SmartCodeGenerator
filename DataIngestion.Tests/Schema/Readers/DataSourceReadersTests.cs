using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class DataSourceReadersTests
{
    [Theory]
    [InlineData(DataSourceKind.Csv)]
    [InlineData(DataSourceKind.Txt)]
    [InlineData(DataSourceKind.Json)]
    [InlineData(DataSourceKind.Excel)]
    [InlineData(DataSourceKind.Sql)]
    public void SupportedKinds_HaveAReader(DataSourceKind kind)
    {
        Assert.True(DataSourceReaders.TryCreate(kind, out var reader));
        Assert.NotNull(reader);
    }

    /// <summary>
    /// Every kind the prompt offers can now be read. This replaces a test asserting that
    /// SQL could not be - the one thing the SQL Server branch existed to change - and is
    /// written over the enum so a kind added without a reader fails here rather than at the
    /// moment a user picks it.
    /// </summary>
    [Fact]
    public void EveryDeclaredKindHasAReader()
    {
        foreach (var kind in Enum.GetValues<DataSourceKind>())
            Assert.True(
                DataSourceReaders.TryCreate(kind, out _),
                $"{kind} is offered at the prompt but has no reader");
    }

    /// <summary>
    /// The message is unreachable now that every kind has a reader, but it must not lie if a
    /// later kind is declared before its reader exists: it names what TryCreate can actually
    /// build rather than a list written by hand, which is how it came to advertise csv, txt,
    /// json and excel while sql sat beside them unread.
    /// </summary>
    [Fact]
    public void NotSupportedMessage_NamesTheFormatsThatActuallyWork()
    {
        var message = DataSourceReaders.NotSupportedMessage(DataSourceKind.Sql);

        Assert.Contains("csv", message);
        Assert.Contains("excel", message);
        Assert.Contains("sql", message);
    }

    /// <summary>Every kind is offered at the prompt, including the one with no reader.</summary>
    [Fact]
    public void Keys_OfferEveryKind()
        => Assert.Equal(new[] { "sql", "excel", "txt", "json", "csv" }, DataSourceReaders.Keys);

    [Theory]
    [InlineData("csv", DataSourceKind.Csv)]
    [InlineData("CSV", DataSourceKind.Csv)]
    [InlineData("  json  ", DataSourceKind.Json)]
    [InlineData("excel", DataSourceKind.Excel)]
    [InlineData("xlsx", DataSourceKind.Excel)]
    [InlineData("sql", DataSourceKind.Sql)]
    [InlineData("sqlserver", DataSourceKind.Sql)]
    [InlineData("txt", DataSourceKind.Txt)]
    [InlineData("text", DataSourceKind.Txt)]
    public void TryParseKind_AcceptsTheNamesAUserWouldType(string text, DataSourceKind expected)
    {
        Assert.True(DataSourceReaders.TryParseKind(text, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseKind_RejectsAnythingElse(string? text)
        => Assert.False(DataSourceReaders.TryParseKind(text, out _));
}
