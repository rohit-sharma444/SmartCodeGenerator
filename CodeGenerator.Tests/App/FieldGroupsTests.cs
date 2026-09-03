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
/// address and ContactPerson is a contact, so `address` has to be tested before `contact`. A
/// substring assertion over the emitted source could not tell those apart.
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
        return GroupsOf(assembly, assembly.GetType("Customer")!);
    }

    /// <summary>Same as <see cref="Groups"/>, generalised to whichever assembly and model type a
    /// test has compiled -- the foreign-key tests below need a second model, PurchaseOrder,
    /// rather than Customer.</summary>
    private static Dictionary<string, string[]> GroupsOf(Assembly assembly, Type model)
    {
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

    /// <summary>Code is excluded here, not merely missing: FieldGroups.Key claims it for the
    /// header now, so it must not also draw in a panel below.</summary>
    [TestMethod]
    public void Everything_unmatched_stays_in_basic_information()
        => CollectionAssert.AreEquivalent(
            new[] { "Name", "CreditLimit", "Reference", "DeliveryDay", "TermsAccepted" },
            Groups()["Basic Information"]);

    /// <summary>Asserted directly, not just by absence elsewhere: without this, IsActive being
    /// dropped from every bucket would go unnoticed.</summary>
    [TestMethod]
    public void An_other_word_lands_in_other_information()
        => CollectionAssert.AreEquivalent(
            new[] { "IsActive" },
            Groups()["Other Information"]);

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

    /// <summary>A second model, for the foreign-key tests: an int &lt;Nav&gt;Id with a real
    /// sibling nav property (twice, one of which must bucket by address), and one that ends in
    /// Id but has no sibling at all.</summary>
    private const string PurchaseOrderStub = """
public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ShipToLocation
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class PurchaseOrder
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = new();
    public int ShipToLocationId { get; set; }
    public ShipToLocation ShipToLocation { get; set; } = new();

    // Ends in Id but has no sibling nav property -- NavigationFor must not match it.
    public int LegacyId { get; set; }
}
""";

    private static Assembly EmitPurchaseOrder()
    {
        var source = EmittedSource.Flatten(
            PurchaseOrderStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    /// <summary>A foreign key is kept, not dropped, and labelled by the entity it points at --
    /// not the raw *Id name -- because that is also what the picker column's header reads
    /// (Tasks 8 and 10).</summary>
    [TestMethod]
    public void A_foreign_key_is_kept_and_labelled_by_its_navigation_property()
    {
        var assembly = EmitPurchaseOrder();
        var model = assembly.GetType("PurchaseOrder")!;
        var supplierId = model.GetProperty("SupplierId")!;

        var label = (string)assembly.GetType("FieldGroups")!
            .GetMethod("LabelOf")!.Invoke(null, [supplierId])!;

        Assert.AreEqual("Supplier", label);
        CollectionAssert.Contains(
            GroupsOf(assembly, model).Values.SelectMany(f => f).ToList(), "SupplierId");
    }

    /// <summary>The bucket decision uses the nav name, not the raw *Id name: ShipToLocationId
    /// only reaches Address Information because its label is ShipToLocation.</summary>
    [TestMethod]
    public void A_foreign_key_buckets_on_its_navigation_name()
    {
        var assembly = EmitPurchaseOrder();
        var model = assembly.GetType("PurchaseOrder")!;

        CollectionAssert.Contains(
            GroupsOf(assembly, model)["Address Information"], "ShipToLocationId");
    }

    /// <summary>The negative case: an *Id property with no sibling navigation property must not
    /// be mistaken for a foreign key, or NavigationFor is matching too eagerly.</summary>
    [TestMethod]
    public void An_id_with_no_sibling_navigation_keeps_its_own_name_and_stays_in_basic()
    {
        var assembly = EmitPurchaseOrder();
        var model = assembly.GetType("PurchaseOrder")!;
        var legacyId = model.GetProperty("LegacyId")!;

        var label = (string)assembly.GetType("FieldGroups")!
            .GetMethod("LabelOf")!.Invoke(null, [legacyId])!;

        Assert.AreEqual("LegacyId", label);
        CollectionAssert.Contains(GroupsOf(assembly, model)["Basic Information"], "LegacyId");
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

    private static (string? Code, string? Date, string? Status) Key(string modelStub, string typeName)
    {
        var source = EmittedSource.Flatten(
            modelStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var key = assembly.GetType("FieldGroups")!
            .GetMethod("Key")!.Invoke(null, [assembly.GetType(typeName)!])!;

        string? Name(string part)
            => (key.GetType().GetProperty(part)!.GetValue(key) as PropertyInfo)?.Name;

        return (Name("Code"), Name("Date"), Name("Status"));
    }

    private const string OrderStub = """
using System;

public class Order
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
""";

    [TestMethod]
    public void The_header_takes_the_code_the_date_and_the_status()
    {
        var key = Key(OrderStub, "Order");

        Assert.AreEqual("OrderNo", key.Code);
        Assert.AreEqual("OrderDate", key.Date);
        Assert.AreEqual("Status", key.Status);
    }

    /// <summary>The audit fields every model carries are never what a user means by "the date" --
    /// the exclusion TransactionScreenViewModel and ScreenFields both already make.</summary>
    [TestMethod]
    public void The_audit_dates_are_never_the_header_date()
        => Assert.AreNotEqual("CreatedOn", Key(OrderStub, "Order").Date);

    /// <summary>The assertion this whole member exists for.</summary>
    [TestMethod]
    public void A_header_field_appears_in_no_panel()
    {
        var source = EmittedSource.Flatten(
            OrderStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var groups = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("For")!.Invoke(null, [assembly.GetType("Order")!])!;

        var shown = groups.Cast<object>()
            .SelectMany(g => (IEnumerable<PropertyInfo>)g.GetType().GetProperty("Fields")!.GetValue(g)!)
            .Select(p => p.Name)
            .ToList();

        CollectionAssert.DoesNotContain(shown, "OrderNo");
        CollectionAssert.DoesNotContain(shown, "OrderDate");
        CollectionAssert.DoesNotContain(shown, "Status");
        CollectionAssert.Contains(shown, "Total");
    }

    [TestMethod]
    public void A_model_with_no_code_date_or_status_yields_none()
    {
        var key = Key(
            "public class Note { public int Id { get; set; } public string Text { get; set; } = string.Empty; }",
            "Note");

        Assert.IsNull(key.Code);
        Assert.IsNull(key.Date);
        Assert.IsNull(key.Status);
    }
}
