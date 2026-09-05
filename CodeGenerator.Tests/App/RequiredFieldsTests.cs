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

    /// <summary>Task 8 and Task 10 call <c>Source("Payroll", null)</c> directly — a null list, not
    /// merely an empty one — so that exact call must also compile and answer false.</summary>
    [TestMethod]
    public void A_null_classes_list_compiles_and_answers_false()
        => Assert.IsFalse(IsRequired(Emit(null!), "Name"));

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

    /// <summary>
    /// Pins every threading link a null-by-construction test cannot: AppScaffolder's classes
    /// reaching FrontEndContext.Classes, each target's FrontEndFiles(FrontEndContext) forwarding
    /// it, and DesktopViewModelFiles.Build passing it to RequiredFieldsTemplate — by reading the
    /// actual emitted file's content rather than only its name.
    /// </summary>
    [TestMethod]
    public void The_classes_reach_the_emitted_map_through_the_context()
    {
        foreach (var target in new ITargetScaffolder[]
                 { new WpfTarget(), new WinFormsTarget() })
        {
            var source = target.FrontEndFiles(new FrontEndContext(
                    "Shop", ["Customer"], AppCustomization.Default with { Layout = AppLayout.Business },
                    EntityRoleMap.Empty, [Customer()]))
                .Single(f => f.Name == "RequiredFields.cs").Source;

            StringAssert.Contains(source, "[\"Customer\"] = [\"Name\"]", target.Key);
        }
    }
}
