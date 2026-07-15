using NaturalLanguageBridge;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class PipelineLearningTests
{
    [TestMethod]
    public void ProcessInput_SuccessfulCommand_RecordsReinforcement()
    {
        LearnedIntentStore.UseInMemory();
        LearningCoordinator.ResetHistory();
        ProjectManager.CurrentProject = null;

        var workspace = Path.Combine(Path.GetTempPath(), "cg_pl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        ProjectMetaData? project = null;
        CommandFront.Run(
            "create class Widget",
            ref project,
            new List<PropertyRule>(),
            workspace,
            p => ProjectManager.CurrentProject = p);

        var signature = LearnedIntentMemory.Signature("create class Widget");
        var entry = LearnedIntentStore.Current.Entries
            .SingleOrDefault(e => e.Signature == signature && e.Intent == IntentType.CreateClass);

        Assert.IsNotNull(entry,
            "ProcessInput should have recorded a CreateClass outcome for the command");
        Assert.IsGreaterThanOrEqualTo(1, entry!.SuccessCount, $"successes={entry.SuccessCount}");
    }
}
