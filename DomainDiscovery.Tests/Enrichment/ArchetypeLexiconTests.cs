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

    [Theory]
    // One head per role, each measured to have blocked a real candidate. The counts in the
    // comments are from the twelve-domain census in the design spec.
    [InlineData("institution", EntityRole.Place)]      // 14 — School, Bank, Hospital
    [InlineData("facility", EntityRole.Place)]         //  6 — Clinic, EmergencyDepartment
    [InlineData("room", EntityRole.Place)]
    [InlineData("professional", EntityRole.Party)]
    [InlineData("employee", EntityRole.Party)]         //      Concierge
    [InlineData("vehicle", EntityRole.Item)]
    [InlineData("animal", EntityRole.Item)]            //  3 — the veterinary domain
    [InlineData("account", EntityRole.Document)]       //  2 — the banking domain
    [InlineData("loan", EntityRole.Transaction)]       //  2
    [InlineData("booking", EntityRole.Event)]
    [InlineData("appointment", EntityRole.Event)]
    public void Resolve_AMeasuredHead_ShapesItWithItsRole(string head, EntityRole role)
    {
        var result = ArchetypeLexicon.Resolve(head);

        Assert.Equal(ShapeVerdict.Shaped, result.Verdict);
        Assert.Equal(role, result.Role);
        Assert.NotEmpty(result.Fields);
    }

    [Theory]
    // Measured heads that name an abstraction. These must REJECT, not merely fail to shape:
    // a candidate defined as "a specialty" (Radiology, Geriatric, Psychiatric) is not a class.
    [InlineData("provision")]
    [InlineData("specialty")]
    [InlineData("totality")]
    [InlineData("risk")]
    [InlineData("amount")]
    [InlineData("value")]
    public void Resolve_AMeasuredAbstraction_RejectsIt(string head)
    {
        Assert.Equal(ShapeVerdict.Abstract, ArchetypeLexicon.Resolve(head).Verdict);
    }

    [Fact]
    public void Resolve_APluralOfAWidenedHead_StillShapesIt()
    {
        // The table is keyed singular; callers must not have to know that.
        Assert.Equal(ShapeVerdict.Shaped, ArchetypeLexicon.Resolve("institutions").Verdict);
    }

    [Fact]
    public void Resolve_AHeadInNeitherTable_IsStillUnknown()
    {
        // Widening must not turn the table into an accept-everything: an unmeasured head has
        // no opinion attached to it, and the candidate is offered to the user, not kept.
        Assert.Equal(ShapeVerdict.Unknown, ArchetypeLexicon.Resolve("quibblewhomp").Verdict);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Apps")]
    [InlineData("Management")]
    [InlineData("Administration")]   // the verdict Resolve already gives, by name
    public void IsAbstractName_AWordForTheSoftwareOrAnAbstraction_IsTrue(string name)
    {
        Assert.True(ArchetypeLexicon.IsAbstractName(name));
    }

    [Theory]
    [InlineData("Book")]
    [InlineData("Tool")]
    [InlineData("Equipment")]
    public void IsAbstractName_AThingAnAppKeeps_IsFalse(string name)
    {
        Assert.False(ArchetypeLexicon.IsAbstractName(name));
    }

    [Fact]
    public void Resolve_ASoftwareWordAsADefinitionsHead_StaysUnknown()
    {
        // "an integrated library system is an enterprise resource planning system" is a topic,
        // and the topic recognizer refuses an Abstract head: the software words are for names.
        Assert.Equal(ShapeVerdict.Unknown, ArchetypeLexicon.Resolve("system").Verdict);
    }
}
