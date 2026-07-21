using System.Collections.Generic;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedDataTemplateTests
{
    private static ClassDefinition Class(string name, params PropertyDefinition[] props)
    {
        var def = new ClassDefinition { Name = name };
        foreach (var p in props) def.Properties.Add(p);
        return def;
    }

    private static PropertyDefinition Prop(string name, string type) =>
        new() { Name = name, Type = type };

    [TestMethod]
    public void OrderByDependency_PutsReferencedEntityBeforeItsReferencer()
    {
        // Order references Customer (CustomerId int + Customer nav), so Customer must seed first.
        var order = Class("Order",
            Prop("Id", "int"), Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var customer = Class("Customer", Prop("Id", "int"), Prop("Name", "string"));

        var ordered = SeedDataTemplate.OrderByDependency(new List<ClassDefinition> { order, customer });

        CollectionAssert.AreEqual(new[] { "Customer", "Order" }, (System.Collections.ICollection)ordered);
    }

    [TestMethod]
    public void OrderByDependency_ToleratesNoRelationships()
    {
        var a = Class("Alpha", Prop("Id", "int"));
        var b = Class("Beta", Prop("Id", "int"));

        var ordered = SeedDataTemplate.OrderByDependency(new List<ClassDefinition> { a, b });

        Assert.HasCount(2, ordered);
        CollectionAssert.Contains((System.Collections.ICollection)ordered, "Alpha");
        CollectionAssert.Contains((System.Collections.ICollection)ordered, "Beta");
    }

    [TestMethod]
    public void Source_EmitsSeedCallsInOrder_AndSeedRowCount()
    {
        var src = SeedDataTemplate.Source("Shop", new[] { "Customer", "Order" });

        StringAssert.Contains(src, "namespace Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "public const int SeedRowCount = 3;");
        StringAssert.Contains(src, "Seed<Customer>(seededIds);");
        StringAssert.Contains(src, "Seed<Order>(seededIds);");
        // Customer's Seed call precedes Order's.
        Assert.IsLessThan(src.IndexOf("Seed<Order>"), src.IndexOf("Seed<Customer>"));
    }
}
