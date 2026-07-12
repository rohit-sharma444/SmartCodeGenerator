using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class RelationshipMinerTests
{
    private readonly RelationshipMiner _miner = new();

    private static CandidateEntity Entity(
        string name, EntityRole role, string? sourceTitle = null, Provenance provenance = Provenance.Wiki)
        => new()
        {
            Name = name,
            Role = role,
            SourceTitle = sourceTitle,
            Provenance = provenance
        };

    private static string Repeat(string sentence, int times)
        => string.Join(" ", Enumerable.Repeat(sentence, times));

    [Fact]
    public void Mine_PairRecurringAtThreshold_LinksTheHigherRankedClassToTheLower()
    {
        var model = new CandidateDomainModel();
        var receipt = Entity("Receipt", EntityRole.Document);
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(receipt);
        model.Entities.Add(customer);

        var article = Repeat(
            "The customer was handed a receipt.", RelationshipMiner.MinCoOccurrences);

        _miner.Mine(model, article);

        var relationship = Assert.Single(receipt.Relationships);
        Assert.Equal("Customer", relationship.Target);
        Assert.Equal("references", relationship.Role);

        var field = Assert.Single(receipt.Fields, f => f.Name == "CustomerId");
        Assert.Equal("int", field.Type);
        Assert.Equal(receipt.Provenance, field.Provenance);

        // Direction is one-way: Customer must not gain a ReceiptId.
        Assert.Empty(customer.Relationships);
        Assert.DoesNotContain(customer.Fields, f => f.Name == "ReceiptId");
    }

    [Fact]
    public void Mine_PairBelowThreshold_IsNotLinked()
    {
        var model = new CandidateDomainModel();
        var receipt = Entity("Receipt", EntityRole.Document);
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(receipt);
        model.Entities.Add(customer);

        var article = Repeat(
            "The customer was handed a receipt.", RelationshipMiner.MinCoOccurrences - 1);

        _miner.Mine(model, article);

        Assert.Empty(receipt.Relationships);
        Assert.Empty(customer.Relationships);
        Assert.DoesNotContain(receipt.Fields, f => f.Name == "CustomerId");
    }

    [Fact]
    public void Mine_TheOverlapRegression_CashNeverLinksToCashRegister()
    {
        // On the real Point of sale article, Cash~CashRegister scored 12 - the HIGHEST pair
        // in the whole article - purely because "cash" is a substring of "cash register".
        // The longest-phrase-first / span-consuming rule must make this pair score zero.
        var model = new CandidateDomainModel();
        var cash = Entity("Cash", EntityRole.None);
        var cashRegister = Entity("CashRegister", EntityRole.Device, sourceTitle: "Cash register");
        model.Entities.Add(cash);
        model.Entities.Add(cashRegister);

        var article = Repeat(
            "The cash register prints a receipt for every sale.", 6);

        _miner.Mine(model, article);

        Assert.Empty(cash.Relationships);
        Assert.Empty(cashRegister.Relationships);
        Assert.DoesNotContain(cashRegister.Fields, f => f.Name == "CashId");
        Assert.DoesNotContain(cash.Fields, f => f.Name == "CashRegisterId");
    }

    [Fact]
    public void Mine_EqualRankedPair_LinksNeitherWay()
    {
        var model = new CandidateDomainModel();
        var customer = Entity("Customer", EntityRole.Party);
        var merchant = Entity("Merchant", EntityRole.Party);
        model.Entities.Add(customer);
        model.Entities.Add(merchant);

        var article = Repeat("The customer paid the merchant directly.", 5);

        _miner.Mine(model, article);

        Assert.Empty(customer.Relationships);
        Assert.Empty(merchant.Relationships);
    }

    [Fact]
    public void Mine_RoleNoneEntity_NeverLinks_EvenWithHeavyCoOccurrence()
    {
        var model = new CandidateDomainModel();
        var bookkeeping = Entity("Bookkeeping", EntityRole.None);
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(bookkeeping);
        model.Entities.Add(customer);

        var article = Repeat("The customer relied on bookkeeping every day.", 5);

        _miner.Mine(model, article);

        Assert.Empty(bookkeeping.Relationships);
        Assert.Empty(customer.Relationships);
    }

    [Fact]
    public void Mine_CapsLinksPerEntity_ToTheThreeHighestScoringPairs()
    {
        var model = new CandidateDomainModel();
        var order = Entity("Order", EntityRole.Document);
        var alpha = Entity("Alpha", EntityRole.Party);
        var beta = Entity("Beta", EntityRole.Party);
        var gamma = Entity("Gamma", EntityRole.Party);
        var delta = Entity("Delta", EntityRole.Party);
        var epsilon = Entity("Epsilon", EntityRole.Party);
        model.Entities.AddRange(new[] { order, alpha, beta, gamma, delta, epsilon });

        var article = string.Join(" ", new[]
        {
            Repeat("Order concerns Alpha.", 7),
            Repeat("Order concerns Beta.", 6),
            Repeat("Order concerns Gamma.", 5),
            Repeat("Order concerns Delta.", 4),
            Repeat("Order concerns Epsilon.", RelationshipMiner.MinCoOccurrences)
        });

        _miner.Mine(model, article);

        Assert.Equal(RelationshipMiner.MaxLinksPerEntity, order.Relationships.Count);
        Assert.Equal(
            new[] { "Alpha", "Beta", "Gamma" },
            order.Relationships.Select(r => r.Target).OrderBy(n => n));
        Assert.DoesNotContain(order.Relationships, r => r.Target is "Delta" or "Epsilon");
    }

    [Fact]
    public void Mine_FieldGuard_ExistingFieldName_RecordsRelationshipWithoutDuplicateField()
    {
        var model = new CandidateDomainModel();
        var order = Entity("Order", EntityRole.Document);
        order.Fields.Add(new CandidateField { Name = "CustomerId", Type = "string", Provenance = Provenance.Seed });
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(order);
        model.Entities.Add(customer);

        var article = Repeat("Order placed by the customer.", RelationshipMiner.MinCoOccurrences);

        _miner.Mine(model, article);

        Assert.Single(order.Relationships, r => r.Target == "Customer");
        var field = Assert.Single(order.Fields, f => string.Equals(f.Name, "CustomerId", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("string", field.Type); // untouched - not overwritten by the miner
    }

    [Fact]
    public void Mine_FieldGuard_WouldCollideWithOwnEntityName_RecordsRelationshipWithoutField()
    {
        // class SupplierId { int SupplierId } is CS0542 - the referencing entity's own name
        // must never be reused as the generated reference field's name.
        var model = new CandidateDomainModel();
        var referencing = Entity("SupplierId", EntityRole.Document, sourceTitle: "SupplierId");
        var supplier = Entity("Supplier", EntityRole.Party);
        model.Entities.Add(referencing);
        model.Entities.Add(supplier);

        var article = Repeat("The SupplierId processed the Supplier.", RelationshipMiner.MinCoOccurrences);

        _miner.Mine(model, article);

        Assert.Single(referencing.Relationships, r => r.Target == "Supplier");
        Assert.DoesNotContain(referencing.Fields, f => f.Name == "SupplierId");
    }

    [Fact]
    public void Mine_BlankArticleText_LeavesModelUntouched_AndDoesNotThrow()
    {
        var model = new CandidateDomainModel();
        var receipt = Entity("Receipt", EntityRole.Document);
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(receipt);
        model.Entities.Add(customer);

        var exception = Record.Exception(() =>
        {
            _miner.Mine(model, string.Empty);
            _miner.Mine(model, "   ");
            _miner.Mine(model, null!);
        });

        Assert.Null(exception);
        Assert.Empty(receipt.Relationships);
        Assert.Empty(customer.Relationships);
        Assert.Empty(receipt.Fields);
        Assert.Empty(customer.Fields);
    }

    [Fact]
    public void Mine_PluralMention_CountsAsAMentionOfTheSingularClass()
    {
        var model = new CandidateDomainModel();
        var receipt = Entity("Receipt", EntityRole.Document);
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(receipt);
        model.Entities.Add(customer);

        var article = Repeat(
            "Customers keep their receipts.", RelationshipMiner.MinCoOccurrences);

        _miner.Mine(model, article);

        var relationship = Assert.Single(receipt.Relationships);
        Assert.Equal("Customer", relationship.Target);
    }
}
