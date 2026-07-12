using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class ArchetypeLexiconTests
{
    [Fact]
    public void Resolve_AKnownHead_ShapesIt()
    {
        var result = ArchetypeLexicon.Resolve("document");

        Assert.Equal(ShapeVerdict.Shaped, result.Verdict);
        Assert.Equal(new[] { "Number", "IssuedOn", "Total" }, result.Fields.Select(f => f.Name));
        Assert.Equal("decimal", result.Fields.Single(f => f.Name == "Total").Type);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("machine")]
    [InlineData("recipient")]
    [InlineData("business")]
    [InlineData("card")]
    public void Resolve_EachArchetypeHead_ShapesIt(string head)
    {
        Assert.Equal(ShapeVerdict.Shaped, ArchetypeLexicon.Resolve(head).Verdict);
    }

    [Theory]
    // These are what System, Business-as-practice, Data and Management actually resolve to.
    [InlineData("group")]
    [InlineData("practice")]
    [InlineData("collection")]
    [InlineData("concept")]
    [InlineData("administration")]
    public void Resolve_AnAbstractHead_RejectsIt(string head)
    {
        var result = ArchetypeLexicon.Resolve(head);

        Assert.Equal(ShapeVerdict.Abstract, result.Verdict);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Resolve_AHeadItHasNeverSeen_IsUnknown_NotRejected()
    {
        // Unknown is not denial: the entity becomes a suggestion, not a deletion.
        var result = ArchetypeLexicon.Resolve("kiosk");

        Assert.Equal(ShapeVerdict.Unknown, result.Verdict);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Resolve_Blank_IsUnknown()
    {
        Assert.Equal(ShapeVerdict.Unknown, ArchetypeLexicon.Resolve("").Verdict);
    }

    [Fact]
    public void Resolve_APluralHead_ResolvesTheSameAsItsSingular()
    {
        // HypernymExtractor already singularizes, but Resolve must not depend on a caller's
        // discipline: "Self-checkouts are machines..." only stops being a suggestion because
        // this holds on its own.
        var result = ArchetypeLexicon.Resolve("machines");

        Assert.Equal(ShapeVerdict.Shaped, result.Verdict);
        Assert.Equal(
            new[] { "Name", "Model", "SerialNumber" }, result.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Resolve_Business_IsNotMangledBySingularization()
    {
        // "business" ends in "ss", which NameNormalizer's singularizer deliberately leaves
        // alone. A careless plural-strip would corrupt it to "busines" and lose the archetype.
        var result = ArchetypeLexicon.Resolve("business");

        Assert.Equal(ShapeVerdict.Shaped, result.Verdict);
        Assert.Equal(new[] { "Name", "Address" }, result.Fields.Select(f => f.Name));
    }

    [Theory]
    // Unaffected by singularization: verifies the fold-to-singular lookup does not disturb
    // words that already resolved correctly before it existed.
    [InlineData("goods", ShapeVerdict.Shaped)]
    [InlineData("collection", ShapeVerdict.Abstract)]
    [InlineData("group", ShapeVerdict.Abstract)]
    [InlineData("class", ShapeVerdict.Unknown)]
    public void Resolve_AfterSingularization_StillResolvesAsBefore(string head, ShapeVerdict expected)
    {
        Assert.Equal(expected, ArchetypeLexicon.Resolve(head).Verdict);
    }

    [Fact]
    public void Resolve_Display_HasTheSameShapeAsDevice()
    {
        // Unlocks Touchscreen: "A touchscreen is a type of display that can detect touch input..."
        var result = ArchetypeLexicon.Resolve("display");

        Assert.Equal(ShapeVerdict.Shaped, result.Verdict);
        Assert.Equal(
            new[] { "Name", "Model", "SerialNumber" }, result.Fields.Select(f => f.Name));
    }

    [Theory]
    [InlineData("money")]
    [InlineData("currency")]
    public void Resolve_MoneyAndItsSynonym_ShapeToAnAmountAndCurrency(string head)
    {
        // Unlocks Cash: "Cash is money in the tangible form of currency."
        var result = ArchetypeLexicon.Resolve(head);

        Assert.Equal(ShapeVerdict.Shaped, result.Verdict);
        Assert.Equal(new[] { "Amount", "Currency" }, result.Fields.Select(f => f.Name));
        Assert.Equal("decimal", result.Fields.Single(f => f.Name == "Amount").Type);
    }

    [Fact]
    public void Resolve_Device_HasRoleDevice()
    {
        Assert.Equal(EntityRole.Device, ArchetypeLexicon.Resolve("device").Role);
    }

    [Fact]
    public void Resolve_Document_HasRoleDocument()
    {
        Assert.Equal(EntityRole.Document, ArchetypeLexicon.Resolve("document").Role);
    }

    [Fact]
    public void Resolve_Card_HasRoleItem()
    {
        // "card" shapes like a document (Number, Holder, ExpiresOn) but a credit card is
        // something a Party carries, not something that owns a Party — it must rank as Item.
        Assert.Equal(EntityRole.Item, ArchetypeLexicon.Resolve("card").Role);
    }

    [Fact]
    public void Resolve_Money_HasRoleNone()
    {
        // Cash is not something other classes point at.
        Assert.Equal(EntityRole.None, ArchetypeLexicon.Resolve("money").Role);
    }

    [Fact]
    public void Resolve_AnAbstractHead_HasRoleNone()
    {
        Assert.Equal(EntityRole.None, ArchetypeLexicon.Resolve("group").Role);
    }

    [Fact]
    public void Resolve_AHeadItHasNeverSeen_HasRoleNone()
    {
        Assert.Equal(EntityRole.None, ArchetypeLexicon.Resolve("kiosk").Role);
    }
}
