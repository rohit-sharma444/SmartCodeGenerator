using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The red asterisk and the generated validator must never disagree, so both read the same
/// PropertyDefinition.Rules scan.
/// </summary>
[TestClass]
public class RequiredFieldsTests
{
    private static ClassDefinition Customer()
    {
        var required = new PropertyDefinition { Name = "Name", Type = "string" };
        required.Rules.Add(new PropertyRule { Name = "required" });

        var optional = new PropertyDefinition { Name = "Remarks", Type = "string" };

        return new ClassDefinition
        {
            Name = "Customer",
            Properties = { required, optional },
        };
    }

    private static Assembly Emit(params ClassDefinition[] classes)
    {
        var source = EmittedSource.Flatten(
            "public class Customer { public string Name { get; set; } = string.Empty; public string Remarks { get; set; } = string.Empty; }",
            RequiredFieldsTemplate.Source("Acme", classes)
                .Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private static bool IsRequired(Assembly assembly, string property)
        => (bool)assembly.GetType("RequiredFields")!
            .GetMethod("IsRequired")!
            .Invoke(null, [assembly.GetType("Customer")!, property])!;

    [TestMethod]
    public void A_required_property_is_reported_required()
        => Assert.IsTrue(IsRequired(Emit(Customer()), "Name"));

    [TestMethod]
    public void An_optional_property_is_not()
        => Assert.IsFalse(IsRequired(Emit(Customer()), "Remarks"));

    [TestMethod]
    public void An_unknown_property_is_not()
        => Assert.IsFalse(IsRequired(Emit(Customer()), "Nonexistent"));

    /// <summary>Always emitted, empty when the build has no validation — so no consumer has to
    /// strike the conditional-fragment bargain RecordEditorTemplate does.</summary>
    [TestMethod]
    public void An_empty_map_compiles_and_answers_false()
        => Assert.IsFalse(IsRequired(Emit(), "Name"));

    [TestMethod]
    public void Both_desktop_targets_emit_the_file()
    {
        foreach (var target in new ITargetScaffolder[]
                 { new WpfTarget(), new WinFormsTarget() })
        {
            var names = target.FrontEndFiles(
                    "Shop", ["Order"],
                    AppCustomization.Default with { Layout = AppLayout.Business })
                .Select(f => f.Name)
                .ToList();

            CollectionAssert.Contains(names, "RequiredFields.cs", target.Key);
        }
    }
}
