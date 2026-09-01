using CodeGenerator.App.Resume;

namespace CodeGenerator.Tests.App.Resume;

[TestClass]
public class BuildSnapshotStoreTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "resume-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Nothing_saved_means_nothing_to_load()
        => Assert.IsNull(BuildSnapshotStore.Load(_root));

    [TestMethod]
    public void What_was_saved_comes_back()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot { Description = "a shop" });

        Assert.AreEqual("a shop", BuildSnapshotStore.Load(_root)!.Description);
    }

    [TestMethod]
    public void It_is_written_where_the_other_codegen_files_live()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot());

        var path = BuildSnapshotStore.PathFor(_root);

        Assert.IsTrue(File.Exists(path));
        StringAssert.Contains(path, ".codegen");
        StringAssert.Contains(path, BuildSnapshotStore.FileName);
    }

    [TestMethod]
    public void Clearing_removes_it()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot());
        BuildSnapshotStore.Clear(_root);

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }

    [TestMethod]
    public void Clearing_what_was_never_saved_is_not_an_error()
    {
        BuildSnapshotStore.Clear(_root);
        BuildSnapshotStore.Clear(_root);
    }

    /// <summary>
    /// A broken config must leave the CLI working exactly as it does without one — the rule
    /// CustomizationDefaults and AiConfig.Load already follow.
    /// </summary>
    [TestMethod]
    public void A_corrupt_file_reads_as_nothing_rather_than_throwing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BuildSnapshotStore.PathFor(_root))!);
        File.WriteAllText(BuildSnapshotStore.PathFor(_root), "{ this is not json");

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }

    /// <summary>
    /// A file from a NEWER build is refused whole rather than partially read. Half a model is
    /// worse than no offer, and someone running two versions must not have the older one quietly
    /// drop what the newer one saved.
    /// </summary>
    [TestMethod]
    public void A_snapshot_from_a_later_schema_is_refused()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot
        {
            SchemaVersion = BuildSnapshot.CurrentSchemaVersion + 1,
            Description = "from the future",
        });

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }

    [TestMethod]
    public void Saving_over_an_unwritable_path_is_not_an_error()
    {
        // A file where the folder should be: creating .codegen must fail, and the command must
        // carry on regardless.
        File.WriteAllText(Path.Combine(_root, ".codegen"), "in the way");

        BuildSnapshotStore.Save(_root, new BuildSnapshot { Description = "a shop" });

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }
}
