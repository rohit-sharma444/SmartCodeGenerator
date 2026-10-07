using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DomainAppNameTests
{
    [TestMethod]
    public void FromDescription_PascalCasesWords()
        => Assert.AreEqual("Payroll",
            DomainAppName.FromDescription("payroll application"));

    [TestMethod]
    public void FromDescription_DropsLeadingVerbAndTrailingTypeNoun()
        => Assert.AreEqual("CashBook",
            DomainAppName.FromDescription("develop Cash Book application"));

    [TestMethod]
    public void FromDescription_DropsCommonCommandVerbs()
    {
        Assert.AreEqual("Inventory", DomainAppName.FromDescription("build inventory system"));
        Assert.AreEqual("OrderTracker", DomainAppName.FromDescription("create an Order Tracker app"));
        Assert.AreEqual("Timesheet", DomainAppName.FromDescription("please generate a Timesheet tool"));
    }

    [TestMethod]
    public void FromDescription_KeepsNameWhenNoFillerPresent()
        => Assert.AreEqual("CashBook", DomainAppName.FromDescription("Cash Book"));

    [TestMethod]
    public void FromDescription_AllFillerWords_FallsBackToWholeDescription()
        => Assert.AreEqual("BuildApplication",
            DomainAppName.FromDescription("build application"));

    [TestMethod]
    public void FromDescription_BlankOrNonAlphanumeric_ReturnsGeneratedApp()
    {
        Assert.AreEqual("GeneratedApp", DomainAppName.FromDescription(""));
        Assert.AreEqual("GeneratedApp", DomainAppName.FromDescription("   "));
        Assert.AreEqual("GeneratedApp", DomainAppName.FromDescription("!!!"));
    }

    // Measured live: the whole sentence became the project, 84 characters long, cut off in the
    // Blazor brand bar and deepening every path under it.
    [TestMethod]
    public void FromDescription_NamesTheSubjectNotTheWholeSentence()
    {
        Assert.AreEqual("DentalPractice", DomainAppName.FromDescription(
            "a dental practice that schedules appointments for patients with dentists and bills treatments"));
        Assert.AreEqual("CarRentalCompany", DomainAppName.FromDescription(
            "a car rental company that rents vehicles to customers"));
        Assert.AreEqual("Gym", DomainAppName.FromDescription(
            "a gym that sells memberships to members and runs fitness classes"));
        Assert.AreEqual("Library", DomainAppName.FromDescription("a library which lends books"));
        Assert.AreEqual("Clinic", DomainAppName.FromDescription("a clinic where vets treat pets"));
        Assert.AreEqual("PointOfSale", DomainAppName.FromDescription("point of sale for a corner shop"));
    }

    [TestMethod]
    public void FromDescription_UsesWhatFollowsWhenTheSubjectIsOnlyTheKindOfThing()
        => Assert.AreEqual("DentalPractice", DomainAppName.FromDescription("an app for a dental practice"));

    [TestMethod]
    public void FromDescription_IsNeverLongerThanAFewWords()
    {
        var name = DomainAppName.FromDescription(
            "a regional community veterinary emergency referral hospital network");

        Assert.IsLessThanOrEqualTo(30, name.Length, name);
        Assert.StartsWith("Regional", name);
    }

    [TestMethod]
    public void FromTypedName_KeepsEveryWordTheUserTyped()
        => Assert.AreEqual("DentalPracticeThatSchedulesAppointments",
            DomainAppName.FromTypedName("Dental Practice That Schedules Appointments"));

    [TestMethod]
    public void FromDescription_StartingWithDigit_StartsWithLetter()
    {
        var name = DomainAppName.FromDescription("123 app");
        Assert.IsTrue(char.IsLetter(name[0]), $"expected letter start, got '{name}'");
    }
}
