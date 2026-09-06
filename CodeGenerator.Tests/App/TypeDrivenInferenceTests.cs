using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TypeDrivenInferenceTests
{
    [TestMethod]
    public void ANvarcharFifty_IsBoundedAtFifty_NotTwoHundred()
    {
        // The shipped defect: a generated app accepts 120 characters into a column
        // that holds 50, and the database rejects the insert.
        var property = Mapped("Surname", "string", new TypeFacts { MaxLength = 50 });

        Assert.AreEqual(50, Rule(property, "maxlength")!.MaxLength);
    }

    [TestMethod]
    public void AnUnboundedColumn_GetsNoLengthRule()
    {
        var property = Mapped("Notes", "string", new TypeFacts { IsUnbounded = true });

        Assert.IsNull(Rule(property, "maxlength"));
    }

    [TestMethod]
    public void AColumnWithNoKnownLength_KeepsTheTwoHundredFallback()
    {
        // A discovered domain has no facts. It must still generate, and a bound is
        // better than none for a column that will back a text box.
        var property = Mapped("Surname", "string", facts: null);

        Assert.AreEqual(200, Rule(property, "maxlength")!.MaxLength);
    }

    [TestMethod]
    public void ANotNullColumn_IsRequired_WhateverItIsCalled()
    {
        // Today only "Name" and "Title" are required, by name equality.
        var property = Mapped("Surname", "string", new TypeFacts { IsNullable = false });

        Assert.IsNotNull(Rule(property, "required"));
    }

    [TestMethod]
    public void ANullableColumn_IsNotRequired_EvenWhenCalledName()
    {
        var property = Mapped("Name", "string", new TypeFacts { IsNullable = true });

        Assert.IsNull(Rule(property, "required"));
    }

    [TestMethod]
    public void Tinyint_IsBoundedZeroToTwoFiveFive()
    {
        var property = Mapped("Rating", "int", new TypeFacts { DeclaredType = "tinyint" });

        Assert.AreEqual(0m, Rule(property, "minvalue")!.MinValue);
        Assert.AreEqual(255m, Rule(property, "maxvalue")!.MaxValue);
    }

    [TestMethod]
    public void Smallint_IsBoundedToItsRange()
    {
        var property = Mapped("Year", "int", new TypeFacts { DeclaredType = "smallint" });

        Assert.AreEqual(-32768m, Rule(property, "minvalue")!.MinValue);
        Assert.AreEqual(32767m, Rule(property, "maxvalue")!.MaxValue);
    }

    [TestMethod]
    public void DecimalEighteenTwo_LimitsItsDecimalPlaces()
    {
        var property = Mapped("Price", "decimal",
            new TypeFacts { DeclaredType = "decimal", Precision = 18, Scale = 2 });

        Assert.AreEqual(2, Rule(property, "scale")!.Scale);
    }

    [TestMethod]
    public void DecimalEighteenTwo_AlsoLimitsTheDigitsBeforeThePoint()
    {
        // decimal(18,2) overflows at 10^16. Without this the app accepts the value
        // and the database refuses the insert — the same shape as the length defect.
        var property = Mapped("Price", "decimal",
            new TypeFacts { DeclaredType = "decimal", Precision = 18, Scale = 2 });

        Assert.AreEqual(9999999999999999.99m, Rule(property, "maxvalue")!.MaxValue);
        Assert.AreEqual(-9999999999999999.99m, Rule(property, "minvalue")!.MinValue);
    }

    [TestMethod]
    public void ADecimalFromAFile_GetsItsScaleButNoPrecisionBound()
    {
        // Evidence gives a scale. It cannot give a precision, and inventing one
        // would reject a larger value the source may legitimately grow.
        var property = Mapped("Price", "decimal", new TypeFacts { Scale = 2 });

        Assert.AreEqual(2, Rule(property, "scale")!.Scale);
        Assert.IsNull(Rule(property, "maxvalue"));
    }

    [TestMethod]
    public void Datetime_GetsSqlServersOwnFloor()
    {
        var property = Mapped("OrderedOn", "DateTime", new TypeFacts { DeclaredType = "datetime" });

        Assert.AreEqual("1753-01-01", Rule(property, "mindate")!.MinDate);
    }

    [TestMethod]
    public void Datetime2_GetsNoFloor()
    {
        // datetime2 starts at year 1. Borrowing datetime's floor would refuse
        // dates the column accepts.
        var property = Mapped("OrderedOn", "DateTime", new TypeFacts { DeclaredType = "datetime2" });

        Assert.IsNull(Rule(property, "mindate"));
    }

    [TestMethod]
    public void ADateColumn_RejectsATimeOfDay()
    {
        var property = Mapped("DueOn", "DateTime", new TypeFacts { DeclaredType = "date" });

        Assert.IsNotNull(Rule(property, "dateonly"));
    }

    [TestMethod]
    public void ABitColumn_GetsNoRules()
    {
        // A bool cannot be invalid.
        var property = Mapped("IsActive", "bool", new TypeFacts { DeclaredType = "bit", IsNullable = false });

        Assert.HasCount(0, property.Rules);
    }

    [TestMethod]
    public void EveryRuleInferenceRecords_HasAnEmitter()
    {
        // Closes the silent-drop risk across the whole table above: any rule name
        // inference produces that ValidatorTemplate cannot emit fails here.
        var properties = new[]
        {
            Mapped("Surname", "string", new TypeFacts { MaxLength = 50, IsNullable = false }),
            Mapped("Rating", "int", new TypeFacts { DeclaredType = "tinyint" }),
            Mapped("Price", "decimal", new TypeFacts { DeclaredType = "decimal", Precision = 18, Scale = 2 }),
            Mapped("OrderedOn", "DateTime", new TypeFacts { DeclaredType = "datetime" }),
            Mapped("DueOn", "DateTime", new TypeFacts { DeclaredType = "date" })
        };

        foreach (var property in properties)
            foreach (var rule in property.Rules)
                Assert.IsTrue(
                    ValidatorTemplate.EmittableRuleNames.Contains(rule.Name),
                    $"Inference records '{rule.Name}' but ValidatorTemplate cannot emit it.");
    }

    private static PropertyDefinition Mapped(string name, string type, TypeFacts? facts)
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer" };
        entity.Fields.Add(new CandidateField { Name = name, Type = type, Facts = facts });
        model.Entities.Add(entity);

        return DomainModelMapper.ToClassDefinitions(model)
            .Single(c => c.Name == "Customer").Properties.Single(p => p.Name == name);
    }

    private static PropertyRule? Rule(PropertyDefinition property, string ruleName)
        => property.Rules.FirstOrDefault(
            r => string.Equals(r.Name, ruleName, StringComparison.OrdinalIgnoreCase));
}
