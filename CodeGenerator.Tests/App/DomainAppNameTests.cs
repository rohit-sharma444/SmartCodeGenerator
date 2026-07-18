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

    [TestMethod]
    public void FromDescription_StartingWithDigit_StartsWithLetter()
    {
        var name = DomainAppName.FromDescription("123 app");
        Assert.IsTrue(char.IsLetter(name[0]), $"expected letter start, got '{name}'");
    }
}
