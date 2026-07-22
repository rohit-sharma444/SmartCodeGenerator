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
    Transaction,

    /// <summary>
    /// Something scheduled that happens to somebody, somewhere, between two times: Booking,
    /// Appointment, Reservation, Flight, Visit, Admission. Distinct from
    /// <see cref="Transaction"/> (which has an amount and a method) and from
    /// <see cref="Document"/> (which is a record issued about something, not the thing itself).
    /// </summary>
    Event
}

/// <summary>
/// Ranks <see cref="EntityRole"/> so direction can be derived from a pair of roles rather than
/// guessed or hard-coded per pair.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE THIS RANK EXISTS FOR: a Document, Transaction, or Event (rank 4) MAY point at anything of
/// strictly lower rank — an Item (rank 3), a Device or Place (rank 2), or a Party (rank 1).
/// EQUAL ranks link in NEITHER direction: Device and Place intentionally share rank 2 (a
/// Warehouse must not gain a CashRegisterId, nor the reverse), and likewise Document and
/// Transaction share rank 4 (a Receipt must not gain a PaymentId on rank alone). Rank 0
/// (<see cref="EntityRole.None"/>) never links, in either direction — Cash is not a thing other
/// classes reference. Because two roles deliberately share a rank, the rank cannot be the
/// enum's own ordinal (which is strictly increasing by declaration order); it must be looked up
/// from this separate table instead.
/// </para>
/// <para>
/// RANK ALONE IS NOT ENOUGH TO DECIDE WHO REFERENCES WHOM — see <see cref="CanReference"/>.
/// A real end-to-end run against this rank table on its own produced
/// <c>Restaurant.CustomerId</c> (Place, rank 2, outranking Party, rank 1),
/// <c>CashRegister.CustomerId</c> (Device outranking Party) and <c>Inventory.ComputerId</c>
/// (Item outranking Device) — all junk: a restaurant does not have ONE customer, a till does
/// not have ONE customer, and nothing is gained by an inventory record pointing at "the"
/// computer. In a real schema only a document, transaction, or event carries a foreign key; an Item,
/// Device, Place or Party is something other rows point AT, never something that itself points
/// anywhere. <see cref="CanReference"/> is the explicit gate for that: the rank comparison in
/// <see cref="RankOf"/> still decides what a qualifying referencer may point at, but it no
/// longer decides who is allowed to be the referencer in the first place.
/// </para>
/// </remarks>
public static class EntityRoleRank
{
    /// <summary>The rank of <paramref name="role"/>, for comparing two classes' roles to decide
    /// what a referencer (see <see cref="CanReference"/>) may point at. See the type-level
    /// remarks for the rule.</summary>
    public static int RankOf(EntityRole role) => role switch
    {
        EntityRole.None => 0,
        EntityRole.Party => 1,
        EntityRole.Device => 2,
        EntityRole.Place => 2,
        EntityRole.Item => 3,
        EntityRole.Document => 4,
        EntityRole.Transaction => 4,
        EntityRole.Event => 4,
        _ => 0
    };

    /// <summary>
    /// Whether a class with this <see cref="EntityRole"/> is allowed to carry foreign keys at
    /// all — i.e. to be the referencing side of a mined relationship. Only <see
    /// cref="EntityRole.Document"/>, <see cref="EntityRole.Transaction"/> and <see
    /// cref="EntityRole.Event"/> qualify: a booking points at its guest and its room the same
    /// way an invoice points at its customer.
    /// </summary>
    /// <remarks>
    /// This exists because "the higher rank references the lower" is too permissive at the
    /// bottom end: a real run gave <c>Restaurant</c> (Place) a <c>CustomerId</c> and
    /// <c>CashRegister</c> (Device) a <c>CustomerId</c> purely because Place and Device
    /// outrank Party — despite neither a restaurant nor a till having "a" customer. A Document
    /// (a record of something that happened — Receipt, Invoice), a Transaction (the event of
    /// value changing hands — Payment, Order), or an Event (a scheduled thing — Booking,
    /// Appointment) is the only kind of class a real schema puts foreign keys on; an Item,
    /// Device, Place or Party is what other rows point at, never a pointer itself. Named and
    /// checked explicitly so this reads as "only documents, transactions, and events carry
    /// foreign keys" and cannot be mistaken for a general rank rule.
    /// </remarks>
    public static bool CanReference(EntityRole role)
        => role is EntityRole.Document or EntityRole.Transaction or EntityRole.Event;
}
