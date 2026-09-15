using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Model fixtures shared across the validator-stage tests (tasks 6, 7, 8 and 11), built once so a
/// later task does not grow its own private copy of the same shape under a name another task
/// already claimed.
/// </summary>
internal static class ValidatorFixtures
{
    /// <summary>A master with no foreign key at all. Carries both <c>Code</c> and <c>Status</c> —
    /// later tasks each do a <c>.Properties.Single(p => p.Name == "...")</c> against one of these
    /// two on this same fixture, and <c>Single</c> throws on a miss, so both are declared here even
    /// though task 6's own tests read neither.</summary>
    public static ClassDefinition PlainCustomer()
    {
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Code", Type = "string" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });
        return definition;
    }

    /// <summary>A document line carrying a foreign key to Product, shaped so
    /// <see cref="AppScaffolding.ForeignKeys.TargetsOf"/> recognises it: the <c>ProductId</c> int
    /// AND a sibling nav property typed <c>Product</c> — an int alone is not enough by that method's
    /// own definition.</summary>
    public static ClassDefinition LineWithProductId()
    {
        var definition = new ClassDefinition { Name = "OrderLine" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "ProductId", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Product", Type = "Product" });
        return definition;
    }

    /// <summary>A master carrying an optional foreign key to Region, shaped the same way —
    /// <c>RegionId</c> plus the <c>Region</c> nav property — so real detection sees it too.</summary>
    public static ClassDefinition CustomerWithRegionId()
    {
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "RegionId", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Region", Type = "Region" });
        return definition;
    }
}
