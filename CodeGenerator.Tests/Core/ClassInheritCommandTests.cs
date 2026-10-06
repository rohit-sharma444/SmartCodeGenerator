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
        StringAssert.Contains(code, "class Order : AuditableBase, IAuditable");
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

    private string WriteOrder(string code)
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file, code);
        return file;
    }

    // Always inserted at index 0: Order inherits AuditableBase, then Order extends IRepository,
    // wrote class Order : IRepository, AuditableBase (CS1722).
    [TestMethod]
    public void An_interface_added_after_a_base_class_goes_after_it()
    {
        var file = WriteOrder("namespace Shop { public interface IRepository { } public class AuditableBase { } "
            + "public class Order { } }");

        Assert.IsTrue(Run("Order inherits AuditableBase"));
        Assert.IsTrue(Run("Order extends IRepository"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : AuditableBase, IRepository");
        AssertCompiles(code);
    }

    [TestMethod]
    // derives from Missing was written as it was (CS0246) and said "now inherits".
    public void An_unknown_type_is_refused_and_the_file_left_alone()
    {
        const string code = "namespace Shop { public class Order { } }";
        var file = WriteOrder(code);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", "Missing", out var message), message);

        StringAssert.Contains(message, "Missing");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    // Order inherits Order was accepted (CS0146).
    public void A_class_cannot_inherit_itself_or_its_own_subclass()
    {
        const string code = "namespace Shop { public class Order { } public class SpecialOrder : Order { } }";
        var file = WriteOrder(code);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", "Order", out var self), self);
        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", "SpecialOrder", out var cycle), cycle);

        StringAssert.Contains(cycle, "SpecialOrder");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    // A second base class was inserted in front of the first (CS1721).
    public void A_second_base_class_is_refused()
    {
        const string code = "namespace Shop { public class AuditableBase { } public class Entity { } "
            + "public class Order : AuditableBase { } }";
        var file = WriteOrder(code);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", "Entity", out var message), message);

        StringAssert.Contains(message, "AuditableBase");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    public void A_framework_interface_is_added_and_its_missing_members_named()
    {
        var file = WriteOrder("namespace Shop { public class Order { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", "IDisposable", out var message), message);

        StringAssert.Contains(File.ReadAllText(file), "class Order : IDisposable");
        StringAssert.Contains(message, "implements");
        StringAssert.Contains(message, "Dispose");
    }

    [TestMethod]
    public void A_sealed_class_cannot_be_a_base()
    {
        const string code = "namespace Shop { public sealed class Locked { } public class Order { } }";
        var file = WriteOrder(code);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", "Locked", out var message), message);

        StringAssert.Contains(message, "sealed");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    // `create a Customer class that inherits X` resolves to Unknown today: NlpCommandMapper
    // never tags "that inherits" as an Action entity, so no (verb, target) row matches. That
    // is a pre-existing gap, out of scope here, and this test deliberately does NOT pin
    // Unknown as the expected value -- if someone correctly closes that gap so the phrase
    // resolves to CreateClass, this test must not fail on their behalf. The actual invariant
    // owned by this test is narrower: the new ClassInheritRe guard must not claim the
    // phrasing -- if it ever loosened enough to match a sentence starting with `create`,
    // this would come back ClassInherit and fail.
    [TestMethod]
    public void Does_not_claim_the_create_class_phrasing()
    {
        Assert.AreNotEqual(
            IntentType.ClassInherit,
            CommandFront.Parse("create a Customer class that inherits AuditableBase", _workspace).Intent);
    }
}
