using DomainDiscovery.Enrichment;
using DomainDiscovery.SeedExtraction;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class DomainFieldLexiconTests
{
    /// <summary>Every entity the lexicon shapes. Kept in one place so the invariants below cover all of them.</summary>
    private static readonly string[] KnownEntities =
    [
        "Recipe", "Ingredient", "Cookbook", "Meal", "Dish",
        "Payroll", "Employee", "Employer", "Company", "Department", "Salary", "Wage",
        "Overtime", "Deduction", "Garnishment", "Tax", "Bonus", "Benefit", "Income", "Timesheet",
        "Invoice", "Payment", "Receipt", "Refund", "Expense", "Budget", "Order", "Product",
        "Customer", "Client", "Supplier", "Vendor", "Account", "Transaction", "Contract",
        "Subscription", "Shipment", "Inventory", "Warehouse",
        "Library", "Book", "Material", "Member", "Loan", "Author", "Publisher", "Catalog",
        "User", "Role", "Permission",
    ];

    [Fact]
    public void Knows_AKnownEntity_AndNotAnArbitraryNoun()
    {
        Assert.True(DomainFieldLexicon.Knows("Recipe"));
        Assert.True(DomainFieldLexicon.Knows("recipe"));   // case-insensitive
        Assert.False(DomainFieldLexicon.Knows("Century"));
        Assert.False(DomainFieldLexicon.Knows(""));
    }

    [Fact]
    public void Fields_AnUnknownEntity_IsEmptyRatherThanNull()
        => Assert.Empty(DomainFieldLexicon.Fields("Century"));

    [Fact]
    public void EveryKnownEntity_HasAtLeastOneField()
    {
        // A shape with no fields would leave EntityShaper unable to tell this entity apart
        // from one the lexicon does not know at all.
        foreach (var entity in KnownEntities)
            Assert.NotEmpty(DomainFieldLexicon.Fields(entity));
    }

    [Fact]
    public void NoEntity_DeclaresAnIdField()
    {
        // DomainModelMapper prepends `int Id`, and JsonStore assigns it by reflection.
        foreach (var entity in KnownEntities)
            Assert.DoesNotContain(DomainFieldLexicon.Fields(entity),
                f => string.Equals(f.Name, "Id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoEntity_DeclaresAFieldNamedAfterItself()
    {
        // `class Recipe { public string Recipe { get; set; } }` is CS0542.
        foreach (var entity in KnownEntities)
            Assert.DoesNotContain(DomainFieldLexicon.Fields(entity),
                f => string.Equals(f.Name, entity, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryFieldName_IsAnIdentifierCSharpCanDeclare()
    {
        foreach (var entity in KnownEntities)
            foreach (var field in DomainFieldLexicon.Fields(entity))
                Assert.True(NameNormalizer.IsValidIdentifier(field.Name),
                    $"{entity}.{field.Name} is not a valid identifier");
    }

    [Fact]
    public void EveryFieldType_IsOneTheGeneratorSupports()
    {
        string[] supported = ["string", "int", "decimal", "bool", "DateTime"];

        foreach (var entity in KnownEntities)
            foreach (var field in DomainFieldLexicon.Fields(entity))
                Assert.Contains(field.Type, supported);
    }

    [Fact]
    public void NoEntity_DeclaresTheSameFieldTwice()
    {
        foreach (var entity in KnownEntities)
        {
            var names = DomainFieldLexicon.Fields(entity).Select(f => f.Name).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void EveryKnownEntity_HasAnAnnotatedRole()
    {
        // Role/Permission are legitimately EntityRole.None (they are not something another
        // class points at), which is the same value C# gives a forgotten entry by default. So
        // this checks presence in the Role table itself, not the resolved value — a future
        // entity added to the field table without a matching Role entry must fail here rather
        // than silently resolving to a default that looks identical to a deliberate None.
        foreach (var entity in KnownEntities)
            Assert.True(DomainFieldLexicon.HasAnnotatedRole(entity), $"{entity} has no annotated Role");
    }

    [Fact]
    public void KnownEntities_MatchesTheLexiconsOwnEntityList()
    {
        // Guards the guard above: if this drifts from the lexicon's real key set, the role
        // test could pass by only checking names nobody added yet.
        Assert.Equal(
            KnownEntities.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
            DomainFieldLexicon.KnownEntityNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
    }
}
