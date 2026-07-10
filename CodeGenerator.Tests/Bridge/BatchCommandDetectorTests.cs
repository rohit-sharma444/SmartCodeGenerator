using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

// The context tests below mutate the process-wide ContextManager.Current /
// SessionContext singletons; method-level parallelization (see
// MSTestSettings.cs) would otherwise race them against each other and against
// ParsingStateSnapshotTests.
[TestClass]
[DoNotParallelize]
public class BatchCommandDetectorTests
{
    private static string NewWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "batch_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    [TestMethod]
    public void Detect_ActionableSentences_ResolveInInputOrder()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(
            ["create a Customer class", "delete class Foo", "list projects"], root);

        Assert.AreEqual(3, actions.Count);
        Assert.AreEqual(IntentType.CreateClass, actions[0].Intent);
        Assert.AreEqual(IntentType.DeleteClass, actions[1].Intent);
        Assert.AreEqual(IntentType.ListProjects, actions[2].Intent);
        Assert.IsTrue(actions.All(a => a.IsActionable));
        Assert.IsTrue(actions.All(a => a.Preview is not null));
    }

    [TestMethod]
    public void Detect_ProseLine_IsUnactionableWithNoteAndNoPreview()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(["the big red car"], root);

        Assert.AreEqual(1, actions.Count);
        Assert.IsFalse(actions[0].IsActionable);
        Assert.AreEqual(IntentType.Unknown, actions[0].Intent);
        Assert.IsNull(actions[0].Preview);
        Assert.IsNotNull(actions[0].Note);
    }

    [TestMethod]
    public void Detect_BlankLines_AreDropped()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(
            ["", "   ", "create a Customer class", ""], root);

        Assert.AreEqual(1, actions.Count);
        Assert.AreEqual("create a Customer class", actions[0].Sentence);
    }

    [TestMethod]
    public void Detect_SentenceIsPreservedVerbatimForReplay()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(["  create a Customer class  "], root);

        // Trimmed, but otherwise untouched: this string is re-parsed at execution.
        Assert.AreEqual("create a Customer class", actions[0].Sentence);
    }

    [TestMethod]
    public void Detect_MixedBatch_CountsActionableCorrectly()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(
            ["create a Customer class", "the big red car", "delete class Foo"], root);

        Assert.AreEqual(3, actions.Count);
        Assert.AreEqual(2, actions.Count(a => a.IsActionable));
    }

    [TestMethod]
    public void Detect_WritesNothingToTheWorkspace()
    {
        var root = NewWorkspace();

        BatchCommandDetector.Detect(
            ["create a Customer class", "delete class Foo", "create a service for Payment"], root);

        // The whole point of "present before execute": detection must not act.
        Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length);
    }

    [TestMethod]
    public void Detect_LeavesSharedSemanticContextUnchanged()
    {
        var root = NewWorkspace();

        ContextManager.Current.LastEntity = "SENTINEL";

        BatchCommandDetector.Detect(
            ["create a Customer class", "add property Email to Customer"], root);

        // ContextResolver.Resolve writes LastEntity during Analyze. If detection
        // leaked, execution would re-parse line 1 against line 2's entity.
        Assert.AreEqual("SENTINEL", ContextManager.Current.LastEntity);
    }

    [TestMethod]
    public void Detect_LeavesSessionContextUnchanged()
    {
        var root = NewWorkspace();

        SessionContext.CurrentClass = "SentinelClass";

        BatchCommandDetector.Detect(["create a Customer class"], root);

        // EntityExtractor.Extract sets SessionContext.CurrentClass on any
        // create-class sentence, and nothing in the codebase ever clears it.
        // Without the restore, merely previewing (or rejecting!) this line would
        // make "Customer" the implicit target of every later command.
        Assert.AreEqual("SentinelClass", SessionContext.CurrentClass);
    }
}
