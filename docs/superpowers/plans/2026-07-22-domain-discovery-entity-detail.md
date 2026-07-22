# Domain Discovery Entity Detail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise the number of domain classes `build app:` discovers from a measured 43 across twelve domains to at least 120, by unblocking the archetype gate that currently demotes 148 candidates on its own.

**Architecture:** Five changes inside `DomainDiscovery/Enrichment`, none of which alters an interface: widen `ArchetypeLexicon`'s head table from the measured blocking-head census; add a `SubtypeChaining` pass so a candidate defined as "a hotel" inherits `Hotel`'s shape; stop `HypernymExtractor` returning adverbs and participles as head nouns; keep a `Seed` candidate whose verdict is `Unknown` instead of demoting it; and add `EntityRole.Event` for the booking/appointment/visit shape no existing role fits. The lead gate (`DomainEnricher.DemoteWhatTheDefinitionDoesNotName`) is deliberately untouched.

**Tech Stack:** C# / .NET 10, xUnit 2.9.2, SQLite (`Microsoft.Data.Sqlite`) over the local 20 GB Wikipedia dump store.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-22-domain-discovery-entity-detail-design.md`. Read it before starting.
- Branch: `feat/domain-discovery-entity-detail`, already created off `main` at `99152c4`. Do not create another.
- Repository root is `D:\SmartCodeGenerator-Repository`. The `.slnx` is one level down, at `SmartCodeGenerator\SmartCodeGenerator.slnx`. All commands in this plan are run from the repository root.
- CI-parity build (this is what must stay clean, **not** the plain Debug build):
  `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`
- CI-parity test run:
  `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
- Test framework is **xUnit** (`[Fact]`, `[Theory]`/`[InlineData]`, `Assert.*`). Do not use MSTest attributes.
- The lead gate in `DomainDiscovery/Enrichment/DomainEnricher.cs` (`DemoteWhatTheDefinitionDoesNotName`) must not be modified, weakened, or bypassed by any task.
- `MaxSupportingArticles` in `WikipediaTopicRecognizer` stays at 3. Topic recognition is out of scope.
- Every new `ArchetypeLexicon` entry must be traceable to a head noun that was **measured** to block a candidate (Task 6 re-runs that measurement). Do not invent domain-specific heads such as "pizza" or "mortgage" — the table is domain-independent by design.
- Commit after every task. Do not squash tasks together.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `DomainDiscovery/Enrichment/EntityRole.cs` | Add `Event` member; rank it 4; allow it to reference | 1 |
| `DomainDiscovery/Enrichment/RoleTemplate.cs` | Baseline fields for `Event` | 1 |
| `DomainDiscovery/Enrichment/HypernymExtractor.cs` | Skip adverbs/participles/conjunctions when scanning for the head | 2 |
| `DomainDiscovery/Enrichment/ArchetypeLexicon.cs` | Widened head table, grouped by role; widened abstract list | 3 |
| `DomainDiscovery/Enrichment/SubtypeChaining.cs` | **New.** Resolves a candidate whose head names another candidate | 4 |
| `DomainDiscovery/Enrichment/EntityShaper.cs` | Defer removal until after chaining; call chaining; keep `Seed`+`Unknown` | 4, 5 |
| `DomainDiscovery.Tests/Enrichment/*` | Tests mirroring each of the above | 1-5 |

`SubtypeChaining` is its own file rather than a private method on `EntityShaper` so it can be tested against a plain list of candidates with no `IWikipediaClient` in play. It is still called only from `EntityShaper.Shape`, which is the one place that sees the whole model at once.

---

### Task 1: `EntityRole.Event`

Booking, appointment, reservation, flight, visit and admission are the spine of a scheduling domain, and no existing role fits: they are not `Transaction` (no amount, no method) and not `Document` (not a record issued to someone). Task 3 maps head nouns onto this role, so it has to exist first.

**Files:**
- Modify: `DomainDiscovery/Enrichment/EntityRole.cs`
- Modify: `DomainDiscovery/Enrichment/RoleTemplate.cs`
- Test: `DomainDiscovery.Tests/Enrichment/RoleTemplateTests.cs` (create if absent)

**Interfaces:**
- Consumes: nothing.
- Produces: `EntityRole.Event`; `EntityRoleRank.RankOf(EntityRole.Event) == 4`; `EntityRoleRank.CanReference(EntityRole.Event) == true`; `RoleTemplate.For(EntityRole.Event)` returns four `FieldShape`s named `Reference`, `StartsOn`, `EndsOn`, `Status` with types `string`, `DateTime`, `DateTime`, `string`.

- [ ] **Step 1: Write the failing test**

Create `DomainDiscovery.Tests/Enrichment/RoleTemplateTests.cs` (if the file already exists, append the three `[Fact]`s to the existing class instead of recreating it):

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class RoleTemplateTests
{
    [Fact]
    public void For_Event_CarriesTheSchedulingBaseline()
    {
        var fields = RoleTemplate.For(EntityRole.Event);

        Assert.Equal(
            new[] { "Reference", "StartsOn", "EndsOn", "Status" },
            fields.Select(f => f.Name));
        Assert.Equal("DateTime", fields.Single(f => f.Name == "StartsOn").Type);
        Assert.Equal("DateTime", fields.Single(f => f.Name == "EndsOn").Type);
        Assert.Equal("string", fields.Single(f => f.Name == "Reference").Type);
    }

    [Fact]
    public void Event_RanksWithDocumentsAndTransactions()
    {
        // Rank 4 is the "may hold a foreign key" tier: a Booking references a Guest (Party,
        // rank 1) and a Room (Place, rank 2). Equal ranks link in neither direction, so an
        // Event never acquires an InvoiceId on rank alone.
        Assert.Equal(4, EntityRoleRank.RankOf(EntityRole.Event));
        Assert.Equal(
            EntityRoleRank.RankOf(EntityRole.Transaction),
            EntityRoleRank.RankOf(EntityRole.Event));
    }

    [Fact]
    public void Event_MayCarryForeignKeys()
    {
        Assert.True(EntityRoleRank.CanReference(EntityRole.Event));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~RoleTemplateTests"`
Expected: FAIL — compile error, `'EntityRole' does not contain a definition for 'Event'`.

- [ ] **Step 3: Add the enum member**

In `DomainDiscovery/Enrichment/EntityRole.cs`, add a member after `Transaction` (append, so no existing ordinal moves):

```csharp
    /// <summary>The event of value changing hands: Payment, Refund, Order.</summary>
    Transaction,

    /// <summary>
    /// Something scheduled that happens to somebody, somewhere, between two times: Booking,
    /// Appointment, Reservation, Flight, Visit, Admission. Distinct from
    /// <see cref="Transaction"/> (which has an amount and a method) and from
    /// <see cref="Document"/> (which is a record issued about something, not the thing itself).
    /// </summary>
    Event
```

- [ ] **Step 4: Rank it and let it reference**

In the same file, in `EntityRoleRank.RankOf`, add the case alongside the other rank-4 roles:

```csharp
        EntityRole.Document => 4,
        EntityRole.Transaction => 4,
        EntityRole.Event => 4,
```

and widen `CanReference`:

```csharp
    public static bool CanReference(EntityRole role)
        => role is EntityRole.Document or EntityRole.Transaction or EntityRole.Event;
```

Also update that method's XML `<summary>` so the prose matches the code — replace "Only `EntityRole.Document` and `EntityRole.Transaction` qualify." with:

```
    /// Whether a class with this <see cref="EntityRole"/> is allowed to carry foreign keys at
    /// all — i.e. to be the referencing side of a mined relationship. Only <see
    /// cref="EntityRole.Document"/>, <see cref="EntityRole.Transaction"/> and <see
    /// cref="EntityRole.Event"/> qualify: a booking points at its guest and its room the same
    /// way an invoice points at its customer.
```

- [ ] **Step 5: Add the template**

In `DomainDiscovery/Enrichment/RoleTemplate.cs`, add to the `Templates` dictionary, immediately after the `EntityRole.Transaction` entry:

```csharp
        [EntityRole.Event] =
            [Text("Reference"), When("StartsOn"), When("EndsOn"), Text("Status")],
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~RoleTemplateTests"`
Expected: PASS, 3 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS. A `switch` over `EntityRole` elsewhere may now warn about the unhandled member; if the Release build reports one, handle `Event` explicitly at that site rather than suppressing the warning.

- [ ] **Step 8: Commit**

```bash
git add DomainDiscovery/Enrichment/EntityRole.cs DomainDiscovery/Enrichment/RoleTemplate.cs DomainDiscovery.Tests/Enrichment/RoleTemplateTests.cs
git commit -m "feat: add EntityRole.Event for scheduled things"
```

---

### Task 2: Stop `HypernymExtractor` returning non-nouns

Measured across twelve domains, the extractor produced these as head nouns: `primarily(4) and(3) not(3) or(3) made(3) any(3) humanly(2) undertaken(2) large(2) those(2) taught spent emerging`. It takes the first token after the copula, so "A hotel rating is **often** a ranking..." yields `often`. These candidates are not merely unresolved — they are resolved *wrongly*, and a wrong head could shape a candidate for the wrong reason once Task 3 widens the table.

**Files:**
- Modify: `DomainDiscovery/Enrichment/HypernymExtractor.cs`
- Test: `DomainDiscovery.Tests/Enrichment/HypernymExtractorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `HypernymExtractor.Extract(string)` — signature unchanged, returns a lowercase singular head or `""`. Task 4's `SubtypeChaining` relies on that "lowercase singular" contract.

- [ ] **Step 1: Write the failing tests**

Append to the existing `HypernymExtractorTests` class in `DomainDiscovery.Tests/Enrichment/HypernymExtractorTests.cs`:

```csharp
    [Theory]
    // Each of these is a real Wikipedia lead whose head the extractor got wrong, taken from
    // the twelve-domain census in the design spec. The word it used to return is in the
    // second column of that census: often, undertaken, primarily, not.
    [InlineData("A hotel rating is often a ranking of a hotel's quality.", "ranking")]
    [InlineData("Business travel is travel undertaken for work purposes.", "travel")]
    [InlineData("A public hospital is primarily a hospital owned by a government.", "hospital")]
    [InlineData("A hostel is a low-cost building providing shared accommodation.", "building")]
    public void Extract_SkipsAModifier_AndTakesTheNounBehindIt(string lead, string expected)
    {
        Assert.Equal(expected, HypernymExtractor.Extract(lead));
    }

    [Fact]
    public void Extract_DoesNotReturnAConjunction_AsTheHead()
    {
        // "and"/"or" join phrases, they are never the thing being defined.
        Assert.NotEqual("and", HypernymExtractor.Extract("A till is and was a drawer."));
        Assert.NotEqual("or", HypernymExtractor.Extract("A ledger is or may be a record."));
    }

    [Fact]
    public void Extract_StillReturnsAHeadThatMerelyLooksLikeAModifier()
    {
        // "gathering" is the head, not a modifier of one — nothing has been captured yet when
        // it is reached. This is existing behaviour and must not regress.
        Assert.Equal("gathering", HypernymExtractor.Extract("A meeting is a gathering of people."));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~HypernymExtractorTests"`
Expected: FAIL — the `[Theory]` cases return `often`, `undertaken`, `primarily`, `lowcost` instead of the expected nouns.

- [ ] **Step 3: Add the modifier list**

In `DomainDiscovery/Enrichment/HypernymExtractor.cs`, add these two members beside the existing `Terminators` and `ContainerWords` sets:

```csharp
    /// <summary>
    /// Words that modify the head rather than being it, and which sit BEFORE it: "is often a
    /// ranking", "is primarily a hospital", "is not a device". The scan steps over them and
    /// keeps looking, instead of capturing the first token after the copula and stopping.
    /// </summary>
    /// <remarks>
    /// Measured over twelve topic articles, these accounted for some thirty-five candidates
    /// whose definition resolved to an adverb — "often", "primarily", "generally", "humanly",
    /// "not" — and which therefore could never reach an archetype. Adverbs ending in "-ly" are
    /// caught by rule (see <see cref="IsModifier"/>); this list is the ones that do not.
    /// </remarks>
    private static readonly HashSet<string> Modifiers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "not", "often", "also", "still", "just", "very", "quite", "rather", "more",
            "most", "much", "any", "either", "neither", "sometimes", "always", "never",
            "perhaps", "maybe", "so", "such", "well", "then", "now", "here", "there",
            "large", "small", "big", "little", "new", "old", "long", "short", "high", "low"
        };

    /// <summary>
    /// Words that join phrases and so cannot be a head: "is and was a drawer". Reaching one
    /// before any head has been captured means the phrase has not started yet, so the scan
    /// steps over it exactly as it does a modifier.
    /// </summary>
    private static readonly HashSet<string> Conjunctions =
        new(StringComparer.OrdinalIgnoreCase) { "and", "or", "but", "nor", "yet" };

    /// <summary>
    /// True for a word that qualifies the head noun rather than being it: an adverb (by the
    /// "-ly" suffix or by the closed <see cref="Modifiers"/> list) or a conjunction. Deliberately
    /// does NOT treat "-ing"/"-ed" words as modifiers here — <see cref="ScanHead"/> already has
    /// a position-sensitive rule for those, and it must keep it: "a gathering of people" needs
    /// "gathering" as the head, while "a document acknowledging receipt" needs "document".
    /// </summary>
    private static bool IsModifier(string word)
        => Conjunctions.Contains(word)
           || Modifiers.Contains(word)
           || (word.Length > 3 && word.EndsWith("ly", StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 4: Use it in the scan**

In `ScanHead`, insert the skip **after** the `Determiners` block and **before** the `Terminators` block, so a modifier is stepped over but a terminator still ends the phrase:

```csharp
            if (Determiners.Contains(word))
            {
                if (EndsPhrase(sentence, end))
                    return (head, string.Empty, i + 1);
                continue;
            }

            // "is often a ranking", "is primarily a hospital", "is and was a drawer": step over
            // the qualifier and keep looking for the noun. Only BEFORE a head is captured —
            // after one, a following word is either a terminator, a participle modifying the
            // head (handled below), or a continuation that legitimately overwrites it.
            if (head.Length == 0 && IsModifier(word))
                continue;

            if (Terminators.Contains(word))
                return (head, word, i + 1);
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~HypernymExtractorTests"`
Expected: PASS, all cases including the pre-existing ones.

Note on the `hostel` case: "low-cost" tokenizes as the single word `low-cost` (the tokenizer keeps hyphens), which is neither in `Modifiers` nor `-ly`-suffixed, so it is captured as a head and then **overwritten** by `building` on the next iteration — the loop's existing `head = word` assignment already does this. The test passes because of that, not because `low-cost` is skipped. Do not add hyphen handling.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add DomainDiscovery/Enrichment/HypernymExtractor.cs DomainDiscovery.Tests/Enrichment/HypernymExtractorTests.cs
git commit -m "fix: stop hypernym extraction returning adverbs as head nouns"
```

---

### Task 3: Widen `ArchetypeLexicon`

This is the single biggest lever: the archetype gate alone demoted 148 of 557 candidates. The table holds about thirty heads; the census of what it turned away is in the spec.

**Files:**
- Modify: `DomainDiscovery/Enrichment/ArchetypeLexicon.cs`
- Test: `DomainDiscovery.Tests/Enrichment/ArchetypeLexiconTests.cs`

**Interfaces:**
- Consumes: `EntityRole.Event` (Task 1).
- Produces: `ArchetypeLexicon.Resolve(string)` — signature unchanged. Returns `ShapeVerdict.Shaped` for roughly 150 heads instead of ~30.

- [ ] **Step 1: Write the failing tests**

Append to `DomainDiscovery.Tests/Enrichment/ArchetypeLexiconTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~ArchetypeLexiconTests"`
Expected: FAIL — `institution`, `facility`, `room`, `booking` etc. currently resolve `Unknown`.

- [ ] **Step 3: Factor the existing field sets into named arrays**

The current table repeats the same `FieldShape[]` literal for every head of a role. Before adding 120 more heads, name them once. In `DomainDiscovery/Enrichment/ArchetypeLexicon.cs`, add above the `Archetypes` dictionary:

```csharp
    // One field set per role, named once so the table below reads as a list of heads rather
    // than a wall of repeated literals. These are the values the table already used; the
    // fuller baseline each class ends up with comes from RoleTemplate, applied afterwards by
    // EntityShaper.Widen. Keeping them separate is deliberate — see RoleTemplate's remarks.
    private static readonly FieldShape[] PlaceFields = [Text("Name"), Text("Address")];
    private static readonly FieldShape[] PartyFields = [Text("FullName"), Text("Email"), Text("Phone")];
    private static readonly FieldShape[] DeviceFields = [Text("Name"), Text("Model"), Text("SerialNumber")];
    private static readonly FieldShape[] ItemFields = [Text("Name"), Text("Sku"), Money("Price")];
    private static readonly FieldShape[] DocumentFields = [Text("Number"), When("IssuedOn"), Money("Total")];
    private static readonly FieldShape[] TransactionFields = [Money("Amount"), When("OccurredOn"), Text("Method")];
    private static readonly FieldShape[] EventFields = [Text("Reference"), When("StartsOn"), When("EndsOn")];

    private static (FieldShape[] Fields, EntityRole Role) Place => (PlaceFields, EntityRole.Place);
    private static (FieldShape[] Fields, EntityRole Role) Party => (PartyFields, EntityRole.Party);
    private static (FieldShape[] Fields, EntityRole Role) Device => (DeviceFields, EntityRole.Device);
    private static (FieldShape[] Fields, EntityRole Role) Item => (ItemFields, EntityRole.Item);
    private static (FieldShape[] Fields, EntityRole Role) Document => (DocumentFields, EntityRole.Document);
    private static (FieldShape[] Fields, EntityRole Role) Transaction => (TransactionFields, EntityRole.Transaction);
    private static (FieldShape[] Fields, EntityRole Role) Event => (EventFields, EntityRole.Event);
```

These arrays hold exactly the values the existing entries used, so `ArchetypeLexiconTests.Resolve_AKnownHead_ShapesIt` (which asserts `document` → `Number, IssuedOn, Total`) keeps passing unchanged.

- [ ] **Step 4: Replace the table**

Replace the whole `Archetypes` dictionary initializer with the following. Every head below either appears in the census in the design spec or is a direct sibling of one (e.g. `physician` beside the measured `professional`).

```csharp
    private static readonly Dictionary<string, (FieldShape[] Fields, EntityRole Role)> Archetypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Place: somewhere a domain keeps things or does business ---
            ["institution"] = Place, ["facility"] = Place, ["building"] = Place,
            ["establishment"] = Place, ["venue"] = Place, ["premises"] = Place,
            ["site"] = Place, ["room"] = Place, ["space"] = Place, ["department"] = Place,
            ["centre"] = Place, ["center"] = Place, ["complex"] = Place,
            ["warehouse"] = Place, ["school"] = Place, ["hospital"] = Place,
            ["hotel"] = Place, ["restaurant"] = Place, ["clinic"] = Place,
            ["office"] = Place, ["factory"] = Place, ["station"] = Place,
            ["laboratory"] = Place, ["library"] = Place, ["museum"] = Place,
            ["structure"] = Place, ["house"] = Place, ["home"] = Place,
            ["business"] = Place, ["organization"] = Place, ["organisation"] = Place,
            ["company"] = Place, ["shop"] = Place, ["store"] = Place,
            ["city"] = Place, ["town"] = Place, ["location"] = Place, ["place"] = Place,

            // --- Party: somebody the domain deals with ---
            ["person"] = Party, ["individual"] = Party, ["professional"] = Party,
            ["practitioner"] = Party, ["employee"] = Party, ["worker"] = Party,
            ["staff"] = Party, ["physician"] = Party, ["doctor"] = Party,
            ["nurse"] = Party, ["surgeon"] = Party, ["teacher"] = Party,
            ["instructor"] = Party, ["student"] = Party, ["pupil"] = Party,
            ["member"] = Party, ["user"] = Party, ["client"] = Party,
            ["customer"] = Party, ["recipient"] = Party, ["buyer"] = Party,
            ["seller"] = Party, ["merchant"] = Party, ["guest"] = Party,
            ["patient"] = Party, ["passenger"] = Party, ["traveller"] = Party,
            ["traveler"] = Party, ["owner"] = Party, ["supplier"] = Party,
            ["vendor"] = Party, ["manager"] = Party, ["driver"] = Party,
            ["operator"] = Party, ["agent"] = Party, ["contractor"] = Party,
            ["subscriber"] = Party, ["applicant"] = Party, ["candidate"] = Party,
            ["author"] = Party, ["resident"] = Party, ["tenant"] = Party,
            ["landlord"] = Party,

            // --- Device: a machine somebody operates ---
            ["device"] = Device, ["machine"] = Device, ["terminal"] = Device,
            ["equipment"] = Device, ["display"] = Device, ["computer"] = Device,
            ["appliance"] = Device, ["scanner"] = Device, ["printer"] = Device,
            ["sensor"] = Device, ["engine"] = Device, ["apparatus"] = Device,

            // --- Item: a thing bought, held, stocked or consumed ---
            ["item"] = Item, ["product"] = Item, ["good"] = Item, ["goods"] = Item,
            ["merchandise"] = Item, ["commodity"] = Item, ["vehicle"] = Item,
            ["aircraft"] = Item, ["car"] = Item, ["truck"] = Item, ["bus"] = Item,
            ["material"] = Item, ["substance"] = Item, ["food"] = Item,
            ["beverage"] = Item, ["drink"] = Item, ["meal"] = Item, ["dish"] = Item,
            ["drug"] = Item, ["medicine"] = Item, ["medication"] = Item,
            ["tool"] = Item, ["instrument"] = Item, ["animal"] = Item,
            ["book"] = Item, ["publication"] = Item, ["part"] = Item,
            ["piece"] = Item, ["component"] = Item, ["ingredient"] = Item,
            ["container"] = Item, ["package"] = Item, ["garment"] = Item,
            ["fuel"] = Item, ["asset"] = Item,

            // A payment card is an Item you hold, and its own three fields beat the generic
            // Item ones, so it keeps its bespoke entry.
            ["card"] = ([Text("Number"), Text("Holder"), When("ExpiresOn")], EntityRole.Item),

            // --- Document: a record issued about something ---
            ["document"] = Document, ["record"] = Document, ["statement"] = Document,
            ["account"] = Document, ["licence"] = Document, ["license"] = Document,
            ["certificate"] = Document, ["contract"] = Document, ["agreement"] = Document,
            ["report"] = Document, ["ticket"] = Document, ["prescription"] = Document,
            ["invoice"] = Document, ["receipt"] = Document, ["bill"] = Document,
            ["permit"] = Document, ["policy"] = Document, ["catalogue"] = Document,
            ["catalog"] = Document, ["list"] = Document, ["menu"] = Document,

            // --- Transaction: value changing hands ---
            ["payment"] = Transaction, ["transaction"] = Transaction, ["tender"] = Transaction,
            ["loan"] = Transaction, ["sale"] = Transaction, ["purchase"] = Transaction,
            ["deposit"] = Transaction, ["withdrawal"] = Transaction, ["transfer"] = Transaction,
            ["refund"] = Transaction, ["order"] = Transaction, ["shipment"] = Transaction,
            ["delivery"] = Transaction, ["trade"] = Transaction, ["subscription"] = Transaction,
            ["rental"] = Transaction,

            // --- Event: something scheduled, between two times ---
            ["booking"] = Event, ["reservation"] = Event, ["appointment"] = Event,
            ["flight"] = Event, ["trip"] = Event, ["journey"] = Event, ["tour"] = Event,
            ["visit"] = Event, ["admission"] = Event, ["stay"] = Event,
            ["treatment"] = Event, ["procedure"] = Event, ["operation"] = Event,
            ["surgery"] = Event, ["session"] = Event, ["course"] = Event,
            ["lesson"] = Event, ["meeting"] = Event, ["conference"] = Event,
            ["event"] = Event, ["ceremony"] = Event, ["examination"] = Event,
            ["shift"] = Event,

            ["money"] = ([Money("Amount"), Text("Currency")], EntityRole.None),
            ["currency"] = ([Money("Amount"), Text("Currency")], EntityRole.None),
        };
```

- [ ] **Step 5: Widen the abstract list**

Replace the `AbstractHeads` initializer with:

```csharp
    private static readonly HashSet<string> AbstractHeads =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // The original set: what System, Data, Management and Business-as-practice resolve to.
            "group", "collection", "set", "practice", "concept", "idea", "field",
            "discipline", "study", "science", "process", "method", "technique",
            "term", "activity", "administration", "period", "state", "condition",
            "branch", "area", "form", "way",

            // Measured additions. "specialty" is the important one: Radiology, Geriatric,
            // Psychiatric and Intensive care are all defined as a specialty, and a specialty
            // is not something a hospital keeps rows about.
            "specialty", "speciality", "profession", "education", "programme", "program",
            "provision", "totality", "risk", "possibility", "chance", "phase", "stage",
            "experience", "competition", "performance", "approach", "style", "quality",
            "amount", "value", "measure", "degree", "extent", "aspect", "characteristic",
            "feature", "principle", "theory", "notion", "phenomenon", "ability",
            "capacity", "effort", "attempt", "occurrence", "situation", "relationship",
            "maintenance", "movement", "workflow", "arrangement", "phrase", "word"
        };
```

Note the deliberate omissions, and do not "fix" them: `unit` and `service` go in **neither** table. `unit` is as often "a unit of measurement" (abstract) as "an intensive care unit" (a Place), and `service` is as often an abstraction as a thing; leaving them `Unknown` offers those candidates to the user rather than guessing. `area`, `branch`, `form` and `state` stay abstract even though a Place reading exists, because the abstract reading is the common one in encyclopedia leads.

- [ ] **Step 6: Run the tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~ArchetypeLexiconTests"`
Expected: PASS, including the pre-existing `Resolve_AKnownHead_ShapesIt` and `Resolve_AnAbstractHead_RejectsIt`.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS. If an `EntityShaperTests` or `DomainDiscoveryPipelineTests` case now shapes an entity it used to suggest, that is this task working — update the test's expectation and say so in the commit message. Do **not** narrow the table to keep an old expectation.

- [ ] **Step 8: Commit**

```bash
git add DomainDiscovery/Enrichment/ArchetypeLexicon.cs DomainDiscovery.Tests/Enrichment/ArchetypeLexiconTests.cs
git commit -m "feat: widen the archetype table from the measured head census"
```

---

### Task 4: Subtype chaining

`Motel`, `BoutiqueHotel`, `CapsuleHotel` and `ResortHotel` are all defined as "a hotel", and `Hotel` is already shaped — so their shape is available for free. The census counted `hotel(5) hospital(4) school(4) airline(4) bank(3)`: about 25 candidates across twelve domains, recoverable with no curation at all. It compounds with Task 3: once `institution` is in the table, `School` shapes, and everything defined as "a school" chains off it.

This requires `EntityShaper.Shape` to stop mutating the model as it walks pass 2 — chaining has to see candidates that pass 2 would already have removed. That restructuring also fixes a latent ordering dependency: today a candidate's fate depends on where it sits in the list.

**Files:**
- Create: `DomainDiscovery/Enrichment/SubtypeChaining.cs`
- Modify: `DomainDiscovery/Enrichment/EntityShaper.cs`
- Test: `DomainDiscovery.Tests/Enrichment/SubtypeChainingTests.cs` (create)
- Test: `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs` (append)

**Interfaces:**
- Consumes: `ArchetypeLexicon.Resolve` (Task 3), `HypernymExtractor.Extract` (Task 2), `ArchetypeResult`, `ShapeVerdict`, `EntityRole`.
- Produces:
  - `public readonly record struct ShapeCandidate(string Name, string Head, ArchetypeResult Result)`
  - `public static IReadOnlyDictionary<string, ArchetypeResult> SubtypeChaining.Resolve(IReadOnlyList<ShapeCandidate> candidates)` — keyed by `ShapeCandidate.Name`, case-insensitive, one entry per input candidate.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Enrichment/SubtypeChainingTests.cs`:

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class SubtypeChainingTests
{
    private static ShapeCandidate Unknown(string name, string head)
        => new(name, head, new ArchetypeResult(
            ShapeVerdict.Unknown, Array.Empty<DomainFieldLexicon.FieldShape>(), EntityRole.None));

    /// <summary>A candidate the archetype table already has an opinion on.</summary>
    private static ShapeCandidate Shaped(string name, string head)
        => new(name, head, ArchetypeLexicon.Resolve(head));

    [Fact]
    public void Resolve_ACandidateDefinedAsAnotherCandidate_InheritsItsShape()
    {
        // "A motel is a hotel designed for motorists." Nothing in the archetype table says
        // "motel", but Hotel is in the model and is a Place, so Motel is one too.
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Shaped("Hotel", "establishment"),
            Unknown("Motel", "hotel"),
        });

        Assert.Equal(ShapeVerdict.Shaped, resolved["Motel"].Verdict);
        Assert.Equal(EntityRole.Place, resolved["Motel"].Role);
        Assert.NotEmpty(resolved["Motel"].Fields);
    }

    [Fact]
    public void Resolve_ATwoStepChain_ResolvesThroughTheMiddle()
    {
        // ResortHotel is a hotel; Hotel is an establishment. Both hops must run.
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Unknown("ResortHotel", "motel"),
            Unknown("Motel", "hotel"),
            Shaped("Hotel", "establishment"),
        });

        Assert.Equal(EntityRole.Place, resolved["ResortHotel"].Role);
        Assert.Equal(EntityRole.Place, resolved["Motel"].Role);
    }

    [Fact]
    public void Resolve_ACycle_Terminates_AndLeavesBothUnknown()
    {
        // "An inn is a hostel"; "a hostel is an inn". Neither can shape the other.
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Unknown("Inn", "hostel"),
            Unknown("Hostel", "inn"),
        });

        Assert.Equal(ShapeVerdict.Unknown, resolved["Inn"].Verdict);
        Assert.Equal(ShapeVerdict.Unknown, resolved["Hostel"].Verdict);
    }

    [Fact]
    public void Resolve_ACandidateWhoseHeadNamesNothingInTheModel_StaysUnknown()
    {
        var resolved = SubtypeChaining.Resolve(new[] { Unknown("Motel", "hotel") });

        Assert.Equal(ShapeVerdict.Unknown, resolved["Motel"].Verdict);
    }

    [Fact]
    public void Resolve_DoesNotChainOffAnAbstractParent()
    {
        // "Management is a practice" is a rejection, not a shape. Nothing may inherit it —
        // inheriting Abstract would DELETE the child, which is far worse than suggesting it.
        var practice = new ShapeCandidate("Management", "practice", ArchetypeLexicon.Resolve("practice"));
        var resolved = SubtypeChaining.Resolve(new[] { practice, Unknown("Supervision", "management") });

        Assert.Equal(ShapeVerdict.Unknown, resolved["Supervision"].Verdict);
    }

    [Fact]
    public void Resolve_APluralHead_StillMatchesTheSingularCandidate()
    {
        var resolved = SubtypeChaining.Resolve(new[]
        {
            Shaped("Hotel", "establishment"),
            Unknown("Motel", "hotels"),
        });

        Assert.Equal(EntityRole.Place, resolved["Motel"].Role);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~SubtypeChainingTests"`
Expected: FAIL — compile error, `SubtypeChaining` and `ShapeCandidate` do not exist.

- [ ] **Step 3: Write `SubtypeChaining`**

Create `DomainDiscovery/Enrichment/SubtypeChaining.cs`:

```csharp
using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// One candidate as the shaper sees it before deciding its fate: its class name, the head noun
/// its own Wikipedia lead defined it as, and what <see cref="ArchetypeLexicon"/> made of that
/// head.
/// </summary>
public readonly record struct ShapeCandidate(string Name, string Head, ArchetypeResult Result);

/// <summary>
/// Resolves a candidate whose definition names ANOTHER candidate in the same model: "a motel is
/// a hotel", where Hotel is already shaped, makes Motel the same kind of thing.
/// </summary>
/// <remarks>
/// This is the half of the shaping problem no curation can reach. <see cref="ArchetypeLexicon"/>
/// is domain-independent by design, so it can hold "establishment" but never every subtype of
/// every establishment — and a real article set is full of them. Measured across twelve topic
/// articles, the heads hotel(5), hospital(4), school(4), airline(4) and bank(3) each named
/// another candidate in the very same model and blocked about twenty-five candidates between
/// them.
/// <para>
/// It compounds with the table rather than competing with it: once "institution" is an entry,
/// School shapes from the table, and every candidate defined as "a school" then chains off
/// School. Widening the table therefore widens this too, without a line of extra curation.
/// </para>
/// <para>
/// Only a <see cref="ShapeVerdict.Shaped"/> parent is inherited. An
/// <see cref="ShapeVerdict.Abstract"/> parent is a REJECTION, and propagating it would delete
/// the child rather than offer it — the expensive mistake, where leaving it
/// <see cref="ShapeVerdict.Unknown"/> merely offers it to the user.
/// </para>
/// </remarks>
public static class SubtypeChaining
{
    /// <summary>
    /// Returns each candidate's final <see cref="ArchetypeResult"/>, keyed by its name: the one
    /// it came in with, or the one it inherits from the candidate its head names. Every input
    /// candidate appears in the result exactly once.
    /// </summary>
    public static IReadOnlyDictionary<string, ArchetypeResult> Resolve(
        IReadOnlyList<ShapeCandidate> candidates)
    {
        var byName = new Dictionary<string, ShapeCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
            byName[Key(candidate.Name)] = candidate;

        var resolved = new Dictionary<string, ArchetypeResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            resolved[candidate.Name] =
                Chase(candidate, byName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        return resolved;
    }

    /// <summary>
    /// Follows a candidate's head to the candidate it names, and that one's head in turn, until
    /// something shapes or the chain runs out.
    /// </summary>
    /// <remarks>
    /// <paramref name="visiting"/> is what makes this terminate. "An inn is a hostel" and "a
    /// hostel is an inn" are both real Wikipedia leads, and following them without a guard is a
    /// stack overflow rather than a bad answer.
    /// </remarks>
    private static ArchetypeResult Chase(
        ShapeCandidate candidate,
        Dictionary<string, ShapeCandidate> byName,
        HashSet<string> visiting)
    {
        // Already decided, one way or the other — a head only matters when nothing is known.
        if (candidate.Result.Verdict != ShapeVerdict.Unknown || candidate.Head.Length == 0)
            return candidate.Result;

        if (!visiting.Add(Key(candidate.Name)))
            return candidate.Result;

        if (!byName.TryGetValue(Key(candidate.Head), out var parent))
            return candidate.Result;

        // "A hotel is a hotel" defines nothing; without this a self-reference would burn the
        // one visit the guard allows and look like a cycle.
        if (Key(parent.Name) == Key(candidate.Name))
            return candidate.Result;

        var inherited = Chase(parent, byName, visiting);

        return inherited.Verdict == ShapeVerdict.Shaped ? inherited : candidate.Result;
    }

    /// <summary>
    /// Folds a name or a head onto one spelling so "hotels", "hotel" and the class name "Hotel"
    /// are the same key. Reuses the codebase's single singularizer rather than adding a second;
    /// a word it declines to fold (it is deliberately conservative) simply keeps its own
    /// spelling, which is still a usable key.
    /// </summary>
    private static string Key(string text)
    {
        var normalized = NameNormalizer.ToEntityName(text);
        return normalized.Length == 0 ? text : normalized;
    }
}
```

- [ ] **Step 4: Run the chaining tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~SubtypeChainingTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit the standalone unit**

```bash
git add DomainDiscovery/Enrichment/SubtypeChaining.cs DomainDiscovery.Tests/Enrichment/SubtypeChainingTests.cs
git commit -m "feat: add subtype chaining for candidates defined as another candidate"
```

- [ ] **Step 6: Write the failing shaper test**

Append to `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs`:

```csharp
    [Fact]
    public void Shape_ACandidateDefinedAsAnotherCandidate_InheritsItsRoleAndIsKept()
    {
        var model = ModelOf("Hotel", "Motel");
        _wiki.Leads["Hotel"] = "A hotel is an establishment that provides paid lodging.";
        _wiki.Leads["Motel"] = "A motel is a hotel designed for motorists.";

        Shaper.Shape(model);

        var motel = model.Entities.Single(e => e.Name == "Motel");
        Assert.Equal(EntityRole.Place, motel.Role);
        Assert.Contains(motel.Fields, f => f.Name == "Address");
        Assert.DoesNotContain(model.Suggestions, e => e.Name == "Motel");
    }

    [Fact]
    public void Shape_DoesNotDependOnTheOrderCandidatesAppearIn()
    {
        // Chaining reads the whole model, so the subtype may be listed before its parent.
        var model = ModelOf("Motel", "Hotel");
        _wiki.Leads["Hotel"] = "A hotel is an establishment that provides paid lodging.";
        _wiki.Leads["Motel"] = "A motel is a hotel designed for motorists.";

        Shaper.Shape(model);

        Assert.Equal(EntityRole.Place, model.Entities.Single(e => e.Name == "Motel").Role);
    }
```

- [ ] **Step 7: Run it to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~EntityShaperTests"`
Expected: FAIL — `Motel` is in `Suggestions`, not `Entities`.

- [ ] **Step 8: Restructure `EntityShaper.Shape`'s pass 2**

In `DomainDiscovery/Enrichment/EntityShaper.cs`, replace the whole `// Pass 2: resolve the three-way verdict...` block (the `foreach` containing the `switch`) with the following three blocks. Everything above it (pass 1 and the `FetchLeads` prefetch) and the `Widen(model)` call below it are unchanged.

```csharp
        // Pass 2: resolve a verdict for everything still fieldless, WITHOUT acting on any of
        // them yet. Acting as we walked meant a candidate's fate depended on where it sat in
        // the list, and it left chaining (below) nothing to chain to: the parent it needed may
        // already have been removed.
        var pending = new List<CandidateEntity>();
        var candidates = new List<ShapeCandidate>();

        foreach (var entity in entities)
        {
            if (entity.Fields.Count > 0)
                continue;

            var title = entity.SourceTitle ?? entity.Name;
            var lead = leads.TryGetValue(title, out var fetchedLead) ? fetchedLead : string.Empty;
            var head = HypernymExtractor.Extract(lead);

            pending.Add(entity);
            candidates.Add(new ShapeCandidate(entity.Name, head, ArchetypeLexicon.Resolve(head)));
        }

        // Pass 2b: a candidate the table has no opinion on may still be defined as one it does
        // — "a motel is a hotel". This needs every candidate at once, which is why it happens
        // here and not per-entity above.
        var resolved = SubtypeChaining.Resolve(candidates);

        // Pass 2c: now act, once, on a verdict nothing further can change.
        foreach (var entity in pending)
        {
            var result = resolved[entity.Name];

            switch (result.Verdict)
            {
                case ShapeVerdict.Shaped:
                    entity.Role = result.Role;
                    foreach (var shape in result.Fields)
                        AddField(entity, shape.Name, shape.Type);
                    break;

                case ShapeVerdict.Abstract:
                    model.Entities.Remove(entity);
                    model.Rejected.Add(entity.Name);
                    break;

                default:
                    model.Entities.Remove(entity);
                    model.Suggestions.Add(entity);
                    break;
            }
        }
```

- [ ] **Step 9: Run the shaper tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~EntityShaperTests"`
Expected: PASS.

- [ ] **Step 10: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add DomainDiscovery/Enrichment/EntityShaper.cs DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs
git commit -m "feat: chain candidate shapes through the whole model, not per entity"
```

---

### Task 5: A word the user typed is evidence

`build app: a hotel booking application` produces no `Booking` class. The user typed the word; `SeedEntityExtractor` turned it into a `Provenance.Seed` candidate; and `EntityShaper` — by explicit design — gave it no exemption, so an `Unknown` verdict demoted it to a suggestion.

The rule changes for exactly one verdict. `Abstract` still rejects (so `system`, `application` and `management` do not become classes) and `Shaped` is unaffected.

**Files:**
- Modify: `DomainDiscovery/Enrichment/EntityShaper.cs`
- Test: `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs`

**Interfaces:**
- Consumes: the pass-2c `switch` from Task 4; `DefaultFields.ApplyTo(CandidateEntity, Provenance)` from `DomainDiscovery.Model`.
- Produces: no new API.

- [ ] **Step 1: Rewrite the test that pins the old behaviour**

`EntityShaperTests` already contains `Shape_MovesAnUnshapeableSeedEntity_ToSuggestions`, which asserts exactly the behaviour this task reverses. **Replace that whole test** (its `[Fact]`, its comment block and its body) with:

```csharp
    [Fact]
    public void Shape_KeepsAnUnshapeableSeedEntity_BecauseTheUserNamedIt()
    {
        // A Seed candidate is a noun SeedEntityExtractor lifted out of the user's own typed
        // phrase. An Unknown verdict means "we have no opinion" — and where we have no opinion,
        // the user having named the thing is the only evidence there is. This is why
        // `build app: a hotel booking application` used to produce no Booking class.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed });

        Shaper.Shape(model);

        var widget = Assert.Single(model.Entities);
        Assert.Equal("Widget", widget.Name);
        Assert.Contains(widget.Fields, f => f.Name == "Name");
        Assert.Empty(model.Suggestions);
        Assert.Empty(model.Rejected);
    }

    [Fact]
    public void Shape_StillRejectsASeedEntityWhoseDefinitionIsAnAbstraction()
    {
        // The exemption is for Unknown only. "System" is defined as a group of interacting
        // elements, and no amount of the user having typed it makes it a class.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "System", Provenance = Provenance.Seed });
        _wiki.Leads["System"] = "A system is a group of interacting elements.";

        Shaper.Shape(model);

        Assert.Empty(model.Entities);
        Assert.Empty(model.Suggestions);
        Assert.Equal(new[] { "System" }, model.Rejected);
    }

    [Fact]
    public void Shape_StillSuggestsAnUnshapeableWikiEntity()
    {
        // Only a SEED gets the exemption. A noun harvested from an article is not evidence of
        // anything the user asked for, so it is still offered rather than kept.
        var model = ModelOf("Widget");   // ModelOf builds Provenance.Wiki entities

        Shaper.Shape(model);

        Assert.Empty(model.Entities);
        Assert.Equal(new[] { "Widget" }, model.Suggestions.Select(e => e.Name));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~EntityShaperTests"`
Expected: FAIL on `Shape_KeepsAnUnshapeableSeedEntity_BecauseTheUserNamedIt` — `model.Entities` is empty and `Widget` is in `Suggestions`.

- [ ] **Step 3: Implement**

In `EntityShaper.cs`, in the pass-2c `switch` added by Task 4, replace the `default:` case with:

```csharp
                default:
                    // Unknown means "we have no opinion". Where we have none, the user having
                    // typed the word IS the evidence: SeedEntityExtractor only ever produces a
                    // Seed candidate from the description the user wrote. Abstract still
                    // rejects above, so "system" and "application" cannot get in this way.
                    if (entity.Provenance == Provenance.Seed)
                    {
                        DefaultFields.ApplyTo(entity, Provenance.Seed);
                        break;
                    }

                    model.Entities.Remove(entity);
                    model.Suggestions.Add(entity);
                    break;
```

Add `using DomainDiscovery.Model;` at the top of the file if it is not already there (it is — `CandidateDomainModel` comes from there — so no change is expected).

- [ ] **Step 4: Update the class-level documentation**

`EntityShaper`'s `<remarks>` and `Shape`'s `<summary>` both state at length that `Provenance.Seed` earns no exemption. That is now false and would mislead the next reader. Replace the third `<para>` of the class `<remarks>` with:

```
/// <para>
/// <see cref="Provenance.Seed"/> earns an exemption from ONE of the three verdicts. A seed is a
/// noun SeedEntityExtractor lifted out of the user's typed phrase, so it is shaped and rejected
/// exactly like a Wikipedia candidate — but where the verdict is <see cref="ShapeVerdict.Unknown"/>,
/// meaning we have no opinion, the user having named the thing is the only evidence available and
/// the candidate is kept with <see cref="DefaultFields"/> rather than offered back to them.
/// <see cref="ShapeVerdict.Abstract"/> still rejects it, so "system" and "application" cannot
/// become classes this way. Without this, `build app: a hotel booking application` produced no
/// Booking class at all.
/// </para>
```

and replace the sentence in `Shape`'s `<summary>` that reads "A seed entity gets no exemption here: ... so it takes the same gate as everything else." with:

```
/// A seed entity takes the same gate as everything else, except that an Unknown verdict keeps it
/// (with <see cref="DefaultFields"/>) rather than suggesting it — the user named it, and Unknown
/// means we have nothing to say against that.
```

- [ ] **Step 5: Run the shaper tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "FullyQualifiedName~EntityShaperTests"`
Expected: PASS.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS. `DomainDiscoveryPipelineTests` has cases that assert a seed-only model degrades to suggestions; if one now keeps the entity, that is this task working — update the expectation and note it in the commit message.

- [ ] **Step 7: Commit**

```bash
git add DomainDiscovery/Enrichment/EntityShaper.cs DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs
git commit -m "feat: keep a seed entity the archetype table has no opinion on"
```

---

### Task 6: Verify the yield against the real store

Everything above is unit-tested against fakes. The claim in the spec — 43 classes to at least 120 — is only true or false against the real 20 GB dump store, and per the repository's own history a green suite has hidden defects at this exact boundary more than once. This task measures, and is allowed to send work back to Task 3.

**Files:**
- Create: `docs/superpowers/plans/2026-07-22-domain-discovery-entity-detail-results.md`
- Possibly modify: `DomainDiscovery/Enrichment/ArchetypeLexicon.cs` (only to move a misfiring head)

**Interfaces:**
- Consumes: everything from Tasks 1-5.
- Produces: the results document, and a verdict on the spec's five success criteria.

- [ ] **Step 1: Confirm the store is present and indexed**

Run: `ls -la "D:/SmartCodeGenerator-Repository/Dump/enwiki-full.db"`
Expected: a file of roughly 20 GB. If it is absent, **stop and report** — do not fabricate results and do not fall back to the HTTP client, whose rate limit makes a twelve-domain run meaningless.

- [ ] **Step 2: Build the probe**

Create a throwaway console project **outside the repository**, in the session scratchpad, so it is never committed. Its `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="D:\SmartCodeGenerator-Repository\DomainDiscovery\DomainDiscovery.csproj" />
  </ItemGroup>
</Project>
```

Its `Program.cs`:

```csharp
using DomainDiscovery;

var descriptions = new[]
{
    "a point of sale system for a retail shop",
    "a library management system",
    "a hospital patient records system",
    "a hotel booking application",
    "an online store selling books to customers",
    "a payroll system",
    "a restaurant ordering system",
    "a school administration system",
    "a bank account management system",
    "a warehouse stock system",
    "an airline reservation system",
    "a veterinary clinic system",
};

var pipeline = DomainDiscoveryPipeline.CreateDefault(
    Path.Combine(Path.GetTempPath(), "entityprobe-cache"));

var total = 0;
foreach (var text in descriptions)
{
    var result = pipeline.Discover(text);
    total += result.Model.Entities.Count;

    Console.WriteLine($"== {text}");
    Console.WriteLine($"   topic: {result.Topic.Title} ({result.Topic.Confidence:F2})");
    Console.WriteLine($"   classes ({result.Model.Entities.Count}): " +
        string.Join(", ", result.Model.Entities.Select(e => $"{e.Name}[{e.Role}]({e.Fields.Count})")));
    Console.WriteLine($"   suggestions: {result.Model.Suggestions.Count}");
}

Console.WriteLine($"\nTOTAL CLASSES ACROSS {descriptions.Length} DOMAINS: {total}");
```

- [ ] **Step 3: Run it and capture the output**

Run (from the scratchpad project directory): `dotnet run -c Release 2>&1 | tee results.txt`
Expected: twelve blocks and a total. The whole run takes a few minutes; each `Discover` is 3-6 seconds against the indexed store.

- [ ] **Step 4: Check each success criterion**

Against `results.txt`, check the five criteria from the spec, and write down the actual number for each:

1. `TOTAL CLASSES` ≥ **120** (baseline: 43).
2. No single domain below **5** classes (baseline: School 1, Veterinarian 0).
3. None of `Circumflex`, `Horse`, `Bunker`, `Jpg`, `Stagecoach` appears in any `classes` line.
4. The `a hotel booking application` line contains both a `Booking` class and a `Room` class.
5. CI-parity build and test, below.

- [ ] **Step 5: If a criterion fails, fix the table — not the criterion**

If criterion 3 fails, the offending name's head has been mapped too generously in Task 3: move that head out of `Archetypes` (to `AbstractHeads` if it genuinely names an abstraction, otherwise to neither table so the candidate is merely suggested), re-run steps 3-4, and record the change. If criterion 1 or 2 falls short, look at what the run's suggestion lists are full of, add only heads you can point at in that output, and re-run. Do not lower a number in the spec.

- [ ] **Step 6: Run the CI-parity build and the full suite**

```bash
cd /d/SmartCodeGenerator-Repository
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```
Expected: build clean (no warnings promoted to errors), all tests pass. Record the test count.

- [ ] **Step 7: Write the results document**

Create `docs/superpowers/plans/2026-07-22-domain-discovery-entity-detail-results.md` containing: the before/after class total, a per-domain before/after table, the verdict on each of the five criteria with its measured number, any head moved in step 5 and why, and the final test count. Paste the actual probe output for the hotel and school domains, since those were the worst baselines.

- [ ] **Step 8: Commit**

```bash
git add docs/superpowers/plans/2026-07-22-domain-discovery-entity-detail-results.md DomainDiscovery/Enrichment/ArchetypeLexicon.cs
git commit -m "docs: measured entity-yield results against the real dump store"
```

---

## Self-Review

**Spec coverage.** Spec §1 (widen `ArchetypeLexicon`) → Task 3. §2 (subtype chaining) → Task 4. §3 (harden `HypernymExtractor`) → Task 2. §4 (seed evidence) → Task 5. §5 (`EntityRole.Event`) → Task 1. Spec "Components and data flow" — the deferred-removal restructure of pass 2 is Task 4 step 8. Spec "Error handling" — cycle guard is Task 4 step 3, tested in step 1. Spec "Testing" — every named test appears in Tasks 1-5; the end-to-end census is Task 6. Spec "Success criteria" — all five are checked in Task 6 step 4. Spec "Out of scope" — restated in Global Constraints (lead gate, `MaxSupportingArticles`) and nothing in any task touches suggestion ranking, type inference or `PropertyMiner`.

**Type consistency.** `ShapeCandidate(string Name, string Head, ArchetypeResult Result)` is declared in Task 4 step 3 and used with that exact shape in Task 4 steps 1 and 8. `SubtypeChaining.Resolve(IReadOnlyList<ShapeCandidate>) → IReadOnlyDictionary<string, ArchetypeResult>` is consistent across steps 1, 3 and 8. `EntityRole.Event` is introduced in Task 1 and consumed by name in Tasks 3 and 4. `ArchetypeResult(ShapeVerdict, IReadOnlyList<FieldShape>, EntityRole)` matches the existing record in `ArchetypeLexicon.cs`. `DefaultFields.ApplyTo(CandidateEntity, Provenance)` matches the existing signature in `DomainDiscovery/Model/DefaultFields.cs`.

**Known interaction.** Task 5's exemption applies to `Provenance.Seed` candidates only; `DomainEnricher.Merge` attributes everything harvested from an article as `Provenance.Wiki`, so widening the seed rule cannot admit article nouns by the back door.
