using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class ActionFormatterTests
{
    private static DetectedAction Actionable(IntentType intent, ParsedCommand command)
        => new("some sentence", intent, 0.9, command, null);

    [TestMethod]
    public void Format_ProjectIntent_ShowsProjectName()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateProject, new ParsedCommand { ProjectName = "Foo" }));

        StringAssert.StartsWith(result, "CreateProject");
        StringAssert.Contains(result, "Foo");
    }

    [TestMethod]
    public void Format_ClassIntent_ShowsClassName()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateClass, new ParsedCommand { ClassName = "Customer" }));

        StringAssert.StartsWith(result, "CreateClass");
        StringAssert.Contains(result, "Customer");
    }

    [TestMethod]
    public void Format_RenameClass_ShowsOldNameThenNewName()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.RenameClass,
                new ParsedCommand { ClassName = "Old", NewClassName = "New" }));

        // Direction matters: "New -> Old" would be actively misleading in a
        // review list, and a bare Contains on each name would not catch it.
        StringAssert.Contains(result, "Old -> New");
    }

    [TestMethod]
    public void Format_PropertyIntent_ShowsNameThenType()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateProperty,
                new ParsedCommand { PropertyName = "Email", PropertyType = "string" }));

        StringAssert.Contains(result, "Email : string");
    }

    // The preview said "CreateProperty Email : string" and never which class it goes to.
    [TestMethod]
    public void Format_PropertyIntent_ShowsTheClassItGoesTo()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateProperty,
                new ParsedCommand { ClassName = "Customer", PropertyName = "Email", PropertyType = "string" }));

        StringAssert.Contains(result, "Customer.Email : string");
    }

    // A bare "AddValidation" said nothing about what would be added, or where.
    [TestMethod]
    [DataRow(IntentType.AddValidation)]
    [DataRow(IntentType.AddBusinessValidation)]
    public void Format_ValidationIntent_ShowsTheClassAndTheRule(IntentType intent)
    {
        var result = ActionFormatter.Format(
            Actionable(intent, new ParsedCommand { ClassName = "Customer", ValidationText = "Name is required" }));

        StringAssert.Contains(result, "Customer: Name is required");
    }

    [TestMethod]
    [DataRow(IntentType.CreateInterface, "ICustomer")]
    [DataRow(IntentType.CreateService, "PaymentService")]
    [DataRow(IntentType.CreateRepository, "OrderRepository")]
    [DataRow(IntentType.CreateController, "HomeController")]
    [DataRow(IntentType.CreateApi, "BillingApi")]
    public void Format_ArchitectureIntent_ShowsItsDedicatedName(IntentType intent, string name)
    {
        var command = new ParsedCommand();
        switch (intent)
        {
            case IntentType.CreateInterface: command.InterfaceName = name; break;
            case IntentType.CreateService: command.ServiceName = name; break;
            case IntentType.CreateRepository: command.RepositoryName = name; break;
            case IntentType.CreateController: command.ControllerName = name; break;
            case IntentType.CreateApi: command.ApiName = name; break;
        }

        var result = ActionFormatter.Format(Actionable(intent, command));

        StringAssert.StartsWith(result, intent.ToString());
        StringAssert.Contains(result, name);
    }

    [TestMethod]
    public void Format_ArchitectureIntentWithNoName_ShowsIntentNameAlone()
    {
        // The dedicated name fields are nullable; a null must not crash or print "".
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateInterface, new ParsedCommand { InterfaceName = null }));

        Assert.AreEqual("CreateInterface", result.Trim());
    }

    [TestMethod]
    public void Format_MethodIntent_ShowsMethodName()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateMethod, new ParsedCommand { MethodName = "Save" }));

        StringAssert.Contains(result, "Save");
    }

    [TestMethod]
    public void Format_IntentWithNoSalientField_ShowsIntentNameAlone()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.ListProjects, new ParsedCommand()));

        Assert.AreEqual("ListProjects", result.Trim());
    }

    [TestMethod]
    public void Format_Unactionable_ShowsSentenceAndNote()
    {
        var action = new DetectedAction(
            "the big red car", IntentType.Unknown, 0, null, "not understood");

        var result = ActionFormatter.Format(action);

        StringAssert.Contains(result, "the big red car");
        StringAssert.Contains(result, "not understood");
    }
}
