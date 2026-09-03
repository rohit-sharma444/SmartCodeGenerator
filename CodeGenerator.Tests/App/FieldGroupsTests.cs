using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The bucket rules, compiled and run rather than matched as text.
/// </summary>
/// <remarks>
/// The order of the buckets is the substance, not an implementation detail: ContactAddress is an
/// address and ContactPerson is a contact, so `address` has to be tested before `contact`, and
/// `pincode` before `pin`. A substring assertion over the emitted source could not tell those apart.
/// </remarks>
[TestClass]
public class FieldGroupsTests
{
    /// <summary>A model wide enough to reach every bucket, plus the two shapes that have to be
    /// kept — an enum and a Guid, which the desktop form shows and the web list does not.</summary>
    private const string ModelStub = """
using System;

public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
    public Guid Reference { get; set; }
    public DayOfWeek DeliveryDay { get; set; }

    public string AddressLine1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PinCode { get; set; } = string.Empty;
    public string ContactAddress { get; set; } = string.Empty;

    public string ContactPerson { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string Remarks { get; set; } = string.Empty;
    public bool TermsAccepted { get; set; }

    public List<string> Tags { get; set; } = new();
}
""";

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            ModelStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    /// <summary>Title → the field names in it, from the compiled emitted code.</summary>
    private static Dictionary<string, string[]> Groups()
    {
        var assembly = Emit();
        var model = assembly.GetType("Customer")!;
        var groups = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("For")!.Invoke(null, [model])!;

        var result = new Dictionary<string, string[]>();

        foreach (var group in groups)
        {
            var type = group.GetType();
            var title = (string)type.GetProperty("Title")!.GetValue(group)!;
            var fields = ((IEnumerable<PropertyInfo>)type.GetProperty("Fields")!.GetValue(group)!)
                .Select(p => p.Name)
                .ToArray();

            result[title] = fields;
        }

        return result;
    }

    [TestMethod]
    public void Address_names_land_in_address_information()
        => CollectionAssert.AreEquivalent(
            new[] { "AddressLine1", "City", "PinCode", "ContactAddress" },
            Groups()["Address Information"]);

    /// <summary>The ordering assertion. ContactAddress is an address; ContactPerson is not.</summary>
    [TestMethod]
    public void Contact_is_tested_after_address()
        => CollectionAssert.AreEquivalent(
            new[] { "ContactPerson", "Email" },
            Groups()["Contact Information"]);

    [TestMethod]
    public void Everything_unmatched_stays_in_basic_information()
        => CollectionAssert.AreEquivalent(
            new[] { "Code", "Name", "CreditLimit", "Reference", "DeliveryDay", "TermsAccepted" },
            Groups()["Basic Information"]);

    /// <summary>The desktop form shows enums and Guids; the web list's IsScalar does not. Reusing
    /// the web predicate here would silently drop both.</summary>
    [TestMethod]
    public void An_enum_and_a_guid_reach_a_panel()
    {
        var basic = Groups()["Basic Information"];

        CollectionAssert.Contains(basic, "DeliveryDay");
        CollectionAssert.Contains(basic, "Reference");
    }

    [TestMethod]
    public void Id_and_collections_are_not_shown()
    {
        var all = Groups().Values.SelectMany(f => f).ToList();

        CollectionAssert.DoesNotContain(all, "Id");
        CollectionAssert.DoesNotContain(all, "Tags");
    }

    /// <summary>Only strings, so a TermsAccepted bool stays a checkbox in a panel rather than
    /// becoming a multi-line box in a tab.</summary>
    [TestMethod]
    public void Only_long_text_strings_reach_the_additional_tab()
    {
        var assembly = Emit();
        var additional = ((IEnumerable<PropertyInfo>)assembly.GetType("FieldGroups")!
            .GetMethod("Additional")!.Invoke(null, [assembly.GetType("Customer")!])!)
            .Select(p => p.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Remarks" }, additional);
        CollectionAssert.DoesNotContain(
            Groups().Values.SelectMany(f => f).ToList(), "Remarks");
        CollectionAssert.Contains(Groups()["Basic Information"], "TermsAccepted");
    }

    /// <summary>A heading with nothing under it is noise — the rule DashboardViewModel.Group
    /// already follows.</summary>
    [TestMethod]
    public void An_empty_group_is_not_emitted()
    {
        var source = EmittedSource.Flatten(
            "public class Note { public int Id { get; set; } public string Text { get; set; } = string.Empty; }",
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var groups = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("For")!.Invoke(null, [assembly.GetType("Note")!])!;

        Assert.HasCount(1, groups.Cast<object>().ToList());
    }

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

            CollectionAssert.Contains(names, "FieldGroups.cs", target.Key);
        }
    }
}
