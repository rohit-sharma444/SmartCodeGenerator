using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// The "create a &lt;Name&gt; class" patterns strip an optional leading article. The
/// article group used to be unanchored — <c>(?:a|an)?\s*</c> — so on a class name
/// beginning with "a" it ate the first letter instead: "create Alpha class" produced
/// a class (and a file) called "Lpha". These pin the article down to a standalone
/// word while keeping the real article forms working.
/// </summary>
[TestClass]
public class ClassNameExtractionTests
{
    private static string? ClassNameOf(string sentence) =>
        CommandFront.Parse(sentence, "").ClassName;

    [TestMethod]
    // Names beginning with the article letters must survive intact.
    [DataRow("create Alpha class", "Alpha")]
    [DataRow("create Account class", "Account")]
    [DataRow("create Antenna class", "Antenna")]
    [DataRow("create Address class", "Address")]
    // ...as must everything else.
    [DataRow("create Invoice class", "Invoice")]
    [DataRow("create Item class", "Item")]
    public void Class_name_keeps_its_leading_letter(string sentence, string expected) =>
        Assert.AreEqual(expected, ClassNameOf(sentence));

    [TestMethod]
    // A genuine article is still dropped.
    //
    // Only "create", "generate" and "i need" appear here: ExtractClassName also
    // carries "build" and "make" patterns, but no IntentRepository rule maps those
    // verbs, so "make a Product class" never resolves an intent and those two
    // patterns are unreachable. Asserting on them would be asserting on dead code.
    [DataRow("create a Customer class", "Customer")]
    [DataRow("create an Invoice class", "Invoice")]
    [DataRow("generate a Payment class", "Payment")]
    [DataRow("generate an Address class", "Address")]
    public void Leading_article_is_still_stripped(string sentence, string expected) =>
        Assert.AreEqual(expected, ClassNameOf(sentence));

    [TestMethod]
    public void Create_class_with_an_article_name_resolves_the_create_intent()
    {
        var cmd = CommandFront.Parse("create Alpha class", "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual("Alpha", cmd.ClassName);
    }
}
