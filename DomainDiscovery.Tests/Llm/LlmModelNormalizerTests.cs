using DomainDiscovery.Enrichment;
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

public class LlmModelNormalizerTests
{
    private static LlmClass Class(string name, string role, params LlmProperty[] properties)
        => new() { Name = name, Role = role, Purpose = "a thing", Properties = properties.ToList() };

    private static LlmProperty Property(string name, string type)
        => new() { Name = name, Type = type };

    [Fact]
    public void PascalCasesPropertyNamesTheModelWroteInSnakeCase()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("Order", "Transaction", Property("order_date", "datetime")) }
        };

        var model = LlmModelNormalizer.Normalize(raw);

        var order = model.Entities.Single(e => e.Name == "Order");
        Assert.Contains(order.Fields, f => f.Name == "OrderDate");
    }

    [Fact]
    public void MapsModelTypeNamesOntoRealCsharpTypes()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("Order", "Transaction",
                Property("Placed", "datetime"),
                Property("Total", "money"),
                Property("Note", "text")) }
        };

        var order = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Order");

        Assert.Equal("DateTime", order.Fields.Single(f => f.Name == "Placed").Type);
        Assert.Equal("decimal", order.Fields.Single(f => f.Name == "Total").Type);
        Assert.Equal("string", order.Fields.Single(f => f.Name == "Note").Type);
    }

    [Fact]
    public void AddsAKeyToAClassTheModelGaveNone()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("SaleItem", "Document", Property("Quantity", "int")) }
        };

        var item = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "SaleItem");

        var id = item.Fields.Single(f => f.Name == "Id");
        Assert.Equal("int", id.Type);
    }

    /// <summary>
    /// The relationship AND the key, which is the whole of a reference and not two views of one.
    /// </summary>
    /// <remarks>
    /// This test used to assert the opposite of its second line — that the scalar was DROPPED,
    /// because the relationship had replaced it. Downstream that is not true of anything:
    /// DomainModelMapper turns a relationship into navigation properties only, so the class came
    /// out with a Customer reference and no CustomerId beside it, and ForeignKeys.TargetsOf — which
    /// wants both — saw no foreign key at all. Seven masters, zero transactions, on a purchase
    /// ordering model. Asserting the field's TYPE as well: an int is what TargetsOf will accept.
    /// </remarks>
    [Fact]
    public void TurnsAForeignKeyColumnIntoARelationshipAndKeepsTheKey()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Order", "Transaction", Property("customer_id", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var order = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Order");

        Assert.Contains(order.Relationships, r => r.Target == "Customer");
        Assert.Contains(order.Fields, f => f.Name == "CustomerId" && f.Type == "int");

        // The model's own spelling is what gets dropped, not the field: one key, not two.
        Assert.Single(order.Fields, f => f.Name.Equals("CustomerId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(order.Fields, f => f.Name == "customer_id");
    }

    /// <summary>
    /// A document line referencing the document it belongs to — the relationship every lines grid
    /// in the tool is built on, and the one the rank rule refused.
    /// </summary>
    /// <remarks>
    /// Document and Transaction deliberately share rank 4, and equal ranks link in neither
    /// direction. That is right for a MINED link, where two classes in one sentence says nothing
    /// about which way a key points — and wrong for a column the model declared, where the
    /// direction is already stated. PurchaseOrderLine.PurchaseOrderId was refused on rank, kept as
    /// a stray int with no relationship, and no lines ever attached to any document.
    /// </remarks>
    [Fact]
    public void LetsALineReferenceTheDocumentItBelongsTo()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("PurchaseOrderLine", "Document", Property("purchase_order_id", "int")),
                Class("PurchaseOrder", "Transaction", Property("TotalPrice", "decimal"))
            }
        };

        var line = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "PurchaseOrderLine");

        Assert.Contains(line.Relationships, r => r.Target == "PurchaseOrder");
        Assert.Contains(line.Fields, f => f.Name == "PurchaseOrderId" && f.Type == "int");
    }

    /// <summary>
    /// A relationship the model states in its own relationships array, rather than by writing a
    /// column, arrives with the same key. Both routes go through CandidateReference.
    /// </summary>
    [Fact]
    public void GivesARelationshipTheModelDeclaredItsForeignKeyToo()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Order", "Transaction", Property("Reference", "string")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        raw.Classes[0].Relationships.Add(new LlmRelationship { Target = "Customer", Role = "has" });

        var order = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Order");

        Assert.Contains(order.Relationships, r => r.Target == "Customer");
        Assert.Contains(order.Fields, f => f.Name == "CustomerId" && f.Type == "int");
    }

    /// <summary>
    /// The defect EntityRoleRank.CanReference exists to stop. A real run once produced
    /// Restaurant.CustomerId; a 3B model proposes exactly the same thing, and the rule must
    /// still hold on this path.
    /// </summary>
    [Fact]
    public void RefusesAForeignKeyOnAClassThatMayNotCarryOne()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Restaurant", "Place", Property("customer_id", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var restaurant = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Restaurant");

        Assert.DoesNotContain(restaurant.Relationships, r => r.Target == "Customer");
    }

    /// <summary>A refused relationship must not silently delete the property it came from.</summary>
    [Fact]
    public void KeepsARefusedForeignKeyAsAPlainScalar()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Restaurant", "Place", Property("customer_id", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var restaurant = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Restaurant");

        Assert.Contains(restaurant.Fields, f => f.Name == "CustomerId" && f.Type == "int");
    }

    /// <summary>
    /// A class the model returned with no properties would otherwise scaffold a grid holding
    /// nothing but a read-only Id. EntityShaper, DataModelBuilder and ModelEditor all guarantee
    /// against that on their own paths; this path bypasses EntityShaper, so it must do it here.
    /// </summary>
    [Fact]
    public void GivesAnEditableFieldToAClassTheModelLeftEmpty()
    {
        var raw = new LlmDomainModel { Classes = { Class("Cash", "None") } };

        var entity = LlmModelNormalizer.Normalize(raw).Entities.Single();

        Assert.Contains(entity.Fields, f => f.Name == DefaultFields.NameField);
    }

    /// <summary>A class the model DID give fields to keeps exactly those — an Order has no Name.</summary>
    [Fact]
    public void DoesNotInventANameFieldForAClassThatAlreadyHasFields()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("Order", "Transaction", Property("Total", "decimal")) }
        };

        var order = LlmModelNormalizer.Normalize(raw).Entities.Single();

        Assert.DoesNotContain(order.Fields, f => f.Name == DefaultFields.NameField);
    }

    /// <summary>
    /// Both measured models name compound classes SaleItem and OrderItem. ToEntityName would
    /// reduce those to "Saleitem" and "Orderitem" — a class name no user typed and no scaffolder
    /// should emit. This is why the normalizer uses ToUserTypedEntityName.
    /// </summary>
    [Fact]
    public void KeepsACompoundClassNameAsTheModelWroteIt()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("SaleItem", "Document", Property("Quantity", "int")) }
        };

        Assert.Equal("SaleItem", LlmModelNormalizer.Normalize(raw).Entities.Single().Name);
    }

    /// <summary>
    /// The default model writes SaleID, not SaleId. Case-sensitive suffix checks would miss
    /// every foreign key it produces and then add a duplicate key beside the one it gave.
    /// </summary>
    [Fact]
    public void RecognizesTheAllCapsIdSpellingTheDefaultModelWrites()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Sale", "Transaction", Property("SaleID", "int"), Property("CustomerID", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var sale = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Sale");

        // Its own key is the one canonical Id, not SaleID beside an Id (see LlmKeyFieldTests).
        Assert.DoesNotContain(sale.Fields, f => f.Name == "SaleID");
        Assert.Single(sale.Fields, f => f.Name == "Id");

        // The reference to Customer became a relationship despite the all-caps spelling.
        Assert.Contains(sale.Relationships, r => r.Target == "Customer");
    }

    [Fact]
    public void DropsAClassWhoseNameCouldNeverCompile()
    {
        var raw = new LlmDomainModel { Classes = { Class("2nd course", "Item", Property("Name", "string")) } };

        var model = LlmModelNormalizer.Normalize(raw);

        Assert.Empty(model.Entities);
        Assert.Contains(model.Rejected, r => r.Contains("2nd course"));
    }

    [Fact]
    public void RecordsWhyEachClassIsThereSoWhyCanAnswer()
    {
        var raw = new LlmDomainModel { Classes = { Class("Customer", "Party", Property("Name", "string")) } };
        raw.Classes[0].Purpose = "someone who buys things";

        var model = LlmModelNormalizer.Normalize(raw);

        var decision = model.Decisions.Single(d => d.Name == "Customer");
        Assert.Equal(DecisionStage.ModelProposal, decision.Stage);
        Assert.Equal(DecisionOutcome.Kept, decision.Outcome);
        Assert.Equal(DecisionReasons.ProposedByTheModel, decision.Reason);
        Assert.Equal("someone who buys things", decision.Evidence);
    }

    [Fact]
    public void MarksEveryClassAsComingFromTheModel()
    {
        var raw = new LlmDomainModel { Classes = { Class("Customer", "Party", Property("Name", "string")) } };

        var customer = LlmModelNormalizer.Normalize(raw).Entities.Single();

        Assert.Equal(Provenance.Model, customer.Provenance);
        Assert.Equal(EntityRole.Party, customer.Role);
    }

    [Fact]
    public void FallsBackToNoRoleWhenTheModelInventsOne()
    {
        var raw = new LlmDomainModel { Classes = { Class("Widget", "Doohickey", Property("Name", "string")) } };

        Assert.Equal(EntityRole.None, LlmModelNormalizer.Normalize(raw).Entities.Single().Role);
    }

    [Fact]
    public void DropsARelationshipToAClassThatIsNotInTheModel()
    {
        var raw = new LlmDomainModel { Classes = { Class("Order", "Transaction", Property("Total", "decimal")) } };
        raw.Classes[0].Relationships.Add(new LlmRelationship { Target = "Ghost", Role = "has" });

        Assert.Empty(LlmModelNormalizer.Normalize(raw).Entities.Single().Relationships);
    }

    /// <summary>
    /// A class named "Id" must not get a field named "Id" inserted by EnsureKey — that emits
    /// `class Id { public int Id { get; set; } }`, which is CS0542 and will not compile.
    /// DefaultFields.ApplyTo still gives it a Name field, so the grid stays editable.
    /// </summary>
    [Fact]
    public void GivesAClassNamedIdASaneShapeInsteadOfADuplicateMember()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("Id", "None", Property("Id", "int")) }
        };

        var entity = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Id");

        Assert.DoesNotContain(entity.Fields, f => f.Name == "Id");
        Assert.Contains(entity.Fields, f => f.Name == DefaultFields.NameField);
    }

    /// <summary>A second class normalizing to a name already taken must not vanish silently —
    /// `why` needs a trail for it just like the invalid-identifier branch above it.</summary>
    [Fact]
    public void RecordsADuplicateClassNameAsRejectedRatherThanDroppingItSilently()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Order", "Transaction", Property("Total", "decimal")),
                Class("order", "Transaction", Property("Note", "string"))
            }
        };

        var model = LlmModelNormalizer.Normalize(raw);

        Assert.Contains(model.Rejected, r => r == "order");
        Assert.Contains(model.Decisions, d =>
            d.Name == "order"
            && d.Stage == DecisionStage.ModelProposal
            && d.Outcome == DecisionOutcome.Rejected
            && d.Reason == DecisionReasons.AlreadyNamed);
    }
}
