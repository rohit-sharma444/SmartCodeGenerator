using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class UnrecognizedCommandTests
{
    [TestMethod]
    public void Describe_NoSuggestions_IsSingleLine()
    {
        var text = UnrecognizedCommand.Describe("zzz", []);
        StringAssert.Contains(text, "Command not recognized: 'zzz'");
        StringAssert.Contains(text, "Type 'help'");
        Assert.IsFalse(text.Contains("Did you mean"));
    }

    [TestMethod]
    public void Describe_WithSuggestions_AppendsDidYouMean()
    {
        var text = UnrecognizedCommand.Describe("craete class", ["create class", "create project"]);
        StringAssert.Contains(text, "Command not recognized: 'craete class'");
        StringAssert.Contains(text, "Did you mean: create class, create project?");
    }

    [TestMethod]
    public void Describe_TrimsTheEchoedInput()
        => StringAssert.Contains(
            UnrecognizedCommand.Describe("   foo   ", []), "'foo'");
}
