using CodeGenerationEngine.Core.Nlp;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class NlpActionProviderTests
{
    [DataTestMethod]
    [DataRow("create a Customer class", "create")]
    [DataRow("add method Save to Order", "add")]
    [DataRow("list projects", "list")]
    [DataRow("delete class Foo", "delete")]
    [DataRow("rename class A to B", "rename")]
    [DataRow("analyze class Foo", "analyze")]
    public void GetPrimaryAction_CommandVerb_ReturnsLowercasedVerb(
        string input, string expected)
    {
        Assert.AreEqual(expected, NlpActionProvider.GetPrimaryAction(input));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("the big red car")]
    public void GetPrimaryAction_EmptyOrNonActionable_ReturnsNull(string input)
    {
        Assert.IsNull(NlpActionProvider.GetPrimaryAction(input));
    }
}
