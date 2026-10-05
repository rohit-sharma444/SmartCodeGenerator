using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Validation belongs to the class that holds the data. On an interface, add validation stored a
/// rule nothing could use, and generate validator wrote a partial class beside the partial
/// interface - a file that does not compile.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class InterfaceValidationRefusalTests : InterfaceWorkspaceTests
{
    // ----- commands with no meaning on an interface --------------------------

    [TestMethod]
    public void Add_validation_on_an_interface_is_refused()
    {
        WriteShape("        string Name { get; set; }\n");

        var (handled, output) = Run("add validation required on Name in IShape");

        Assert.IsFalse(handled, output);
        StringAssert.Contains(output, "interface", output);
        Assert.IsEmpty(ValidationRegistry.Get("IShape"));
    }

    [TestMethod]
    public void Generate_validator_for_an_interface_is_refused_and_writes_nothing()
    {
        WriteShape("        string Name { get; set; }\n");
        ValidationRegistry.Add("IShape", ValidationRuleCatalog.Default
            .Parse("Name is required", ValidationCategory.Data,
                new ValidationParseContext { Fragment = "Name is required", ClassName = "IShape", KnownProperties = ["Name"] })
            .Single());
        var before = Shape;

        var (handled, output) = Run("generate validator for IShape");

        Assert.IsFalse(handled, output);
        StringAssert.Contains(output, "interface", output);
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "IShape.Validation.cs")), output);
        Assert.AreEqual(before, Shape);
    }

    [TestMethod]
    // The chat page's validation box writes through ClassValidationWriter, which took any type:
    // into an interface it wrote a Validate() default implementation no class's data goes through.
    public void The_chat_validation_writer_refuses_an_interface()
    {
        var file = WriteShape("        string Name { get; set; }\n");
        var before = Shape;
        var rule = ValidationRuleCatalog.Default
            .Parse("Name is required", ValidationCategory.Data,
                new ValidationParseContext { Fragment = "Name is required", ClassName = "IShape", KnownProperties = ["Name"] })
            .Single();

        var result = ClassValidationWriter.Add(file, "IShape", rule);

        Assert.IsFalse(result.Ok, result.Message);
        StringAssert.Contains(result.Message, "interface", result.Message);
        Assert.AreEqual(before, Shape);
    }
}
