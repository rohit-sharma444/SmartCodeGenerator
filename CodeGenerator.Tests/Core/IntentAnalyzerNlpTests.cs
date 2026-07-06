using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class IntentAnalyzerNlpTests
{
    // Regression: canonical commands still resolve, with the NLE verb injected
    // exactly as the runtime pipeline supplies it.
    [DataTestMethod]
    [DataRow("create a Customer class", "create", IntentType.CreateClass)]
    [DataRow("add method Save to Order", "add", IntentType.CreateMethod)]
    [DataRow("list projects", "list", IntentType.ListProjects)]
    [DataRow("delete class Foo", "delete", IntentType.DeleteClass)]
    [DataRow("rename class A to B", "rename", IntentType.RenameClass)]
    [DataRow("create a service for Payment", "create", IntentType.CreateService)]
    [DataRow("remove the class Foo", "remove", IntentType.DeleteClass)]
    [DataRow("add property Email to Customer", "add", IntentType.CreateProperty)]
    [DataRow("generate a repository for Order", "generate", IntentType.CreateRepository)]
    public void Detect_CanonicalCommands_WithInjectedVerb_ResolveToExpectedIntent(
        string input, string nlpAction, IntentType expected)
    {
        Assert.AreEqual(expected,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect(input, nlpAction).Intent);
    }

    // Value-add: "validate Foo" scores 30 on keywords (< 40 => Unknown); the
    // injected "validate" verb (+25, unique to ValidateProject) lifts it past 40.
    [TestMethod]
    public void Detect_TerseCommandWithInjectedVerb_ResolvedViaNlpSignal()
    {
        Assert.AreEqual(IntentType.ValidateProject,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect("validate Foo", "validate").Intent);
    }

    // Without the signal, the same terse command stays Unknown -> proves the
    // injected verb is what does the work.
    [TestMethod]
    public void Detect_TerseCommandWithoutSignal_IsUnknown()
    {
        Assert.AreEqual(IntentType.Unknown,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect("validate Foo", null).Intent);
    }

    // A null signal must not disturb an already-correct keyword-only result.
    [TestMethod]
    public void Detect_CanonicalCommandWithoutSignal_StillResolves()
    {
        Assert.AreEqual(IntentType.CreateClass,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect("create a Customer class", null).Intent);
    }
}
