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
        // Signatures differ but share 3 of 4 tokens -> Jaccard 3/5 = 0.6 (>= 0.6),
        // and the signatures are NOT identical, so IsRephrase returns true.
        const string first = "alpha beta gamma delta";
        const string rephrase = "alpha beta gamma epsilon";

        // Record the first command, then a near-identical rephrase immediately after.
        LearningCoordinator.RecordOutcome(first, IntentType.CreateClass, success: true, command: null);
        LearningCoordinator.RecordOutcome(rephrase, IntentType.ListClasses, success: true, command: null);

        // The prior interpretation (CreateClass for `first`) must have been weakened:
        // only the quick-rephrase branch can add a failure to that entry.
        var priorSignature = LearnedIntentMemory.Signature(first);
        var priorEntry = LearnedIntentStore.Current.Entries
            .Single(e => e.Signature == priorSignature && e.Intent == IntentType.CreateClass);

        Assert.IsTrue(priorEntry.FailureCount >= 1,
            $"quick-rephrase should have weakened the prior intent; failures={priorEntry.FailureCount}");
    }
}
