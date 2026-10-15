using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema;

/// <summary>
/// Values that became a different number, stayed text when they were plainly dates, or were
/// offered a format rule they do not have.
/// </summary>
public class ValueFidelityTests
{
    // --- F9: a number decimal cannot hold exactly is not rounded into a different one ---

    [Fact]
    public void ADecimalWithMoreDigitsThanDecimalHolds_KeepsTheColumnText()
        => Assert.Equal(
            "string",
            ColumnTypeInference.Infer(["0.12345678901234567890123456789012", "1.5"], "Rate"));

    [Theory]
    [InlineData("0.1234567890123456789012345678")]   // 28 places: exactly what decimal holds
    [InlineData("1.50")]
    [InlineData("12345678901234567890")]
    [InlineData("-0.000001")]
    public void ADecimalThatFitsExactly_IsStillADecimal(string value)
        => Assert.Equal("decimal", ColumnTypeInference.Infer([value, "1.5"], "Rate"));

    [Fact]
    public void AJsonNumberTooSmallForDecimal_StaysAsWritten_NotZero()
    {
        var table = JsonTableReader.ReadJson(
            """[{"tiny":1.5e-30},{"tiny":0.5}]""", "orders", "orders.json")[0];

        Assert.Equal("1.5e-30", table.Value(0, "Tiny"));
        Assert.Equal("string", table.Columns[0].Type);
    }

    [Fact]
    public void AJsonExponentNumberThatFits_IsStillPlainDigits()
    {
        var table = JsonTableReader.ReadJson(
            """[{"sci":1.0E3,"small":1e-5}]""", "orders", "orders.json")[0];

        Assert.Equal("1000", table.Value(0, "Sci"));
        Assert.Equal("0.00001", table.Value(0, "Small"));
    }

    // --- F10: ISO date-times with fractional seconds, or without seconds ---

    [Theory]
    [InlineData("2024-01-15 10:30:00.123")]
    [InlineData("2024-01-15T10:30")]
    public void AnIsoDateTime_IsADateTime(string value)
        => Assert.Equal("DateTime", ColumnTypeInference.Infer([value, "2024-01-16 10:30:00.000"], "Stamp"));

    [Fact]
    public void ASlashDate_StaysText()
        => Assert.Equal("string", ColumnTypeInference.Infer(["01/02/2024", "03/04/2024"], "Day"));

    // --- F11: 32 digits are not a GUID ---

    [Fact]
    public void ThirtyTwoDigits_AreNotOfferedAsAGuid()
    {
        var scan = new ColumnScanResult(["99999999999999999999999999999999", "12345678901234567890123456789012"], false, 32, 0, 2, 2);

        Assert.DoesNotContain(
            RuleProposals.For(scan, "Guid", "string", mayBeUnique: true),
            p => p.Kind == RuleProposalKind.Pattern);
    }

    [Theory]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    public void ARealGuid_IsStillOfferedAsOne(string value)
    {
        var scan = new ColumnScanResult([value], false, value.Length, 0, 1, 1);

        var pattern = Assert.Single(
            RuleProposals.For(scan, "Guid", "string", mayBeUnique: true),
            p => p.Kind == RuleProposalKind.Pattern);
        Assert.Equal("GUID", pattern.Rule.CustomRule);
    }
}
