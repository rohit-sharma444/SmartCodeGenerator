using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpActionProviderBridgeTests
{
    [DataTestMethod]
    [DataRow("create a Customer class", "create")]
    [DataRow("add method Save to Order", "add")]
    [DataRow("list projects", "list")]
    [DataRow("delete class Foo", "delete")]
    [DataRow("rename class A to B", "rename")]
    [DataRow("validate foo", "validate")]
    public void GetPrimaryAction_CommandVerb_ReturnsLowercasedVerb(string input, string expected)
    {
        Assert.AreEqual(expected, NlpActionProvider.GetPrimaryAction(input));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("the big red car")]
    public void GetPrimaryAction_BlankOrNonActionable_ReturnsNull(string input)
    {
        Assert.IsNull(NlpActionProvider.GetPrimaryAction(input));
    }

    [TestMethod]
    public void Analyze_Blank_ReturnsNull()
    {
        Assert.IsNull(NlpActionProvider.Analyze("   "));
    }

    [TestMethod]
    public void Analyze_Command_ReturnsResultWhoseActionIsTheVerb()
    {
        var result = NlpActionProvider.Analyze("create a Customer class");
        Assert.IsNotNull(result);
        Assert.AreEqual("create", NlpActionProvider.GetPrimaryAction(result));
    }
}
