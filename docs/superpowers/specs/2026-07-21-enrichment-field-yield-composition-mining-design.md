# Enrichment field yield — composition-list mining — design

**Date:** 2026-07-21
**Status:** Approved (design)
**Feature:** 2 of 9 ("Field/type inference during enrichment" — yield/richness axis)

## Goal

`build app: <description>` shapes each discovered class with three field sources
(curated `DomainFieldLexicon`, `RoleTemplate` baseline, mined `PropertyMiner`). Only the
last one makes a class *specific* to what it is — and it under-delivers. Measured against
the real 20 GB store (`Dump\enwiki-full.db`) on ten commerce/library entities:

- `Order`, `Product`, `Customer`, `Payment` mine **0** fields.
- `Receipt` mines **1** (`SalesTax`).
- `Book` mines noise (`Scrolls`, `ClayTablets`, `InventedNarratives`); `Employee` mines
  benefit *examples* as attributes (`Housing`, `DisabilityInsurance`).

Yet the *right* fields are present in the article — below the lead, under section
headings the miner never reads. `Invoice`'s "Header"/"Body"/"Footer" sections list
`Invoice number`, `Invoice date`, `Payment due date`, `Purchase-order number`,
`Subtotal`, `Tax amounts`, `Discounts`, `Shipping charges`, `Total amount due` — a near
-perfect schema.

This feature adds a **heading-gated composition-list source** to `PropertyMiner`:
bulleted lists under a section heading that names composition/structure are mined; the
noise sections (`History`, `Types/Variations`, `Overview`, `See also`) are excluded
because their headings are not in the gate.

Non-goals (explicitly deferred):

- **Type-inference accuracy.** `TypeInference.Infer` is naive substring matching with
  known false positives (`Discounts`→`int` via the `count` substring; `InvoiceNumber`→
  `int`; `Isbn`→`bool`). That is the *other* axis of Feature 2 and is **not** touched here.
- **Infobox recovery.** Wikipedia infoboxes are the richest structured attribute source
  but are stripped at import time by `WikitextStripper.RemoveNested` (`{{…}}`/`{|…|}`).
  Recovering them needs a re-import of the store or a per-article API refresh — out of
  scope.
- No change to `CandidateField`, `EntityHarvester` (which nouns become classes),
  relationship mining, or any generator/front-end. This is a `DomainDiscovery/Enrichment`
  change only.

## Verified starting state

- `DomainDiscovery/Enrichment/EntityShaper.cs:157` — `Widen` mines with
  `PropertyMiner.Mine(LeadSection.Of(article), entity.Name)`. The `LeadSection.Of` call
  here is the single reason the miner cannot see below-lead content. It runs over
  `model.Entities` (survivors only), after curated/archetype/role fields are in place, and
  `AddField` silently declines a name already taken — so mined fields are strictly
  additive and never overwrite a curated/template field.
- `DomainDiscovery/Enrichment/PropertyMiner.cs`
  - `:41` `MaxMinedFields = 8` (readability cap; **kept**).
  - `:128` `Mine(string articleBody, string entityName)` — collects phrases from
    `FromIntroducedLists` then `FromProseEnumerations` (`:136-137`), maps each through
    `ToFieldShape`, dedups by name, caps at 8.
  - `:169` `FromIntroducedLists` — yields bullets only when the preceding non-list line
    matches `ContainmentLeadIn` (`…contains:`/`…includes:`/`…:`). Verified on the store:
    `Invoice`'s Header/Body/Footer lists are introduced by bare headings ("Header"), **not**
    a containment sentence, so this source does **not** reach them even with full-body input.
  - `:192` `FromProseEnumerations` — the lower-precision source; produced the `Book`/
    `Employee` noise from lead prose. Stays lead-scoped.
  - `:211` `ToFieldShape` — the per-phrase filter (`LeadingDeterminer`/`Gloss` strip,
    `MaxPhraseWords = 4`, `FragmentOpeners`, `NameNormalizer`, `StopWords`, not `Id`, not
    the entity's own name). Reused unchanged by the new source.
- `DomainDiscovery/Enrichment/LeadSection.cs:63` — `IsHeading(string)` (currently
  `private`) already encodes the heading predicate: non-empty, ≤80 chars, not a `"* "`
  list item, and ends in a letter/digit. This is the recognizer the new source needs; it
  will be promoted to `internal` (or lifted to a shared helper) so there is exactly one
  heading recognizer in the codebase.
- `DomainDiscovery/Wikipedia/Dump/WikitextStripper.cs` — flattens `== History ==` to a
  bare line "History" and rewrites list bullets to a `"* "` line prefix; strips all
  templates/tables (hence no infobox). Confirms headings and bullets are exactly the plain
  -text shapes the new source will match.

## Design

### 1. `EntityShaper.Widen` feeds the full body

Change the mine call from `PropertyMiner.Mine(LeadSection.Of(article), entity.Name)` to
`PropertyMiner.Mine(article, entity.Name)`. All source-scoping now lives inside
`PropertyMiner`, so the decision "what part of the article is fair game for which source"
is in one cohesive place. The existing lead-only behaviour of the two current sources is
preserved by having `Mine` compute `LeadSection.Of(body)` internally for them (see §2).

### 2. `PropertyMiner.Mine` runs three sources, highest-precision first

```
Mine(fullBody, entityName):
    lead = LeadSection.Of(fullBody)
    phrases  = FromCompositionSections(fullBody)      // NEW — full body, heading-gated
    phrases += FromIntroducedLists(lead)              // unchanged, now explicitly lead-scoped
    phrases += FromProseEnumerations(lead)            // unchanged, lead-scoped
    → ToFieldShape per phrase, dedup by name, cap at MaxMinedFields (8)
```

Order matters because of the cap: the composition source is the highest-precision on the
evidence, so its items are offered first and survive the cap; the lead sources fill any
remaining slots. Dedup is unchanged (first occurrence of a name wins).

### 3. `FromCompositionSections(fullBody)` — the new source

Walk the body line by line, tracking the current heading:

- A **heading** is any line for which the shared `IsHeading` predicate is true.
- When the current heading **matches the composition gate** (below), every following
  `"* "` list item is yielded as a phrase, until the list ends — i.e. until the next
  heading or the next non-list, non-blank prose line.
- A heading not in the gate resets the state to "not mining"; its bullets are ignored.

No containment lead-in is required — the matching heading *is* the gate. Yielded phrases
flow through the identical `ToFieldShape` filter as every other source, so length,
fragment-opener, gloss, stop-word and self-name guards all still apply.

**Composition-heading gate** (case-insensitive; a heading matches if it contains any of
these as a whole word, so "Structure of an invoice" and "Anatomy" both match):

```
format, structure, components, contents, elements, fields,
anatomy, parts, sections, layout, header, body, footer
```

`header`/`body`/`footer` are the riskiest (a "Body" section in a non-document article) but
are what unlock the `Invoice`/`Purchase order` schemas; they are kept deliberately. The
risk is bounded: the source only runs on entities that already survived shaping as domain
classes, every bullet still passes `ToFieldShape`, and `MaxMinedFields` caps the total.

### Worked example (Invoice, real store)

Body (heading lines shown): `History` → `Format` → **`Header`** (`* Invoice number`,
`* Invoice date`, `* Payment due date`, `* Purchase-order number (…)`) → **`Body`**
(`* Subtotal`, `* Tax amounts, if relevant (…)`, `* Discounts`, `* Shipping charges`,
`* Total amount due`) → **`Footer`** (`* Credit terms`, …) → `Variations` ("types of
invoices:" — a list of invoice *kinds*) → `See also`.

The gate mines Header/Body/Footer and skips History/Variations/See also. `ToFieldShape`
turns `Invoice number`→`InvoiceNumber`, strips the gloss from `Purchase-order number (or
similar…)`→`PurchaseOrderNumber`, and drops over-length bullets (`Seller's and buyer's
contact Information`, 5 words). Net: a concrete Invoice schema instead of today's four
prose fragments. (Note the deferred type bug: `Discounts`→`int` via the `count` substring;
correct types are a separate feature.)

## Testing (hermetic — no real store)

`DomainDiscovery.Tests` (xunit). All fixtures are synthetic bodies in the stripped shape
`WikitextStripper` emits (bare heading lines, `"* "` bullets).

- **`FromCompositionSections` recall:** an Invoice-shaped body (Header/Body/Footer bullets)
  yields the expected field names; assert `InvoiceNumber`, `PaymentDueDate`,
  `PurchaseOrderNumber`, `Subtotal`, `TotalAmountDue` appear.
- **Heading gate precision (the key test):** the *same* body carries a `History` paragraph,
  a `Variations`/`Types` list ("There are different types: * Pro forma invoice * Credit
  memo"), and a `See also` bullet list ("* Bill of lading"). Assert **none** of those
  become fields.
- **`header`/`body`/`footer` inclusion:** a body whose only composition heading is `Header`
  still mines its bullets.
- **Lead sources unchanged:** existing `PropertyMinerTests` stay green; add one asserting a
  lead prose-enumeration is still mined and a below-lead prose enumeration under a
  non-composition heading is **not**.
- **Cap + ordering:** a body with a composition list of >8 valid bullets plus a lead
  enumeration yields exactly 8, composition items first.
- **`EntityShaper.Widen` integration:** with a fake `IWikipediaClient` returning an
  Invoice-shaped body, a shaped `Invoice` entity gains at least one below-lead composition
  field it did not have when only the lead was read — proving the `Widen` call-site change.
- **`LeadSection` predicate:** if `IsHeading` is promoted to `internal`, a direct test
  pins its contract; existing `LeadSection.Of` behaviour is unchanged.

## Files touched (anticipated)

- `DomainDiscovery/Enrichment/PropertyMiner.cs` — add `FromCompositionSections`, the
  composition-heading gate, and wire the three sources with internal lead-scoping in `Mine`.
- `DomainDiscovery/Enrichment/EntityShaper.cs` — `Widen` passes the full body to `Mine`.
- `DomainDiscovery/Enrichment/LeadSection.cs` — promote `IsHeading` to `internal` (or lift
  to a tiny shared helper) so both classes share one heading recognizer.
- `DomainDiscovery.Tests/Enrichment/PropertyMinerTests.cs`,
  `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs` — new tests above.

## Backward compatibility

- Mined fields remain strictly additive (`AddField` declines a taken name), so curated and
  role-template fields are never displaced; a class only gains fields.
- The two existing sources produce identical output (they still see only
  `LeadSection.Of(body)`), so no current `PropertyMinerTests` assertion changes meaning.
- No store re-import; the change reads the same `Body` already stored.
- Composition-source fields are offered first into the `MaxMinedFields = 8` cap, so on
  document-like classes they can substitute lower-precision lead-mined fragments — e.g.
  `Invoice` now emits `InvoiceNumber`/`InvoiceDate`/`PaymentDueDate`/… in place of
  `ItemizedListOfGoods`/`TermsOfSale`/`Quantities`/`AgreedUponPrices`. Curated and
  role-template fields are never at risk (only other mined fields can be displaced), and
  CI stays green under `-warnaserror`.
```
