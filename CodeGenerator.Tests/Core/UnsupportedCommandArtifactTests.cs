using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// `// Unsupported command` is what CodeGenerationEngine.Generate returns for any
/// intent it does not handle. It is non-empty, so GenerateAndSave's
/// IsNullOrWhiteSpace guard would have written it to a .cs file. No routed intent
/// may reach it.
/// </summary>
[TestClass]
[DoNotParallelize] // ExecuteCommand reads and writes SessionContext.
public sealed class UnsupportedCommandArtifactTests
{
    [TestMethod]
    [DataRow(IntentType.CreateService)]
    [DataRow(IntentType.CreateRepository)]
    [DataRow(IntentType.CreateController)]
    [DataRow(IntentType.CreateApi)]
    public void The_architecture_intents_are_not_routed_into_the_code_generator(IntentType intent)
    {
        var code = NaturalLanguageCodeEngine.ExecuteCommand(
            new ParsedCommand { Intent = intent, ClassName = "Customer" },
            Path.GetTempPath());

        Assert.DoesNotContain(code, "// Unsupported command",
            $"{intent} still reaches CodeGenerationEngine.Generate, which returns a "
            + "comment that would be written to disk as a class file.");
    }
}
