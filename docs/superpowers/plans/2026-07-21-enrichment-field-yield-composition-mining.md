# Enrichment Field-Yield Composition-List Mining — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `PropertyMiner` a heading-gated source that mines below-lead bulleted lists under composition/structure headings, so generated classes gain the concrete fields an encyclopedia states under "Format"/"Header"/"Contents" sections instead of the thin prose fragments the lead-only miner recovers today.

**Architecture:** One new private source, `FromCompositionSections`, is added to `PropertyMiner` and wired into `Mine` ahead of the two existing sources; the existing sources are pinned to the lead (behaviour-preserving) while the new one reads the whole body but fires only under a curated set of composition headings. The `EntityShaper.Widen` call-site then passes the full article body instead of `LeadSection.Of(article)` to activate it end-to-end. `LeadSection.IsHeading` becomes the single shared heading recognizer.

**Tech Stack:** C# / .NET 10, xUnit (DomainDiscovery.Tests).

## Global Constraints

- Target framework: `net10.0`; `Nullable` enable; `ImplicitUsings` enable (from `Directory.Build.props`).
- Test framework for `DomainDiscovery.Tests` is **xUnit** (`[Fact]`/`Assert`).
- CI builds with `-warnaserror`: the solution must compile with **zero warnings**.
- Mined fields are strictly additive; `EntityShaper.AddField` already declines a name already taken or equal to the entity's own name — do not change that.
- `MaxMinedFields` stays **8** (readability cap — do not raise).
- Type inference (`TypeInference.Infer`) is **out of scope** — do not touch it, even where it produces a visibly wrong type (e.g. `Discounts`→`int`).
- Source lives under `D:\SmartCodeGenerator-Repository` (the git root is the parent of the tool's working dir). Run all `git`/`dotnet` commands from that directory.

---

### Task 1: Composition-list source in `PropertyMiner`

Adds the new source and its heading gate, and promotes `LeadSection.IsHeading` so the source can reuse it. Tested directly through the public `PropertyMiner.Mine` with full-article inputs. The production call-site still passes lead-only text after this task, so there is **no** behaviour change in `build app:` until Task 2 — this task is complete and reviewable on its own.

**Files:**
- Modify: `DomainDiscovery/Enrichment/LeadSection.cs` (promote `IsHeading` to `internal`)
- Modify: `DomainDiscovery/Enrichment/PropertyMiner.cs` (new source + gate + wire into `Mine`)
- Test: `DomainDiscovery.Tests/Enrichment/PropertyMinerTests.cs` (append new tests)

**Interfaces:**
- Consumes: `LeadSection.Of(string)` and `LeadSection.IsHeading(string)` (same namespace/assembly), and the existing private `ToFieldShape`, `FromIntroducedLists`, `FromProseEnumerations`, `MaxMinedFields`.
- Produces: `PropertyMiner.Mine(string articleBody, string entityName)` — unchanged signature — now also mines composition sections found anywhere in `articleBody`.

- [ ] **Step 1: Promote the heading recognizer to internal**

In `DomainDiscovery/Enrichment/LeadSection.cs`, change the `IsHeading` declaration (currently at `:63`) from `private` to `internal` so `PropertyMiner` (same assembly) can reuse the one recognizer. Update its summary's first line to note the shared use:

```csharp
    /// <summary>
    /// A line that names a section rather than saying anything. Shared with
    /// <see cref="PropertyMiner"/>, which uses it to find composition sections below the lead.
    /// Prose ends on punctuation — a full stop, a colon introducing a list, a closing quote —
    /// and a heading ends on the last letter of its last word ("Terminology", "History").
    /// </summary>
    internal static bool IsHeading(string line)
```

Leave the method body unchanged.

- [ ] **Step 2: Write the failing tests**

Append to `DomainDiscovery.Tests/Enrichment/PropertyMinerTests.cs` (inside the existing `PropertyMinerTests` class):

```csharp
    [Fact]
    public void Mines_a_list_under_a_composition_heading_with_no_containment_lead_in()
    {
        // The real Invoice article states its fields under bare "Header"/"Body" headings,
        // not a sentence ending in a colon — so the containment-lead-in source never reaches
        // them. The composition-heading source does.
        var article = """
            An invoice is a commercial document.
            History
            Invoices are an ancient record.
            Header
            * Invoice number
            * Payment due date
            Body
            * Subtotal
            * Total amount due
            """;

        var names = PropertyMiner.Mine(article, "Invoice").Select(f => f.Name).ToList();

        Assert.Contains("InvoiceNumber", names);
        Assert.Contains("PaymentDueDate", names);
        Assert.Contains("Subtotal", names);
        Assert.Contains("TotalAmountDue", names);
    }

    [Fact]
    public void Skips_below_lead_lists_that_are_not_under_a_composition_heading()
    {
        // The key precision test: History, Variations (a list of TYPES) and See also all sit
        // below the lead as bulleted lists, and none is a property. The heading gate is what
        // separates them from a real "Header"/"Body" field list.
        var article = """
            An invoice is a commercial document.
            History
            * Ancient Mesopotamia
            * The Middle Ages
            Variations
            * Pro forma invoice
            * Credit memo
            See also
            * Receipt
            * Purchase order
            """;

        Assert.Empty(PropertyMiner.Mine(article, "Invoice"));
    }

    [Fact]
    public void Treats_header_body_and_footer_as_composition_headings()
    {
        var article = """
            A cheque is a written order to a bank.
            Footer
            * Signature line
            """;

        var names = PropertyMiner.Mine(article, "Cheque").Select(f => f.Name).ToList();

        Assert.Contains("SignatureLine", names);
    }

    [Fact]
    public void Does_not_mine_a_prose_enumeration_below_the_lead()
    {
        // The prose-enumeration source is deliberately lead-only: below the lead it mined
        // noise (Book's "scrolls and clay tablets"). A non-composition section must stay
        // untouched even though it contains a containment verb ("include").
        var article = """
            A book is a written work.
            Media
            Older writing media include scrolls and clay tablets.
            """;

        Assert.Empty(PropertyMiner.Mine(article, "Book"));
    }

    [Fact]
    public void Caps_mined_fields_and_offers_composition_items_first()
    {
        // Nine valid composition bullets plus a lead prose enumeration. The cap is 8, and
        // composition items are offered first, so the lead enumeration ("summary total") is
        // pushed out entirely.
        var article = """
            A form is a document that includes the summary total.
            Fields
            * field alpha
            * field beta
            * field gamma
            * field delta
            * field epsilon
            * field zeta
            * field eta
            * field theta
            * field iota
            """;

        var fields = PropertyMiner.Mine(article, "Form");

        Assert.Equal(PropertyMiner.MaxMinedFields, fields.Count);
        Assert.DoesNotContain("SummaryTotal", fields.Select(f => f.Name));
        Assert.Contains("FieldAlpha", fields.Select(f => f.Name));
    }
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~PropertyMinerTests"`
Expected: the four new composition tests FAIL (`Mines_a_list_under_a_composition_heading…`, `Treats_header_body_and_footer…` assert `Contains` on an empty/partial result; `Caps_mined_fields…` returns fewer than 8 or contains `SummaryTotal`). `Skips_below_lead_lists…` and `Does_not_mine_a_prose_enumeration…` may already pass (no composition source yet) — that is fine, they guard against regressions in Step 4.

- [ ] **Step 4: Implement the composition source**

In `DomainDiscovery/Enrichment/PropertyMiner.cs`:

**(a)** Add the heading-word set and its predicate. Place them next to the other `private static readonly` members (e.g. after `FragmentOpeners`):

```csharp
    /// <summary>
    /// Section headings whose bulleted lists state what a thing is MADE OF, as opposed to its
    /// history, its variants, or where to read next. This gate is what lets the miner reach
    /// below the lead safely: measured on the real store, the other below-lead lists are
    /// navigation ("See also"), type enumerations ("Variations") and history — never fields.
    /// </summary>
    private static readonly HashSet<string> CompositionHeadingWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "format", "structure", "components", "contents", "elements", "fields",
            "anatomy", "parts", "sections", "layout", "header", "body", "footer",
        };

    /// <summary>
    /// True when any whole word of <paramref name="heading"/> names composition, so "Header",
    /// "Structure of an invoice" and "Anatomy" match while "History", "Variations" and "See
    /// also" do not. Matched on whole words so "Antibody" never counts as "body".
    /// </summary>
    private static bool IsCompositionHeading(string heading)
    {
        foreach (var word in heading.Split(
                     (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (CompositionHeadingWords.Contains(word.Trim('(', ')', ',', '.', ':', ';', '"', '\'')))
                return true;
        }

        return false;
    }
```

**(b)** Add the source itself. Place it next to `FromIntroducedLists`:

```csharp
    /// <summary>
    /// Every item of every bulleted list that sits under a composition/structure heading.
    /// Unlike <see cref="FromIntroducedLists"/> this needs no containment lead-in — the heading
    /// IS the gate — which is what reaches the field lists an article states under a
    /// "Format"/"Header"/"Contents" section well below the lead.
    /// </summary>
    private static IEnumerable<string> FromCompositionSections(string body)
    {
        var mining = false;

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("* ", StringComparison.Ordinal))
            {
                if (mining)
                    yield return line[2..];
                continue;
            }

            // Any non-list line is either a heading — which turns mining on when it names
            // composition and off when it does not — or a prose line, which ends the list
            // above it (IsHeading is false, so mining goes false).
            mining = LeadSection.IsHeading(line) && IsCompositionHeading(line);
        }
    }
```

**(c)** Wire it into `Mine`, highest-precision first, and pin the existing sources to the lead. Replace the phrase-collection block at the top of the `try` in `Mine` (currently `:135-137`):

```csharp
            // The composition-list source reads the whole body (gated by heading). The two
            // lead sources are pinned to the lead REGARDLESS of how much body the caller passed:
            // the prose enumeration in particular mines noise from below the lead (measured:
            // Book's "scrolls and clay tablets"), so it stays confined here.
            var lead = LeadSection.Of(articleBody);

            var phrases = new List<string>();
            phrases.AddRange(FromCompositionSections(articleBody));
            phrases.AddRange(FromIntroducedLists(lead));
            phrases.AddRange(FromProseEnumerations(lead));
```

Leave the rest of `Mine` (the cap/dedup loop and the `catch`) unchanged.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~PropertyMinerTests"`
Expected: PASS — all PropertyMinerTests (the 5 new + every pre-existing one) green. If `Caps_mined_fields…` fails on a field name not normalizing as expected, print `PropertyMiner.Mine(article, "Form").Select(f => f.Name)` to confirm the eight survivors, but do not change the cap.

- [ ] **Step 6: Verify the whole suite still builds warning-free**

Run: `dotnet build DomainDiscovery/DomainDiscovery.csproj -warnaserror`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add DomainDiscovery/Enrichment/LeadSection.cs DomainDiscovery/Enrichment/PropertyMiner.cs DomainDiscovery.Tests/Enrichment/PropertyMinerTests.cs
git commit -m "feat: mine below-lead composition-section lists in PropertyMiner

New heading-gated source: bulleted lists under a Format/Header/Body/
Footer/Contents/... heading are mined without a containment lead-in.
Existing lead sources pinned to the lead so the noisy prose enumeration
cannot reach below-lead content. IsHeading promoted to a shared recognizer.
Not yet wired into the call-site (Task 2).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Activate at the `EntityShaper.Widen` call-site

Flips `Widen` to pass the full body to the miner, which turns the new source on for real `build app:` runs, and corrects the now-stale "the LEAD, not the article" comment. Proven by an `EntityShaper` integration test showing a shaped entity gains a below-lead composition field.

**Files:**
- Modify: `DomainDiscovery/Enrichment/EntityShaper.cs:144-159` (`Widen`)
- Test: `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs` (append one test)

**Interfaces:**
- Consumes: `PropertyMiner.Mine(string articleBody, string entityName)` from Task 1 (now composition-aware); `FakeWikipediaClient.Extracts` (title→full body) already exists.
- Produces: no new public surface; behaviour change only (mined fields now include below-lead composition fields).

- [ ] **Step 1: Write the failing test**

Append to `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs` (inside `EntityShaperTests`):

```csharp
    [Fact]
    public void Shape_MinesFieldsFromBelowLeadCompositionSections()
    {
        // Invoice is curated, so it survives shaping; Widen then reads its article. The fields
        // we assert on live BELOW the lead under a "Header" heading — unreachable until Widen
        // passes the full body to PropertyMiner.
        _wiki.Extracts["Invoice"] = """
            An invoice is a commercial document issued by a seller to a buyer.
            History
            Invoices appear in ancient Mesopotamia.
            Header
            * Invoice number
            * Payment due date
            * Purchase order number
            Variations
            * Pro forma invoice
            * Credit memo
            See also
            * Receipt
            """;

        var model = ModelOf("Invoice");
        Shaper.Shape(model);

        var invoice = Assert.Single(model.Entities);
        var names = invoice.Fields.Select(f => f.Name).ToList();

        // Below-lead composition fields the lead-only miner could never reach.
        Assert.Contains("PaymentDueDate", names);
        Assert.Contains("PurchaseOrderNumber", names);

        // The heading gate still excludes the type-enumeration and navigation lists.
        Assert.DoesNotContain("ProFormaInvoice", names);
        Assert.DoesNotContain("Receipt", names);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~EntityShaperTests.Shape_MinesFieldsFromBelowLeadCompositionSections"`
Expected: FAIL — `PaymentDueDate` absent, because `Widen` still passes `LeadSection.Of(article)` and the composition source sees only the lead.

- [ ] **Step 3: Pass the full body and fix the stale comment**

In `DomainDiscovery/Enrichment/EntityShaper.cs`, inside `Widen`, replace the comment block and mine call (the `// The LEAD, not the article.` block and the `foreach … PropertyMiner.Mine(LeadSection.Of(article), …)` line, currently `:148-158`) with:

```csharp
            // The FULL article now, not just the lead. PropertyMiner scopes each of its sources
            // itself: the containment-list and prose-enumeration sources still read only the
            // lead (below it they mined junk — Merchant's CalmlyConsidered, Inventory's
            // FIFOAndLIFO, Receipt's stray SalesTax neighbours), while the composition-list
            // source reads the whole body but fires only under a "Format"/"Header"/"Contents"
            // heading, which is where an article actually states a document's fields.
            foreach (var shape in PropertyMiner.Mine(article, entity.Name))
                AddField(entity, shape.Name, shape.Type);
```

(The `var article = _client.FetchExtract(...)` line and the `if (string.IsNullOrWhiteSpace(article)) continue;` guard just above it are unchanged.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~EntityShaperTests"`
Expected: PASS — the new test and every existing `EntityShaperTests` case green.

- [ ] **Step 5: Run the full DomainDiscovery suite**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`
Expected: PASS — 0 failed. (Confirms no existing enrichment test regressed now that the real call-site feeds full bodies.)

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery/Enrichment/EntityShaper.cs DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs
git commit -m "feat: feed the full article body to PropertyMiner in EntityShaper.Widen

Activates the composition-list source end-to-end: a shaped class now gains
the fields an article states under a Format/Header/Contents section below
the lead. Corrects the stale 'the LEAD, not the article' comment.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** §1 (Widen full body) → Task 2; §2 (three sources, precision-first, cap 8) → Task 1 Step 4c + `Caps_mined_fields…`; §3 (`FromCompositionSections` + gate incl. header/body/footer) → Task 1 Step 4a/4b + `Treats_header_body_and_footer…`; shared `IsHeading` → Task 1 Step 1; deferred type/infobox → Global Constraints. Testing section (recall, precision, header/body/footer, cap+order, lead-source confinement, Widen integration) → all mapped to named tests.
- **Type consistency:** `Mine(string, string)` signature unchanged across both tasks; `LeadSection.IsHeading`/`LeadSection.Of` used exactly as declared; `MaxMinedFields` is the existing `public const int`.
- **No placeholders:** every code and command step is concrete.
- **Manual store re-verification (optional, post-merge):** the hermetic tests do not touch `Dump\enwiki-full.db`; a real-store spot check of `Invoice`/`Purchase order` field yield can be done with a throwaway probe if desired, but is not required for the plan to pass.
```
