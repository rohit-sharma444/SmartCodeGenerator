using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class LearnedIntentStoreTests
{
    [TestMethod]
    public void Initialize_LoadsAndSavesUnderWorkspaceCodegenFolder()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "cg_lis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        LearnedIntentStore.Initialize(workspace);
        for (var i = 0; i < 4; i++)
            LearnedIntentStore.Current.Reinforce("show my widgets", IntentType.ListClasses);
        LearnedIntentStore.Save();

        var expected = Path.Combine(workspace, ".codegen", "learned.json");
        Assert.IsTrue(File.Exists(expected), $"expected learned.json at {expected}");

        // Re-initialize from the same workspace: data survives.
        LearnedIntentStore.Initialize(workspace);
        Assert.AreEqual(IntentType.ListClasses,
            LearnedIntentStore.Current.Suggest("show my widgets", out _));
    }

    [TestMethod]
    public void Enabled_FollowsEnvVar()
    {
        Environment.SetEnvironmentVariable("CG_NO_LEARN", "1");
        try
        {
            Assert.IsFalse(LearnedIntentStore.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CG_NO_LEARN", null);
        }

        Assert.IsTrue(LearnedIntentStore.Enabled);
    }

    [TestMethod]
    public void UseInMemory_GivesFreshNonPersistedMemory()
    {
        LearnedIntentStore.UseInMemory();
        Assert.IsEmpty(LearnedIntentStore.Current.Entries);
        // Save is a no-op with no path; must not throw.
        LearnedIntentStore.Save();
    }

    [TestMethod]
    public void ReloadFromDisk_DiscardsChangesNeverSaved()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "cg_lis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        LearnedIntentStore.Initialize(workspace);
        LearnedIntentStore.Save();

        LearnedIntentStore.Current.Reinforce("show my widgets", IntentType.ListClasses);

        LearnedIntentStore.ReloadFromDisk();

        Assert.IsEmpty(LearnedIntentStore.Current.Entries);
    }

    [TestMethod]
    public void ReloadFromDisk_WithNoBackingFile_LeavesMemoryAlone()
    {
        LearnedIntentStore.UseInMemory();
        LearnedIntentStore.Current.Reinforce("show my widgets", IntentType.ListClasses);

        // No file to reload from; wiping memory here would be data loss.
        LearnedIntentStore.ReloadFromDisk();

        Assert.IsNotEmpty(LearnedIntentStore.Current.Entries);
    }
}
