using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class SemanticContextSnapshotTests
{
    [TestMethod]
    public void RestoreTo_PutsAllFiveFieldsBack()
    {
        var context = new SemanticContext
        {
            CurrentProject = "P",
            CurrentClass = "C",
            CurrentNamespace = "N",
            CurrentFile = "F",
            LastEntity = "E"
        };

        var snapshot = SemanticContextSnapshot.Capture(context);

        context.CurrentProject = "changed";
        context.CurrentClass = "changed";
        context.CurrentNamespace = "changed";
        context.CurrentFile = "changed";
        context.LastEntity = "changed";

        snapshot.RestoreTo(context);

        Assert.AreEqual("P", context.CurrentProject);
        Assert.AreEqual("C", context.CurrentClass);
        Assert.AreEqual("N", context.CurrentNamespace);
        Assert.AreEqual("F", context.CurrentFile);
        Assert.AreEqual("E", context.LastEntity);
    }

    [TestMethod]
    public void Capture_TakesACopy_NotALiveReference()
    {
        var context = new SemanticContext { LastEntity = "original" };

        var snapshot = SemanticContextSnapshot.Capture(context);
        context.LastEntity = "mutated";
        snapshot.RestoreTo(context);

        Assert.AreEqual("original", context.LastEntity);
    }

    [TestMethod]
    public void RestoreTo_RestoresNullsAsNull()
    {
        var context = new SemanticContext();

        var snapshot = SemanticContextSnapshot.Capture(context);
        context.LastEntity = "set";
        snapshot.RestoreTo(context);

        Assert.IsNull(context.LastEntity);
    }
}
