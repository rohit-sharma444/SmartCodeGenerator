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

    [Theory]
    [InlineData("name", "Name")]
    [InlineData("prep time", "PrepTime")]
    [InlineData("due-date", "DueDate")]
    [InlineData("  total  amount ", "TotalAmount")]
    public void ToFieldName_PascalCasesEveryWord(string input, string expected)
        => Assert.Equal(expected, NameNormalizer.ToFieldName(input));

    [Theory]
    [InlineData("servings", "Servings")]
    [InlineData("ingredients", "Ingredients")]
    public void ToFieldName_KeepsPlurals(string input, string expected)
        // A field may legitimately be plural; only entity names are singularized,
        // because they become one class and one file each.
        => Assert.Equal(expected, NameNormalizer.ToFieldName(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void ToFieldName_RejectsNamesWithNoLettersOrDigits(string input)
        => Assert.Equal(string.Empty, NameNormalizer.ToFieldName(input));

    [Theory]
    [InlineData("Name")]
    [InlineData("PrepTime")]
    [InlineData("Line2")]
    public void IsValidIdentifier_AcceptsAnIdentifierCSharpCanDeclare(string name)
        => Assert.True(NameNormalizer.IsValidIdentifier(name));

    [Theory]
    [InlineData("")]
    [InlineData("2ndCourse")]   // a leading digit is not an identifier: CS1519
    [InlineData("123")]
    public void IsValidIdentifier_RejectsWhatCSharpCannotDeclare(string name)
        => Assert.False(NameNormalizer.IsValidIdentifier(name));

    [Theory]
    [InlineData("Cash register", "CashRegister")]
    [InlineData("Payment terminal", "PaymentTerminal")]
    [InlineData("Credit cards", "CreditCard")]
    [InlineData("Receipt", "Receipt")]
    [InlineData("self-checkout", "SelfCheckout")]
    public void ToCompoundEntityName_KeepsEveryWord_AndSingularizesTheLast(
        string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.ToCompoundEntityName(input));
    }

    [Fact]
    public void ToCompoundEntityName_OfBlank_IsEmpty()
    {
        Assert.Equal(string.Empty, NameNormalizer.ToCompoundEntityName("   "));
    }

    [Theory]
    // A name the user typed is already the class name they want, so its shape survives.
    [InlineData("CashRegister", "CashRegister")]
    [InlineData("cash register", "CashRegister")]
    [InlineData("widget", "Widget")]
    // Only the last segment is singularized, and its case is kept while that happens.
    [InlineData("Books", "Book")]
    [InlineData("BookCategories", "BookCategory")]
    [InlineData("CreditCards", "CreditCard")]
    // A run of capitals is not a word boundary, so an acronym survives intact.
    [InlineData("CashREGISTER", "CashREGISTER")]
    [InlineData("ID", "ID")]
    // The -ss/-us/-is guards still apply to the last segment.
    [InlineData("BusinessAddress", "BusinessAddress")]
    public void ToUserTypedEntityName_KeepsTheTypedShape_AndSingularizesTheLastSegment(
        string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.ToUserTypedEntityName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void ToUserTypedEntityName_OfBlank_IsEmpty(string input)
        => Assert.Equal(string.Empty, NameNormalizer.ToUserTypedEntityName(input));

    [Fact]
    public void ToUserTypedEntityName_DoesNotRescueANameCSharpCannotDeclare()
        // The caller still guards with IsValidIdentifier; normalizing must not hide a leading
        // digit by silently dropping it.
        => Assert.False(
            NameNormalizer.IsValidIdentifier(NameNormalizer.ToUserTypedEntityName("2ndthing")));
}
