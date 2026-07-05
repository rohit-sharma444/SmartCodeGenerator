using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class LearnedIntentMemoryPersistenceTests
{
    private static string NewTempFile()
        => Path.Combine(Path.GetTempPath(),
            "learned_" + Guid.NewGuid().ToString("N") + ".json");

    [TestMethod]
    public void SaveThenLoad_RoundTripsEntries()
    {
        var file = NewTempFile();
        var mem = new LearnedIntentMemory();
        mem.Reinforce("show my widgets", IntentType.ListClasses);
        mem.Reinforce("show my widgets", IntentType.ListClasses);
        mem.Save(file);

        var loaded = new LearnedIntentMemory();
        loaded.Load(file);

        var intent = loaded.Suggest("show my widgets", out _);
        Assert.AreEqual(IntentType.ListClasses, intent);
    }

    [TestMethod]
    public void Load_MissingFile_IsEmpty()
    {
        var mem = new LearnedIntentMemory();
        mem.Load(NewTempFile());
        Assert.AreEqual(0, mem.Entries.Count);
    }

    [TestMethod]
    public void Decay_HalvesCountsForStaleEntries()
    {
        var mem = new LearnedIntentMemory();
        for (var i = 0; i < 8; i++)
            mem.Reinforce("show my widgets", IntentType.ListClasses);

        // Force the entry to look old, then decay relative to "now".
        var entry = mem.Entries.Single();
        entry.LastUsedUtc = DateTime.UtcNow.AddDays(-90);

        mem.Decay(DateTime.UtcNow);

        Assert.AreEqual(4, mem.Entries.Single().SuccessCount);
    }

    [TestMethod]
    public void Decay_LeavesRecentEntriesUntouched()
    {
        var mem = new LearnedIntentMemory();
        for (var i = 0; i < 8; i++)
            mem.Reinforce("show my widgets", IntentType.ListClasses);

        mem.Decay(DateTime.UtcNow);

        Assert.AreEqual(8, mem.Entries.Single().SuccessCount);
    }
}
