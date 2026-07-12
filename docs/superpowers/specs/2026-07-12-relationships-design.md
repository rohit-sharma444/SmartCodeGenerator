# Relating the discovered classes to each other

**Date:** 2026-07-12
**Branch:** `Seperation`
**Status:** Approved, not yet implemented
**Follows:** `2026-07-12-lexicon-gate-design.md`

## The problem

`build app: develop pointofsale application` now proposes 20 classes, and every one of
them stands alone. A `Receipt` belongs to no `Customer`; a `Payment` settles no `Order`.
The generated app is twenty unrelated tables.

`CandidateEntity.Relationships` — a `List<CandidateRelationship>` — has existed in the
model since it was written and **no production code has ever populated it**. This fills it.

## What the evidence supports

Measured on the cached Point of sale article (299 sentences, the 20 shaped classes):
**72 class pairs co-occur in a sentence**, and the strongest are exactly the ones a
point-of-sale model needs — Customer~Order (10), Customer~Payment (7),
Payment~PaymentTerminal (6), Order~Restaurant (5), Customer~Merchant (3).

The same measurement exposed three ways a naive reading of it goes wrong, and each becomes
a rule:

1. **Substring overlap.** `Cash~CashRegister` scored 12 — the highest pair in the article —
   purely because "cash" is a substring of "cash register". A shorter name matched inside a
   longer candidate's span is not a mention.
2. **One rambling sentence manufactures n² pairs.** A single sentence naming merchant,
   customer, cash, payment and register was the example behind half the top pairs. A pair
   needs to recur.
3. **Co-occurrence is symmetric; a foreign key is not.** Knowing Receipt and Customer are
   related does not say whether it is `Receipt.CustomerId` or `Customer.ReceiptId`.

## The design

### Which pairs

Same-sentence co-occurrence of the shaped classes in the topic article, filtered by:

- **Overlap exclusion.** Match the longest candidate phrases first and consume their spans,
  so a shorter name cannot match inside one ("cash" inside "cash register").
- **`MinCoOccurrences = 3`.** A pair must recur, which is what separates a real association
  from an accident of one long sentence.
- **`MaxLinksPerEntity = 3`**, highest count first. A class with eight foreign keys is not a
  clearer model than one with three.

A class is matched in prose by its **source phrase**: `SourceTitle` when it came from a
wikilink ("Cash register"), otherwise its own name lowercased ("customer"), allowing a
trailing plural.

### Which way it points

> **Amended 2026-07-12, during implementation.** The rule as originally written below —
> "the higher rank references the lower" — was too permissive at the bottom end. A real
> end-to-end run produced `Restaurant.CustomerId`, `CashRegister.CustomerId`, and
> `Inventory.ComputerId`: junk, because Place/Device/Item outranked Party/Device
> respectively, and the rank comparison alone was happy to wire them up. A restaurant does
> not have *one* customer, and a till does not have *one* customer. The rank table below is
> kept — it still decides what a qualifying class **may** point at — but a second, explicit
> gate was added: **only a class whose Role is Document or Transaction may be a referencer at
> all.** Everything else (Item, Device, Place, Party, None) references nothing, no matter how
> often it co-occurs with something of lower rank. This is implemented as
> `EntityRoleRank.CanReference(EntityRole)` in `DomainDiscovery/Enrichment/EntityRole.cs`, and
> applied in `RelationshipMiner.DirectedProposals`.

Every class carries a **Role**, and roles are ranked:

| Rank | Role | Example |
|---|---|---|
| 4 | Document, Transaction | Receipt, Invoice, Order, Payment |
| 3 | Item | Product, CreditCard, Ingredient |
| 2 | Device, Place | CashRegister, Restaurant, Warehouse |
| 1 | Party | Customer, Merchant, Employee |
| 0 | None | Cash, Bookkeeping — no links either way |

**Only Document and Transaction may reference anything.** A qualifying referencer may point
at any class of strictly lower rank — an Item, a Device or Place, or a Party. Equal ranks
still get no link — `Customer ~ Merchant` (both Party) produces nothing, which is the honest
answer rather than a guess, and `Receipt ~ Payment` (Document and Transaction, both rank 4)
likewise produces nothing. Rank 0 (`None`) never links, on either side. Unlike the original
rule, a Place, Device, Item or Party is never itself a referencer even when it outranks the
other side of the pair: in a real schema, only a document or a transaction carries a foreign
key — everything else is something other rows point *at*.

This reuses knowledge the model already carries rather than inventing any: shape and
direction come from the same judgement about what kind of thing a class is.

### Where Role comes from

- **Archetype-shaped classes** get it from `ArchetypeLexicon`, which already knows the kind:
  device/machine/terminal/equipment/display → Device; document/record/statement → Document;
  payment/transaction/tender → Transaction; person/recipient/buyer/customer → Party;
  business/organization/establishment/company/shop/store → Place; item/product/good/goods/
  merchandise → Item; card → Item; money/currency → **None**.

- **Curated classes** (`DomainFieldLexicon`) get an explicit Role per entry — about fifty
  mechanical annotations. This is unavoidable, not laziness: those classes are shaped by the
  curated table and never resolve an archetype, and their kind *cannot* be recovered from
  Wikipedia either. `Order` — one half of the strongest pair in the article — is a
  disambiguation page with no definition at all. Without an annotated Role there is no
  `Order.CustomerId`.

### What it produces

For each surviving pair, on the referencing class:

- a `CandidateRelationship { Target, Role = "references" }` — filling the structure that has
  been dead since it was written;
- a reference **field** `<Target>Id : int`, which is also a real gain in field detail.

A reference field is skipped when it would collide with an existing field, with `Id`, or
with the entity's own name (`class Order { int OrderId }` is fine, but `class Order { Order }`
is CS0542 — the existing guards apply).

### Where it runs

A new `RelationshipMiner` in `DomainDiscovery/Enrichment`, invoked by
`DomainDiscoveryPipeline` **after** `EntityShaper.Shape` — it needs the final class list and
their roles. It re-reads the article extract through `IWikipediaClient.FetchExtract`, which
is a disk-cache hit by then, so it costs no network.

### What the user sees

```
Candidate model (20 entities):
  - Customer [Wiki] { FullName:string, Email:string, Phone:string }
  - Order [Wiki] { Reference:string, PlacedOn:DateTime, Total:decimal, CustomerId:int, RestaurantId:int }  -> Customer, Restaurant
  - Receipt [Wiki] { Number:string, IssuedOn:DateTime, Total:decimal, CustomerId:int }  -> Customer
  - Payment [Wiki] { Amount:decimal, PaidOn:DateTime, Method:string, PaymentTerminalId:int }  -> PaymentTerminal
```

## Error handling

Relationship mining is **additive and best-effort**. If the article extract is unavailable,
or no pair clears the threshold, the model is exactly what it is today — twenty standalone
classes. Nothing that exists now can be made worse by this stage, and it must never throw.

## Testing

- `RelationshipMiner` over a fixture article: a pair that recurs is linked; a pair that
  appears once is not; **`Cash` does not link to `CashRegister`** (the overlap regression);
  the cap holds; equal-rank pairs produce nothing.
- Direction: `Receipt`(Document) ~ `Customer`(Party) yields `Receipt.CustomerId` and **not**
  `Customer.ReceiptId`.
- `RoleLexicon`/`ArchetypeLexicon`: every archetype maps to a Role; every `DomainFieldLexicon`
  entry has one (a test that enumerates the lexicon and asserts no entry is unannotated, so a
  future entry cannot silently arrive role-less).
- Reference fields never collide with an existing field, `Id`, or the entity's own name.
- Pipeline integration with a fake client: a shaped model comes back with relationships.
- **End to end:** `build app: develop pointofsale application` must show `Order.CustomerId`
  and `Receipt.CustomerId`, and must NOT show any `Cash`–`CashRegister` link. Tests passing
  is not the claim; that output is.

## Known limitation, accepted

Co-occurrence is evidence of association, not of cardinality. Every link is emitted as a
single reference field (many-to-one); a genuine many-to-many (Order ~ Product) is flattened
to one key. The review loop remains the place to correct it, and nothing is silently deleted.
