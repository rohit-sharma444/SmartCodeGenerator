using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
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
}
