using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// The commands that name one class (list/show properties or methods, show, analyze, delete,
/// restore and rename class, add method) are recognised with the class name in any case, and
/// the name arrives as the class declares it.
/// </summary>
/// <remarks>
/// They went to the mapper, which sees a lower-case name as part of the target: "show class
/// customer" was "Command not recognized". "list methods of Employee" took the word "of" as the
/// class, and the documented "show methods Customer" found the class ''.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ClassTargetGuardTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_ctg_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_workspace, "Shop"));
        foreach (var name in new[] { "Customer", "Employee", "OrderLine", "Circle" })
            File.WriteAllText(Path.Combine(_workspace, "Shop", name + ".cs"),
                $"namespace Shop;\npublic class {name}\n{{\n}}\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    [DataRow("list methods of Employee", IntentType.ListMethods, "Employee")]
    [DataRow("show methods Customer", IntentType.ListMethods, "Customer")]
    [DataRow("list methods Customer", IntentType.ListMethods, "Customer")]
    public void The_member_list_forms_take_the_class_name(string sentence, IntentType intent, string className)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(intent, command.Intent, sentence);
        Assert.AreEqual(className, command.ClassName, sentence);
    }

    [TestMethod]
    [DataRow("list properties customer", IntentType.ListProperties, "Customer")]
    [DataRow("list properties of customer", IntentType.ListProperties, "Customer")]
    [DataRow("show properties customer", IntentType.ListProperties, "Customer")]
    [DataRow("show properties of employee", IntentType.ListProperties, "Employee")]
    [DataRow("show methods customer", IntentType.ListMethods, "Customer")]
    [DataRow("list methods of orderline", IntentType.ListMethods, "OrderLine")]
    public void A_lower_case_class_name_is_recognised_as_declared(string sentence, IntentType intent, string className)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(intent, command.Intent, sentence);
        Assert.AreEqual(className, command.ClassName, sentence);
    }
}
