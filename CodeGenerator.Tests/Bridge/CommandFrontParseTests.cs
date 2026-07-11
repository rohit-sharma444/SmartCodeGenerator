using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

// CommandFront.Parse for a create-class phrase drives CommandEntityExtractor.Extract,
// which writes the un-synchronized static SessionContext.CurrentClass; the assembly
// runs method-parallel, so this class is opted out to match CommandEntityExtractorTests
// and BatchCommandDetectorTests.
[TestClass]
[DoNotParallelize]
public class CommandFrontParseTests
{
    [TestMethod]
    public void Create_class_resolves_intent_and_name()
    {
        var cmd = CommandFront.Parse("create a class called Customer", "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Classes_in_project_guard_sets_intent_and_project_name()
    {
        var cmd = CommandFront.Parse("list classes in MyProject", "");
        Assert.AreEqual(IntentType.ListClassesInProject, cmd.Intent);
        Assert.AreEqual("MyProject", cmd.ProjectName);
    }

    [TestMethod]
    public void Literal_meta_command_bypasses_the_mapper()
    {
        var cmd = CommandFront.Parse("show learning", "");
        Assert.AreEqual(IntentType.ShowLearning, cmd.Intent);
    }

    [TestMethod]
    public void Unrecognized_input_is_Unknown()
    {
        var cmd = CommandFront.Parse("juggle the flamingo", "");
        Assert.AreEqual(IntentType.Unknown, cmd.Intent);
    }

    [TestMethod]
    public void Repository_intent_stays_dead()
    {
        // CreateRepository has no handler; the mapper must not reach it.
        var cmd = CommandFront.Parse("create a repository for Customer", "");
        Assert.AreNotEqual(IntentType.CreateRepository, cmd.Intent);
    }

    [TestMethod]
    public void Use_project_guard_sets_project_name()
    {
        var cmd = CommandFront.Parse("use project MyApp", "");
        Assert.AreEqual(IntentType.UseProject, cmd.Intent);
        Assert.AreEqual("MyApp", cmd.ProjectName);
    }

    [TestMethod]
    public void Add_validation_with_args_resolves_intent_and_fields()
    {
        var cmd = CommandFront.Parse(
            "add validation Email should not be blank on Email in Customer", "");
        Assert.AreEqual(IntentType.AddValidation, cmd.Intent);
        Assert.AreEqual("Email", cmd.PropertyName);
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Show_validation_with_args_resolves_intent_and_class_name()
    {
        var cmd = CommandFront.Parse("show validation for Customer", "");
        Assert.AreEqual(IntentType.ShowValidation, cmd.Intent);
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Generate_validator_resolves_intent_and_class_name()
    {
        var cmd = CommandFront.Parse("generate validator for Customer", "");
        Assert.AreEqual(IntentType.GenerateValidator, cmd.Intent);
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Guard_command_reports_full_confidence()
    {
        var cmd = CommandFront.Parse("use project MyApp", "");
        Assert.AreEqual(1.0, cmd.Confidence);
    }

    [TestMethod]
    public void Mapper_command_carries_a_confidence()
    {
        var cmd = CommandFront.Parse("create a class called Customer", "");
        Assert.IsTrue(cmd.Confidence > 0d, $"expected NLE confidence, got {cmd.Confidence}");
    }
}
