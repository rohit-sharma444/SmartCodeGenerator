using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class ClassInheritCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_inherit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
        return CommandFront.Run(input, ref project, [], _workspace, _ => { });
    }

    private static void AssertCompiles(string code)
    {
        var result = Compiler.Compile(code);
        Assert.IsTrue(result.Success,
            "Edited file does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Recognizes_the_command_and_adds_the_base_type()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public class AuditableBase { } public class Order { public int Id { get; set; } } }");

        Assert.IsTrue(Run("Order inherits AuditableBase"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, ": AuditableBase");
        AssertCompiles(code);
    }

    // Appending put the base class after the interface, which is CS1722.
    [TestMethod]
    public void Keeps_an_existing_interface_and_puts_the_base_class_first()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public interface IAuditable { } public class AuditableBase { } "
            + "public class Order : IAuditable { public int Id { get; set; } } }");

        Assert.IsTrue(Run("Order inherits AuditableBase"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "IAuditable");
        AssertCompiles(code);
    }

    // The service took the first class declaration in the file, not the named one.
    [TestMethod]
    public void Targets_the_named_class_not_the_first_in_the_file()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public class AuditableBase { } public class OrderLine { } "
            + "public class Order { } }");

        Assert.IsTrue(Run("Order inherits AuditableBase"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : AuditableBase");
        StringAssert.DoesNotMatch(code, new System.Text.RegularExpressions.Regex(@"class OrderLine\s*:"));
        AssertCompiles(code);
    }

    [TestMethod]
    public void Reports_failure_when_the_base_type_is_already_present()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public class AuditableBase { } public class Order : AuditableBase { } }");

        Assert.IsFalse(Run("Order inherits AuditableBase"),
            "Nothing changed, so the command must report failure.");
    }

    [TestMethod]
    public void Reports_failure_when_the_class_is_missing()
    {
        Assert.IsFalse(Run("Ghost inherits AuditableBase"));
    }

    // `create a Customer class that inherits X` resolves to Unknown today: NlpCommandMapper
    // never tags "that inherits" as an Action entity, so no (verb, target) row matches. That
    // is a pre-existing gap, out of scope here. The point of this test is that the new
    // ClassInheritRe guard must not claim the phrasing either -- if it ever loosened enough
    // to match a sentence starting with `create`, this would come back ClassInherit and fail.
    [TestMethod]
    public void Does_not_claim_the_create_class_phrasing()
    {
        Assert.AreEqual(
            IntentType.Unknown,
            CommandFront.Parse("create a Customer class that inherits AuditableBase", _workspace).Intent);
    }
}
