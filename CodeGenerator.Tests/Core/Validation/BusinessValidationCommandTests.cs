using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class BusinessValidationCommandTests
{
    [TestInitialize]
    public void Reset()
    {
        ValidationRegistry.Clear();
        LearnedIntentStore.UseInMemory();
    }

    [TestMethod]
    public void Add_business_validation_registers_a_business_rule()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddBusinessValidation,
            ClassName = "Booking",
            ValidationText = "EndDate must be after StartDate",
            OriginalSentence = "add business validation EndDate must be after StartDate in Booking"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsTrue(ok);
        Assert.HasCount(1, ValidationRegistry.Get("Booking", ValidationCategory.Business));
        Assert.IsEmpty(ValidationRegistry.Get("Booking", ValidationCategory.Data));
    }

    [TestMethod]
    public void Add_business_validation_with_blank_class_fails_cleanly()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddBusinessValidation,
            ClassName = "",
            ValidationText = "EndDate must be after StartDate",
            OriginalSentence = "add business validation EndDate must be after StartDate"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsFalse(ok);
        Assert.IsEmpty(ValidationRegistry.Get(""));
    }

    [TestMethod]
    public void Both_intents_are_supported()
    {
        Assert.IsTrue(CommandHandlingService.IsSupported(IntentType.AddBusinessValidation));
        Assert.IsTrue(CommandHandlingService.IsSupported(IntentType.ShowBusinessValidation));
    }

    // Drives "add business validation" from the raw sentence, the way the CLI does:
    // CommandFront.Parse composes ValidationText before the catalog ever sees it,
    // mirroring AddValidationPhrasingTests' end-to-end coverage for data rules.
    private static bool Run(string sentence)
    {
        var command = CommandFront.Parse(sentence, "");
        ProjectMetaData? project = null;
        return CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });
    }

    private static void AssertRegistered(string sentence, string className, string expectedDescription)
    {
        Assert.IsTrue(Run(sentence), $"Command failed: {sentence}");

        var rules = ValidationRegistry.Get(className, ValidationCategory.Business);
        Assert.HasCount(1, rules);
        Assert.AreEqual(expectedDescription, rules[0].Describe());
    }

    [TestMethod]
    public void Either_or_phrasing_registers_a_business_rule() =>
        AssertRegistered(
            "add business validation Either Email or Phone must be provided in Customer",
            "Customer", "Either Email or Phone must be provided");

    // SameAsRuleParser's pattern requires the literal article "the" ("must be the same
    // as"), same shape of defect as RelativeDateRuleParser. This is the business-side
    // half of the two parsers Finding 1 asked to be proven, not just assumed.
    [TestMethod]
    public void Same_as_phrasing_registers_a_business_rule() =>
        AssertRegistered(
            "add business validation BillingPostcode must be the same as ShippingPostcode in Customer",
            "Customer", "BillingPostcode must be the same as ShippingPostcode");
}
