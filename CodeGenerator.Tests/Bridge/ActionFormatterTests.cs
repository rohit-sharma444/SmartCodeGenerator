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
    public void Format_RenameClass_ShowsBothNames()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.RenameClass,
                new ParsedCommand { ClassName = "Old", NewClassName = "New" }));

        StringAssert.Contains(result, "Old");
        StringAssert.Contains(result, "New");
    }

    [TestMethod]
    public void Format_PropertyIntent_ShowsNameAndType()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateProperty,
                new ParsedCommand { PropertyName = "Email", PropertyType = "string" }));

        StringAssert.Contains(result, "Email");
        StringAssert.Contains(result, "string");
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
