using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests.Schema;

public class ColumnTypeInferenceTests
{
    [Theory]
    [InlineData("int", "1", "2", "3")]
    [InlineData("decimal", "1.50", "2", "3.75")]
    [InlineData("bool", "true", "false", "true")]
    [InlineData("bool", "yes", "no", "y")]
    [InlineData("DateTime", "2026-01-15", "2026-02-01", "2026-03-30")]
    [InlineData("string", "ORD-1", "ORD-2", "ORD-3")]
    public void Infers_TheTypeTheValuesActuallyCarry(string expected, params string[] values)
        => Assert.Equal(expected, ColumnTypeInference.Infer(values, "Whatever"));

    /// <summary>
    /// A 0/1 column stays int. A column wrongly typed bool cannot round-trip a 2, while one
    /// wrongly typed int loses nothing — and the review loop can correct either.
    /// </summary>
    [Fact]
    public void ZeroAndOne_StayInt_RatherThanBecomingBool()
        => Assert.Equal("int", ColumnTypeInference.Infer(new[] { "0", "1", "1", "0" }, "Active"));

    [Fact]
    public void EmptyValues_AreIgnored_NotTreatedAsStrings()
        => Assert.Equal("int", ColumnTypeInference.Infer(new[] { "1", "", null, "  ", "2" }, "Qty"));

    /// <summary>With no evidence at all, fall back to the existing name-based heuristic.</summary>
    [Fact]
    public void AllEmptyColumn_FallsBackToTheNameHeuristic()
    {
        Assert.Equal("decimal", ColumnTypeInference.Infer(new string?[] { "", null }, "UnitPrice"));
        Assert.Equal("string", ColumnTypeInference.Infer(new string?[] { "", null }, "Notes"));
    }

    /// <summary>One rogue value demotes the whole column: a column is only int if every value is.</summary>
    [Fact]
    public void OneNonNumericValue_DemotesTheColumnToString()
        => Assert.Equal("string", ColumnTypeInference.Infer(new[] { "1", "2", "n/a" }, "Qty"));

    /// <summary>Beyond int.MaxValue is decimal, never a silently truncated int.</summary>
    [Fact]
    public void OversizedIntegers_BecomeDecimal()
        => Assert.Equal("decimal", ColumnTypeInference.Infer(new[] { "3000000000", "5" }, "Reference"));

    /// <summary>
    /// A zero-padded value is a CODE, not a quantity: product codes, account numbers and
    /// extensions all look numeric and all lose meaning as numbers. "007" typed int is stored
    /// as 7, which is a silent change to the user's own data — the one thing this path must
    /// never do.
    /// </summary>
    [Theory]
    [InlineData("007", "0123")]
    [InlineData("01", "02")]
    [InlineData("0001", "9999")]
    public void ZeroPaddedValues_StayStrings_BecauseThePaddingIsMeaning(string first, string second)
        => Assert.Equal("string", ColumnTypeInference.Infer(new[] { first, second }, "Code"));

    /// <summary>A single zero is a number, not padding — and a leading zero before a decimal
    /// point is ordinary money.</summary>
    [Fact]
    public void ALoneZeroAndADecimalBelowOne_AreStillNumbers()
    {
        Assert.Equal("int", ColumnTypeInference.Infer(new[] { "0", "7" }, "Qty"));
        Assert.Equal("decimal", ColumnTypeInference.Infer(new[] { "0.99", "1.50" }, "Price"));
    }

    /// <summary>
    /// A comma is never a thousands separator here. Reading "1,2" as the number twelve is a
    /// silent corruption, where reading "1,234" as text is merely imprecise — and the review
    /// loop can retype a column, while it cannot recover a value that was already mangled.
    /// </summary>
    [Theory]
    [InlineData("1,2", "3,4")]
    [InlineData("1,234", "5,678")]
    public void CommaSeparatedValues_AreNeverNumbers(string first, string second)
        => Assert.Equal("string", ColumnTypeInference.Infer(new[] { first, second }, "Total"));

    /// <summary>
    /// Only UNAMBIGUOUS date formats are typed DateTime. A loose parse accepts far too much —
    /// "1,2" and "1/2" both become dates — and worse, it reads a slash date as month-first, so a
    /// UK "15/01/2026" is either rejected or, for "01/02/2026", silently turned into 2 January
    /// when the user meant 1 February. Left as a string the value is preserved exactly and the
    /// review loop can retype the column; misread, the date is gone.
    /// </summary>
    [Theory]
    [InlineData("2026-01-15", "2026-02-01")]
    [InlineData("2026/01/15", "2026/02/01")]
    [InlineData("2026-01-15 09:30:00", "2026-02-01 14:00:00")]
    [InlineData("15 Jan 2026", "1 Feb 2026")]
    public void UnambiguousDateFormats_AreDateTime(string first, string second)
        => Assert.Equal("DateTime", ColumnTypeInference.Infer(new[] { first, second }, "Placed"));

    [Theory]
    [InlineData("15/01/2026", "01/02/2026")]   // day-first or month-first? unknowable
    [InlineData("01/02/2026", "03/04/2026")]
    [InlineData("1/2", "3/4")]                 // fractions, not dates
    public void AmbiguousOrLooseDateLikeValues_StayStrings(string first, string second)
        => Assert.Equal("string", ColumnTypeInference.Infer(new[] { first, second }, "Placed"));

    /// <summary>Only the first TypeSampleSize non-empty values are examined; the cap is a constant
    /// so this test cannot drift from the implementation.</summary>
    [Fact]
    public void OnlyTheFirstSampleOfValuesIsExamined()
    {
        var values = Enumerable.Repeat("1", ColumnTypeInference.TypeSampleSize)
            .Append("not a number")
            .ToArray();

        Assert.Equal("int", ColumnTypeInference.Infer(values, "Qty"));
    }
}
