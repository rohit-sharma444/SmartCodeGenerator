using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// Baseline for the three commands this branch makes work. Two are unrecognized
/// (Unknown); the third is worse — `validate project` is actively mis-routed to
/// generate-validator by the keyword corrector. CommandFront.Parse resolves intent
/// from LiteralCommandTable, its own regex guards, and NlpCommandMapper's table;
/// IntentRepository's rules do not drive recognition.
///
/// Each method here is deleted by the task that changes the corresponding
/// behaviour, and replaced by a positive assertion.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RecognitionBaselineTests
{
    [TestMethod]
    public void Validate_project_is_not_recognized_yet()
    {
        Assert.AreEqual(IntentType.Unknown, CommandFront.Parse("validate project", "").Intent);
        Assert.IsFalse(CommandHandlingService.IsSupported(IntentType.ValidateProject));
    }

}
