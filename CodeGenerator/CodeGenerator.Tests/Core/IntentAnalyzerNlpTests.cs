using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class IntentAnalyzerNlpTests
{
    // --- Regression: canonical commands must keep resolving as before. ---
    [DataTestMethod]
    [DataRow("create a Customer class", IntentType.CreateClass)]
    [DataRow("add method Save to Order", IntentType.CreateMethod)]
    [DataRow("list projects", IntentType.ListProjects)]
    [DataRow("delete class Foo", IntentType.DeleteClass)]
    [DataRow("rename class A to B", IntentType.RenameClass)]
    [DataRow("create a service for Payment", IntentType.CreateService)]
    public void Detect_CanonicalCommands_ResolveToExpectedIntent(
        string input, IntentType expected)
    {
        Assert.AreEqual(expected, CodeGenerationEngine.Core.IntentAnalyzer.Detect(input).Intent);
    }

    // --- Value-add: a terse command that scores below the keyword threshold
    // (verb present, no target word => 30 < 40 => Unknown without the signal).
    // NLE recognizes "validate" (unique to ValidateProject), adding +25 which
    // lifts it above the threshold. This resolves ONLY because of the NLE signal.
    [TestMethod]
    public void Detect_TerseCommandWithKnownVerb_ResolvedViaNlpSignal()
    {
        Assert.AreEqual(
            IntentType.ValidateProject,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect("validate Foo").Intent);
    }
}
