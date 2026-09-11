using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidatorStagesTests
{
    [TestMethod]
    public void AModelWithOnlyFieldRules_DeclaresOneStage()
    {
        // The mitigation for "seven stages is more machinery than a three-entity CRUD app needs",
        // asserted rather than assumed. Six empty stage methods per entity would be six times the
        // noise in the commonest app there is.
        var source = GeneratedFor(WithSurname());

        StringAssert.Contains(source, "private static void Field(");
        Assert.DoesNotContain("private static void Entity(", source);
        Assert.DoesNotContain("private static void Business(", source);
        Assert.DoesNotContain("private static void Transaction(", source);
        Assert.DoesNotContain("private static void Workflow(", source);
        Assert.DoesNotContain("private static void Database(", source);
    }

    [TestMethod]
    public void TheStageList_NamesTheStagesInSpecOrder()
    {
        // Security first, then Field. The ordering is the design, so it is asserted, not implied.
        var definition = WithSurname();
        var source = GeneratedFor(definition);

        StringAssert.Contains(source, "Stages =");
        var security = source.IndexOf("Security", System.StringComparison.Ordinal);
        var field = source.IndexOf("Field", System.StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, security, "the Security stage is always present");
        Assert.IsLessThan(field, security, "Security must run before Field");
    }

    [TestMethod]
    public void AModelWithNoRulesAtAll_StillCompilesAndStillDeclaresSecurity()
    {
        // Security is not conditional on there being any inferred rule: it is the one stage that
        // exists because of the operation, not because of the data.
        var definition = new ClassDefinition { Name = "Note" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var source = GeneratedFor(definition);

        StringAssert.Contains(source, "private static void Security(");
    }

    [TestMethod]
    public void EveryErrorCarriesItsProperty()
    {
        // Attribution at the point the check is written, which is what SaveResult later reads.
        // A sentence with a null property would surface as a whole-record error and no front end
        // could highlight the field.
        var source = GeneratedFor(WithSurname());

        StringAssert.Contains(source, "new ValidationError(\"Surname\"");
        Assert.DoesNotContain("new ValidationError(null, \"Surname", source);
    }

    private static ClassDefinition WithSurname()
    {
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var surname = new PropertyDefinition { Name = "Surname", Type = "string" };
        surname.Rules.Add(new PropertyRule { Name = "required" });
        surname.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = 50 });
        definition.Properties.Add(surname);

        return definition;
    }

    private static string GeneratedFor(ClassDefinition definition)
        => ValidatorTemplate.Generated("Acme", definition, []);
}
