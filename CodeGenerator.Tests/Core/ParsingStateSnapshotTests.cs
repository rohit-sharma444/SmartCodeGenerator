using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

// Mutates the process-wide ContextManager.Current / SessionContext singletons;
// method-level parallelization (see MSTestSettings.cs) would otherwise race
// these tests against each other and against BatchCommandDetectorTests.
[TestClass]
[DoNotParallelize]
public class ParsingStateSnapshotTests
{
    [TestMethod]
    public void Restore_PutsSemanticContextFieldsBack()
    {
        ContextManager.Current.CurrentProject = "P";
        ContextManager.Current.CurrentClass = "C";
        ContextManager.Current.CurrentNamespace = "N";
        ContextManager.Current.CurrentFile = "F";
        ContextManager.Current.LastEntity = "E";

        var snapshot = ParsingStateSnapshot.Capture();

        ContextManager.Current.CurrentProject = "changed";
        ContextManager.Current.CurrentClass = "changed";
        ContextManager.Current.CurrentNamespace = "changed";
        ContextManager.Current.CurrentFile = "changed";
        ContextManager.Current.LastEntity = "changed";

        snapshot.Restore();

        Assert.AreEqual("P", ContextManager.Current.CurrentProject);
        Assert.AreEqual("C", ContextManager.Current.CurrentClass);
        Assert.AreEqual("N", ContextManager.Current.CurrentNamespace);
        Assert.AreEqual("F", ContextManager.Current.CurrentFile);
        Assert.AreEqual("E", ContextManager.Current.LastEntity);
    }

    [TestMethod]
    public void Restore_PutsSessionContextFieldsBack()
    {
        SessionContext.CurrentProject = "P";
        SessionContext.CurrentClass = "C";
        SessionContext.CurrentNamespace = "N";

        var snapshot = ParsingStateSnapshot.Capture();

        SessionContext.CurrentProject = "changed";
        SessionContext.CurrentClass = "changed";
        SessionContext.CurrentNamespace = "changed";

        snapshot.Restore();

        Assert.AreEqual("P", SessionContext.CurrentProject);
        Assert.AreEqual("C", SessionContext.CurrentClass);
        Assert.AreEqual("N", SessionContext.CurrentNamespace);
    }

    [TestMethod]
    public void Restore_RestoresNullsAsNull()
    {
        ContextManager.Current.LastEntity = null;
        SessionContext.CurrentClass = null;

        var snapshot = ParsingStateSnapshot.Capture();

        ContextManager.Current.LastEntity = "set";
        SessionContext.CurrentClass = "set";

        snapshot.Restore();

        Assert.IsNull(ContextManager.Current.LastEntity);
        Assert.IsNull(SessionContext.CurrentClass);
    }
}
