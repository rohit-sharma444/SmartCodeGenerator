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
