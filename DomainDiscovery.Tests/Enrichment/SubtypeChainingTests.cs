using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class SubtypeChainingTests
{
    private static ShapeCandidate Unknown(string name, string head)
        => new(name, head, new ArchetypeResult(
            ShapeVerdict.Unknown, Array.Empty<DomainFieldLexicon.FieldShape>(), EntityRole.None));

    /// <summary>A candidate the archetype table already has an opinion on.</summary>
    private static ShapeCandidate Shaped(string name, string head)
        => new(name, head, ArchetypeLexicon.Resolve(head));

    [Fact]
    public void Resolve_ACandidateDefinedAsAnotherCandidate_InheritsItsShape()
    {
        // "A motel is a hotel designed for motorists." Nothing in the archetype table says
        // "motel", but Hotel is in the model and is a Place, so Motel is one too.
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Shaped("Hotel", "establishment"),
            Unknown("Motel", "hotel"),
        });

        Assert.Equal(ShapeVerdict.Shaped, resolved["Motel"].Verdict);
        Assert.Equal(EntityRole.Place, resolved["Motel"].Role);
        Assert.NotEmpty(resolved["Motel"].Fields);
    }

    [Fact]
    public void Resolve_ATwoStepChain_ResolvesThroughTheMiddle()
    {
        // ResortHotel is a hotel; Hotel is an establishment. Both hops must run.
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Unknown("ResortHotel", "motel"),
            Unknown("Motel", "hotel"),
            Shaped("Hotel", "establishment"),
        });

        Assert.Equal(EntityRole.Place, resolved["ResortHotel"].Role);
        Assert.Equal(EntityRole.Place, resolved["Motel"].Role);
    }

    [Fact]
    public void Resolve_ACycle_Terminates_AndLeavesBothUnknown()
    {
        // "An inn is a hostel"; "a hostel is an inn". Neither can shape the other.
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Unknown("Inn", "hostel"),
            Unknown("Hostel", "inn"),
        });

        Assert.Equal(ShapeVerdict.Unknown, resolved["Inn"].Verdict);
        Assert.Equal(ShapeVerdict.Unknown, resolved["Hostel"].Verdict);
    }

    [Fact]
    public void Resolve_ACandidateWhoseHeadNamesNothingInTheModel_StaysUnknown()
    {
        var resolved = SubtypeChaining.Resolve(new[] { Unknown("Motel", "hotel") });

        Assert.Equal(ShapeVerdict.Unknown, resolved["Motel"].Verdict);
    }

    [Fact]
    public void Resolve_DoesNotChainOffAnAbstractParent()
    {
        // "Management is a practice" is a rejection, not a shape. Nothing may inherit it —
        // inheriting Abstract would DELETE the child, which is far worse than suggesting it.
        var practice = new ShapeCandidate("Management", "practice", ArchetypeLexicon.Resolve("practice"));
        var resolved = SubtypeChaining.Resolve(new[] { practice, Unknown("Supervision", "management") });

        Assert.Equal(ShapeVerdict.Unknown, resolved["Supervision"].Verdict);
    }

    [Fact]
    public void Resolve_APluralHead_StillMatchesTheSingularCandidate()
    {
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Shaped("Hotel", "establishment"),
            Unknown("Motel", "hotels"),
        });

        Assert.Equal(EntityRole.Place, resolved["Motel"].Role);
    }
}
