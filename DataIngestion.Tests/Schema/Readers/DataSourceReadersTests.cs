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
    public void SupportedKinds_HaveAReader(DataSourceKind kind)
    {
        Assert.True(DataSourceReaders.TryCreate(kind, out var reader));
        Assert.NotNull(reader);
    }

    [Theory]
    [InlineData(DataSourceKind.Excel)]
    [InlineData(DataSourceKind.Sql)]
    public void FormatsNotInThisBuild_HaveNoReader(DataSourceKind kind)
        => Assert.False(DataSourceReaders.TryCreate(kind, out _));

    /// <summary>A refusal without an alternative leaves the user at a dead end, so the message
    /// must name the formats that do work.</summary>
    [Theory]
    [InlineData(DataSourceKind.Excel)]
    [InlineData(DataSourceKind.Sql)]
    public void NotSupportedMessage_NamesTheWorkingFormats(DataSourceKind kind)
    {
        var message = DataSourceReaders.NotSupportedMessage(kind);

        Assert.Contains("csv", message);
        Assert.Contains("txt", message);
        Assert.Contains("json", message);
    }

    /// <summary>Every kind is offered at the prompt, including the two with no reader.</summary>
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
