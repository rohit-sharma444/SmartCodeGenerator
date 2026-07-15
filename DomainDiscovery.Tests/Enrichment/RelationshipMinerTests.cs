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

    [Fact]
    public void Mine_TransactionReferencingDevice_StillLinks_PaymentGetsPaymentTerminalId()
    {
        // Existing behaviour that must not regress: a Transaction (rank 4) referencing a
        // Device (rank 2, strictly lower) is exactly the "only a document or transaction
        // carries a foreign key" rule working as intended.
        var model = new CandidateDomainModel();
        var payment = Entity("Payment", EntityRole.Transaction);
        var terminal = Entity("PaymentTerminal", EntityRole.Device, sourceTitle: "Payment terminal");
        model.Entities.Add(payment);
        model.Entities.Add(terminal);

        var article = Repeat(
            "The payment was processed by the payment terminal.", RelationshipMiner.MinCoOccurrences);

        _miner.Mine(model, article);

        var relationship = Assert.Single(payment.Relationships);
        Assert.Equal("PaymentTerminal", relationship.Target);
        Assert.Contains(payment.Fields, f => f.Name == "PaymentTerminalId");
        Assert.Empty(terminal.Relationships);
    }

    [Fact]
    public void Mine_PlaceCoOccurringWithParty_GetsNoRelationship_ARestaurantDoesNotHaveOneCustomer()
    {
        // Real end-to-end defect: on a real run, Restaurant { Name, Address, CustomerId }
        // pointed at Customer purely because Place (rank 2) outranked Party (rank 1). A
        // restaurant does not have ONE customer. Only Document/Transaction may reference
        // anything; a Place never does, no matter how often it co-occurs with a Party.
        var model = new CandidateDomainModel();
        var restaurant = Entity("Restaurant", EntityRole.Place);
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(restaurant);
        model.Entities.Add(customer);

        var article = Repeat("The customer sat down in the restaurant.", 5);

        _miner.Mine(model, article);

        Assert.Empty(restaurant.Relationships);
        Assert.Empty(customer.Relationships);
        Assert.DoesNotContain(restaurant.Fields, f => f.Name == "CustomerId");
    }

    [Fact]
    public void Mine_DeviceCoOccurringWithParty_GetsNoRelationship_ATillDoesNotHaveOneCustomer()
    {
        // Same real defect, Device flavour: CashRegister { ..., CustomerId } is junk - a till
        // does not have ONE customer. Device (rank 2) outranking Party (rank 1) is not enough
        // on its own; Device is not a referencer.
        var model = new CandidateDomainModel();
        var cashRegister = Entity("CashRegister", EntityRole.Device, sourceTitle: "Cash register");
        var customer = Entity("Customer", EntityRole.Party);
        model.Entities.Add(cashRegister);
        model.Entities.Add(customer);

        var article = Repeat("The customer paid the cash register directly.", 5);

        _miner.Mine(model, article);

        Assert.Empty(cashRegister.Relationships);
        Assert.Empty(customer.Relationships);
        Assert.DoesNotContain(cashRegister.Fields, f => f.Name == "CustomerId");
    }

    [Fact]
    public void Mine_ItemCoOccurringWithDevice_GetsNoRelationship_InventoryDoesNotReferenceAComputer()
    {
        // Same real defect, Item flavour: Inventory { Sku, Quantity, ComputerId } is junk.
        // Item (rank 3) outranking Device (rank 2) is not enough; only Document/Transaction
        // are referencers, so Item never acquires a foreign key either.
        var model = new CandidateDomainModel();
        var inventory = Entity("Inventory", EntityRole.Item);
        var computer = Entity("Computer", EntityRole.Device);
        model.Entities.Add(inventory);
        model.Entities.Add(computer);

        var article = Repeat("The inventory was tracked by the computer.", 5);

        _miner.Mine(model, article);

        Assert.Empty(inventory.Relationships);
        Assert.Empty(computer.Relationships);
        Assert.DoesNotContain(inventory.Fields, f => f.Name == "ComputerId");
    }

    [Fact]
    public void Mine_DocumentAndTransactionEqualRank_LinksNeitherWay()
    {
        // Document and Transaction deliberately share rank 4 (see EntityRoleRank remarks): a
        // Receipt must not gain a PaymentId, nor the reverse, on rank alone.
        var model = new CandidateDomainModel();
        var receipt = Entity("Receipt", EntityRole.Document);
        var payment = Entity("Payment", EntityRole.Transaction);
        model.Entities.Add(receipt);
        model.Entities.Add(payment);

        var article = Repeat("The receipt matched the payment exactly.", 5);

        _miner.Mine(model, article);

        Assert.Empty(receipt.Relationships);
        Assert.Empty(payment.Relationships);
    }
}
