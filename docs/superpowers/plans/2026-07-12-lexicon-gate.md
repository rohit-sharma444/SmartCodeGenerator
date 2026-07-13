# Lexicon Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop deleting discovered entities that a hand-curated lexicon happens not to know, and shape them instead from the article's wikilinks and each candidate's own definition.

**Architecture:** `DomainFieldLexicon` currently answers both "is this a domain class?" and "what fields does it have?", so a miss on the second silently deletes the entity. This splits the two. Candidates now come from prose nouns *and* the topic article's wikilinks; shaping resolves in precedence order (curated lexicon → archetype derived from the candidate's own first-sentence hypernym → unshaped). An *abstract* hypernym rejects the entity; an *unknown* one demotes it to a suggestion the user can promote in the review loop. Nothing is silently deleted.

**Tech Stack:** C# / .NET 10, xUnit, MediaWiki action API, existing `WikipediaCache` disk cache.

**Spec:** `docs/superpowers/specs/2026-07-12-lexicon-gate-design.md`

## Global Constraints

- Target framework `net10.0`; existing project layout, no new NuGet dependencies.
- `IWikipediaClient` implementations **never throw** — network failure degrades to an empty result. `HttpWikipediaClient` already does this; new methods must follow it.
- New disk-cache keys only (`links:{title}`, `lead:{title}`). Existing `extract:` and `search:` keys must keep working so `.dd-cache` stays valid.
- Seed entities (user-named, `Provenance.Seed`) are never dropped and never demoted to suggestions.
- Supported field types are exactly: `string`, `int`, `decimal`, `bool`, `DateTime` (see `ModelEditor.SupportedTypes`).
- Run tests from the repo root: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`.
- The CLI's `bin` goes stale after `dotnet test`; before any end-to-end run, `dotnet build CodeGenerator/CodeGenerator.csproj`.

## File Structure

| File | Responsibility |
|---|---|
| `DomainDiscovery/SeedExtraction/NameNormalizer.cs` (modify) | add `ToCompoundEntityName` — multi-word titles → PascalCase |
| `DomainDiscovery/Enrichment/HypernymExtractor.cs` (create) | lead sentence → head noun |
| `DomainDiscovery/Enrichment/ArchetypeLexicon.cs` (create) | head noun → shape / abstract / unknown |
| `DomainDiscovery/Enrichment/LinkedConceptSource.cs` (create) | wikilink titles + article text → salient candidates |
| `DomainDiscovery/Wikipedia/IWikipediaClient.cs` (modify) | `FetchLinks`, `FetchLead` |
| `DomainDiscovery/Wikipedia/HttpWikipediaClient.cs` (modify) | implement both, throttled |
| `DomainDiscovery/Model/CandidateDomainModel.cs` (modify) | `SourceTitle`, `Suggestions`, `Rejected` |
| `DomainDiscovery/Enrichment/EntityShaper.cs` (create) | precedence + three-way partition; **replaces** `DomainFieldAssigner` |
| `DomainDiscovery/Enrichment/DomainFieldAssigner.cs` (delete) | folded into `EntityShaper` |
| `DomainDiscovery/Review/ModelEditor.cs` (modify) | `AddEntity` promotes from `Suggestions` |
| `DomainDiscovery/Enrichment/DomainEnricher.cs` (modify) | merge link candidates alongside prose harvest |
| `DomainDiscovery/DomainDiscoveryPipeline.cs` (modify) | call `EntityShaper` |
| `CodeGenerator/App/BuildAppCommand.cs` (modify) | print the two extra tiers |
| `DomainDiscovery.Tests/Fakes/FakeWikipediaClient.cs` (modify) | implement new interface methods |

---

### Task 1: Compound entity names

`NameNormalizer.ToEntityName` keeps only the **last word** ("Cash register" → `Register`), which would destroy every multi-word link title this design depends on. Link titles need a normalizer that keeps all words.

**Files:**
- Modify: `DomainDiscovery/SeedExtraction/NameNormalizer.cs`
- Test: `DomainDiscovery.Tests/SeedExtraction/NameNormalizerTests.cs`

**Interfaces:**
- Produces: `public static string ToCompoundEntityName(string text)` — PascalCases every word and singularizes the last: `"Cash register"` → `"CashRegister"`.

- [ ] **Step 1: Write the failing tests**

Append to `DomainDiscovery.Tests/SeedExtraction/NameNormalizerTests.cs` (inside the existing class):

```csharp
    [Theory]
    [InlineData("Cash register", "CashRegister")]
    [InlineData("Payment terminal", "PaymentTerminal")]
    [InlineData("Credit cards", "CreditCard")]
    [InlineData("Receipt", "Receipt")]
    [InlineData("self-checkout", "SelfCheckout")]
    public void ToCompoundEntityName_KeepsEveryWord_AndSingularizesTheLast(
        string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.ToCompoundEntityName(input));
    }

    [Fact]
    public void ToCompoundEntityName_OfBlank_IsEmpty()
    {
        Assert.Equal(string.Empty, NameNormalizer.ToCompoundEntityName("   "));
    }
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~ToCompoundEntityName"`
Expected: compile error — `ToCompoundEntityName` does not exist.

- [ ] **Step 3: Implement**

Add to `NameNormalizer` (it can reuse the existing private `Singularize`):

```csharp
    /// <summary>
    /// Turns a multi-word concept into a PascalCase type name, keeping every word and
    /// singularizing only the last ("Cash register" -> "CashRegister"). Unlike
    /// <see cref="ToEntityName"/>, which reduces a phrase to its head noun, this preserves
    /// compounds — a wikilink title names one concept, not a noun with modifiers.
    /// </summary>
    public static string ToCompoundEntityName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var words = text
            .Split(text.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(),
                   StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        if (words.Length == 0)
            return string.Empty;

        words[^1] = Singularize(words[^1].ToLowerInvariant());

        return string.Concat(words.Select(
            w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~ToCompoundEntityName"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/SeedExtraction/NameNormalizer.cs DomainDiscovery.Tests/SeedExtraction/NameNormalizerTests.cs
git commit -m "Add a normalizer that keeps compound concept names"
```

---

### Task 2: Hypernym extractor

**Files:**
- Create: `DomainDiscovery/Enrichment/HypernymExtractor.cs`
- Test: `DomainDiscovery.Tests/Enrichment/HypernymExtractorTests.cs`

**Interfaces:**
- Produces: `public static string Extract(string leadSentence)` — returns a lowercase head noun, or `""` when the sentence defines nothing.

The sentences below are **real** Wikipedia leads measured on 2026-07-12. Do not replace them with invented ones.

The rule: find the first copula (`is`/`are`/`was`/`were`), skip a determiner, then collect words until a terminator (`of`, `that`, `which`, `who`, `used`, a preposition, a comma, or the end) and take the **last** word collected. "an organized collection of facts" → `collection`; "a mobile device" → `device`.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class HypernymExtractorTests
{
    [Theory]
    // Real first sentences, fetched 2026-07-12.
    [InlineData("In sales, commerce and economics, a customer is the recipient of a good.", "recipient")]
    [InlineData("A receipt is a document acknowledging that a person has received money.", "document")]
    [InlineData("A restaurant is a business that prepares and serves food.", "business")]
    [InlineData("A computer is a machine that can be programmed.", "machine")]
    [InlineData("In computing, a database is an organized collection of data.", "collection")]
    [InlineData("A system is a group of interacting elements.", "group")]
    [InlineData("Business is the practice of making one's living.", "practice")]
    [InlineData("A tablet computer is a mobile device.", "device")]
    public void Extract_TakesTheHeadNoun_OfThePredicateNominal(string lead, string expected)
    {
        Assert.Equal(expected, HypernymExtractor.Extract(lead));
    }

    [Fact]
    public void Extract_OfADisambiguationLead_IsEmpty()
    {
        Assert.Equal(string.Empty, HypernymExtractor.Extract("Sale may refer to:"));
    }

    [Fact]
    public void Extract_WhenThereIsNoCopula_IsEmpty()
    {
        Assert.Equal(
            string.Empty,
            HypernymExtractor.Extract("Software consists of computer programs."));
    }

    [Fact]
    public void Extract_OfBlank_IsEmpty()
    {
        Assert.Equal(string.Empty, HypernymExtractor.Extract("   "));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~HypernymExtractorTests"`
Expected: compile error — `HypernymExtractor` does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace DomainDiscovery.Enrichment;

/// <summary>
/// Pulls the head noun of the predicate nominal out of a Wikipedia lead sentence:
/// "A receipt is a document acknowledging..." -> "document".
/// </summary>
/// <remarks>
/// The *topic* article cannot supply this — it defines its own title and then uses the rest
/// of its vocabulary without defining it (measured: zero definitions for any of the twenty
/// Point of sale candidates across its 299 sentences). Each candidate's own article does
/// define it, in its first sentence, which is what this reads.
/// </remarks>
public static class HypernymExtractor
{
    private static readonly string[] Copulas = { "is", "are", "was", "were" };

    private static readonly HashSet<string> Determiners =
        new(StringComparer.OrdinalIgnoreCase) { "a", "an", "the" };

    /// <summary>
    /// Ends the noun phrase. Everything after one of these modifies the head rather than
    /// being it, so "an organized collection of facts" stops at "of" and yields "collection".
    /// </summary>
    private static readonly HashSet<string> Terminators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "of", "that", "which", "who", "whose", "where", "when", "used",
            "in", "on", "for", "with", "to", "from", "by", "at"
        };

    public static string Extract(string leadSentence)
    {
        if (string.IsNullOrWhiteSpace(leadSentence))
            return string.Empty;

        // "Sale may refer to:" - a disambiguation page defines nothing, and pairing its
        // words with an archetype would invent a shape out of nothing.
        if (leadSentence.Contains("may refer to", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var words = leadSentence
            .Split(leadSentence.Where(c => !char.IsLetterOrDigit(c) && c != '\'' && c != '-')
                               .Distinct().ToArray(),
                   StringSplitOptions.RemoveEmptyEntries);

        var copula = Array.FindIndex(
            words, w => Copulas.Contains(w, StringComparer.OrdinalIgnoreCase));

        if (copula < 0)
            return string.Empty;

        string head = string.Empty;

        for (var i = copula + 1; i < words.Length; i++)
        {
            var word = words[i];

            if (Determiners.Contains(word))
                continue;

            if (Terminators.Contains(word))
                break;

            head = word;
        }

        return head.ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~HypernymExtractorTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/HypernymExtractor.cs DomainDiscovery.Tests/Enrichment/HypernymExtractorTests.cs
git commit -m "Read a definition's head noun from a Wikipedia lead"
```

---

### Task 3: Archetype lexicon

**Files:**
- Create: `DomainDiscovery/Enrichment/ArchetypeLexicon.cs`
- Test: `DomainDiscovery.Tests/Enrichment/ArchetypeLexiconTests.cs`

**Interfaces:**
- Consumes: `DomainFieldLexicon.FieldShape` (existing `public readonly record struct FieldShape(string Name, string Type)`).
- Produces:
  - `public enum ShapeVerdict { Shaped, Abstract, Unknown }`
  - `public readonly record struct ArchetypeResult(ShapeVerdict Verdict, IReadOnlyList<DomainFieldLexicon.FieldShape> Fields)`
  - `public static ArchetypeResult Resolve(string hypernymHead)`

The three-way distinction is the point: *abstract* means "not a class" (reject), *unknown* means "we don't know" (suggest). Silence is not denial.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class ArchetypeLexiconTests
{
    [Fact]
    public void Resolve_AKnownHead_ShapesIt()
    {
        var result = ArchetypeLexicon.Resolve("document");

        Assert.Equal(ShapeVerdict.Shaped, result.Verdict);
        Assert.Equal(new[] { "Number", "IssuedOn", "Total" }, result.Fields.Select(f => f.Name));
        Assert.Equal("decimal", result.Fields.Single(f => f.Name == "Total").Type);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("machine")]
    [InlineData("recipient")]
    [InlineData("business")]
    [InlineData("card")]
    public void Resolve_EachArchetypeHead_ShapesIt(string head)
    {
        Assert.Equal(ShapeVerdict.Shaped, ArchetypeLexicon.Resolve(head).Verdict);
    }

    [Theory]
    // These are what System, Business-as-practice, Data and Management actually resolve to.
    [InlineData("group")]
    [InlineData("practice")]
    [InlineData("collection")]
    [InlineData("concept")]
    [InlineData("administration")]
    public void Resolve_AnAbstractHead_RejectsIt(string head)
    {
        var result = ArchetypeLexicon.Resolve(head);

        Assert.Equal(ShapeVerdict.Abstract, result.Verdict);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Resolve_AHeadItHasNeverSeen_IsUnknown_NotRejected()
    {
        // Unknown is not denial: the entity becomes a suggestion, not a deletion.
        var result = ArchetypeLexicon.Resolve("kiosk");

        Assert.Equal(ShapeVerdict.Unknown, result.Verdict);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Resolve_Blank_IsUnknown()
    {
        Assert.Equal(ShapeVerdict.Unknown, ArchetypeLexicon.Resolve("").Verdict);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~ArchetypeLexiconTests"`
Expected: compile error — `ArchetypeLexicon` does not exist.

- [ ] **Step 3: Implement**

```csharp
using FieldShape = DomainDiscovery.Enrichment.DomainFieldLexicon.FieldShape;

namespace DomainDiscovery.Enrichment;

/// <summary>What a hypernym tells us about a candidate.</summary>
public enum ShapeVerdict
{
    /// <summary>The head names a kind of thing we can shape.</summary>
    Shaped,

    /// <summary>The head names an abstraction ("a group of interacting elements"), so the
    /// candidate is prose, not a domain class.</summary>
    Abstract,

    /// <summary>We have no opinion. The candidate is offered to the user, not deleted.</summary>
    Unknown
}

public readonly record struct ArchetypeResult(
    ShapeVerdict Verdict, IReadOnlyList<FieldShape> Fields);

/// <summary>
/// Maps the head noun of a definition to a field shape. This is the only curation in the
/// discovery path, and unlike <see cref="DomainFieldLexicon"/> it is domain-independent:
/// "device" shapes a cash register, a payment terminal and an MRI scanner alike, so a new
/// topic needs no new entries.
/// </summary>
public static class ArchetypeLexicon
{
    private static FieldShape Text(string name) => new(name, "string");
    private static FieldShape Money(string name) => new(name, "decimal");
    private static FieldShape When(string name) => new(name, "DateTime");

    private static readonly Dictionary<string, FieldShape[]> Archetypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["device"] = [Text("Name"), Text("Model"), Text("SerialNumber")],
            ["machine"] = [Text("Name"), Text("Model"), Text("SerialNumber")],
            ["terminal"] = [Text("Name"), Text("Model"), Text("SerialNumber")],
            ["equipment"] = [Text("Name"), Text("Model"), Text("SerialNumber")],

            ["document"] = [Text("Number"), When("IssuedOn"), Money("Total")],
            ["record"] = [Text("Number"), When("IssuedOn"), Money("Total")],
            ["statement"] = [Text("Number"), When("IssuedOn"), Money("Total")],

            ["person"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["recipient"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["buyer"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["customer"] = [Text("FullName"), Text("Email"), Text("Phone")],

            ["business"] = [Text("Name"), Text("Address")],
            ["organization"] = [Text("Name"), Text("Address")],
            ["organisation"] = [Text("Name"), Text("Address")],
            ["establishment"] = [Text("Name"), Text("Address")],
            ["company"] = [Text("Name"), Text("Address")],
            ["shop"] = [Text("Name"), Text("Address")],
            ["store"] = [Text("Name"), Text("Address")],

            ["item"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["product"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["good"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["goods"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["merchandise"] = [Text("Name"), Text("Sku"), Money("Price")],

            ["payment"] = [Money("Amount"), When("OccurredOn"), Text("Method")],
            ["transaction"] = [Money("Amount"), When("OccurredOn"), Text("Method")],
            ["tender"] = [Money("Amount"), When("OccurredOn"), Text("Method")],

            ["card"] = [Text("Number"), Text("Holder"), When("ExpiresOn")],
        };

    /// <summary>
    /// Heads that name an abstraction rather than a thing. These are the drop signal that
    /// replaces the curated allow-list: System resolves to "group", Data to "collection",
    /// Business-the-activity to "practice".
    /// </summary>
    private static readonly HashSet<string> AbstractHeads =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "group", "collection", "set", "practice", "concept", "idea", "field",
            "discipline", "study", "science", "process", "method", "technique",
            "term", "activity", "administration", "period", "state", "condition",
            "branch", "area", "form", "way"
        };

    public static ArchetypeResult Resolve(string hypernymHead)
    {
        if (string.IsNullOrWhiteSpace(hypernymHead))
            return new ArchetypeResult(ShapeVerdict.Unknown, Array.Empty<FieldShape>());

        if (Archetypes.TryGetValue(hypernymHead, out var fields))
            return new ArchetypeResult(ShapeVerdict.Shaped, fields);

        if (AbstractHeads.Contains(hypernymHead))
            return new ArchetypeResult(ShapeVerdict.Abstract, Array.Empty<FieldShape>());

        return new ArchetypeResult(ShapeVerdict.Unknown, Array.Empty<FieldShape>());
    }
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~ArchetypeLexiconTests"`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/ArchetypeLexicon.cs DomainDiscovery.Tests/Enrichment/ArchetypeLexiconTests.cs
git commit -m "Shape an entity from its kind, not its name"
```

---

### Task 4: Linked concept source

**Files:**
- Create: `DomainDiscovery/Enrichment/LinkedConceptSource.cs`
- Test: `DomainDiscovery.Tests/Enrichment/LinkedConceptSourceTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlyList<string> Candidates(IReadOnlyList<string> linkTitles, string articleText)` — returns the surviving **raw titles** ("Cash register"), not normalized names. Task 9 normalizes them.

Filters, all measured against the real 209-link Point of sale link set:
- title occurs at least twice in the article text (case-insensitive) — separates `Cash register` (everywhere) from `National Retail Federation` (once);
- at most 3 words — drops `Automated sales suppression device`;
- no parenthetical qualifier — drops `Gondola (retail)`;
- fewer than 2 capitalized words — drops proper nouns like `National Retail Federation`.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class LinkedConceptSourceTests
{
    private const string Article = """
        A point of sale system is used in a retail store. The cash register records each
        sale, and the cash register prints a receipt. A receipt is given to the customer.
        The customer may pay with a credit card; a credit card is read by the payment
        terminal. The payment terminal talks to the merchant bank. Gondola shelving holds
        the stock. The National Retail Federation publishes standards.
        """;

    [Fact]
    public void Candidates_KeepsALinkTheArticleActuallyTalksAbout()
    {
        var links = new[] { "Cash register", "Receipt", "Credit card", "Payment terminal" };

        var kept = LinkedConceptSource.Candidates(links, Article);

        Assert.Equal(new[] { "Cash register", "Receipt", "Credit card", "Payment terminal" }, kept);
    }

    [Fact]
    public void Candidates_DropsALinkMentionedOnlyOnce()
    {
        // Linked, but the article says it once in passing - it is not what the article is about.
        var kept = LinkedConceptSource.Candidates(new[] { "Merchant bank" }, Article);

        Assert.Empty(kept);
    }

    [Fact]
    public void Candidates_DropsAParentheticalTitle()
    {
        Assert.Empty(LinkedConceptSource.Candidates(new[] { "Gondola (retail)" }, Article));
    }

    [Fact]
    public void Candidates_DropsAnOverlongTitle()
    {
        var kept = LinkedConceptSource.Candidates(
            new[] { "Automated sales suppression device" }, Article);

        Assert.Empty(kept);
    }

    [Fact]
    public void Candidates_DropsAProperNoun()
    {
        // Two or more capitalized words: an organization, not a domain concept.
        var kept = LinkedConceptSource.Candidates(new[] { "National Retail Federation" }, Article);

        Assert.Empty(kept);
    }

    [Fact]
    public void Candidates_OfNoLinks_IsEmpty()
    {
        Assert.Empty(LinkedConceptSource.Candidates(Array.Empty<string>(), Article));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~LinkedConceptSourceTests"`
Expected: compile error — `LinkedConceptSource` does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace DomainDiscovery.Enrichment;

/// <summary>
/// Picks domain concepts out of a topic article's wikilinks.
/// </summary>
/// <remarks>
/// Wikilink targets are disambiguated by human editors, and they name the multi-word
/// concepts prose-noun frequency structurally cannot see: "cash register" tokenizes into
/// two common nouns that normalize to junk, so the harvester can never surface it, yet the
/// article links it. A link alone is not enough — an article links hundreds of things it
/// merely mentions — so a candidate must also be something the prose keeps talking about.
/// </remarks>
public static class LinkedConceptSource
{
    /// <summary>Mentions in the prose before a linked title counts as a domain concept.</summary>
    public const int MinMentions = 2;

    /// <summary>Longer titles are descriptions, not concepts ("Automated sales suppression device").</summary>
    public const int MaxTitleWords = 3;

    public static IReadOnlyList<string> Candidates(
        IReadOnlyList<string> linkTitles, string articleText)
    {
        if (linkTitles is null || linkTitles.Count == 0 || string.IsNullOrWhiteSpace(articleText))
            return Array.Empty<string>();

        var kept = new List<string>();

        foreach (var title in linkTitles)
        {
            if (string.IsNullOrWhiteSpace(title))
                continue;

            // "Gondola (retail)": the qualifier exists because the bare word is ambiguous.
            if (title.Contains('('))
                continue;

            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length > MaxTitleWords)
                continue;

            // "National Retail Federation" - a name, not a kind of thing.
            if (words.Count(w => w.Length > 0 && char.IsUpper(w[0])) >= 2)
                continue;

            if (CountOccurrences(articleText, title) >= MinMentions)
                kept.Add(title);
        }

        return kept;
    }

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var at = 0;

        while ((at = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            at += term.Length;
        }

        return count;
    }
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~LinkedConceptSourceTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/LinkedConceptSource.cs DomainDiscovery.Tests/Enrichment/LinkedConceptSourceTests.cs
git commit -m "Mine domain concepts from the article's wikilinks"
```

---

### Task 5: Fetch links and leads

**Files:**
- Modify: `DomainDiscovery/Wikipedia/IWikipediaClient.cs`
- Modify: `DomainDiscovery/Wikipedia/HttpWikipediaClient.cs`
- Modify: `DomainDiscovery.Tests/Fakes/FakeWikipediaClient.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/HttpWikipediaClientTests.cs` (create if absent)

**Interfaces:**
- Produces on `IWikipediaClient`:
  - `IReadOnlyList<string> FetchLinks(string articleTitle)`
  - `string FetchLead(string articleTitle)`
- Produces on `FakeWikipediaClient`: `Dictionary<string, List<string>> Links` and `Dictionary<string, string> Leads`, both keyed by article title.

Both new methods must degrade to an empty result rather than throw, and use new cache keys so `.dd-cache` stays valid.

- [ ] **Step 1: Write the failing test**

Create `DomainDiscovery.Tests/Wikipedia/HttpWikipediaClientTests.cs`:

```csharp
using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia;

public class HttpWikipediaClientTests : IDisposable
{
    private readonly string _cacheDir =
        Path.Combine(Path.GetTempPath(), "dd-cache-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FetchLinks_ParsesTitles_FromACachedApiResponse()
    {
        // The cache is read before the network, so seeding it exercises the parser offline.
        Directory.CreateDirectory(_cacheDir);
        WriteCache("links:Point of sale", """
            {"query":{"pages":{"1":{"links":[
                {"ns":0,"title":"Cash register"},{"ns":0,"title":"Receipt"}]}}}}
            """);

        var client = new HttpWikipediaClient(_cacheDir);

        Assert.Equal(new[] { "Cash register", "Receipt" }, client.FetchLinks("Point of sale"));
    }

    [Fact]
    public void FetchLead_ParsesTheExtract_FromACachedApiResponse()
    {
        Directory.CreateDirectory(_cacheDir);
        WriteCache("lead:Receipt", """
            {"query":{"pages":{"1":{"extract":"A receipt is a document."}}}}
            """);

        var client = new HttpWikipediaClient(_cacheDir);

        Assert.Equal("A receipt is a document.", client.FetchLead("Receipt"));
    }

    [Fact]
    public void FetchLinks_WhenTheResponseIsUnusable_IsEmpty()
    {
        Directory.CreateDirectory(_cacheDir);
        WriteCache("links:Nonsense", "{ not json");

        var client = new HttpWikipediaClient(_cacheDir);

        Assert.Empty(client.FetchLinks("Nonsense"));
    }

    // Mirrors WikipediaCache's own key-to-filename scheme.
    private void WriteCache(string key, string body)
    {
        var safe = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        File.WriteAllText(Path.Combine(_cacheDir, safe + ".cache"), body);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }
}
```

**Before writing the implementation, open `DomainDiscovery/Wikipedia/WikipediaCache.cs` and make `WriteCache` above match its real key-to-filename mapping exactly.** If it differs, fix the test helper — not the cache.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~HttpWikipediaClientTests"`
Expected: compile error — `FetchLinks` / `FetchLead` do not exist.

- [ ] **Step 3: Implement**

In `IWikipediaClient.cs`, add to the interface:

```csharp
    /// <summary>
    /// Mainspace article titles this article links to, or empty when unavailable. Link
    /// targets are disambiguated by editors, which is why they beat raw prose nouns.
    /// </summary>
    IReadOnlyList<string> FetchLinks(string articleTitle);

    /// <summary>
    /// The article's first sentence — its definition — or empty when unavailable.
    /// </summary>
    string FetchLead(string articleTitle);
```

In `HttpWikipediaClient.cs`, add:

```csharp
    /// <summary>
    /// Between uncached requests. Shaping fetches one lead per candidate, and the API
    /// answered ~14 rapid requests with HTTP 429 during design measurement.
    /// </summary>
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(150);

    public IReadOnlyList<string> FetchLinks(string articleTitle)
    {
        var url = $"{ApiBase}?action=query&prop=links&plnamespace=0&pllimit=500"
                + $"&redirects=1&format=json&titles={Uri.EscapeDataString(articleTitle)}";

        var json = GetCached($"links:{articleTitle}", url);
        if (json.Length == 0)
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var pages = doc.RootElement.GetProperty("query").GetProperty("pages");

            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("links", out var links))
                    continue;

                var titles = new List<string>();
                foreach (var link in links.EnumerateArray())
                {
                    if (link.TryGetProperty("title", out var title))
                        titles.Add(title.GetString() ?? string.Empty);
                }
                return titles;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // fall through
        }

        return Array.Empty<string>();
    }

    public string FetchLead(string articleTitle)
    {
        var url = $"{ApiBase}?action=query&prop=extracts&exintro=1&explaintext=1&exsentences=1"
                + $"&redirects=1&format=json&titles={Uri.EscapeDataString(articleTitle)}";

        var json = GetCached($"lead:{articleTitle}", url);
        if (json.Length == 0)
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var pages = doc.RootElement.GetProperty("query").GetProperty("pages");

            foreach (var page in pages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("extract", out var extract))
                    return extract.GetString() ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // fall through
        }

        return string.Empty;
    }
```

And in `GetCached`, throttle only the network path (a cache hit must stay instant) — replace the body's `try` block opener so the delay happens before the HTTP call:

```csharp
    private string GetCached(string key, string url)
    {
        if (_cache.TryGet(key, out var cached))
            return cached;

        Thread.Sleep(ThrottleDelay);

        try
        {
            var body = _http.GetStringAsync(url).GetAwaiter().GetResult();
            try { _cache.Put(key, body); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* caching is best-effort */ }
            return body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
```

In `FakeWikipediaClient.cs`, add:

```csharp
    public Dictionary<string, List<string>> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Leads { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> FetchLinks(string articleTitle)
        => Links.TryGetValue(articleTitle, out var links) ? links : Array.Empty<string>();

    public string FetchLead(string articleTitle)
        => Leads.TryGetValue(articleTitle, out var lead) ? lead : string.Empty;
```

- [ ] **Step 4: Run the whole suite and watch it pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS. The interface grew, so every existing test still compiles only because `FakeWikipediaClient` implements the new members.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Wikipedia/ DomainDiscovery.Tests/Fakes/FakeWikipediaClient.cs DomainDiscovery.Tests/Wikipedia/
git commit -m "Fetch an article's links and a candidate's definition"
```

---

### Task 6: Model carries the two extra tiers

**Files:**
- Modify: `DomainDiscovery/Model/CandidateDomainModel.cs`
- Test: none of its own — Tasks 7 and 8 exercise these.

**Interfaces:**
- Produces:
  - `CandidateEntity.SourceTitle` — `public string? SourceTitle { get; set; }`, the Wikipedia title a link-derived candidate came from ("Cash register" for entity `CashRegister`). `null` for prose-derived entities, whose own name is the title to look up.
  - `CandidateDomainModel.Suggestions` — `public List<CandidateEntity> Suggestions { get; } = new();`
  - `CandidateDomainModel.Rejected` — `public List<string> Rejected { get; } = new();`

- [ ] **Step 1: Implement (no test of its own — these are data holders)**

In `CandidateEntity`, add:

```csharp
    /// <summary>
    /// The Wikipedia article this candidate came from, when its title differs from its type
    /// name ("Cash register" -> CashRegister). Null for a prose-harvested noun, whose name
    /// is already the title to look up.
    /// </summary>
    public string? SourceTitle { get; set; }
```

In `CandidateDomainModel`, add:

```csharp
    /// <summary>
    /// Candidates we could not shape. They are offered to the user rather than deleted:
    /// not knowing an entity's fields is not the same as knowing it is not an entity.
    /// </summary>
    public List<CandidateEntity> Suggestions { get; } = new();

    /// <summary>
    /// Names rejected as prose — their definition resolved to an abstraction. Shown once,
    /// so a missing class is explainable rather than mysterious.
    /// </summary>
    public List<string> Rejected { get; } = new();
```

- [ ] **Step 2: Build**

Run: `dotnet build DomainDiscovery/DomainDiscovery.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add DomainDiscovery/Model/CandidateDomainModel.cs
git commit -m "Let the candidate model carry suggestions and rejections"
```

---

### Task 7: EntityShaper replaces DomainFieldAssigner

This is the heart of the change: the `model.Entities.Remove(entity)` that deletes sixteen of twenty POS candidates goes away.

**Files:**
- Create: `DomainDiscovery/Enrichment/EntityShaper.cs`
- Delete: `DomainDiscovery/Enrichment/DomainFieldAssigner.cs`
- Delete: `DomainDiscovery.Tests/Enrichment/DomainFieldAssignerTests.cs` (ported below)
- Create: `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs`

**Interfaces:**
- Consumes: `IWikipediaClient.FetchLead`, `HypernymExtractor.Extract`, `ArchetypeLexicon.Resolve`, `DomainFieldLexicon.Fields`, `DefaultFields.ApplyTo`.
- Produces: `public sealed class EntityShaper { public EntityShaper(IWikipediaClient client); public void Shape(CandidateDomainModel model); }`

Precedence: curated lexicon → archetype from the candidate's own definition → unshaped. Seed entities are never dropped or demoted.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs`. The first five tests are ported from `DomainFieldAssignerTests` and must keep passing; the sixth is the deliberate contract change.

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Fakes;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class EntityShaperTests
{
    private readonly FakeWikipediaClient _wiki = new();

    private EntityShaper Shaper => new(_wiki);

    private static CandidateDomainModel ModelOf(params string[] names)
    {
        var model = new CandidateDomainModel();
        foreach (var n in names)
            model.Entities.Add(new CandidateEntity { Name = n, Provenance = Provenance.Wiki });
        return model;
    }

    [Fact]
    public void Shape_GivesAKnownEntity_ItsOwnFields()
    {
        var model = ModelOf("Recipe");

        Shaper.Shape(model);

        var recipe = Assert.Single(model.Entities);
        Assert.Equal(
            new[] { "Name", "Servings", "PrepMinutes", "Instructions" },
            recipe.Fields.Select(f => f.Name));
        Assert.Equal("int", recipe.Fields.Single(f => f.Name == "Servings").Type);
    }

    [Fact]
    public void Shape_KeepsFieldsAlreadyOnTheEntity_AndDoesNotDuplicateThem()
    {
        var model = ModelOf("Recipe");
        model.Entities[0].Fields.Add(new CandidateField
        {
            Name = "Name", Type = "string", Provenance = Provenance.Seed
        });

        Shaper.Shape(model);

        var recipe = Assert.Single(model.Entities);
        Assert.Single(recipe.Fields, f => f.Name == "Name");
        Assert.Equal(Provenance.Seed, recipe.Fields.Single(f => f.Name == "Name").Provenance);
    }

    [Fact]
    public void Shape_KeepsASeedEntity_ItCannotShape()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed });

        Shaper.Shape(model);

        // The user asserted it exists, so it is never dropped and never demoted.
        var widget = Assert.Single(model.Entities);
        Assert.Equal(DefaultFields.NameField, Assert.Single(widget.Fields).Name);
        Assert.Empty(model.Suggestions);
    }

    [Fact]
    public void Shape_TheCuratedLexicon_BeatsTheArchetype()
    {
        // Customer is curated; its definition would otherwise shape it as a "recipient".
        _wiki.Leads["Customer"] = "A customer is the recipient of a good.";
        var model = ModelOf("Customer");

        Shaper.Shape(model);

        var customer = Assert.Single(model.Entities);
        Assert.Equal(
            new[] { "FullName", "Email", "Phone" },
            customer.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Shape_ShapesAnUncuratedEntity_FromItsOwnDefinition()
    {
        _wiki.Leads["Cash register"] = "A cash register is a device for registering sales.";
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "CashRegister", SourceTitle = "Cash register", Provenance = Provenance.Wiki
        });

        Shaper.Shape(model);

        var register = Assert.Single(model.Entities);
        Assert.Equal(
            new[] { "Name", "Model", "SerialNumber" },
            register.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Shape_RejectsAnAbstraction_AndSuggestsWhatItCannotShape()
    {
        // Replaces DomainFieldAssignerTests.Apply_DropsEntities_WhoseShapeIsUnknown.
        // System is prose (an abstraction) and is rejected; Kiosk is simply unknown, so it
        // is offered rather than deleted.
        _wiki.Leads["System"] = "A system is a group of interacting elements.";
        _wiki.Leads["Kiosk"] = "A kiosk is a small booth.";
        var model = ModelOf("Recipe", "System", "Kiosk");

        Shaper.Shape(model);

        Assert.Equal(new[] { "Recipe" }, model.Entities.Select(e => e.Name));
        Assert.Equal(new[] { "Kiosk" }, model.Suggestions.Select(e => e.Name));
        Assert.Equal(new[] { "System" }, model.Rejected);
    }

    [Fact]
    public void Shape_WhenTheLeadCannotBeFetched_SuggestsRatherThanDeletes()
    {
        // Offline, or a 429: we know nothing, so we must not pretend to know it is not a class.
        var model = ModelOf("Barcode");

        Shaper.Shape(model);

        Assert.Empty(model.Entities);
        Assert.Equal(new[] { "Barcode" }, model.Suggestions.Select(e => e.Name));
        Assert.Empty(model.Rejected);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~EntityShaperTests"`
Expected: compile error — `EntityShaper` does not exist.

- [ ] **Step 3: Implement, and delete the assigner**

Create `DomainDiscovery/Enrichment/EntityShaper.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Shapes a discovered model, and decides what is a domain class at all.
/// </summary>
/// <remarks>
/// <para>
/// Replaces DomainFieldAssigner, which answered both questions with one lookup in
/// <see cref="DomainFieldLexicon"/> — so an entity whose fields were merely uncurated was
/// deleted as though it were prose. Sixteen of the twenty candidates harvested from the
/// Point of sale article died that way.
/// </para>
/// <para>
/// Shape resolves in precedence order: the curated lexicon, then the archetype implied by
/// the candidate's own Wikipedia definition, then nothing. An abstract definition ("a
/// system is a group of interacting elements") rejects the candidate — that is the drop
/// signal, and unlike an allow-list it needs no curation per domain. An absent or unreadable
/// definition means we do not know, and the candidate is offered to the user instead.
/// </para>
/// </remarks>
public sealed class EntityShaper
{
    private readonly IWikipediaClient _client;

    public EntityShaper(IWikipediaClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public void Shape(CandidateDomainModel model)
    {
        foreach (var entity in model.Entities.ToList())
        {
            foreach (var shape in DomainFieldLexicon.Fields(entity.Name))
                AddField(entity, shape.Name, shape.Type);

            if (entity.Fields.Count > 0)
                continue;

            // The user named it: they have asserted it exists. Never dropped, never demoted.
            if (entity.Provenance == Provenance.Seed)
            {
                DefaultFields.ApplyTo(entity, Provenance.Seed);
                continue;
            }

            var lead = _client.FetchLead(entity.SourceTitle ?? entity.Name);
            var result = ArchetypeLexicon.Resolve(HypernymExtractor.Extract(lead));

            switch (result.Verdict)
            {
                case ShapeVerdict.Shaped:
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
    }

    private static void AddField(CandidateEntity entity, string name, string type)
    {
        if (entity.Fields.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        entity.Fields.Add(new CandidateField
        {
            Name = name,
            Type = type,
            Provenance = entity.Provenance
        });
    }
}
```

Then delete both old files:

```bash
git rm DomainDiscovery/Enrichment/DomainFieldAssigner.cs DomainDiscovery.Tests/Enrichment/DomainFieldAssignerTests.cs
```

This breaks `DomainDiscoveryPipeline`, which still calls `DomainFieldAssigner.Apply`. Task 9 rewires it; to keep this task's build green, do the pipeline edit now as part of this step:

In `DomainDiscoveryPipeline.Discover`, replace both `DomainFieldAssigner.Apply(...)` calls:

```csharp
    public DiscoveryResult Discover(string freeText)
    {
        var analysis = _analyzer.Analyze(freeText ?? string.Empty);
        var seed = _seedExtractor.Extract(analysis);

        // Recognition searches on the seed names, so shaping — which decides what is a domain
        // class at all — has to wait until the article has been chosen.
        var topic = _recognizer.Recognize(seed.Entities.Select(e => e.Name));
        if (!topic.IsRecognized)
        {
            _shaper.Shape(seed);
            return new DiscoveryResult(topic, seed);
        }

        var enriched = _enricher.Enrich(topic, seed);
        _shaper.Shape(enriched);
        return new DiscoveryResult(topic, enriched);
    }
```

Add the field and constructor parameter, and wire it in `CreateDefault`:

```csharp
    private readonly EntityShaper _shaper;

    public DomainDiscoveryPipeline(
        ISemanticAnalyzer analyzer,
        SeedEntityExtractor seedExtractor,
        ITopicRecognizer recognizer,
        IDomainEnricher enricher,
        EntityShaper shaper)
    {
        _analyzer = analyzer;
        _seedExtractor = seedExtractor;
        _recognizer = recognizer;
        _enricher = enricher;
        _shaper = shaper;
    }
```

and in `CreateDefault`, pass `new EntityShaper(client)` as the fifth argument.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS. Any pipeline test constructing `DomainDiscoveryPipeline` directly needs the new fifth argument — fix those call sites, do not change the design.

- [ ] **Step 5: Commit**

```bash
git add -A DomainDiscovery DomainDiscovery.Tests
git commit -m "Shape entities from their own definition instead of deleting them"
```

---

### Task 8: Promote a suggestion with `add`

**Files:**
- Modify: `DomainDiscovery/Review/ModelEditor.cs`
- Test: `DomainDiscovery.Tests/Review/ModelEditorTests.cs`

**Interfaces:**
- Modifies: `ModelEditor.AddEntity(CandidateDomainModel, string)` — now checks `model.Suggestions` first, moving the entity across with its provenance and confidence intact. No new command.

- [ ] **Step 1: Write the failing test**

Append to the existing `ModelEditorTests` class:

```csharp
    [Fact]
    public void AddEntity_PromotesASuggestion_KeepingItsProvenanceAndConfidence()
    {
        var model = new CandidateDomainModel();
        model.Suggestions.Add(new CandidateEntity
        {
            Name = "Barcode", Provenance = Provenance.Wiki, Confidence = 0.42
        });

        Assert.True(ModelEditor.AddEntity(model, "barcode"));

        var barcode = Assert.Single(model.Entities);
        Assert.Equal("Barcode", barcode.Name);
        Assert.Equal(Provenance.Wiki, barcode.Provenance);
        Assert.Equal(0.42, barcode.Confidence);
        Assert.Equal(DefaultFields.NameField, Assert.Single(barcode.Fields).Name);
        Assert.Empty(model.Suggestions);
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~AddEntity_PromotesASuggestion"`
Expected: FAIL — the entity is added fresh with `Provenance.Seed` and confidence 0, and `Suggestions` still holds it.

- [ ] **Step 3: Implement**

In `ModelEditor.AddEntity`, after the duplicate check and before constructing a fresh entity:

```csharp
        // Promoting a suggestion is not the same as inventing an entity: keep what discovery
        // already learned about it.
        var suggested = model.Suggestions.FirstOrDefault(
            e => string.Equals(e.Name, normalized, StringComparison.OrdinalIgnoreCase));

        if (suggested is not null)
        {
            model.Suggestions.Remove(suggested);

            foreach (var shape in DomainFieldLexicon.Fields(normalized))
                suggested.Fields.Add(new CandidateField
                {
                    Name = shape.Name,
                    Type = shape.Type,
                    Provenance = suggested.Provenance
                });

            if (suggested.Fields.Count == 0)
                DefaultFields.ApplyTo(suggested, suggested.Provenance);

            model.Entities.Add(suggested);
            return true;
        }
```

- [ ] **Step 4: Run the suite and watch it pass**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Review/ModelEditor.cs DomainDiscovery.Tests/Review/ModelEditorTests.cs
git commit -m "Promote a suggested entity with the add command"
```

---

### Task 9: Feed link candidates into enrichment

**Files:**
- Modify: `DomainDiscovery/Enrichment/DomainEnricher.cs`
- Test: `DomainDiscovery.Tests/Enrichment/DomainEnricherTests.cs`

**Interfaces:**
- Consumes: `LinkedConceptSource.Candidates`, `NameNormalizer.ToCompoundEntityName`, `IWikipediaClient.FetchLinks`.
- Produces: link-derived `CandidateEntity` values with `SourceTitle` set to the raw title and `Provenance.Wiki`, merged into the model alongside the prose harvest. No constructor change.

- [ ] **Step 1: Write the failing test**

Append to the existing `DomainEnricherTests` class (match its existing setup style for building the enricher and topic):

```csharp
    [Fact]
    public void Enrich_AddsALinkedConcept_ProseNounCountingCannotSee()
    {
        // "cash register" tokenizes into two common nouns and normalizes to junk, so the
        // harvester can never surface it - but the article links it.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] =
            "The cash register records each sale. The cash register prints a receipt.";
        wiki.Links["Point of sale"] = new List<string> { "Cash register" };

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich(
            new TopicResult("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 0.5),
            new CandidateDomainModel());

        var register = Assert.Single(model.Entities, e => e.Name == "CashRegister");
        Assert.Equal("Cash register", register.SourceTitle);
        Assert.Equal(Provenance.Wiki, register.Provenance);
    }
```

**Check `TopicResult`'s real constructor before running** — match whatever `DomainEnricherTests` already uses to build one.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~Enrich_AddsALinkedConcept"`
Expected: FAIL — no `CashRegister` entity.

- [ ] **Step 3: Implement**

Replace `DomainEnricher.Enrich`:

```csharp
    public CandidateDomainModel Enrich(TopicResult topic, CandidateDomainModel seed)
    {
        var text = _client.FetchExtract(topic.Title);
        if (string.IsNullOrWhiteSpace(text))
            return seed;

        var harvested = _harvester.Harvest(_analyzer.Analyze(text));

        // Wikilinks name the multi-word concepts prose-noun frequency structurally cannot
        // see: "cash register" tokenizes into two common nouns that normalize to junk.
        foreach (var title in LinkedConceptSource.Candidates(_client.FetchLinks(topic.Title), text))
        {
            var name = NameNormalizer.ToCompoundEntityName(title);

            if (!NameNormalizer.IsValidIdentifier(name))
                continue;

            if (harvested.Entities.Any(
                    e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            harvested.Entities.Add(new CandidateEntity
            {
                Name = name,
                SourceTitle = title,
                Provenance = Provenance.Wiki,
                Confidence = LinkConfidence
            });
        }

        return Merge(seed, harvested);
    }

    /// <summary>
    /// A linked concept is a real, editor-disambiguated topic, but it carries no mention
    /// count of its own, so it ranks below the article's most-repeated nouns.
    /// </summary>
    private const double LinkConfidence = 0.5;
```

Add `using DomainDiscovery.SeedExtraction;` to the file's usings.

- [ ] **Step 4: Run the suite and watch it pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/DomainEnricher.cs DomainDiscovery.Tests/Enrichment/DomainEnricherTests.cs
git commit -m "Harvest linked concepts alongside prose nouns"
```

---

### Task 10: Show the tiers in the review loop

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs` (`PrintModel`, around line 136)

**Interfaces:**
- Consumes: `CandidateDomainModel.Suggestions`, `CandidateDomainModel.Rejected`.

- [ ] **Step 1: Implement**

Replace `PrintModel`:

```csharp
    private static void PrintModel(CandidateDomainModel model)
    {
        Console.WriteLine();
        Console.WriteLine($"Candidate model ({model.Entities.Count} entities):");
        foreach (var entity in model.Entities)
        {
            var fields = entity.Fields.Count == 0
                ? string.Empty
                : " { " + string.Join(", ", entity.Fields.Select(f => $"{f.Name}:{f.Type}")) + " }";
            Console.WriteLine($"  - {entity.Name} [{entity.Provenance}]{fields}");
        }

        if (model.Suggestions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Also found - 'add <name>' to include: "
                + string.Join(", ", model.Suggestions.Select(e => e.Name)));
        }

        if (model.Rejected.Count > 0)
        {
            Console.WriteLine("Not classes: " + string.Join(", ", model.Rejected));
        }
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build CodeGenerator/CodeGenerator.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs
git commit -m "Show suggested and rejected candidates in the review loop"
```

---

### Task 11: End-to-end verification

Green tests are not the claim. The claim is what `build app:` actually prints.

**Files:** none — this task produces evidence.

- [ ] **Step 1: Rebuild the CLI**

A solution-level `dotnet test` leaves `CodeGenerator/bin` stale, which has previously made a working fix look dead.

```bash
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx
dotnet build CodeGenerator/CodeGenerator.csproj
```

Expected: all tests pass; 0 build errors.

- [ ] **Step 2: Run `build app:` against a scratch workspace**

The cached article lives in `D:\SampleProjects\.dd-cache`; seed a scratch workspace with it so the run neither touches the repo nor refetches the extract. Leads and links are *not* cached yet, so this first run hits the network.

```bash
WS="$TMPDIR/posws" && rm -rf "$WS" && mkdir -p "$WS/.dd-cache"
cp "D:/SampleProjects/.dd-cache/extract_Point of sale.cache" "$WS/.dd-cache/"
printf 'build app: develop pointofsale application\nc\nexit\n' \
  | PROJECTPATH="$(cygpath -w "$WS")" \
    D:/SmartCodeGenerator-Repository/CodeGenerator/bin/Debug/net10.0/CodeGenerator.exe
```

(`c` cancels at the review prompt — this task verifies the proposed model, not generation. `a` approves.)

- [ ] **Step 3: Check the output against the spec's success criteria**

Expected in the candidate model: `CashRegister`, `Receipt`, `CreditCard`, `PaymentTerminal`, alongside the four that already survive (`Customer`, `Payment`, `Order`, `Inventory`).
Expected absent from the classes: `System`, `Data`, `Business` — they should appear on the `Not classes:` line instead.

If a concept is missing, find out which stage dropped it before changing anything: check whether the title survived `LinkedConceptSource` (mention count), then whether `FetchLead` returned a definition, then what `HypernymExtractor` made of it. Do not tune thresholds without knowing which one is at fault.

- [ ] **Step 4: Re-run to confirm the cache works**

```bash
printf 'build app: develop pointofsale application\nc\nexit\n' \
  | PROJECTPATH="$(cygpath -w "$WS")" \
    D:/SmartCodeGenerator-Repository/CodeGenerator/bin/Debug/net10.0/CodeGenerator.exe
```

Expected: the same model, noticeably faster, and `$WS/.dd-cache` now holds `links:` and `lead:` entries. This proves the throttle cost is paid once per topic, not once per run.

- [ ] **Step 5: Commit the evidence**

Record the real before/after in the commit message — the actual entity list, not a claim that it works.

```bash
git commit --allow-empty -F - <<'EOF'
Verify the lexicon gate end to end

build app: develop pointofsale application

  before: Customer, Payment, Order, Inventory (4)
  after:  <paste the actual list>

Not classes: <paste>
Also found:  <paste>
EOF
```

---

## Self-Review

**Spec coverage.** Selection from wikilinks → Tasks 4, 9. Shaping precedence → Task 7. Archetype table and the abstract/unknown split → Task 3. Hypernym extraction → Task 2. New client methods, cache keys, throttle → Task 5. `Suggestions`/`Rejected`/`SourceTitle` → Task 6. Promotion via `add` → Task 8. Review-loop display → Task 10. Error handling (offline, 429, disambiguation) → Task 2 (disambiguation lead), Task 5 (degrade to empty), Task 7 (`Shape_WhenTheLeadCannotBeFetched_SuggestsRatherThanDeletes`). End-to-end verification → Task 11. The rewrite of `Apply_DropsEntities_WhoseShapeIsUnknown` → Task 7, step 1.

**Two corrections the spec needed, made here.** The spec said link titles "go through the existing `NameNormalizer`", but `ToEntityName` keeps only the last word — "Cash register" would become `Register` and collide with the prose noun. Task 1 adds `ToCompoundEntityName`. The spec also omitted that widening `IWikipediaClient` breaks `FakeWikipediaClient`, which every existing DomainDiscovery test depends on; Task 5 updates it in the same commit.

**Known risk carried from the spec.** `Tablet` resolves to "device" and will be proposed as a class in a Recipe app. Accepted: salience is the defence, `remove Tablet` is the backstop, and it is now visible rather than silently deleted.
