using System.IO.Compression;
using System.Security;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class ExcelNumberFormatTests
{
    /// <summary>
    /// A styles part with one cellXfs entry per numFmtId given, so style index i uses
    /// <paramref name="numFmtIds"/>[i]. Custom codes are declared as ids from 164 up, which is
    /// where Excel's own custom formats start.
    /// </summary>
    private static string Styles(int[] numFmtIds, params (int Id, string Code)[] custom)
        => "<?xml version=\"1.0\"?>"
         + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
         + $"<numFmts count=\"{custom.Length}\">"
         + string.Concat(custom.Select(c =>
             // Escaped, because a format code may itself contain a quote -- 0.00" m" is a number
             // with a unit -- and Excel writes that as &quot; rather than breaking the attribute.
             $"<numFmt numFmtId=\"{c.Id}\" formatCode=\"{SecurityElement.Escape(c.Code)}\"/>"))
         + "</numFmts>"
         + $"<cellXfs count=\"{numFmtIds.Length}\">"
         + string.Concat(numFmtIds.Select(id => $"<xf numFmtId=\"{id}\" xfId=\"0\"/>"))
         + "</cellXfs></styleSheet>";

    private static ExcelNumberFormats Load(string? styles)
    {
        var path = ExcelFixture.Path(".xlsx");
        ExcelFixture.Write(path, null, styles, new ExcelFixture.Sheet("Orders", ""));

        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ExcelNumberFormats.Load(zip);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(14)]   // mm-dd-yy
    [InlineData(15)]   // d-mmm-yy
    [InlineData(17)]   // mmm-yy
    [InlineData(22)]   // m/d/yy h:mm
    [InlineData(46)]   // [h]:mm:ss
    public void BuiltinDateFormats_AreDates(int numFmtId)
        => Assert.True(Load(Styles([numFmtId])).IsDate(0));

    [Theory]
    [InlineData(0)]    // General
    [InlineData(1)]    // 0
    [InlineData(4)]    // #,##0.00
    [InlineData(9)]    // 0%
    [InlineData(11)]   // 0.00E+00
    [InlineData(44)]   // accounting
    [InlineData(49)]   // @  (text)
    public void BuiltinNumberFormats_AreNotDates(int numFmtId)
        => Assert.False(Load(Styles([numFmtId])).IsDate(0));

    [Theory]
    [InlineData("yyyy\\-mm\\-dd")]
    [InlineData("dd/mm/yyyy")]
    [InlineData("d mmmm yyyy")]
    [InlineData("[$-409]dddd, mmmm d, yyyy")]
    [InlineData("hh:mm:ss")]
    public void CustomDateCodes_AreDates(string code)
        => Assert.True(Load(Styles([164], (164, code))).IsDate(0));

    /// <summary>
    /// A false date is a corruption: 9.99 read as a date becomes 9 January 1900. Every code that
    /// is not recognisably a date must therefore stay a number.
    /// </summary>
    [Theory]
    [InlineData("General")]
    [InlineData("#,##0.00")]
    [InlineData("0.00E+00")]
    [InlineData("0.00_);(0.00)")]
    [InlineData("# ?/?")]
    [InlineData("0.00\" m\"")]                      // a unit, quoted -- not a month
    [InlineData("0.0\\m")]                          // an escaped literal -- not a month
    [InlineData("[Red]#,##0.00;[Blue]-#,##0.00")]   // colours, not tokens
    [InlineData("[$-409]#,##0.00")]                 // a locale, not tokens
    public void CodesThatAreNotDates_StayNumbers(string code)
        => Assert.False(Load(Styles([164], (164, code))).IsDate(0));

    [Fact]
    public void AStyleIndexPastTheTable_IsNotADate()
        => Assert.False(Load(Styles([14])).IsDate(7));

    [Fact]
    public void NoStylesPart_MeansNothingIsADate()
        => Assert.False(Load(null).IsDate(0));

    /// <summary>
    /// The one case where the display format IS the value: a postcode of numeric cells formatted
    /// 00000 shows 01234, and emitted as 1234 the user's own data has changed.
    /// </summary>
    [Theory]
    [InlineData("00000", 5)]
    [InlineData("000", 3)]
    public void AnAllZerosCode_PadsToItsWidth(string code, int width)
        => Assert.Equal(width, Load(Styles([164], (164, code))).PadWidth(0));

    /// <summary>Narrow on purpose. Anything but bare zeros is an ordinary number format, and
    /// padding one would be inventing digits.</summary>
    [Theory]
    [InlineData("0")]          // one digit pads nothing
    [InlineData("#,##0")]
    [InlineData("0.00")]
    [InlineData("00.0")]
    [InlineData("General")]
    [InlineData("yyyy")]
    public void AnythingElse_PadsNothing(string code)
        => Assert.Null(Load(Styles([164], (164, code))).PadWidth(0));

    /// <summary>The point of the padding rule: the column stays a string, so the padding survives
    /// into the generated app instead of being rounded off into an int.</summary>
    [Fact]
    public void APaddedValue_TypesAsString()
        => Assert.Equal("string", ColumnTypeInference.Infer(["01234", "00777"], "Postcode"));
}
