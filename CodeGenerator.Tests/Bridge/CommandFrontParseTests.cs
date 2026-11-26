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
    public void Create_class_named_A_inherits_keeps_the_name()
    {
        var cmd = CommandFront.Parse("create class A inherits Shape", "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual("A", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
    }

    [TestMethod]
    public void Create_class_extends_reads_the_base()
    {
        var cmd = CommandFront.Parse("create class A extends Shape", "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual("A", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
    }

    [TestMethod]
    public void Create_class_colon_reads_the_base()
    {
        var cmd = CommandFront.Parse("create class B : Shape", "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual("B", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
    }

    [TestMethod]
    public void Create_class_colon_with_properties()
    {
        var cmd = CommandFront.Parse("create class B : Shape with properties Size int", "");
        Assert.AreEqual("B", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
        Assert.HasCount(1, cmd.Properties);
        Assert.AreEqual("Size", cmd.Properties[0].Name);
    }

    [TestMethod]
    public void Create_a_class_a_inherits()
    {
        var cmd = CommandFront.Parse("create a class a inherits Shape", "");
        Assert.AreEqual("A", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
    }

    [TestMethod]
    public void Create_class_derives_from_reads_the_base()
    {
        var cmd = CommandFront.Parse("create class A derives from Shape", "");
        Assert.AreEqual("A", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
    }

    [TestMethod]
    public void Create_class_colon_list_is_base_then_interfaces()
    {
        var cmd = CommandFront.Parse("create class B : Shape, IFoo", "");
        Assert.AreEqual("B", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
        CollectionAssert.AreEqual(new[] { "IFoo" }, cmd.Interfaces.ToArray());
    }

    [TestMethod]
    public void Create_class_inherits_and_list_is_base_then_interfaces()
    {
        var cmd = CommandFront.Parse("create class A inherits Shape and IFoo", "");
        Assert.AreEqual("A", cmd.ClassName);
        Assert.AreEqual("Shape", cmd.BaseClass);
        CollectionAssert.AreEqual(new[] { "IFoo" }, cmd.Interfaces.ToArray());
    }

    [TestMethod]
    public void Create_class_implements_after_name()
    {
        var cmd = CommandFront.Parse("create class A implements IShape, IOther", "");
        Assert.AreEqual("A", cmd.ClassName);
        CollectionAssert.AreEqual(new[] { "IShape", "IOther" }, cmd.Interfaces.ToArray());
    }

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
        Assert.IsGreaterThan(0d, cmd.Confidence, $"expected NLE confidence, got {cmd.Confidence}");
    }

    [TestMethod]
    // "create class employee" was "Command not recognized" while "create class Employee"
    // worked: NLE tags a lower-case word as part of the target, so the mapper looked up
    // "class employee". The name is PascalCased, as the with-properties form already did,
    // and a lower-case manager/model/entity/field after "class" is a name, not a synonym.
    [DataRow("create class employee", "Employee")]
    [DataRow("create class manager", "Manager")]
    [DataRow("create class model", "Model")]
    [DataRow("create class entity", "Entity")]
    [DataRow("create class field", "Field")]
    [DataRow("create class order_line", "OrderLine")]
    [DataRow("Create Class employee", "Employee")]
    [DataRow("generate class invoice", "Invoice")]
    [DataRow("add class customer", "Customer")]
    [DataRow("create class Employee", "Employee")]
    [DataRow("create class Manager", "Manager")]
    public void Create_class_with_a_lower_case_name_is_recognised(string sentence, string expected)
    {
        var cmd = CommandFront.Parse(sentence, "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent, sentence);
        Assert.AreEqual(expected, cmd.ClassName, sentence);
    }

    [TestMethod]
    public void Create_class_with_a_lower_case_name_and_properties_keeps_the_properties()
    {
        var cmd = CommandFront.Parse("create class employee with properties Name string, Age int", "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual("Employee", cmd.ClassName);
        CollectionAssert.AreEqual(
            new[] { "Name", "Age" },
            cmd.Properties.Select(p => p.Name).ToArray());
    }

    [TestMethod]
    // The lower-case form must not turn other commands or ordinary sentences into a class.
    [DataRow("create class library mylib")]
    [DataRow("create class with no name")]
    [DataRow("create classes for my shop")]
    [DataRow("I want to create class diagrams today")]
    public void A_sentence_that_is_not_create_class_name_is_not_a_class(string sentence)
    {
        var cmd = CommandFront.Parse(sentence, "");
        Assert.AreNotEqual(
            IntentType.CreateClass, cmd.Intent, $"{sentence} made class {cmd.ClassName}");
    }

    [TestMethod]
    // "create interface ishape" was "Command not recognized" while "create interface IShape"
    // worked - the create class defect again: NLE tags the lower-case name as part of the target.
    // The name follows InterfaceNaming: kept when it is I + a capital, else PascalCased and prefixed.
    [DataRow("create interface ishape", "IIshape")]
    [DataRow("create interface shape", "IShape")]
    [DataRow("generate interface repository", "IRepository")]
    [DataRow("create an interface shape", "IShape")]
    [DataRow("create interface IShape", "IShape")]
    [DataRow("create interface Shape", "IShape")]
    public void Create_interface_with_a_lower_case_name_is_recognised(string sentence, string expected)
    {
        var cmd = CommandFront.Parse(sentence, "");
        Assert.AreEqual(IntentType.CreateInterface, cmd.Intent, sentence);
        Assert.AreEqual(expected, cmd.InterfaceName, sentence);
        Assert.AreEqual(expected, cmd.ClassName, sentence);
    }

    [TestMethod]
    [DataRow("create interface with no name")]
    [DataRow("create interfaces for my shop")]
    [DataRow("I want to create interface diagrams today")]
    public void A_sentence_that_is_not_create_interface_name_is_not_an_interface(string sentence)
    {
        var cmd = CommandFront.Parse(sentence, "");
        Assert.AreNotEqual(
            IntentType.CreateInterface, cmd.Intent, $"{sentence} made interface {cmd.InterfaceName}");
    }
}
