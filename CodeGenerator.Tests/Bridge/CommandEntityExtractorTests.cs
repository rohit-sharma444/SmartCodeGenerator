using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
[DoNotParallelize]
public class CommandEntityExtractorTests
{
    [TestMethod]
    public void Extracts_class_name_for_create_class()
    {
        var cmd = CommandEntityExtractor.Extract("create a class called Customer", IntentType.CreateClass, "");
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Extracts_rename_source_and_target_positionally()
    {
        var cmd = CommandEntityExtractor.Extract("rename class Foo to Bar", IntentType.RenameClass, "");
        Assert.AreEqual("Foo", cmd.ClassName);
        Assert.AreEqual("Bar", cmd.NewClassName);
    }

    [TestMethod]
    public void Carries_the_raw_sentence_as_provenance()
    {
        var cmd = CommandEntityExtractor.Extract("create a class called Customer", IntentType.CreateClass, "");
        Assert.AreEqual("create a class called Customer", cmd.OriginalSentence);
    }

    private static ParsedCommand CreateClass(string sentence) =>
        CommandEntityExtractor.Extract(sentence, IntentType.CreateClass, "");

    [TestMethod]
    public void And_add_method_to_same_class_is_a_method()
    {
        var cmd = CreateClass("create class Order with properties Total decimal and add method Ship to Order");
        CollectionAssert.AreEqual(new[] { "Total" }, cmd.Properties.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Ship" }, cmd.Methods.Select(m => m.Name).ToArray());
        Assert.IsEmpty(cmd.IgnoredProperties);
        Assert.IsEmpty(cmd.IgnoredMethods);
    }

    [TestMethod]
    public void And_add_method_without_class()
    {
        var cmd = CreateClass("create class Order with properties Name string and add method Ship");
        CollectionAssert.AreEqual(new[] { "Ship" }, cmd.Methods.Select(m => m.Name).ToArray());
        Assert.IsEmpty(cmd.IgnoredProperties);
        Assert.IsEmpty(cmd.IgnoredMethods);
    }

    [TestMethod]
    public void And_create_method_with_returns()
    {
        var cmd = CreateClass("create class Order with properties Total decimal and create method Total2 returns int to Order");
        Assert.HasCount(1, cmd.Methods);
        Assert.AreEqual("Total2", cmd.Methods[0].Name);
        Assert.AreEqual("int", cmd.Methods[0].ReturnType);
        Assert.IsEmpty(cmd.IgnoredProperties);
        Assert.IsEmpty(cmd.IgnoredMethods);
    }

    [TestMethod]
    public void And_add_method_to_other_class_is_refused()
    {
        var cmd = CreateClass("create class Order with properties Total decimal and add method Ship to Customer");
        Assert.IsEmpty(cmd.Methods);
        Assert.HasCount(1, cmd.IgnoredMethods);
        StringAssert.Contains(cmd.IgnoredMethods[0].Error, "Customer");
        StringAssert.Contains(cmd.IgnoredMethods[0].Error, "add method Ship to Customer");
    }

    [TestMethod]
    public void Unreadable_method_entry_is_reported()
    {
        var cmd = CreateClass("create class Order with methods Ship quickly now");
        Assert.IsEmpty(cmd.Methods);
        Assert.HasCount(1, cmd.IgnoredMethods);
        Assert.AreEqual("Ship quickly now", cmd.IgnoredMethods[0].Entry);
    }
}
