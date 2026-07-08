using DomainDiscovery.SeedExtraction;
using Xunit;

namespace DomainDiscovery.Tests.SeedExtraction;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("employees", "Employee")]
    [InlineData("accounts", "Account")]
    [InlineData("bills", "Bill")]
    [InlineData("deductions", "Deduction")]
    [InlineData("taxes", "Tax")]
    [InlineData("boxes", "Box")]
    [InlineData("matches", "Match")]
    [InlineData("companies", "Company")]
    [InlineData("policies", "Policy")]
    [InlineData("businesses", "Business")]
    [InlineData("addresses", "Address")]
    public void ToEntityName_SingularizesPlurals(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.ToEntityName(input));

    [Theory]
    [InlineData("address", "Address")]     // -ss is not a plural
    [InlineData("business", "Business")]
    [InlineData("gross", "Gross")]
    [InlineData("class", "Class")]
    [InlineData("status", "Status")]       // -us is not a plural
    [InlineData("analysis", "Analysis")]   // -is is not a plural
    [InlineData("basis", "Basis")]
    [InlineData("pay", "Pay")]
    [InlineData("payroll", "Payroll")]
    [InlineData("data", "Data")]
    public void ToEntityName_LeavesNonPluralsAlone(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.ToEntityName(input));

    [Fact]
    public void ToEntityName_SingularizesTheHeadWordOfAPhrase()
        => Assert.Equal("Member", NameNormalizer.ToEntityName("library members"));

    [Fact]
    public void ToEntityName_SingularAndPluralNormalizeToTheSameName()
        => Assert.Equal(NameNormalizer.ToEntityName("employee"), NameNormalizer.ToEntityName("Employees"));
}
