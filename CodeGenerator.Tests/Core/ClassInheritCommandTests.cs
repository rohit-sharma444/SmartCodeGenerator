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

    private string Write(string name, string code)
    {
        var file = Path.Combine(_workspace, name + ".cs");
        File.WriteAllText(file, code);
        return file;
    }

    private const string Auditable = "public interface IAuditable { string Who { get; set; } void Stamp(); } ";

    // Inherits used to add the interface and only warn about its members, so the project no
    // longer built (CS0535).
    [TestMethod]
    public void Implements_adds_the_interface_and_stubs_its_members()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order { public int Id { get; set; } } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : IAuditable");
        StringAssert.Contains(code, "public string Who { get; set; }");
        StringAssert.Contains(code, "public void Stamp() => throw new System.NotImplementedException();");
        StringAssert.Contains(message, "now implements interface 'IAuditable'");
        StringAssert.Contains(message, "Added Who, Stamp, which it lacked");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Implements_is_recognised()
    {
        var command = CommandFront.Parse("Order implements IAuditable", _workspace);

        Assert.AreEqual(IntentType.ClassInherit, command.Intent);
        Assert.AreEqual("Order", command.ClassName);
        Assert.HasCount(1, command.Interfaces);
        Assert.AreEqual("IAuditable", command.Interfaces[0]);
        Assert.IsTrue(string.IsNullOrEmpty(command.BaseClass));
    }

    [TestMethod]
    public void Implements_reads_a_list_with_commas_and_and()
    {
        var command = CommandFront.Parse("Order implements IAuditable, IExportable and ITracked", _workspace);

        Assert.AreEqual(IntentType.ClassInherit, command.Intent);
        CollectionAssert.AreEqual(new[] { "IAuditable", "IExportable", "ITracked" }, command.Interfaces);
    }

    [TestMethod]
    public void Implements_reads_a_generic_name()
    {
        var command = CommandFront.Parse("Order implements IComparable<Order>, IAuditable", _workspace);

        Assert.AreEqual(IntentType.ClassInherit, command.Intent);
        CollectionAssert.AreEqual(new[] { "IComparable<Order>", "IAuditable" }, command.Interfaces);
    }

    [TestMethod]
    public void Implements_and_a_second_command_is_two_commands()
    {
        Assert.IsNotNull(CommandFront.TwoCommands("Order implements IAuditable and add method Save"));
    }

    [TestMethod]
    public void Run_implements_end_to_end()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order { public int Id { get; set; } } }");

        Assert.IsTrue(Run("Order implements IAuditable"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : IAuditable");
        StringAssert.Contains(code, "public void Stamp() => throw new System.NotImplementedException();");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Implements_two_interfaces_in_one_command()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public interface IExportable { string Export(); } "
            + "public class Order { public int Id { get; set; } } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable", "IExportable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : IAuditable, IExportable");
        StringAssert.Contains(code, "public string Export() => throw new System.NotImplementedException();");
        StringAssert.Contains(message, "IExportable");
        AssertCompiles(code);
    }

    [TestMethod]
    public void A_bad_name_in_the_list_leaves_the_file_unchanged()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order { public int Id { get; set; } } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable", "INope"], interfacesOnly: true, out var message), message);

        StringAssert.Contains(message, "INope");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Implements_refuses_a_class()
    {
        const string code = "namespace Shop { public class AuditableBase { } public class Order { } }";
        var file = WriteOrder(code);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", ["AuditableBase"], interfacesOnly: true, out var message), message);

        StringAssert.Contains(message, "'AuditableBase' is a class, not an interface; use 'Order inherits AuditableBase'.");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    public void Inherits_an_abstract_base_adds_overrides()
    {
        var file = Write("Sq", "namespace Shop { public abstract class Shape { public abstract double Area(); } public class Sq { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Sq", "Shape", out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Sq : Shape");
        StringAssert.Contains(code, "public override double Area() => throw new System.NotImplementedException();");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Struct_implements_an_interface()
    {
        var file = Write("Point", "namespace Shop { public interface IShape { double Area(); } public struct Point { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Point", ["IShape"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "struct Point : IShape");
        StringAssert.Contains(message, "Struct 'Point' now implements interface 'IShape'.");
        AssertCompiles(code);
    }

    [TestMethod]
    public void A_struct_cannot_inherit_a_class()
    {
        const string code = "namespace Shop { public class Shape { } public struct Point { } }";
        var file = Write("Point", code);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Point", "Shape", out var message), message);

        StringAssert.Contains(message, "a struct can only implement interfaces");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    public void A_record_inherits_only_a_record()
    {
        const string code = "namespace Shop { public class Entity { } public record Base(int Id); public record Order(int Id) : Base(Id); "
            + "public record Line(int Id); }";
        var file = WriteOrder(code);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Line", "Entity", out var message), message);

        StringAssert.Contains(message, "a record can inherit only from a record");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    public void Positional_record_without_body_gets_one()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public record Order(int Id); }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "record Order(int Id) : IAuditable");
        StringAssert.Contains(code, "public void Stamp() => throw new System.NotImplementedException();");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Stubs_keep_crlf_and_member_indentation()
    {
        var file = WriteOrder(string.Join("\r\n",
            "namespace Shop",
            "{",
            "    public interface IAuditable",
            "    {",
            "        string Who { get; set; }",
            "        void Stamp();",
            "    }",
            "",
            "    public class Order",
            "    {",
            "        public int Id { get; set; }",
            "    }",
            "}",
            ""));

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.DoesNotMatch(code, new System.Text.RegularExpressions.Regex(@"(?<!\r)\n"));
        StringAssert.Contains(code,
            "        public int Id { get; set; }\r\n"
            + "        public string Who { get; set; }\r\n"
            + "        public void Stamp() => throw new System.NotImplementedException();\r\n"
            + "    }\r\n}");
        AssertCompiles(code);
    }

    [TestMethod]
    public void Abstract_class_gets_abstract_declarations()
    {
        var file = Write("Base", "namespace Shop { " + Auditable + "public abstract class Base { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Base", ["IAuditable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public abstract void Stamp();");
        StringAssert.Contains(message, "as abstract declarations");
        AssertCompiles(code);
    }

    // A stub beside a member of the same name that does not implement the interface's one wrote
    // the name twice: CS0111 for a method, CS0102 for a property.
    [TestMethod]
    public void A_private_method_of_the_same_name_is_refused()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order { public string Who { get; set; } = \"\"; void Stamp() { } } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        StringAssert.Contains(message, "'Order' already has a member 'Stamp' that does not match IAuditable.Stamp");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void A_property_of_the_same_name_and_another_type_is_refused()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order { public int Who { get; set; } } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        StringAssert.Contains(message, "'Who'");
        StringAssert.Contains(message, "IAuditable.Who (string Who)");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void A_method_that_hides_the_abstract_one_is_refused()
    {
        var file = Write("Sq", "namespace Shop { public abstract class Shape { public abstract double Area(); } "
            + "public class Sq { public double Area() => 1; } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Sq", "Shape", out var message), message);

        StringAssert.Contains(message, "'Area'");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    // Another parameter list is an overload, not a clash: the stub is still written.
    [TestMethod]
    public void An_overload_of_the_same_name_still_gets_the_stub()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order { public void Stamp(int at) { } } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public void Stamp() => throw new System.NotImplementedException();");
        AssertCompiles(code);
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

    // A set accessor does not implement an init-only one (CS8854).
    [TestMethod]
    public void An_init_only_interface_property_gets_an_init_stub()
    {
        var file = WriteOrder("namespace Shop { public interface IKeyed { int Id { get; init; } } public class Order { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IKeyed"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public int Id { get; init; }");
        AssertCompiles(code);
    }

    // A settable auto-property in a readonly struct is CS8341.
    [TestMethod]
    public void A_readonly_struct_gets_a_get_only_property()
    {
        var file = Write("Point", "namespace Shop { public interface IShape { double Area { get; } } "
            + "public readonly struct Point { } public readonly record struct Spot(int X); }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Point", ["IShape"], interfacesOnly: true, out var m1), m1);
        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Spot", ["IShape"], interfacesOnly: true, out var m2), m2);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public double Area { get; }");
        Assert.IsFalse(code.Contains("Area { get; set; }", StringComparison.Ordinal), code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void A_readonly_struct_cannot_take_a_settable_interface_property()
    {
        var file = Write("Point", "namespace Shop { public interface INamed { string Name { get; set; } } public readonly struct Point { } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Point", ["INamed"], interfacesOnly: true, out var message), message);

        StringAssert.Contains(message, "CS8341");
        StringAssert.Contains(message, "'Point' was not changed");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    // An implicit implementation must repeat the method's constraints (CS0425).
    [TestMethod]
    public void A_constrained_generic_method_keeps_its_constraints()
    {
        var file = WriteOrder("using System; namespace Shop { public interface IStore { T Load<T>(int id) where T : class, new(); "
            + "void Sort<T>(T value) where T : struct, IComparable<T>; void Copy<T>(T value) where T : unmanaged; } "
            + "public class Order { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IStore"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public T Load<T>(int id) where T : class, new() => throw new System.NotImplementedException();");
        StringAssert.Contains(code, "public void Sort<T>(T value) where T : struct, IComparable<T> => throw");
        StringAssert.Contains(code, "public void Copy<T>(T value) where T : unmanaged => throw");
        AssertCompiles(code);
    }

    // record Line(int Id) : Base needs Base's arguments (CS1729: no constructor that takes 0
    // arguments), which the command cannot know.
    [TestMethod]
    public void A_positional_record_cannot_inherit_a_positional_record()
    {
        var file = WriteOrder("namespace Shop { public record Base(int Id); public record Line(int Id); }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Line", "Base", out var message), message);

        StringAssert.Contains(message, "CS1729");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    // CS8865: a class cannot inherit a record.
    [TestMethod]
    public void A_class_cannot_inherit_a_record()
    {
        var file = WriteOrder("namespace Shop { public record Base(int Id); public class Order { } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", "Base", out var message), message);

        StringAssert.Contains(message, "'Base' is a record, and only a record can inherit from a record.");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    // add property X to IAuditable leaves Order without X; implements IAuditable again used to
    // be refused ("already inherits"), so the stubs could not be added.
    [TestMethod]
    public void An_interface_already_listed_still_gets_the_stubs_it_lacks()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order : IAuditable { public string Who { get; set; } = \"\"; } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public void Stamp() => throw new System.NotImplementedException();");
        StringAssert.Contains(message, "Class 'Order' already implements 'IAuditable'. Added Stamp, which it lacked");
        AssertCompiles(code);
    }

    [TestMethod]
    public void An_interface_already_listed_with_nothing_missing_is_a_no_op()
    {
        var file = WriteOrder("namespace Shop { " + Auditable
            + "public class Order : IAuditable { public string Who { get; set; } = \"\"; public void Stamp() { } } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable"], interfacesOnly: true, out var message), message);

        Assert.AreEqual("Class 'Order' already implements 'IAuditable'; nothing to add.", message);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void A_list_with_one_listed_and_one_new_interface_adds_the_new_one()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public interface IExportable { string Export(); } "
            + "public class Order : IAuditable { public string Who { get; set; } = \"\"; public void Stamp() { } } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable", "IExportable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : IAuditable, IExportable");
        StringAssert.Contains(code, "public string Export() => throw new System.NotImplementedException();");
        StringAssert.Contains(message, "Class 'Order' already implements 'IAuditable'. Class 'Order' now implements interface 'IExportable'.");
        AssertCompiles(code);
    }

    // The constructed type is compared: IComparable<Customer> is not IComparable<Order>.
    [TestMethod]
    public void Another_construction_of_a_listed_generic_interface_is_added()
    {
        var file = WriteOrder("namespace Shop { public class Customer { } "
            + "public class Order : System.IComparable<Customer> { public int CompareTo(Customer? other) => 0; } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["System.IComparable<Order>"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : System.IComparable<Customer>, System.IComparable<Order>");
        StringAssert.Contains(code, "public int CompareTo(Order");
        AssertCompiles(code);
    }

    [TestMethod]
    public void The_same_name_twice_is_added_once()
    {
        var file = WriteOrder("namespace Shop { " + Auditable + "public class Order { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Order", ["IAuditable", "iauditable"], interfacesOnly: true, out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : IAuditable {");
        Assert.IsFalse(message.Contains("already", StringComparison.Ordinal), message);
        AssertCompiles(code);
    }

    [TestMethod]
    public void A_base_class_already_listed_still_gets_its_overrides_and_says_inherits()
    {
        var file = Write("Sq", "namespace Shop { public abstract class Shape { public abstract double Area(); } public class Sq : Shape { } }");

        Assert.IsTrue(ClassInheritanceService.AddInheritance(_workspace, "Sq", "Shape", out var message), message);

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "public override double Area() => throw new System.NotImplementedException();");
        StringAssert.Contains(message, "Class 'Sq' already inherits from 'Shape'. Added Area, which it lacked");
        AssertCompiles(code);
    }

    // The hint used to be "use 'IFoo inherits Base'", which is refused too.
    [TestMethod]
    public void An_interface_cannot_implement_a_class()
    {
        var file = Write("IFoo", "namespace Shop { public class Base { } public interface IFoo { } }");
        var before = File.ReadAllBytes(file);

        Assert.IsFalse(ClassInheritanceService.AddInheritance(_workspace, "IFoo", ["Base"], interfacesOnly: true, out var message), message);

        Assert.AreEqual("'Base' is a class, and an interface can only extend interfaces.", message);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Implements_reads_a_qualified_name()
    {
        CollectionAssert.AreEqual(new[] { "System.IDisposable" },
            CommandFront.Parse("Order implements System.IDisposable", _workspace).Interfaces);
        CollectionAssert.AreEqual(new[] { "System.IComparable<Order>", "IAuditable" },
            CommandFront.Parse("Order implements System.IComparable<Order>, IAuditable", _workspace).Interfaces);
    }

    [TestMethod]
    public void Run_implements_a_qualified_name_end_to_end()
    {
        var file = WriteOrder("namespace Shop { public class Order { } }");

        Assert.IsTrue(Run("Order implements System.IDisposable"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : System.IDisposable");
        StringAssert.Contains(code, "public void Dispose() => throw new System.NotImplementedException();");
        AssertCompiles(code);
    }

    [TestMethod]
    [DataRow("struct Point implements IShape", "Point")]
    [DataRow("record Order implements IA", "Order")]
    [DataRow("record struct P implements IA", "P")]
    [DataRow("record class P implements IA", "P")]
    [DataRow("class Order implements IA", "Order")]
    [DataRow("Order class implements IA", "Order")]
    public void Implements_accepts_the_kind_of_type_before_its_name(string sentence, string name)
    {
        var command = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.ClassInherit, command.Intent);
        Assert.AreEqual(name, command.ClassName);
        Assert.HasCount(1, command.Interfaces);
    }

    [TestMethod]
    public void Interface_extends_is_recognised()
    {
        var command = CommandFront.Parse("interface IA extends IB", _workspace);

        Assert.AreEqual(IntentType.ClassInherit, command.Intent);
        Assert.AreEqual("IA", command.ClassName);
        Assert.AreEqual("IB", command.BaseClass);
    }

    [TestMethod]
    [DataRow("create struct Point implements IShape")]
    [DataRow("create record Order implements IA")]
    [DataRow("create interface IA extends IB")]
    public void Does_not_claim_create_sentences_for_other_kinds(string sentence)
    {
        Assert.AreNotEqual(IntentType.ClassInherit, CommandFront.Parse(sentence, _workspace).Intent);
    }

    [TestMethod]
    public void Implements_reads_an_oxford_comma()
    {
        var command = CommandFront.Parse("Order implements IA, IB, and IC", _workspace);

        Assert.AreEqual(IntentType.ClassInherit, command.Intent);
        CollectionAssert.AreEqual(new[] { "IA", "IB", "IC" }, command.Interfaces);
    }
}
