using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// The member commands on an interface. Every one of them looked only for a class, so
/// <c>add method Area to IShape</c> answered "Class 'IShape' not found in IShape.cs". Asserted by
/// compiling the edited file, not by substrings.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class InterfaceMemberEditTests : InterfaceWorkspaceTests
{
    // ----- add -----------------------------------------------------------

    [TestMethod]
    public void Add_method_writes_a_signature_without_a_body()
    {
        WriteShape();

        var (handled, output) = Run("add method Area returns double to IShape");

        Assert.IsTrue(handled, output);
        var method = ParseShape().Members.OfType<MethodDeclarationSyntax>().Single();
        Assert.AreEqual("Area", method.Identifier.Text);
        Assert.AreEqual("double", method.ReturnType.ToString());
        Assert.IsNull(method.Body, Shape);
        Assert.IsNull(method.ExpressionBody, Shape);
        Assert.IsEmpty(method.Modifiers, Shape);
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Add_method_keeps_public_when_the_interface_already_writes_it()
    {
        WriteShape("        public string Describe();\n");

        Assert.IsTrue(Run("add method Area returns double to IShape").Handled);

        var method = ParseShape().Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Area");
        Assert.AreEqual("public", method.Modifiers.ToString(), Shape);
        Assert.IsNull(method.Body, Shape);
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Add_property_writes_an_auto_property_without_modifiers_or_initializer()
    {
        WriteShape();

        var (handled, output) = Run("add property Name string to IShape");

        Assert.IsTrue(handled, output);
        var property = ParseShape().Members.OfType<PropertyDeclarationSyntax>().Single();
        Assert.AreEqual("Name", property.Identifier.Text);
        Assert.AreEqual("string", property.Type.ToString());
        Assert.IsEmpty(property.Modifiers, Shape);
        Assert.IsNull(property.Initializer, Shape);
        Assert.IsEmpty(property.AttributeLists, Shape);
        AssertWorkspaceCompiles();
    }

    // ----- remove / rename / retype ---------------------------------------

    [TestMethod]
    public void Remove_method_and_property_work_on_an_interface()
    {
        WriteShape("        double Area();\n        string Name { get; set; }\n");

        Assert.IsTrue(Run("remove method Area from IShape").Handled, Shape);
        Assert.IsTrue(Run("remove property name from IShape").Handled, Shape);

        Assert.IsEmpty(ParseShape().Members, Shape);
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    public void Rename_method_and_property_work_on_an_interface()
    {
        WriteShape("        double Area();\n        string Name { get; set; }\n");

        Assert.IsTrue(Run("rename method Area to Surface in IShape").Handled, Shape);
        Assert.IsTrue(Run("rename property Name to Title in IShape").Handled, Shape);

        var iface = ParseShape();
        Assert.AreEqual("Surface", iface.Members.OfType<MethodDeclarationSyntax>().Single().Identifier.Text);
        Assert.AreEqual("Title", iface.Members.OfType<PropertyDeclarationSyntax>().Single().Identifier.Text);
        AssertWorkspaceCompiles();
    }

    [TestMethod]
    [DataRow("rename property Name to Area in IShape", "already has a method named 'Area'")]
    [DataRow("rename method Area to Name in IShape", "already has a property named 'Name'")]
    [DataRow("rename property Name to class in IShape", "keyword")]
    [DataRow("rename property Name to IShape in IShape", "own name")]
    [DataRow("change type of Name to Strng in IShape", "not a type this project knows")]
    public void An_interface_edit_is_refused_as_a_class_edit_is(string input, string why)
    {
        var file = WriteShape("        double Area();\n        string Name { get; set; }\n");
        var before = File.ReadAllText(file);

        var (handled, output) = Run(input);

        Assert.IsFalse(handled, output);
        StringAssert.Contains(output, why, output);
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void Change_type_works_on_an_interface_property()
    {
        WriteShape("        string Name { get; set; }\n");

        var (handled, output) = Run("change type of name to int in IShape");

        Assert.IsTrue(handled, output);
        Assert.AreEqual("int", ParseShape().Members.OfType<PropertyDeclarationSyntax>().Single().Type.ToString());
        AssertWorkspaceCompiles();
    }

    // ----- reading it back ------------------------------------------------

    [TestMethod]
    public void List_methods_and_properties_show_the_interface_members()
    {
        WriteShape("        double Area();\n        string Name { get; set; }\n");

        var (_, methods) = Run("list methods IShape");
        var (_, properties) = Run("list properties IShape");

        StringAssert.Contains(methods, "double Area()", methods);
        StringAssert.Contains(properties, "string Name", properties);
    }

    [TestMethod]
    public void Undo_restores_an_interface_edit()
    {
        var file = WriteShape();
        var before = File.ReadAllText(file);
        using var journal = new UndoJournal(_workspace, shadowParent: _shadowParent);

        using (journal.Begin("add method Area to IShape"))
            Assert.IsTrue(Run("add method Area returns double to IShape").Handled);
        Assert.AreNotEqual(before, File.ReadAllText(file));

        var undone = journal.Undo();

        Assert.IsTrue(undone.Done, undone.Message);
        Assert.AreEqual(before, File.ReadAllText(file));
    }
}
