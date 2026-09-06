using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TypeFactsReachPropertiesTests
{
    [TestMethod]
    public void FactsOnAField_ArriveOnItsProperty()
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField
        {
            Name = "Surname",
            Type = "string",
            Facts = new TypeFacts { MaxLength = 50, IsNullable = false }
        });
        model.Entities.Add(entity);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var surname = classes.Single(c => c.Name == "Customer")
            .Properties.Single(p => p.Name == "Surname");

        Assert.AreEqual(50, surname.Facts!.MaxLength);
        Assert.IsFalse(surname.Facts.IsNullable!.Value);
    }

    [TestMethod]
    public void AFieldWithNoFacts_YieldsAPropertyWithNone()
    {
        // A discovered domain supplies no facts and must still map cleanly.
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField { Name = "Surname", Type = "string" });
        model.Entities.Add(entity);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsNull(classes.Single(c => c.Name == "Customer")
            .Properties.Single(p => p.Name == "Surname").Facts);
    }

    [TestMethod]
    public void RenamingAField_KeepsItsFacts()
    {
        // CandidateField.Name is settable so the review loop can rename. Facts ride on the
        // field object, so a rename cannot orphan them — which a name-keyed side table would.
        var field = new CandidateField
        {
            Name = "Surname",
            Type = "string",
            Facts = new TypeFacts { MaxLength = 50 }
        };

        field.Name = "FamilyName";

        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(field);
        model.Entities.Add(entity);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.AreEqual(50, classes.Single(c => c.Name == "Customer")
            .Properties.Single(p => p.Name == "FamilyName").Facts!.MaxLength);
    }
}
