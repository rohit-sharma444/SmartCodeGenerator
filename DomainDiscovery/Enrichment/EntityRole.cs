namespace DomainDiscovery.Enrichment;

/// <summary>
/// What kind of thing a discovered class is. Co-occurrence in prose (two classes named in the
/// same sentence) is symmetric — it says two classes are related, never which way a foreign
/// key should point. Role is what breaks that symmetry: every class carries one, and
/// <see cref="EntityRoleRank"/> ranks them so a relationship miner can decide, from the pair's
/// two roles alone, which class references the other.
/// </summary>
public enum EntityRole
{
    /// <summary>Not something another class points at (e.g. Cash) — never links, either way.</summary>
    None,

    /// <summary>A person or organisation-as-actor: Customer, Employee, Supplier.</summary>
    Party,

    /// <summary>A physical machine: CashRegister, PaymentTerminal, Scanner.</summary>
    Device,

    /// <summary>A physical or organisational location: Restaurant, Warehouse, Store.</summary>
    Place,

    /// <summary>A thing bought, held or consumed: Product, Ingredient, CreditCard.</summary>
    Item,

    /// <summary>A record of something that happened, without itself being the event: Receipt, Invoice.</summary>
    Document,

    /// <summary>The event of value changing hands: Payment, Refund, Order.</summary>
    Transaction
}

/// <summary>
/// Ranks <see cref="EntityRole"/> so direction can be derived from a pair of roles rather than
/// guessed or hard-coded per pair.
/// </summary>
/// <remarks>
/// THE RULE THIS RANK EXISTS FOR: the higher rank references the lower — a Document or
/// Transaction (rank 4) points at an Item (rank 3), which points at a Device or Place (rank 2),
/// which points at a Party (rank 1). EQUAL ranks link in NEITHER direction: Device and Place
/// intentionally share rank 2 (a Warehouse must not gain a CashRegisterId, nor the reverse),
/// and likewise Document and Transaction share rank 4 (a Receipt must not gain a PaymentId on
/// rank alone). Rank 0 (<see cref="EntityRole.None"/>) never links, in either direction — Cash
/// is not a thing other classes reference. Because two roles deliberately share a rank, the
/// rank cannot be the enum's own ordinal (which is strictly increasing by declaration order);
/// it must be looked up from this separate table instead.
/// </remarks>
public static class EntityRoleRank
{
    /// <summary>The rank of <paramref name="role"/>, for comparing two classes' roles to decide
    /// which one — if either — references the other. See the type-level remarks for the rule.</summary>
    public static int RankOf(EntityRole role) => role switch
    {
        EntityRole.None => 0,
        EntityRole.Party => 1,
        EntityRole.Device => 2,
        EntityRole.Place => 2,
        EntityRole.Item => 3,
        EntityRole.Document => 4,
        EntityRole.Transaction => 4,
        _ => 0
    };
}
