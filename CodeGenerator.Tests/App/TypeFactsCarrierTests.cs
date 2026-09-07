using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TypeFactsCarrierTests
{
    [TestMethod]
    public void TypeFacts_KnowsNothing_ByDefault()
    {
        // A discovered domain has no data and no schema. Every fact must be
        // separately absent, so inference can tell "not known" from "known to be 0".
        var facts = new TypeFacts();

        Assert.IsNull(facts.IsNullable);
        Assert.IsNull(facts.MaxLength);
        Assert.IsNull(facts.Precision);
        Assert.IsNull(facts.Scale);
        Assert.IsNull(facts.DeclaredType);
        Assert.IsFalse(facts.IsUnbounded);
    }

    [TestMethod]
    public void UnboundedIsDistinct_FromUnknownLength()
    {
        // nvarchar(max) is a KNOWN absence of a bound. nvarchar of unknown size is
        // an unknown one. Collapsing them would put a 200-char cap back on a max column.
        var unbounded = new TypeFacts { IsUnbounded = true };
        var unknown = new TypeFacts();

        Assert.IsNull(unbounded.MaxLength);
        Assert.IsNull(unknown.MaxLength);
        Assert.AreNotEqual(unbounded.IsUnbounded, unknown.IsUnbounded);
    }

    [TestMethod]
    public void AllThreeCarriers_HoldTheSameFacts()
    {
        // Proves the reference graph allows one shared type rather than three
        // parallel shapes that must be mapped between.
        var facts = new TypeFacts
        {
            IsNullable = false,
            MaxLength = 50,
            DeclaredType = "nvarchar"
        };

        var column = new TabularColumn { Name = "Surname", Facts = facts };
        var field = new CandidateField { Name = "Surname", Type = "string", Facts = facts };
        var property = new PropertyDefinition { Name = "Surname", Type = "string", Facts = facts };

        Assert.AreEqual(50, column.Facts!.MaxLength);
        Assert.AreEqual(50, field.Facts!.MaxLength);
        Assert.AreEqual(50, property.Facts!.MaxLength);
        Assert.IsFalse(property.Facts.IsNullable!.Value);
    }
}
