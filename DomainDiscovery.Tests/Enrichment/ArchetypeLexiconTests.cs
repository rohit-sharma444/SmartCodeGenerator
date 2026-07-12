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
}
