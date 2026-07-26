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
    // NOT Unknown, and this is the bug Task 11 fixes: CommandFront.cs:58 runs
    // KeywordCorrector.Correct before any recognition, and `validate` is Levenshtein
    // distance 2 from `validator` with a length delta of 1 — an "unambiguous
    // correction". The rewritten "validator project" then hits the \bvalidator\b
    // guard at CommandFront.cs:111. Any input containing `validate` is rerouted to
    // generate-validator today.
    [TestMethod]
    public void Validate_project_is_swallowed_by_the_validator_guard()
    {
        Assert.AreEqual(
            IntentType.GenerateValidator,
            CommandFront.Parse("validate project", "").Intent);
        Assert.IsFalse(CommandHandlingService.IsSupported(IntentType.ValidateProject));
    }

}
