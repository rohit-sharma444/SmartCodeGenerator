using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class LearningCoordinatorTests
{
    [TestInitialize]
    public void Setup()
    {
        LearnedIntentStore.UseInMemory();
        LearningCoordinator.ResetHistory();
    }

    [TestMethod]
    public void RecordOutcome_Success_ReinforcesIntent()
    {
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: true, command: null);
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: true, command: null);
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: true, command: null);

        Assert.AreEqual(IntentType.ListClasses,
            LearnedIntentStore.Current.Suggest("show my widgets", out _));
    }

    [TestMethod]
    public void RecordOutcome_Failure_DoesNotReinforce()
    {
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: false, command: null);

        Assert.IsNull(LearnedIntentStore.Current.Suggest("show my widgets", out _));
    }

    [TestMethod]
    public void QuickRephrase_WeakensThePriorIntent()
    {
        // First command classified (reinforced by an earlier success elsewhere).
        for (var i = 0; i < 6; i++)
            LearnedIntentStore.Current.Reinforce("show my widget", IntentType.CreateClass);

        // The user runs it (recorded), then immediately rephrases a near-identical
        // command -> the prior interpretation is treated as a misfire.
        LearningCoordinator.RecordOutcome("show my widget", IntentType.CreateClass, success: true, command: null);
        LearningCoordinator.RecordOutcome("show my widgets please", IntentType.ListClasses, success: true, command: null);

        // The prior CreateClass association got a weaken; confidence should have dropped.
        LearnedIntentStore.Current.Suggest("show my widget", out var confidence);
        Assert.IsTrue(confidence < 0.85, $"expected weakened confidence, got {confidence}");
    }
}
