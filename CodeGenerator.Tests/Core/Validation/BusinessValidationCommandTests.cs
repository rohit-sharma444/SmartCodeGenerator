using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

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
}
