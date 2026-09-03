using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>
/// Putting the two phases back together. This is what makes the split worth its extra minute: the
/// roster is cheap and arrives whole, so every class it named survives into the final model even
/// when the detail pass only got part way.
/// </summary>
public class TwoPhaseModelTests
{
    private static LlmClass Named(string name, string role = "Item") =>
        new() { Name = name, Role = role, Purpose = $"the {name}" };

    private static LlmClass Detailed(string name, params string[] properties)
    {
        var c = Named(name);

        foreach (var p in properties)
            c.Properties.Add(new LlmProperty { Name = p, Type = "string" });

        return c;
    }

    private static LlmDomainModel Model(params LlmClass[] classes)
    {
        var m = new LlmDomainModel();
        m.Classes.AddRange(classes);
        return m;
    }

    [Fact]
    public void TakesTheDetailWhereThereIsSome()
    {
        var merged = TwoPhaseModel.Merge(
            Model(Named("Product"), Named("Sale")),
            Model(Detailed("Product", "Name", "Price"), Detailed("Sale", "Total")));

        Assert.Equal(["Product", "Sale"], merged.Classes.Select(c => c.Name));
        Assert.Equal(2, merged.Classes[0].Properties.Count);
        Assert.Single(merged.Classes[1].Properties);
    }

    [Fact]
    public void KeepsARosterClassTheDetailPassNeverReached()
    {
        // The whole point. The detail run was cut off after Product, and Sale and Store would
        // simply have vanished had the merge taken only what the second pass returned.
        var merged = TwoPhaseModel.Merge(
            Model(Named("Product"), Named("Sale"), Named("Store", "Place")),
            Model(Detailed("Product", "Name")));

        Assert.Equal(["Product", "Sale", "Store"], merged.Classes.Select(c => c.Name));
        Assert.Empty(merged.Classes[1].Properties);
        Assert.Equal("Place", merged.Classes[2].Role);
    }

    [Fact]
    public void KeepsTheRosterOrderRatherThanTheDetailOrder()
    {
        var merged = TwoPhaseModel.Merge(
            Model(Named("Product"), Named("Sale")),
            Model(Detailed("Sale", "Total"), Detailed("Product", "Name")));

        Assert.Equal(["Product", "Sale"], merged.Classes.Select(c => c.Name));
    }

    [Fact]
    public void MatchesNamesWithoutCaringAboutCase()
    {
        // The second pass is told to return the same names and mostly does, but a 3B model will
        // still hand back "product" for "Product" often enough to matter.
        var merged = TwoPhaseModel.Merge(
            Model(Named("Product")), Model(Detailed("product", "Name")));

        var only = Assert.Single(merged.Classes);
        Assert.Equal("Product", only.Name);          // the roster's spelling wins
        Assert.Single(only.Properties);
    }

    [Fact]
    public void IgnoresAClassTheDetailPassInvented()
    {
        // It is told not to add classes. When it does anyway, the roster is the authority - an
        // invented class has no purpose, no agreed role, and was never shown to the user.
        var merged = TwoPhaseModel.Merge(
            Model(Named("Product")),
            Model(Detailed("Product", "Name"), Detailed("Warehouse", "Address")));

        Assert.Equal("Product", Assert.Single(merged.Classes).Name);
    }

    [Fact]
    public void KeepsTheRosterWholeWhenTheDetailPassProducedNothing()
    {
        var merged = TwoPhaseModel.Merge(Model(Named("Product"), Named("Sale")), null);

        Assert.Equal(["Product", "Sale"], merged.Classes.Select(c => c.Name));
    }

    [Fact]
    public void KeepsTheRostersRoleAndPurposeWhenTheDetailPassChangedThem()
    {
        // The roles were agreed in phase one and shown on screen. A second pass quietly
        // reclassifying Sale as an Item would change what the generator writes for it.
        var roster = Model(Named("Sale", "Transaction"));
        var detail = Model(Detailed("Sale", "Total"));
        detail.Classes[0].Role = "Item";
        detail.Classes[0].Purpose = string.Empty;

        var merged = TwoPhaseModel.Merge(roster, detail);

        var only = Assert.Single(merged.Classes);
        Assert.Equal("Transaction", only.Role);
        Assert.Equal("the Sale", only.Purpose);
        Assert.Single(only.Properties);
    }

    [Fact]
    public void CountsWhatStillHasNoDetail()
    {
        var roster = Model(Named("Product"), Named("Sale"), Named("Store"));

        Assert.Equal(
            ["Sale", "Store"],
            TwoPhaseModel.WithoutDetail(roster, Model(Detailed("Product", "Name")))
                .Select(c => c.Name));
    }
}
