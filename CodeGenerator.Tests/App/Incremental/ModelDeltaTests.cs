using AppScaffolding.Incremental;
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// Which of the proposed classes are already on disk. Pure: no filesystem, no scaffolder.
/// </summary>
[TestClass]
public class ModelDeltaTests
{
    private static ClassSnapshot OnDisk(string name, params string[] properties)
        => new(name, $"{name}.cs",
            [.. properties.Select(p => new PropertyDefinition { Name = p, Type = "string" })]);

    private static ClassDefinition Proposed(string name, params string[] properties)
    {
        var definition = new ClassDefinition { Name = name };

        foreach (var property in properties)
            definition.Properties.Add(new PropertyDefinition { Name = property, Type = "string" });

        return definition;
    }

    [TestMethod]
    public void A_class_not_on_disk_is_new()
    {
        var delta = ModelDelta.Between([OnDisk("Customer", "Name")], [Proposed("Supplier", "Name")]);

        Assert.AreEqual(ClassFate.New, delta.Single(d => d.Name == "Supplier").Fate);
    }

    [TestMethod]
    public void The_same_class_with_the_same_properties_is_unchanged()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name", "Email")], [Proposed("Customer", "Name", "Email")]);

        Assert.AreEqual(ClassFate.Unchanged, delta.Single().Fate);
    }

    [TestMethod]
    public void The_same_class_with_different_properties_has_changed()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name")], [Proposed("Customer", "Name", "Email")]);

        Assert.AreEqual(ClassFate.Changed, delta.Single().Fate);
    }

    /// <summary>Property ORDER is not a change: nothing downstream depends on it, and a reordered
    /// list would report churn nobody can act on.</summary>
    [TestMethod]
    public void Reordered_properties_are_not_a_change()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Email", "Name")], [Proposed("Customer", "Name", "Email")]);

        Assert.AreEqual(ClassFate.Unchanged, delta.Single().Fate);
    }

    /// <summary>A class name is also a file name, and Windows file names are case-insensitive —
    /// so `customer` on disk and `Customer` proposed are the same class, not a new one.</summary>
    [TestMethod]
    public void A_name_matches_however_it_is_cased()
    {
        var delta = ModelDelta.Between([OnDisk("customer", "Name")], [Proposed("Customer", "Name")]);

        Assert.AreEqual(ClassFate.Unchanged, delta.Single().Fate);
    }

    /// <summary>
    /// Removing is out of scope, and a class the user wrote by hand is not a discrepancy — it is
    /// their code. Reporting it would invite a "shall I delete this?" the tool must never ask.
    /// </summary>
    [TestMethod]
    public void A_class_on_disk_that_the_model_does_not_have_is_not_reported()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name"), OnDisk("MyOwnHelper", "Thing")],
            [Proposed("Customer", "Name")]);

        Assert.HasCount(1, delta);
        Assert.AreEqual("Customer", delta.Single().Name);
    }

    [TestMethod]
    public void Nothing_on_disk_makes_everything_new()
    {
        var delta = ModelDelta.Between([], [Proposed("A", "X"), Proposed("B", "Y")]);

        Assert.HasCount(2, delta);
        Assert.IsTrue(delta.All(d => d.Fate == ClassFate.New));
    }

    [TestMethod]
    public void NewNames_are_the_new_ones_in_order()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name")],
            [Proposed("Customer", "Name"), Proposed("Supplier", "Name"), Proposed("Order", "Placed")]);

        // CollectionAssert, not Assert.AreEqual: MSTest compares arrays by REFERENCE, so
        // AreEqual on two arrays fails even when their contents match.
        CollectionAssert.AreEqual(
            new[] { "Supplier", "Order" }, ModelDelta.NewNames(delta).ToArray());
    }
}
