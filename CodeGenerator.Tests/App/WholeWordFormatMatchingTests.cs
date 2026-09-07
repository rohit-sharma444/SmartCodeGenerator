using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The two grandfathered name-driven rules — <c>EmailAddress</c> and <c>Phone</c> — are the only
/// inference left that reads a property's name rather than its type, so they are the only place
/// the substring-matching defect can still bite.
/// </summary>
[TestClass]
public class WholeWordFormatMatchingTests
{
    [DataTestMethod]
    [DataRow("Email")]
    [DataRow("EmailAddress")]
    [DataRow("CustomerEmail")]
    [DataRow("BillingEmailAddress")]
    [DataRow("email")]
    public void AWholeEmailWord_Matches(string propertyName)
        => Assert.Contains("EmailAddress", Mapped(propertyName).Attributes);

    [DataTestMethod]
    [DataRow("Phone")]
    [DataRow("PhoneNumber")]
    [DataRow("MobilePhone")]
    [DataRow("ContactPhoneNumber")]
    public void AWholePhoneWord_Matches(string propertyName)
        => Assert.Contains("Phone", Mapped(propertyName).Attributes);

    [DataTestMethod]
    [DataRow("Emailer")]
    [DataRow("Headphones")]
    [DataRow("Microphone")]
    [DataRow("Saxophone")]
    public void AWordThatMerelyContainsOne_DoesNotMatch(string propertyName)
    {
        // Substring matching is the defect. "Headphones" contains "phone"; it is not a phone
        // number, and a format rule on it refuses every value the user types.
        var property = Mapped(propertyName);

        Assert.DoesNotContain("EmailAddress", property.Attributes);
        Assert.DoesNotContain("Phone", property.Attributes);
    }

    [DataTestMethod]
    [DataRow("EMAIL", "EmailAddress")]
    [DataRow("EMAILADDRESS", "EmailAddress")]
    [DataRow("PHONE", "Phone")]
    [DataRow("PHONENUMBER", "Phone")]
    [DataRow("CONTACT_PHONE", "Phone")]
    public void AnAllCapitalsColumnStillMatches(string propertyName, string expected)
    {
        // NameNormalizer.ToFieldName only capitalizes each word's first letter and leaves the
        // rest alone, so a SQL Server column named EMAIL or PHONE_NUMBER reaches inference as
        // EMAIL and PHONENUMBER. Splitting those at every capital yields single letters and no
        // word to match, which would silently drop a rule that works today -- and all-capitals
        // column names are the norm in the databases on this machine, not an edge case.
        //
        // A run of capitals carries no word boundaries at all, so containment within the run is
        // the only evidence there is. HEADPHONES is indistinguishable from PHONENUMBER by any
        // rule, and matches; that is no worse than today and affects only shouted names.
        Assert.Contains(expected, Mapped(propertyName).Attributes);
    }

    [TestMethod]
    public void OnlyAStringGetsAFormatAttribute()
    {
        // A column named Phone that arrived as an int is a number, not a phone number to
        // pattern-match, and [Phone] on an int is a rule the front end cannot satisfy.
        Assert.IsEmpty(Mapped("Phone", "int").Attributes);
        Assert.IsEmpty(Mapped("Email", "DateTime").Attributes);
    }

    private static PropertyDefinition Mapped(string name, string type = "string")
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField { Name = name, Type = type });
        model.Entities.Add(entity);

        return DomainModelMapper.ToClassDefinitions(model)
            .Single(c => c.Name == "Customer").Properties.Single(p => p.Name == name);
    }
}
