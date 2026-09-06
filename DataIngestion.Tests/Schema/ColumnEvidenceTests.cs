using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests.Schema;

public class ColumnEvidenceTests
{
    [Fact]
    public void LongestValueSeen_BecomesTheLengthBound()
    {
        var facts = ColumnTypeInference.Evidence(["ab", "abcdef", "abc"], "string");

        Assert.Equal(6, facts.MaxLength);
    }

    [Fact]
    public void EveryValuePresent_MeansNotNullable()
    {
        var facts = ColumnTypeInference.Evidence(["a", "b"], "string");

        Assert.False(facts.IsNullable!.Value);
    }

    [Fact]
    public void OneBlank_MeansNullable()
    {
        // A single blank is proof the column tolerates one. Requiring the field would
        // reject a row the source already contains.
        var facts = ColumnTypeInference.Evidence(["a", "", "b"], "string");

        Assert.True(facts.IsNullable!.Value);
    }

    [Fact]
    public void GreatestDecimalPlacesSeen_BecomesTheScale()
    {
        var facts = ColumnTypeInference.Evidence(["1.5", "2.25", "3"], "decimal");

        Assert.Equal(2, facts.Scale);
    }

    [Fact]
    public void EvidenceScansEveryValue_NotTheTypeSample()
    {
        // The type sample stops at 200. If evidence stopped there too, a longer value
        // further down would produce a bound that rejects the source's own data.
        var values = Enumerable.Repeat("short", 500).Append("a much longer value here").ToList();

        var facts = ColumnTypeInference.Evidence(values, "string");

        Assert.Equal("a much longer value here".Length, facts.MaxLength);
    }

    [Fact]
    public void AllWholeNumbers_YieldNoScaleBound()
    {
        // Every sampled value being whole does not mean the column forbids decimals.
        // Recording scale 0 here would refuse 9.99 for ever after.
        var facts = ColumnTypeInference.Evidence(["1", "2", "3"], "decimal");

        Assert.Null(facts.Scale);
    }

    [Fact]
    public void NonStringTypes_CarryNoLengthBound()
    {
        // "3" being one character is not a bound on an int. Emitting one would be nonsense.
        var facts = ColumnTypeInference.Evidence(["3", "42"], "int");

        Assert.Null(facts.MaxLength);
    }
}
