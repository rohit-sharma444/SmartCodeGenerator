using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class DataValidationCommandTests
{
    [TestInitialize]
    public void Reset()
    {
        ValidationRegistry.Clear();
        LearnedIntentStore.UseInMemory();
    }

    [TestMethod]
    public void Add_validation_registers_a_data_rule()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddValidation,
            ClassName = "Employee",
            PropertyName = "Name",
            ValidationText = "Name is required",
            OriginalSentence = "add validation should not be blank on Name in Employee"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsTrue(ok);
        Assert.HasCount(1, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void Add_validation_reports_failure_on_unparseable_text()
    {
        var command = new ParsedCommand
        {
            Intent = IntentType.AddValidation,
            ClassName = "Employee",
            ValidationText = "flibbertigibbet",
            OriginalSentence = "add validation flibbertigibbet"
        };
        ProjectMetaData? project = null;

        var ok = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsFalse(ok);
    }
}
