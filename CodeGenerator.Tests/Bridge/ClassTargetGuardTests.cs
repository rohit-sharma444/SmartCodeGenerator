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
    [DataRow("show class customer", IntentType.ShowClass, "Customer")]
    [DataRow("show class orderline", IntentType.ShowClass, "OrderLine")]
    [DataRow("delete class customer", IntentType.DeleteClass, "Customer")]
    [DataRow("remove class orderline", IntentType.DeleteClass, "OrderLine")]
    [DataRow("restore class customer", IntentType.RestoreClass, "Customer")]
    [DataRow("recover class customer", IntentType.RestoreClass, "Customer")]
    [DataRow("recover class Customer", IntentType.RestoreClass, "Customer")]
    [DataRow("analyze class customer", IntentType.AnalyzeClass, "Customer")]
    [DataRow("analyse class customer", IntentType.AnalyzeClass, "Customer")]
    public void A_lower_case_class_name_is_recognised_as_declared(string sentence, IntentType intent, string className)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(intent, command.Intent, sentence);
        Assert.AreEqual(className, command.ClassName, sentence);
    }

    [TestMethod]
    public void Rename_class_takes_a_lower_case_old_name()
    {
        var command = CommandFront.Parse("rename class customer to Client", _workspace);

        Assert.AreEqual(IntentType.RenameClass, command.Intent);
        Assert.AreEqual("Customer", command.ClassName);
        Assert.AreEqual("Client", command.NewClassName);
    }

    // "rename class Staff as Worker" said "A new class name is required."
    [TestMethod]
    [DataRow("rename class Customer as Client")]
    [DataRow("rename class customer as Client")]
    public void Rename_class_takes_as(string sentence)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.RenameClass, command.Intent, sentence);
        Assert.AreEqual("Customer", command.ClassName, sentence);
        Assert.AreEqual("Client", command.NewClassName, sentence);
    }

    [TestMethod]
    [DataRow("add method save to Circle", "Save", "void", "Circle")]
    [DataRow("add method save to circle", "Save", "void", "Circle")]
    [DataRow("add method Area returns double to Circle", "Area", "double", "Circle")]
    public void Add_method_takes_a_lower_case_name(string sentence, string method, string returns, string className)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.CreateMethod, command.Intent, sentence);
        Assert.AreEqual(method, command.MethodName, sentence);
        Assert.AreEqual(returns, command.ReturnType, sentence);
        Assert.AreEqual(className, command.ClassName, sentence);
    }

    [TestMethod]
    public void A_lower_case_method_is_added_and_the_class_compiles()
    {
        ProjectMetaData? project = null;
        Assert.IsTrue(CommandFront.Run("add method save to circle", ref project, [], _workspace, _ => { }));

        var code = File.ReadAllText(Path.Combine(_workspace, "Shop", "Circle.cs"));
        StringAssert.Contains(code, "Save(");
        var result = Compiler.Compile(code);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    // Ordinary sentences that share a word with these commands stay what they were.
    [TestMethod]
    [DataRow("show me the class list later", IntentType.ShowClass)]
    [DataRow("list properties of the customer that are required", IntentType.ListProperties)]
    [DataRow("delete class customer and employee", IntentType.DeleteClass)]
    [DataRow("I want to delete class customer", IntentType.DeleteClass)]
    [DataRow("add method save to circle when it is ready", IntentType.CreateMethod)]
    public void An_ordinary_sentence_is_not_made_a_command(string sentence, IntentType notThis)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.IsFalse(command.Intent == notThis && command.Confidence >= 1.0,
            $"{sentence} became {command.Intent} on {command.ClassName}");
    }
}
