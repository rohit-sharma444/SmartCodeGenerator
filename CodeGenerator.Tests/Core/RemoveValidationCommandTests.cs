using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class RemoveValidationCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ValidationRegistry.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_removeval_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, "Employee.cs"),
            "namespace Shop { public class Employee { public string Name { get; set; }\n"
            + "  public int Age { get; set; } } }");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ValidationRegistry.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
        return CommandFront.Run(input, ref project, [], _workspace, _ => { });
    }

    private string RunAndCapture(string input)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { Run(input); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    // Two rules on Name, one on Age — the order `show validation` numbers them in.
    private void AddThreeRules()
    {
        Assert.IsTrue(Run("add validation required on Name in Employee"));
        Assert.IsTrue(Run("add validation length must be between 2 and 50 on Name in Employee"));
        Assert.IsTrue(Run("add validation must be between 18 and 65 on Age in Employee"));
        Assert.HasCount(3, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    private static IReadOnlyList<string> RuleProperties() =>
        ValidationRegistry.Get("Employee", ValidationCategory.Data).Select(r => r.Properties[0]).ToList();

    [TestMethod]
    [DataRow("remove validation from Employee.Name", "Name", null)]
    [DataRow("remove validation on Name in Employee", "Name", null)]
    [DataRow("delete validation on Name of Employee", "Name", null)]
    [DataRow("remove validation 2 from Employee", "", 2)]
    public void Parses_a_remove(string input, string property, int? number)
    {
        var command = CommandFront.Parse(input, _workspace);

        Assert.AreEqual(IntentType.RemoveValidation, command.Intent);
        Assert.AreEqual("Employee", command.ClassName);
        Assert.AreEqual(property, command.PropertyName);
        Assert.AreEqual(number, command.RuleNumber);
    }

    [TestMethod]
    public void Show_validation_numbers_the_rules()
    {
        AddThreeRules();

        var output = RunAndCapture("show validation Employee");

        StringAssert.Contains(output, "1. ");
        StringAssert.Contains(output, "3. ");
    }

    [TestMethod]
    public void Removes_every_rule_on_a_field()
    {
        AddThreeRules();

        Assert.IsTrue(Run("remove validation from Employee.Name"));

        CollectionAssert.AreEqual(new[] { "Age" }, RuleProperties().ToArray());
    }

    [TestMethod]
    public void Removes_one_rule_by_its_number()
    {
        AddThreeRules();

        Assert.IsTrue(Run("remove validation 2 from Employee"));

        CollectionAssert.AreEqual(new[] { "Name", "Age" }, RuleProperties().ToArray());
    }

    [TestMethod]
    public void Refuses_a_number_that_is_not_listed()
    {
        AddThreeRules();

        Assert.IsFalse(Run("remove validation 4 from Employee"));
        Assert.IsFalse(Run("remove validation 0 from Employee"));

        Assert.HasCount(3, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void Refuses_a_field_with_no_rules()
    {
        AddThreeRules();

        Assert.IsFalse(Run("remove validation from Employee.Email"));

        Assert.HasCount(3, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void A_bare_class_removes_nothing()
    {
        AddThreeRules();

        Assert.IsFalse(Run("remove validation from Employee"));

        Assert.HasCount(3, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void Removing_a_data_rule_leaves_business_rules_alone()
    {
        File.WriteAllText(Path.Combine(_workspace, "Booking.cs"),
            "namespace Shop { public class Booking { public System.DateTime StartDate { get; set; }\n"
            + "  public System.DateTime EndDate { get; set; } } }");
        Assert.IsTrue(Run("add business validation EndDate must be after StartDate in Booking"));
        Assert.IsTrue(Run("add validation required on EndDate in Booking"));

        Assert.IsTrue(Run("remove validation 1 from Booking"));

        Assert.IsEmpty(ValidationRegistry.Get("Booking", ValidationCategory.Data));
        Assert.HasCount(1, ValidationRegistry.Get("Booking", ValidationCategory.Business));
    }

    [TestMethod]
    public void Deleting_a_property_removes_its_rules()
    {
        AddThreeRules();

        Assert.IsTrue(Run("remove property Name from Employee"));

        CollectionAssert.AreEqual(new[] { "Age" }, RuleProperties().ToArray());
    }

    [TestMethod]
    public void Renaming_a_property_warns_about_rules_still_naming_it()
    {
        AddThreeRules();

        var output = RunAndCapture("rename property Name to FullName in Employee");

        StringAssert.Contains(output, "2 validation rule(s) still name Name");
        // Warned about, not rewritten or dropped.
        Assert.HasCount(3, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void Renaming_a_property_with_no_rules_does_not_warn()
    {
        var output = RunAndCapture("rename property Name to FullName in Employee");

        Assert.DoesNotContain("still name", output, output);
    }

    [TestMethod]
    public void Removes_a_business_rule_by_its_number()
    {
        File.WriteAllText(Path.Combine(_workspace, "Booking.cs"),
            "namespace Shop { public class Booking { public System.DateTime StartDate { get; set; }\n"
            + "  public System.DateTime EndDate { get; set; } } }");
        Assert.IsTrue(Run("add business validation EndDate must be after StartDate in Booking"));
        Assert.IsTrue(Run("add validation required on EndDate in Booking"));

        var command = CommandFront.Parse("remove business validation 1 from Booking", _workspace);
        Assert.AreEqual(IntentType.RemoveBusinessValidation, command.Intent);

        Assert.IsTrue(Run("remove business validation 1 from Booking"));

        Assert.IsEmpty(ValidationRegistry.Get("Booking", ValidationCategory.Business));
        Assert.HasCount(1, ValidationRegistry.Get("Booking", ValidationCategory.Data));
    }
}
