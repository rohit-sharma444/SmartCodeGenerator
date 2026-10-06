using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class SqlTypeMapTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("smallint")]
    [InlineData("tinyint")]
    [InlineData("bigint")]
    public void Whole_numbers_become_int(string sqlType)
        => Assert.Equal("int", SqlTypeMap.ToModelType(sqlType));

    [Theory]
    [InlineData("decimal")]
    [InlineData("numeric")]
    [InlineData("money")]
    [InlineData("smallmoney")]
    [InlineData("float")]
    [InlineData("real")]
    public void Fractional_numbers_become_decimal(string sqlType)
        => Assert.Equal("decimal", SqlTypeMap.ToModelType(sqlType));

    [Fact]
    public void Bit_becomes_bool()
        => Assert.Equal("bool", SqlTypeMap.ToModelType("bit"));

    [Theory]
    [InlineData("date")]
    [InlineData("datetime")]
    [InlineData("datetime2")]
    [InlineData("smalldatetime")]
    [InlineData("datetimeoffset")]
    public void The_date_family_becomes_DateTime(string sqlType)
        => Assert.Equal("DateTime", SqlTypeMap.ToModelType(sqlType));

    /// <summary>
    /// A time of day is not a date: mapped to DateTime, every value ("09:30:00") failed to read as
    /// one, was dropped from the seed and showed as 01-Jan-01. As text it keeps every value, and
    /// the change of type is said, as a uniqueidentifier's is.
    /// </summary>
    [Fact]
    public void A_time_of_day_becomes_text_and_says_so()
    {
        Assert.Equal("string", SqlTypeMap.ToModelType("time"));

        var note = SqlTypeMap.LossNote("time", "ShipTime");

        Assert.NotNull(note);
        Assert.Contains("ShipTime", note);
        Assert.Contains("time", note);
    }

    [Theory]
    [InlineData("nvarchar")]
    [InlineData("varchar")]
    [InlineData("char")]
    [InlineData("text")]
    [InlineData("uniqueidentifier")]
    [InlineData("varbinary")]
    [InlineData("sql_variant")]
    [InlineData("some_type_that_does_not_exist")]
    public void Everything_else_becomes_string(string sqlType)
        => Assert.Equal("string", SqlTypeMap.ToModelType(sqlType));

    // INFORMATION_SCHEMA reports lower case, but nothing guarantees a caller passes it through
    // untouched.
    [Fact]
    public void The_sql_type_is_matched_however_it_is_cased()
        => Assert.Equal("int", SqlTypeMap.ToModelType("BigInt"));

    [Fact]
    public void Narrowing_a_bigint_is_reported()
    {
        var note = SqlTypeMap.LossNote("bigint", "OrderId");

        Assert.NotNull(note);
        Assert.Contains("OrderId", note);
        Assert.Contains("bigint", note);
    }

    [Fact]
    public void Flattening_a_uniqueidentifier_is_reported()
    {
        var note = SqlTypeMap.LossNote("uniqueidentifier", "RowGuid");

        Assert.NotNull(note);
        Assert.Contains("RowGuid", note);
    }

    // The overwhelmingly common case. A note for every column would be noise, and noise is how a
    // warning stops being read.
    [Fact]
    public void A_type_that_maps_faithfully_says_nothing()
        => Assert.Null(SqlTypeMap.LossNote("nvarchar", "Name"));
}
