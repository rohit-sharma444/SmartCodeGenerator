using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class LearnedIntentEntryTests
{
    [TestMethod]
    public void Confidence_NoObservations_IsZero()
    {
        var e = new LearnedIntentEntry { Intent = IntentType.ListClasses };
        Assert.AreEqual(0d, e.Confidence);
    }

    [TestMethod]
    public void Confidence_AllSuccesses_GrowsButClampedBelowOne()
    {
        var e = new LearnedIntentEntry
        {
            Intent = IntentType.ListClasses,
            SuccessCount = 5,
            FailureCount = 0
        };
        Assert.IsTrue(e.Confidence > 0.6 && e.Confidence <= 1.0,
            $"confidence was {e.Confidence}");
    }

    [TestMethod]
    public void Confidence_MoreFailuresThanSuccesses_IsLow()
    {
        var e = new LearnedIntentEntry
        {
            Intent = IntentType.ListClasses,
            SuccessCount = 1,
            FailureCount = 4
        };
        Assert.IsTrue(e.Confidence < 0.3, $"confidence was {e.Confidence}");
    }
}
