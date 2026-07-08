using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DomainModelMapperTests
{
    [TestMethod]
    public void ToClassDefinitions_PrependsIntId_ThenMapsFields()
    {
        var model = new CandidateDomainModel();
        var employee = new CandidateEntity { Name = "Employee" };
        employee.Fields.Add(new CandidateField { Name = "Salary", Type = "decimal" });
        employee.Fields.Add(new CandidateField { Name = "FullName", Type = "string" });
        model.Entities.Add(employee);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.AreEqual(1, classes.Count);
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

        Assert.AreEqual(1, classes[0].Properties.Count);
        Assert.AreEqual("Id", classes[0].Properties[0].Name);
        Assert.AreEqual("int", classes[0].Properties[0].Type);
    }

    [TestMethod]
    public void ToClassDefinitions_MultipleEntities_PreservesNamesAndOrder()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });
        model.Entities.Add(new CandidateEntity { Name = "Application" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        CollectionAssert.AreEqual(
            new[] { "Payroll", "Application" },
            classes.Select(c => c.Name).ToList());
    }
}
