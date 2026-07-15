using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class EntityRoleTests
{
    [Fact]
    public void RankOf_DocumentAndTransaction_OutrankItem()
    {
        Assert.True(EntityRoleRank.RankOf(EntityRole.Document) > EntityRoleRank.RankOf(EntityRole.Item));
        Assert.True(EntityRoleRank.RankOf(EntityRole.Transaction) > EntityRoleRank.RankOf(EntityRole.Item));
    }

    [Fact]
    public void RankOf_Item_OutranksDeviceAndPlace()
    {
        Assert.True(EntityRoleRank.RankOf(EntityRole.Item) > EntityRoleRank.RankOf(EntityRole.Device));
        Assert.True(EntityRoleRank.RankOf(EntityRole.Item) > EntityRoleRank.RankOf(EntityRole.Place));
    }

    [Fact]
    public void RankOf_DeviceAndPlace_OutrankParty()
    {
        Assert.True(EntityRoleRank.RankOf(EntityRole.Device) > EntityRoleRank.RankOf(EntityRole.Party));
        Assert.True(EntityRoleRank.RankOf(EntityRole.Place) > EntityRoleRank.RankOf(EntityRole.Party));
    }

    [Fact]
    public void RankOf_None_IsLowestOfAll()
    {
        foreach (var role in new[]
                 {
                     EntityRole.Party, EntityRole.Device, EntityRole.Place,
                     EntityRole.Item, EntityRole.Document, EntityRole.Transaction
                 })
            Assert.True(EntityRoleRank.RankOf(role) > EntityRoleRank.RankOf(EntityRole.None));
    }

    [Fact]
    public void RankOf_DeviceAndPlace_AreEqual()
    {
        // Equal ranks must NOT link to each other (a Warehouse must not point at a
        // CashRegister, nor the reverse) — this is the whole reason the rank is a separate
        // table rather than the enum's own ordinal.
        Assert.Equal(EntityRoleRank.RankOf(EntityRole.Device), EntityRoleRank.RankOf(EntityRole.Place));
    }

    [Fact]
    public void RankOf_DocumentAndTransaction_AreEqual()
    {
        // Same rule: a Receipt (Document) must not point at a Payment (Transaction) on rank
        // alone, nor the reverse.
        Assert.Equal(EntityRoleRank.RankOf(EntityRole.Document), EntityRoleRank.RankOf(EntityRole.Transaction));
    }
}
