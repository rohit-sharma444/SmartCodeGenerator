using NaturalLanguageBridge;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class LearningCommandsTests
{
    [TestMethod]
    [DataRow("show learning", IntentType.ShowLearning)]
    [DataRow("reset learning", IntentType.ResetLearning)]
    public void LearningCommands_AreDetected(string input, IntentType expected)
    {
        var semantic = SemanticAnalyzer.Analyze(input);
        var intent = CodeGenerationEngine.Core.IntentAnalyzer.Detect(semantic);
        Assert.AreEqual(expected, intent);
    }

    [TestMethod]
    public void ResetLearning_ClearsMemory()
    {
        LearnedIntentStore.UseInMemory();
        LearnedIntentStore.Current.Reinforce("show my widgets", IntentType.ListClasses);

        var workspace = Path.Combine(Path.GetTempPath(), "cg_lc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        ProjectMetaData? project = null;
        CommandFront.Run(
            "reset learning",
            ref project,
            new List<PropertyRule>(),
            workspace,
            _ => { });

        Assert.IsEmpty(LearnedIntentStore.Current.Entries);
    }
}
