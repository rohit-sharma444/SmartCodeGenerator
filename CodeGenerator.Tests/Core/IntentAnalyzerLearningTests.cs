using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class IntentAnalyzerLearningTests
{
    [TestInitialize]
    public void Setup() => LearnedIntentStore.UseInMemory();

    [TestCleanup]
    public void Cleanup() => LearnedIntentStore.UseInMemory();

    [TestMethod]
    public void LearnedNudge_NeverOverridesCreateClassGuard()
    {
        // Heavily (mis)reinforce CreateMethod for a create-class phrase.
        for (var i = 0; i < 20; i++)
            LearnedIntentStore.Current.Reinforce("create class Foo", IntentType.CreateMethod);

        var semantic = SemanticAnalyzer.Analyze("create class Foo");
        var intent = CodeGenerationEngine.Core.IntentAnalyzer.Detect(semantic);

        Assert.AreEqual(IntentType.CreateClass, intent);
    }

    [TestMethod]
    public void LearnedNudge_TipsABorderlineNonsenseCommand()
    {
        // A phrase the base rules do not confidently classify.
        const string phrase = "wibble the wobble";

        var before = CodeGenerationEngine.Core.IntentAnalyzer.Detect(
            SemanticAnalyzer.Analyze(phrase));
        Assert.AreEqual(IntentType.Unknown, before);

        for (var i = 0; i < 8; i++)
            LearnedIntentStore.Current.Reinforce(phrase, IntentType.ListClasses);

        var after = CodeGenerationEngine.Core.IntentAnalyzer.Detect(
            SemanticAnalyzer.Analyze(phrase));
        Assert.AreEqual(IntentType.ListClasses, after);
    }
}
