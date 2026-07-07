using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DomainAppNameTests
{
    [TestMethod]
    public void FromDescription_PascalCasesWords()
        => Assert.AreEqual("PayrollApplication",
            DomainAppName.FromDescription("payroll application"));

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
