using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// A member added to, removed from, renamed or retyped on an interface leaves the classes that
/// implement it out of step (CS0535, CS0738). They are named, and left as they are: member edits
/// change the declaration only, as their messages say.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class InterfaceImplementerWarningTests : InterfaceWorkspaceTests
{
    [TestMethod]
    public void Add_to_an_interface_names_the_classes_that_now_lack_the_member()
    {
        WriteShape();
        Write("Circle", "namespace Geo { public class Circle : IShape { } }");
        Write("Square", "namespace Geo { public class Square : Geo.IShape { public double Area() => 1; } }");

        var (handled, output) = Run("add method Area returns double to IShape");

        Assert.IsTrue(handled, output);
        StringAssert.Contains(output, "Circle", output);
        // Square already has it; naming it would be noise.
        Assert.DoesNotContain("Square", output, output);
        // Nothing but the interface was edited.
        Assert.AreEqual("namespace Geo { public class Circle : IShape { } }",
            File.ReadAllText(Path.Combine(_workspace, "Circle.cs")));
    }

    [TestMethod]
    public void Add_to_an_interface_nobody_implements_warns_of_nothing()
    {
        WriteShape();

        var (_, output) = Run("add method Area returns double to IShape");

        Assert.DoesNotContain("implement", output, output);
    }

    [TestMethod]
    [DataRow("rename method Area to Surface in IShape")]
    [DataRow("remove method Area from IShape")]
    [DataRow("rename property Name to Title in IShape")]
    [DataRow("change type of Name to int in IShape")]
    [DataRow("remove property Name from IShape")]
    public void An_interface_edit_names_the_implementers_left_out_of_step(string input)
    {
        WriteShape("        double Area();\n        string Name { get; set; }\n");
        var circle = "namespace Geo { public class Circle : IShape "
            + "{ public double Area() => 1; public string Name { get; set; } = \"\"; } }";
        Write("Circle", circle);

        var (handled, output) = Run(input);

        Assert.IsTrue(handled, output);
        StringAssert.Contains(output, "Circle", output);
        StringAssert.Contains(output, "implement", output);
        Assert.AreEqual(circle, File.ReadAllText(Path.Combine(_workspace, "Circle.cs")));
    }
}
