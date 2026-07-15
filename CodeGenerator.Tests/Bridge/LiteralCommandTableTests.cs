using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class LiteralCommandTableTests
{
    [TestMethod]
    public void Resolves_learning_meta_commands_case_insensitively()
    {
        Assert.IsTrue(LiteralCommandTable.TryResolve("  Show Learning ", out var intent));
        Assert.AreEqual(IntentType.ShowLearning, intent);
    }

    [TestMethod]
    public void Rejects_natural_language_that_merely_contains_a_keyword()
    {
        // Not an exact command; must fall through to the NLP path.
        Assert.IsFalse(LiteralCommandTable.TryResolve("show me the learning report", out _));
    }

    [TestMethod]
    public void Rejects_empty_and_null_input()
    {
        Assert.IsFalse(LiteralCommandTable.TryResolve("", out _));
        Assert.IsFalse(LiteralCommandTable.TryResolve(null!, out _));
    }

    [TestMethod]
    public void Resolves_help_meta_command()
    {
        Assert.IsTrue(LiteralCommandTable.TryResolve("help", out var intent));
        Assert.AreEqual(IntentType.Help, intent);
    }
}
