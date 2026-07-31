using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class ExcelSerialDateTests
{
    [Theory]
    [InlineData(1, "1900-01-01")]
    [InlineData(59, "1900-02-28")]
    // Excel's 1900 calendar contains a 29 February 1900 that never existed, so every serial from
    // 60 up is one day ahead of reality. Ignore it and every date after February 1900 is wrong.
    [InlineData(61, "1900-03-01")]
    [InlineData(45658, "2025-01-01")]
    [InlineData(46096, "2026-03-15")]
    public void Converts1900Serials(double serial, string expected)
        => Assert.Equal(expected, ExcelSerialDate.ToText(serial, date1904: false));

    [Theory]
    [InlineData(0, "1904-01-01")]
    [InlineData(44196, "2025-01-01")]
    public void Converts1904Serials(double serial, string expected)
        => Assert.Equal(expected, ExcelSerialDate.ToText(serial, date1904: true));

    /// <summary>A time component is kept, in a form ColumnTypeInference also reads as a date.</summary>
    [Fact]
    public void KeepsATimeOfDay()
        => Assert.Equal("2026-03-15 13:30:00",
            ExcelSerialDate.ToText(46096.5625, date1904: false));

    /// <summary>Serial fractions are binary approximations, so 13:30 can arrive as 13:29:59.999.
    /// Rounding to the second is what stops a clean time reading as one second early.</summary>
    [Fact]
    public void RoundsToTheNearestSecond()
        => Assert.Equal("2026-03-15 13:30:00",
            ExcelSerialDate.ToText(46096.56249999, date1904: false));

    /// <summary>
    /// A serial below 1 has no date part at all — it is a time of day. Emitted as a time it stays
    /// a string, which the review loop can retype; emitted as a date it would claim a day the file
    /// never mentioned.
    /// </summary>
    [Fact]
    public void ASerialUnderOne_IsATimeOfDay()
        => Assert.Equal("09:30:00", ExcelSerialDate.ToText(0.39583333, date1904: false));

    /// <summary>Out of range is not a date, and the caller keeps the stored number instead.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3_000_000)]
    public void ASerialNoDateCanHold_IsNull(double serial)
        => Assert.Null(ExcelSerialDate.ToText(serial, date1904: false));

    /// <summary>Every emitted form must be one ColumnTypeInference types as DateTime, or the
    /// conversion has moved the problem rather than solved it.</summary>
    [Theory]
    [InlineData(46096)]
    [InlineData(46096.5625)]
    public void TheEmittedFormIsOneColumnTypeInferenceReadsAsADate(double serial)
    {
        var text = ExcelSerialDate.ToText(serial, date1904: false);

        Assert.Equal("DateTime",
            DataIngestion.Schema.ColumnTypeInference.Infer([text], "Placed"));
    }
}
