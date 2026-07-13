using System;
using System.Collections.Generic;
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DomainModelMapperTests
{
    private static CandidateDomainModel ModelOf(params string[] entityNames)
    {
        var model = new CandidateDomainModel();
        foreach (var name in entityNames)
            model.Entities.Add(new CandidateEntity { Name = name });
        return model;
    }

    [TestMethod]
    public void ToClassDefinitions_RenamesEntities_ThatCollideWithTypesUsedByGeneratedCode()
    {
        // Entities land in <App>.Models, but a name shared with a type the generated
        // app declares or imports by simple name binds to the wrong type.
        var classes = DomainModelMapper.ToClassDefinitions(
            ModelOf("Type", "Convert", "JsonStore", "Window"));

        CollectionAssert.AreEqual(
            new[] { "TypeItem", "ConvertItem", "JsonStoreItem", "WindowItem" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_RenamesEntitiesThatCollideWithGeneratedWpfTypes()
    {
        // `new EntityViewModel<EntityViewModel>(...)` would bind its type argument to
        // the view model rather than the model, since the current namespace beats a
        // using-imported one. `Application` is both a common noun and System.Windows.Application.
        var classes = DomainModelMapper.ToClassDefinitions(
            ModelOf("Application", "MainWindow", "EntityViewModel", "RelayCommand"));

        CollectionAssert.AreEqual(
            new[] { "ApplicationItem", "MainWindowItem", "EntityViewModelItem", "RelayCommandItem" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_LeavesOrdinaryNamesAlone()
    {
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Employee", "Payroll", "Deduction"));

        CollectionAssert.AreEqual(
            new[] { "Employee", "Payroll", "Deduction" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_KeepsClassNamesUnique_WhenRenamingWouldCollide()
    {
        // "Type" wants to become "TypeItem", but that name is already taken.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Type", "TypeItem"));

        var names = classes.Select(c => c.Name).ToList();
        Assert.AreEqual(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "generated class names must be unique");
        CollectionAssert.Contains(names, "TypeItem");
    }

    [TestMethod]
    public void ToClassDefinitions_KeepsClassNamesUnique_WhenTwoEntitiesDifferOnlyByCase()
    {
        // Each class is written to <Name>.cs, and Windows filenames are case-insensitive.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Invoice", "invoice"));

        Assert.AreEqual(2, classes.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void ToClassDefinitions_PrependsIntId_ThenMapsFields()
    {
        var model = new CandidateDomainModel();
        var employee = new CandidateEntity { Name = "Employee" };
        employee.Fields.Add(new CandidateField { Name = "Salary", Type = "decimal" });
        employee.Fields.Add(new CandidateField { Name = "FullName", Type = "string" });
        model.Entities.Add(employee);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.HasCount(1, classes);
        Assert.AreEqual("Employee", classes[0].Name);
        CollectionAssert.AreEqual(
            new[] { "Id", "Salary", "FullName" },
            classes[0].Properties.Select(p => p.Name).ToList());
        Assert.AreEqual("int", classes[0].Properties[0].Type);
        Assert.AreEqual("decimal", classes[0].Properties[1].Type);
    }

    [TestMethod]
    public void ToClassDefinitions_ExistingIdField_NotDuplicated()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "id", Type = "int" });
        order.Fields.Add(new CandidateField { Name = "Total", Type = "decimal" });
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        var idCount = classes[0].Properties.Count(
            p => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, idCount);
        CollectionAssert.AreEqual(
            new[] { "id", "Total" },
            classes[0].Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_FieldlessEntity_GetsIntId()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.HasCount(1, classes[0].Properties);
        Assert.AreEqual("Id", classes[0].Properties[0].Name);
        Assert.AreEqual("int", classes[0].Properties[0].Type);
    }

    [TestMethod]
    public void ToClassDefinitions_MultipleEntities_PreservesNamesAndOrder()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });
        model.Entities.Add(new CandidateEntity { Name = "Deduction" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        CollectionAssert.AreEqual(
            new[] { "Payroll", "Deduction" },
            classes.Select(c => c.Name).ToList());
    }
}
