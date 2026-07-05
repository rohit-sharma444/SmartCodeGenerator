using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class LearnedIntentMemoryTests
{
    [TestMethod]
    public void Signature_IsWordOrderIndependent()
    {
        Assert.AreEqual(
            LearnedIntentMemory.Signature("class list"),
            LearnedIntentMemory.Signature("list class"));
    }

    [TestMethod]
    public void Suggest_UnknownSignature_ReturnsNull()
    {
        var mem = new LearnedIntentMemory();
        Assert.IsNull(mem.Suggest("list classes", out _));
    }

    [TestMethod]
    public void Reinforce_ThenSuggest_ReturnsReinforcedIntent()
    {
        var mem = new LearnedIntentMemory();
        for (var i = 0; i < 4; i++)
            mem.Reinforce("show my widgets", IntentType.ListClasses);

        var intent = mem.Suggest("show my widgets", out var confidence);

        Assert.AreEqual(IntentType.ListClasses, intent);
        Assert.IsTrue(confidence >= LearnedIntentMemory.SuggestThreshold,
            $"confidence {confidence}");
    }

    [TestMethod]
    public void Weaken_DrivesConfidenceBelowThreshold()
    {
        var mem = new LearnedIntentMemory();
        mem.Reinforce("show my widgets", IntentType.ListClasses);
        for (var i = 0; i < 5; i++)
            mem.Weaken("show my widgets", IntentType.ListClasses);

        mem.Suggest("show my widgets", out var confidence);
        Assert.IsTrue(confidence < LearnedIntentMemory.SuggestThreshold,
            $"confidence {confidence}");
    }

    [TestMethod]
    public void Suggest_PicksHighestConfidenceIntentForSignature()
    {
        var mem = new LearnedIntentMemory();
        mem.Reinforce("do the thing", IntentType.CreateMethod);
        for (var i = 0; i < 5; i++)
            mem.Reinforce("do the thing", IntentType.ListClasses);

        var intent = mem.Suggest("do the thing", out _);
        Assert.AreEqual(IntentType.ListClasses, intent);
    }

    [TestMethod]
    public void Reset_ClearsEntries()
    {
        var mem = new LearnedIntentMemory();
        mem.Reinforce("x y z", IntentType.ListClasses);
        mem.Reset();
        Assert.AreEqual(0, mem.Entries.Count);
    }
}
