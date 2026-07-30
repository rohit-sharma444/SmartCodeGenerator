# Decision Analysis for `build app:` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record why every discovery candidate was kept, suggested, rejected or skipped, and let the user ask for it in the `build app:` review loop with `why [name]`.

**Architecture:** An append-only `List<DecisionRecord>` hangs off `CandidateDomainModel`. Because `DomainEnricher.Merge` mutates and returns the *seed* model, the instance created by `SeedEntityExtractor` is the same object that reaches the review loop, so the log survives the whole pipeline with no plumbing. Each stage appends records at the points where candidates die. Formatting lives in a pure `DecisionReport` class with no `Console` dependency, so it is unit-testable without driving the loop.

**Tech Stack:** C# / .NET 10, xUnit (DomainDiscovery.Tests), MSTest (CodeGenerator.Tests), Roslyn (unused here), no new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-07-29-build-app-decision-analysis-design.md`

## Global Constraints

- **The CI gate is `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`.** The repo has FOUR `.slnx` files; this is the only one CI uses. It must be clean at the end of every task.
- **The two test projects use different frameworks.** `DomainDiscovery.Tests` and `NaturalLanguageEngine.Tests` are **xUnit** (`[Fact]`, `Assert.Equal(expected, actual)`, `Assert.Single`). `CodeGenerator.Tests` is **MSTest** (`[TestClass]`, `[TestMethod]`, `Assert.AreEqual(expected, actual)`, `Assert.IsTrue`). Do not mix them.
- **No new NuGet packages.** There is no central package management in this repo.
- **Reason text comes only from `DecisionReasons` constants.** Never write a reason string literal at a call site, and never assert a copy of the string in a test — assert the constant.
- **`approve` must generate byte-identical output to before this change.** Nothing in this feature may be read by scaffolding.
- **`CodeGenerator` already has `<InternalsVisibleTo Include="CodeGenerator.Tests" />`** (`CodeGenerator/CodeGenerator.csproj:7`), so `internal` members are testable. `DomainDiscovery` has no such attribute — everything tested there must be `public`.
- **Every test is written red first.** Run it and see the expected failure before writing the implementation. A green assertion never seen to fail has hidden real defects in this repo more than once.
- Branch is `feat/build-app-decision-analysis`, already created off `main` at `7c621a6`, with the spec committed at `bf499bb`.

---

## File Structure

**Create:**
- `DomainDiscovery/Model/DecisionRecord.cs` — the record type and its two enums.
- `DomainDiscovery/Model/DecisionReasons.cs` — the reason phrases, as constants, in one place.
- `CodeGenerator/App/DecisionReport.cs` — pure formatting: model + optional name → lines.
- `DomainDiscovery.Tests/Model/DecisionRecordTests.cs`
- `DomainDiscovery.Tests/Model/DecisionTrailTests.cs` — recording-point tests across the three stages.
- `CodeGenerator.Tests/App/DecisionReportTests.cs`
- `CodeGenerator.Tests/App/BuildAppWhyCommandTests.cs`

**Modify:**
- `DomainDiscovery/Model/CandidateDomainModel.cs` — add `Decisions` + `Record(...)`.
- `DomainDiscovery/SeedExtraction/SeedEntityExtractor.cs:21-44` — record the three skips and the keep.
- `DomainDiscovery/Enrichment/DomainEnricher.cs:35-57, 77-93, 101-136` — record the wikilink skip and the lead-demote; carry the harvested log across `Merge`.
- `DomainDiscovery/Enrichment/EntityShaper.cs:60-182` — record the four shaping outcomes.
- `CodeGenerator/App/BuildAppCommand.cs:54-165, 221-263` — the `why` verb, the help line, and the grouped summary.

---

### Task 1: The decision record and the model's log

**Files:**
- Create: `DomainDiscovery/Model/DecisionRecord.cs`
- Create: `DomainDiscovery/Model/DecisionReasons.cs`
- Modify: `DomainDiscovery/Model/CandidateDomainModel.cs:60-69`
- Test: `DomainDiscovery.Tests/Model/DecisionRecordTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DomainDiscovery.Model.DecisionStage` (`SeedExtraction`, `Enrichment`, `Shaping`); `DomainDiscovery.Model.DecisionOutcome` (`Kept`, `Suggested`, `Rejected`, `Skipped`); `DomainDiscovery.Model.DecisionRecord` with `string Name`, `DecisionStage Stage`, `DecisionOutcome Outcome`, `string Reason`, `string? Evidence`; `DomainDiscovery.Model.DecisionReasons` string constants; and on `CandidateDomainModel`: `List<DecisionRecord> Decisions { get; }` plus `void Record(string name, DecisionStage stage, DecisionOutcome outcome, string reason, string? evidence = null)`.

- [ ] **Step 1: Write the failing test**

Create `DomainDiscovery.Tests/Model/DecisionRecordTests.cs`:

```csharp
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class DecisionRecordTests
{
    [Fact]
    public void Record_AppendsOneRecord_CarryingEveryField()
    {
        var model = new CandidateDomainModel();

        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction, "head noun: practice");

        var record = Assert.Single(model.Decisions);
        Assert.Equal("Lending", record.Name);
        Assert.Equal(DecisionStage.Shaping, record.Stage);
        Assert.Equal(DecisionOutcome.Rejected, record.Outcome);
        Assert.Equal(DecisionReasons.DefinitionResolvesToAnAbstraction, record.Reason);
        Assert.Equal("head noun: practice", record.Evidence);
    }

    [Fact]
    public void Record_LeavesEvidenceNull_WhenNoneIsGiven()
    {
        var model = new CandidateDomainModel();

        model.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);

        Assert.Null(Assert.Single(model.Decisions).Evidence);
    }

    [Fact]
    public void Record_IsAppendOnly_AndTouchesNoOtherCollection()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book", Provenance = Provenance.Seed });

        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);
        model.Record("Fine", DecisionStage.Shaping, DecisionOutcome.Suggested,
            DecisionReasons.NoDefinitionWeRecognize);

        Assert.Equal(2, model.Decisions.Count);
        Assert.Equal(new[] { "Lending", "Fine" }, model.Decisions.Select(d => d.Name));

        // The log is a parallel channel. It must never write to the collections the
        // scaffolder actually reads.
        Assert.Single(model.Entities);
        Assert.Empty(model.Suggestions);
        Assert.Empty(model.Rejected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionRecordTests`
Expected: FAIL to **compile** — `DecisionStage`, `DecisionOutcome`, `DecisionReasons` and `CandidateDomainModel.Record` do not exist. A compile failure is the correct red state for a new type.

- [ ] **Step 3: Create the record type**

Create `DomainDiscovery/Model/DecisionRecord.cs`:

```csharp
namespace DomainDiscovery.Model;

/// <summary>Which stage of discovery reached a verdict about a candidate.</summary>
public enum DecisionStage
{
    /// <summary>Reading the nouns out of the user's own description.</summary>
    SeedExtraction,

    /// <summary>Harvesting and filtering candidates from a Wikipedia article.</summary>
    Enrichment,

    /// <summary>Deciding, from a candidate's own definition, whether it is a domain class.</summary>
    Shaping
}

/// <summary>What became of a candidate.</summary>
/// <remarks>
/// <see cref="Rejected"/> and <see cref="Skipped"/> are deliberately distinct, and the
/// difference is what the user should do about it. Rejected means the pipeline judged the
/// candidate and said no — <c>add</c> forces it back. Skipped means it never reached
/// judgement at all, which usually means the description needs rewording instead.
/// </remarks>
public enum DecisionOutcome
{
    /// <summary>It is a class in the model.</summary>
    Kept,

    /// <summary>Offered to the user, who can take it with <c>add</c>.</summary>
    Suggested,

    /// <summary>Judged not to be a domain class.</summary>
    Rejected,

    /// <summary>Never reached a verdict.</summary>
    Skipped
}

/// <summary>
/// One decision the discovery pipeline made about one candidate name. Append-only, and never
/// read by scaffolding: this exists so a user can ask why a class is missing and get an answer
/// instead of a guess.
/// </summary>
public sealed class DecisionRecord
{
    /// <summary>The candidate as it appeared at that stage — "Cash register", not CashRegister.
    /// <see cref="Model.CandidateDomainModel"/> holds both spellings across a run, and the
    /// report matches them canonically rather than forcing one here.</summary>
    public required string Name { get; init; }

    public required DecisionStage Stage { get; init; }

    public required DecisionOutcome Outcome { get; init; }

    /// <summary>A constant from <see cref="DecisionReasons"/>, never an ad-hoc literal — so a
    /// test asserts the same string the pipeline emits rather than a copy of it.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The concrete facts behind the reason — head noun, article title, entity type,
    /// confidence — as one already-composed line, rendered verbatim by the report. A record
    /// may carry several facts here; the reason stays the constant, the specifics stay here.
    /// </summary>
    public string? Evidence { get; init; }
}
```

- [ ] **Step 4: Create the reason constants**

Create `DomainDiscovery/Model/DecisionReasons.cs`:

```csharp
namespace DomainDiscovery.Model;

/// <summary>
/// Every reason the pipeline can give, in one place. Call sites use these constants rather
/// than literals so the phrasing can be changed once, and so a test asserts the same string
/// the pipeline emits instead of a copy that can drift out of step with it.
/// </summary>
public static class DecisionReasons
{
    // --- Seed extraction ---
    public const string NotRecognizedAsAThing = "not recognized as a thing";
    public const string NameNormalizedToNothing = "its name normalized to nothing";
    public const string AlreadyNamed = "already named by another candidate";
    public const string YouNamedIt = "you named it";

    // --- Enrichment ---
    public const string NotAUsableClassName = "not a usable class name";
    public const string LeadDoesNotMentionIt = "the article's lead does not mention it";
    public const string AlreadyAClassFromYourDescription = "already a class from your description";

    // --- Shaping ---
    public const string DefinedAsADomainClass = "defined as a domain class";
    public const string DefinitionResolvesToAnAbstraction = "its definition resolves to an abstraction";
    public const string YouNamedItNoDefinitionFound = "you named it; no definition found";
    public const string NoDefinitionWeRecognize = "no definition we recognize";
}
```

- [ ] **Step 5: Add the log to the model**

In `DomainDiscovery/Model/CandidateDomainModel.cs`, add to the `CandidateDomainModel` class, after the existing `Rejected` property:

```csharp
    /// <summary>
    /// Why each candidate met the fate it did. Append-only, and never read by scaffolding —
    /// this is the audit trail behind the review loop's <c>why</c> verb.
    /// </summary>
    /// <remarks>
    /// It lives on the model rather than in an observer because
    /// <see cref="Enrichment.DomainEnricher"/> mutates and returns the seed model, so this one
    /// instance already flows from seed extraction through to the review loop. A test can then
    /// assert the trail as a value on the result rather than by intercepting a callback.
    /// </remarks>
    public List<DecisionRecord> Decisions { get; } = new();

    /// <summary>Appends one decision. See <see cref="DecisionReasons"/> for
    /// <paramref name="reason"/>, which must be one of those constants.</summary>
    public void Record(
        string name,
        DecisionStage stage,
        DecisionOutcome outcome,
        string reason,
        string? evidence = null)
        => Decisions.Add(new DecisionRecord
        {
            Name = name,
            Stage = stage,
            Outcome = outcome,
            Reason = reason,
            Evidence = evidence
        });
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionRecordTests`
Expected: PASS, 3 tests.

- [ ] **Step 7: Verify the CI gate is still clean**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add DomainDiscovery/Model/DecisionRecord.cs DomainDiscovery/Model/DecisionReasons.cs \
        DomainDiscovery/Model/CandidateDomainModel.cs \
        DomainDiscovery.Tests/Model/DecisionRecordTests.cs
git commit -m "feat(discovery): add the decision record and the model's append-only log"
```

---

### Task 2: Record seed-extraction decisions

**Files:**
- Modify: `DomainDiscovery/SeedExtraction/SeedEntityExtractor.cs:21-44`
- Test: `DomainDiscovery.Tests/Model/DecisionTrailTests.cs` (create)

**Interfaces:**
- Consumes: `CandidateDomainModel.Record`, `DecisionStage.SeedExtraction`, `DecisionOutcome.Skipped`/`Kept`, `DecisionReasons.{NotRecognizedAsAThing, NameNormalizedToNothing, AlreadyNamed, YouNamedIt}` from Task 1.
- Produces: nothing new. `SeedEntityExtractor.Extract(SemanticResult)` keeps its existing signature and its existing entity output — only `model.Decisions` gains content.

**Context the implementer needs:** the method currently filters with `.Where(e => EntityTypes.Contains(e.Type))`, so a rejected entity type is never iterated and cannot be recorded. The loop must walk *every* entity and record the skip itself. `EntityType` is `NaturalLanguageEngine.Models.Semantics.EntityType` and its values include `Unknown`, `Person`, `Organization`, `Location`, `Date`, `Time`, `Quantity`, and `Object`.

- [ ] **Step 1: Write the failing test**

Create `DomainDiscovery.Tests/Model/DecisionTrailTests.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class DecisionTrailTests
{
    private static Entity Ent(string text, EntityType type, double confidence = 0.9)
        => new() { Text = text, Type = type, Confidence = confidence };

    private static SemanticResult Analysis(params Entity[] entities)
    {
        var result = new SemanticResult();
        foreach (var entity in entities)
            result.Entities.Add(entity);
        return result;
    }

    private static DecisionRecord RecordFor(CandidateDomainModel model, string name)
        => Assert.Single(model.Decisions,
            d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void SeedExtraction_RecordsAKeptEntity_WithItsConfidence()
    {
        var model = new SeedEntityExtractor().Extract(Analysis(Ent("book", EntityType.Object, 0.82)));

        var record = RecordFor(model, "Book");
        Assert.Equal(DecisionStage.SeedExtraction, record.Stage);
        Assert.Equal(DecisionOutcome.Kept, record.Outcome);
        Assert.Equal(DecisionReasons.YouNamedIt, record.Reason);
        Assert.Contains("0.82", record.Evidence);
    }

    [Fact]
    public void SeedExtraction_RecordsANounTheEngineDidNotTypeAsAThing()
    {
        // This is the case behind "I said it and got no class" - the word never became a
        // candidate at all, so nothing downstream could ever explain it.
        var model = new SeedEntityExtractor().Extract(Analysis(Ent("quickly", EntityType.Unknown)));

        var record = RecordFor(model, "quickly");
        Assert.Equal(DecisionStage.SeedExtraction, record.Stage);
        Assert.Equal(DecisionOutcome.Skipped, record.Outcome);
        Assert.Equal(DecisionReasons.NotRecognizedAsAThing, record.Reason);
        Assert.Contains("Unknown", record.Evidence);
        Assert.Empty(model.Entities);
    }

    [Fact]
    public void SeedExtraction_RecordsADuplicateName()
    {
        var model = new SeedEntityExtractor().Extract(
            Analysis(Ent("book", EntityType.Object), Ent("books", EntityType.Object)));

        Assert.Single(model.Entities);

        var duplicate = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.AlreadyNamed);
        Assert.Equal(DecisionOutcome.Skipped, duplicate.Outcome);
        Assert.Equal("Book", duplicate.Name);
    }

    [Fact]
    public void SeedExtraction_RecordsANameThatNormalizesToNothing()
    {
        var model = new SeedEntityExtractor().Extract(Analysis(Ent("!!!", EntityType.Object)));

        var record = RecordFor(model, "!!!");
        Assert.Equal(DecisionOutcome.Skipped, record.Outcome);
        Assert.Equal(DecisionReasons.NameNormalizedToNothing, record.Reason);
        Assert.Empty(model.Entities);
    }
}
```

> If `NameNormalizer.ToEntityName("!!!")` does not in fact return an empty string, find an input that does (check `DomainDiscovery.Tests/SeedExtraction/NameNormalizerTests.cs` for one already proven to normalize away) and use that instead. Do not delete the test.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionTrailTests`
Expected: FAIL — all four, because `model.Decisions` is empty (`Assert.Single` reports 0 matches).

- [ ] **Step 3: Record the decisions**

Replace the body of `Extract` in `DomainDiscovery/SeedExtraction/SeedEntityExtractor.cs`:

```csharp
    public CandidateDomainModel Extract(SemanticResult result)
    {
        var model = new CandidateDomainModel();

        // Every entity is walked, not just the ones whose type qualifies: a word the engine
        // typed as something other than a thing is exactly the case a user most wants
        // explained, and filtering it out here would leave nothing to explain it with.
        foreach (var entity in result.Entities)
        {
            if (!EntityTypes.Contains(entity.Type))
            {
                model.Record(entity.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.NotRecognizedAsAThing,
                    $"the language engine read it as {entity.Type}");
                continue;
            }

            var name = NameNormalizer.ToEntityName(entity.Text);
            if (name.Length == 0)
            {
                model.Record(entity.Text, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.NameNormalizedToNothing);
                continue;
            }

            if (model.Entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                model.Record(name, DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
                    DecisionReasons.AlreadyNamed);
                continue;
            }

            // Fields are attached later, by EntityShaper, once the topic is known.
            model.Entities.Add(new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Seed,
                Confidence = entity.Confidence
            });

            model.Record(name, DecisionStage.SeedExtraction, DecisionOutcome.Kept,
                DecisionReasons.YouNamedIt, $"confidence {entity.Confidence:0.00}");
        }

        return model;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionTrailTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Verify nothing else regressed**

Run: `dotnet test DomainDiscovery.Tests`
Expected: PASS. The entity output of `Extract` is unchanged, so `SeedEntityExtractorTests` and `FreeTextSeedExtractionTests` must still be green. If any fails, the loop rewrite changed behaviour — fix the rewrite, not the test.

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery/SeedExtraction/SeedEntityExtractor.cs \
        DomainDiscovery.Tests/Model/DecisionTrailTests.cs
git commit -m "feat(discovery): record seed-extraction decisions"
```

---

### Task 3: Record enrichment decisions, and carry the log across the merge

**Files:**
- Modify: `DomainDiscovery/Enrichment/DomainEnricher.cs:35-57` (wikilink skip), `:77-93` (lead-demote), `:101-136` (merge)
- Test: `DomainDiscovery.Tests/Model/DecisionTrailTests.cs` (append)

**Interfaces:**
- Consumes: `CandidateDomainModel.Record`, `DecisionReasons.{NotAUsableClassName, LeadDoesNotMentionIt, AlreadyAClassFromYourDescription}` from Task 1.
- Produces: `DemoteWhatTheDefinitionDoesNotName` gains a third parameter — its signature becomes `private static void DemoteWhatTheDefinitionDoesNotName(CandidateDomainModel harvested, string lead, string articleTitle)`. It stays private; no other task calls it.

**The bug this task exists to avoid:** `Enrich` records against `harvested`, which is a **different** `CandidateDomainModel` instance built by the harvester. `Merge` copies entities and suggestions from `harvested` into `seed` and returns `seed` — so unless `Merge` also copies `harvested.Decisions`, the entire enrichment stage's trail is silently discarded. That is the single most likely defect in this feature and it gets its own test.

- [ ] **Step 1: Write the failing test**

Append to `DomainDiscovery.Tests/Model/DecisionTrailTests.cs` — add these usings at the top of the file:

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Core;
```

and these tests inside the class:

```csharp
    [Fact]
    public void Enrichment_RecordsACandidateTheLeadDoesNotMention()
    {
        // The lead defines a point of sale without naming a smartphone, so Smartphone is
        // demoted to a suggestion. Today that happens invisibly.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] =
            "The point of sale is where a sale is completed. The smartphone is discussed later. "
            + "The smartphone appears again here.";

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich("Point of sale", new CandidateDomainModel());

        var demoted = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.LeadDoesNotMentionIt
                 && string.Equals(d.Name, "Smartphone", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(DecisionStage.Enrichment, demoted.Stage);
        Assert.Equal(DecisionOutcome.Suggested, demoted.Outcome);
        Assert.Contains("Point of sale", demoted.Evidence);
    }

    [Fact]
    public void Enrichment_CarriesItsTrailAcrossTheMergeIntoTheSeedModel()
    {
        // Enrich records against the HARVESTED model, which is a different instance from the
        // seed it returns. Without an explicit copy in Merge the whole stage's trail is lost -
        // silently, since the entities still arrive.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] =
            "The point of sale is where a sale is completed. The smartphone is discussed later. "
            + "The smartphone appears again here.";

        var seed = new CandidateDomainModel();
        seed.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich("Point of sale", seed);

        Assert.Same(seed, model);
        Assert.Contains(model.Decisions, d => d.Stage == DecisionStage.SeedExtraction);
        Assert.Contains(model.Decisions, d => d.Stage == DecisionStage.Enrichment);
    }

    [Fact]
    public void Enrichment_RecordsASuggestionTheUserAlreadyNamedAsAClass()
    {
        // A demoted candidate the seed already claims is dropped rather than offered back.
        // The user should be able to find out that it was not lost.
        var wiki = new FakeWikipediaClient();
        wiki.Extracts["Point of sale"] =
            "The point of sale is where a sale is completed. The smartphone is discussed later. "
            + "The smartphone appears again here.";

        var seed = new CandidateDomainModel();
        seed.Entities.Add(new CandidateEntity { Name = "Smartphone", Provenance = Provenance.Seed });

        var enricher = new DomainEnricher(
            wiki, EngineBuilder.BuildDefault().Analyzer, new EntityHarvester());

        var model = enricher.Enrich("Point of sale", seed);

        var dropped = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.AlreadyAClassFromYourDescription);
        Assert.Equal(DecisionOutcome.Skipped, dropped.Outcome);
        Assert.Empty(model.Suggestions);
    }
```

> These three use the real analyzer, as `DomainEnricherTests.Enrich_AddsALinkedConcept_ProseNounCountingCannotSee` already does. The harvester ignores nouns an article mentions only once, which is why "smartphone" appears twice in the fixture text.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionTrailTests`
Expected: FAIL on the three new tests — no enrichment records exist.

- [ ] **Step 3: Record the wikilink skip**

In `DomainDiscovery/Enrichment/DomainEnricher.cs`, in the `foreach` over `LinkedConceptSource.Candidates`, replace the identifier guard:

```csharp
            if (!NameNormalizer.IsValidIdentifier(name))
            {
                harvested.Record(title, DecisionStage.Enrichment, DecisionOutcome.Skipped,
                    DecisionReasons.NotAUsableClassName, $"linked from {articleTitle}");
                continue;
            }
```

Leave the duplicate guard immediately below it **unrecorded**. A candidate discarded for already being in the list has not died — it is present under the same name, and its own record already explains it. Recording it would file two contradictory outcomes under one name.

- [ ] **Step 4: Record the lead-demote**

Change the call site (currently `DemoteWhatTheDefinitionDoesNotName(harvested, LeadSection.Of(text));`):

```csharp
        DemoteWhatTheDefinitionDoesNotName(harvested, LeadSection.Of(text), articleTitle);
```

Change the signature and the loop:

```csharp
    private static void DemoteWhatTheDefinitionDoesNotName(
        CandidateDomainModel harvested, string lead, string articleTitle)
    {
        if (lead.Length == 0)
            return;

        var demoted = harvested.Entities
            // The title a linked concept came from is the spelling the prose uses ("Cash register");
            // a harvested noun has no title, and its own name is that spelling already.
            .Where(e => !lead.Contains(e.SourceTitle ?? e.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entity in demoted)
        {
            harvested.Entities.Remove(entity);
            harvested.Suggestions.Add(entity);
            harvested.Record(entity.Name, DecisionStage.Enrichment, DecisionOutcome.Suggested,
                DecisionReasons.LeadDoesNotMentionIt, $"lead of \"{articleTitle}\"");
        }
    }
```

- [ ] **Step 5: Carry the log across the merge, and record the dropped suggestion**

In `Merge`, add the log copy as the **first** statement, and record the drop:

```csharp
    private static CandidateDomainModel Merge(CandidateDomainModel seed, CandidateDomainModel harvested)
    {
        // The harvested model is a DIFFERENT instance, and every enrichment decision was
        // recorded against it. Without this line the whole stage's trail vanishes here while
        // the entities still arrive - a loss nothing downstream can detect.
        seed.Decisions.AddRange(harvested.Decisions);

        foreach (var incoming in harvested.Entities)
        {
            var existing = seed.Entities.FirstOrDefault(
                e => string.Equals(e.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                seed.Entities.Add(incoming);
                continue;
            }

            foreach (var field in incoming.Fields)
            {
                if (!existing.Fields.Any(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                    existing.Fields.Add(field);
            }
        }

        // A name the user typed is a class because they said so, whatever the article's lead does
        // or does not name — so a demoted candidate that the seed already claims is dropped here
        // rather than offered back to them as a suggestion for a class they already have.
        foreach (var suggestion in harvested.Suggestions)
        {
            if (seed.Entities.Any(e => string.Equals(e.Name, suggestion.Name, StringComparison.OrdinalIgnoreCase)))
            {
                seed.Record(suggestion.Name, DecisionStage.Enrichment, DecisionOutcome.Skipped,
                    DecisionReasons.AlreadyAClassFromYourDescription);
                continue;
            }

            if (seed.Suggestions.Any(s => string.Equals(s.Name, suggestion.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            seed.Suggestions.Add(suggestion);
        }

        return seed;
    }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionTrailTests`
Expected: PASS, 7 tests.

- [ ] **Step 7: Verify nothing else regressed**

Run: `dotnet test DomainDiscovery.Tests`
Expected: PASS. `DomainEnricherTests` in particular must be untouched — entity and suggestion output is unchanged.

- [ ] **Step 8: Commit**

```bash
git add DomainDiscovery/Enrichment/DomainEnricher.cs \
        DomainDiscovery.Tests/Model/DecisionTrailTests.cs
git commit -m "feat(discovery): record enrichment decisions and carry the trail across the merge"
```

---

### Task 4: Record shaping decisions

**Files:**
- Modify: `DomainDiscovery/Enrichment/EntityShaper.cs:60-182`
- Test: `DomainDiscovery.Tests/Model/DecisionTrailTests.cs` (append)

**Interfaces:**
- Consumes: `CandidateDomainModel.Record`, `DecisionReasons.{DefinedAsADomainClass, DefinitionResolvesToAnAbstraction, YouNamedItNoDefinitionFound, NoDefinitionWeRecognize}` from Task 1.
- Produces: nothing other tasks call. A new `private static string ShapingEvidence(EntityRole role, string? head, string source)` helper inside `EntityShaper`.

**Context the implementer needs:** `Shape` runs in passes. Pass 1 applies the curated `DomainFieldLexicon`; anything it gives fields to is skipped by pass 2 (`if (entity.Fields.Count > 0) continue;`), so a curated class must be recorded in pass 1 or it gets no record at all. Pass 2 computes `head` (from `HypernymExtractor.Extract(lead)`) and builds `ShapeCandidate(entity.Name, head, result)` values; pass 2b runs `SubtypeChaining.Resolve` over them; pass 2c acts on `resolved[entity.Name]` and **no longer has `head` in scope** — hence the lookup dictionary below. `ArchetypeResult` is `(ShapeVerdict Verdict, IReadOnlyList<FieldShape> Fields, EntityRole Role)`.

- [ ] **Step 1: Write the failing test**

Append to `DomainDiscovery.Tests/Model/DecisionTrailTests.cs`:

```csharp
    private static CandidateDomainModel WikiModel(params string[] names)
    {
        var model = new CandidateDomainModel();
        foreach (var name in names)
            model.Entities.Add(new CandidateEntity { Name = name, Provenance = Provenance.Wiki });
        return model;
    }

    [Fact]
    public void Shaping_RecordsARejection_WithTheHeadNounThatCausedIt()
    {
        var wiki = new FakeWikipediaClient();
        wiki.Leads["Administration"] = "Administration is a process of managing an organization.";

        var model = WikiModel("Administration");
        new EntityShaper(wiki).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.DefinitionResolvesToAnAbstraction);
        Assert.Equal("Administration", record.Name);
        Assert.Equal(DecisionStage.Shaping, record.Stage);
        Assert.Equal(DecisionOutcome.Rejected, record.Outcome);
        Assert.Contains("process", record.Evidence);

        // The existing collections must behave exactly as before.
        Assert.Empty(model.Entities);
        Assert.Contains("Administration", model.Rejected);
    }

    [Fact]
    public void Shaping_RecordsASuggestion_WhenNoDefinitionIsRecognized()
    {
        var wiki = new FakeWikipediaClient();
        wiki.Leads["Widget"] = "Widget is a widget of widgets.";

        var model = WikiModel("Widget");
        new EntityShaper(wiki).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.NoDefinitionWeRecognize);
        Assert.Equal(DecisionOutcome.Suggested, record.Outcome);
        Assert.Contains(model.Suggestions, e => e.Name == "Widget");
    }

    [Fact]
    public void Shaping_RecordsASeedKept_DespiteHavingNoDefinition()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed });

        new EntityShaper(new FakeWikipediaClient()).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.YouNamedItNoDefinitionFound);
        Assert.Equal(DecisionOutcome.Kept, record.Outcome);
        Assert.Single(model.Entities);
    }

    [Fact]
    public void Shaping_RecordsACuratedClass_WhichPassTwoNeverSees()
    {
        // Recipe is in the curated lexicon, so pass 1 gives it fields and pass 2 skips it.
        // Without a record in pass 1 a curated class has no trail at all.
        var model = WikiModel("Recipe");

        new EntityShaper(new FakeWikipediaClient()).Shape(model);

        var record = Assert.Single(model.Decisions,
            d => d.Reason == DecisionReasons.DefinedAsADomainClass);
        Assert.Equal("Recipe", record.Name);
        Assert.Equal(DecisionOutcome.Kept, record.Outcome);
    }

    [Fact]
    public void ASeedOnlyRun_StillCarriesItsSeedExtractionTrail()
    {
        // No topic recognized means enrichment never runs, and Shape is called on the seed
        // model directly. The seed-extraction trail must survive that path too - it is the
        // only trail such a run has.
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("invoice", EntityType.Object));
        analysis.Entities.Add(Ent("quickly", EntityType.Unknown));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient();   // no search hits, so no topic

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client),
            client);

        var result = pipeline.Discover("track invoices");

        Assert.False(result.Topic.IsRecognized);
        Assert.Contains(result.Model.Decisions,
            d => d.Stage == DecisionStage.SeedExtraction
                 && d.Reason == DecisionReasons.NotRecognizedAsAThing);
        Assert.Contains(result.Model.Decisions,
            d => d.Stage == DecisionStage.SeedExtraction && d.Outcome == DecisionOutcome.Kept);
    }
```

This last test needs two more usings at the top of the file:

```csharp
using DomainDiscovery;
using DomainDiscovery.Recognition;
```

> If `ArchetypeLexicon` has an opinion on "widget" (making the Suggested tests fail because the candidate shapes instead), pick a nonsense head noun it genuinely does not resolve — check `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs` for a name already proven to land on `Unknown` and reuse it. Do not delete the test.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionTrailTests`
Expected: FAIL on the four new shaping tests — no shaping records exist. `ASeedOnlyRun_StillCarriesItsSeedExtractionTrail` should **pass** already, since Task 2 supplied that trail; it is here to pin the seed-only path against regression, not to drive new code.

- [ ] **Step 3: Record the curated class in pass 1**

In `EntityShaper.Shape`, inside the pass-1 loop, after the fields are added:

```csharp
            if (curatedFields.Count > 0)
            {
                entity.Role = DomainFieldLexicon.Role(entity.Name);

                // Pass 2 skips anything that already has fields, so a curated class gets no
                // record at all unless it is taken here.
                model.Record(entity.Name, DecisionStage.Shaping, DecisionOutcome.Kept,
                    DecisionReasons.DefinedAsADomainClass,
                    ShapingEvidence(entity.Role, head: null, source: "the curated domain lexicon"));
            }

            foreach (var shape in curatedFields)
                AddField(entity, shape.Name, shape.Type);
```

Note the existing `if (curatedFields.Count > 0)` line that only set `entity.Role` is replaced by this block; the `foreach` that adds fields stays outside it, exactly as it is today.

- [ ] **Step 4: Keep the head noun reachable in pass 2c**

Declare the lookup alongside `pending` and `candidates`:

```csharp
        var pending = new List<CandidateEntity>();
        var candidates = new List<ShapeCandidate>();

        // Pass 2c decides on `resolved[...]`, which carries no head — but the head noun is the
        // single most useful piece of evidence a rejection can give ("its lead defines it as a
        // process"), so it is kept here where it is still in scope.
        var headsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
```

and populate it in the pass-2 loop, next to the existing `candidates.Add(...)`:

```csharp
            pending.Add(entity);
            candidates.Add(new ShapeCandidate(entity.Name, head, result));
            headsByName[entity.Name] = head;
```

- [ ] **Step 5: Record the three verdicts in pass 2c**

Replace the pass-2c `foreach` body:

```csharp
        foreach (var entity in pending)
        {
            var result = resolved[entity.Name];
            var head = headsByName.TryGetValue(entity.Name, out var found) && found.Length > 0
                ? found
                : null;
            var source = entity.SourceTitle ?? entity.Name;

            switch (result.Verdict)
            {
                case ShapeVerdict.Shaped:
                    entity.Role = result.Role;
                    foreach (var shape in result.Fields)
                        AddField(entity, shape.Name, shape.Type);
                    model.Record(entity.Name, DecisionStage.Shaping, DecisionOutcome.Kept,
                        DecisionReasons.DefinedAsADomainClass,
                        ShapingEvidence(result.Role, head, source));
                    break;

                case ShapeVerdict.Abstract:
                    model.Entities.Remove(entity);
                    model.Rejected.Add(entity.Name);
                    model.Record(entity.Name, DecisionStage.Shaping, DecisionOutcome.Rejected,
                        DecisionReasons.DefinitionResolvesToAnAbstraction,
                        ShapingEvidence(EntityRole.None, head, source));
                    break;

                default:
                    // Unknown means "we have no opinion". Where we have none, the user having
                    // typed the word IS the evidence: SeedEntityExtractor only ever produces a
                    // Seed candidate from the description the user wrote. Abstract still
                    // rejects above, so "system" and "application" cannot get in this way.
                    if (entity.Provenance == Provenance.Seed)
                    {
                        DefaultFields.ApplyTo(entity, Provenance.Seed);
                        model.Record(entity.Name, DecisionStage.Shaping, DecisionOutcome.Kept,
                            DecisionReasons.YouNamedItNoDefinitionFound);
                        break;
                    }

                    model.Entities.Remove(entity);
                    model.Suggestions.Add(entity);
                    model.Record(entity.Name, DecisionStage.Shaping, DecisionOutcome.Suggested,
                        DecisionReasons.NoDefinitionWeRecognize,
                        ShapingEvidence(EntityRole.None, head, source));
                    break;
            }
        }
```

> The long explanatory comment currently sitting in the `default` branch (about `ShapeVerdict.Unknown` conflating two cases) should be kept — abbreviate it as above only if it survives verbatim elsewhere; otherwise paste it back unchanged. It documents an accepted trade-off, not an implementation detail.

- [ ] **Step 6: Add the evidence helper**

Add to `EntityShaper`, next to `AddField`:

```csharp
    /// <summary>
    /// Composes the one line of evidence a shaping decision carries. Facts that say nothing are
    /// left out rather than rendered empty: a rejected candidate has no role, and a candidate
    /// whose lead could not be fetched has no head noun.
    /// </summary>
    private static string ShapingEvidence(EntityRole role, string? head, string source)
    {
        var parts = new List<string>();

        if (role != EntityRole.None)
            parts.Add($"role {role}");

        if (!string.IsNullOrWhiteSpace(head))
            parts.Add($"head noun: {head}");

        parts.Add($"from \"{source}\"");

        return string.Join(", ", parts);
    }
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests --filter FullyQualifiedName~DecisionTrailTests`
Expected: PASS, 12 tests.

- [ ] **Step 8: Verify nothing else regressed**

Run: `dotnet test DomainDiscovery.Tests`
Expected: PASS. `EntityShaperTests` must be entirely unaffected — no entity's fate changed, only what was written alongside it.

- [ ] **Step 9: Verify the CI gate**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 10: Commit**

```bash
git add DomainDiscovery/Enrichment/EntityShaper.cs \
        DomainDiscovery.Tests/Model/DecisionTrailTests.cs
git commit -m "feat(discovery): record shaping decisions with the head noun behind them"
```

---

### Task 5: The report formatter

**Files:**
- Create: `CodeGenerator/App/DecisionReport.cs`
- Test: `CodeGenerator.Tests/App/DecisionReportTests.cs`

**Interfaces:**
- Consumes: `CandidateDomainModel.Decisions`, `DecisionRecord`, `DecisionStage`, `DecisionOutcome`, `DecisionReasons` from Tasks 1-4.
- Produces: `internal static class CodeGenerator.App.DecisionReport` with
  - `internal const int MaxNamesPerGroup = 8;`
  - `internal static string Canonical(string name)`
  - `internal static IReadOnlyList<string> Summary(CandidateDomainModel model)`
  - `internal static IReadOnlyList<string> ForName(CandidateDomainModel model, string name)`
  - `internal static IReadOnlyList<string> All(CandidateDomainModel model)`

**This test project is MSTest.** `[TestClass]`, `[TestMethod]`, `Assert.AreEqual(expected, actual)`, `CollectionAssert`. Getting the argument order backwards on MSTest asserts has produced vacuously-passing tests in this repo before — expected comes first.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/DecisionReportTests.cs`:

```csharp
using System.Linq;
using CodeGenerator.App;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DecisionReportTests
{
    private static CandidateDomainModel ModelWithATrail()
    {
        var model = new CandidateDomainModel();
        model.Record("lending", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt, "confidence 0.82");
        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction, "head noun: practice");
        model.Record("Cash register", DecisionStage.Enrichment, DecisionOutcome.Suggested,
            DecisionReasons.LeadDoesNotMentionIt, "lead of \"Point of sale\"");
        return model;
    }

    [TestMethod]
    public void Canonical_IgnoresCaseSpacingAndPunctuation()
    {
        Assert.AreEqual("cashregister", DecisionReport.Canonical("Cash register"));
        Assert.AreEqual("cashregister", DecisionReport.Canonical("CashRegister"));
        Assert.AreEqual("cashregister", DecisionReport.Canonical("  cash-register "));
    }

    [TestMethod]
    public void ForName_MatchesAcrossSpellings_AndShowsTheWholeTrailInStageOrder()
    {
        var lines = DecisionReport.ForName(ModelWithATrail(), "LENDING");
        var text = string.Join("\n", lines);

        StringAssert.Contains(text, DecisionReasons.YouNamedIt);
        StringAssert.Contains(text, DecisionReasons.DefinitionResolvesToAnAbstraction);
        StringAssert.Contains(text, "head noun: practice");

        // Seed extraction happened first and must be shown first.
        Assert.IsTrue(
            text.IndexOf(DecisionReasons.YouNamedIt)
            < text.IndexOf(DecisionReasons.DefinitionResolvesToAnAbstraction),
            "the trail must read in stage order");
    }

    [TestMethod]
    public void ForName_FindsARecordWrittenUnderTheArticleSpelling()
    {
        var lines = DecisionReport.ForName(ModelWithATrail(), "CashRegister");

        StringAssert.Contains(string.Join("\n", lines), DecisionReasons.LeadDoesNotMentionIt);
    }

    [TestMethod]
    public void ForName_OffersTheActionThatTakesARejectedOrSuggestedNameBack()
    {
        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(ModelWithATrail(), "Lending")),
            "add Lending");

        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(ModelWithATrail(), "Cash register")),
            "add CashRegister");
    }

    [TestMethod]
    public void ForName_SaysSoWhenNothingWasRecorded()
    {
        var lines = DecisionReport.ForName(new CandidateDomainModel(), "Nonesuch");

        StringAssert.Contains(string.Join("\n", lines), "No decision recorded for 'Nonesuch'");
    }

    [TestMethod]
    public void ForName_ExplainsAnEntityTheUserAddedByHand()
    {
        // `add Widget` in the review loop leaves no decision - discovery never saw it.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed });

        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(model, "Widget")),
            "added in the review loop");
    }

    [TestMethod]
    public void Summary_GroupsRejectionsByReason()
    {
        var model = new CandidateDomainModel();
        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);
        model.Record("Administration", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);

        var line = DecisionReport.Summary(model).Single(l => l.StartsWith("Not classes:"));

        // The reason is composed from the constant, never copied — a Global Constraint. A test
        // holding its own copy of the phrase goes on passing after the phrase is changed.
        Assert.AreEqual(
            $"Not classes: Lending, Administration — {DecisionReasons.DefinitionResolvesToAnAbstraction}",
            line);
    }

    [TestMethod]
    public void Summary_CapsALongGroup_AndPointsAtTheWhyVerb()
    {
        var model = new CandidateDomainModel();
        for (var i = 1; i <= 20; i++)
        {
            model.Record($"Name{i}", DecisionStage.Shaping, DecisionOutcome.Rejected,
                DecisionReasons.DefinitionResolvesToAnAbstraction);
        }

        var line = DecisionReport.Summary(model).Single(l => l.StartsWith("Not classes:"));

        StringAssert.Contains(line, "Name8");
        Assert.IsFalse(line.Contains("Name9"), "the group must be capped at 8 names");
        StringAssert.Contains(line, "(+12 more — try `why`)");
    }

    [TestMethod]
    public void Summary_IsEmpty_WhenThereIsNothingToReport()
    {
        var model = new CandidateDomainModel();
        model.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);

        // Kept candidates are the model itself - the summary explains only what is missing.
        Assert.AreEqual(0, DecisionReport.Summary(model).Count);
    }

    [TestMethod]
    public void All_GroupsEveryDecisionByOutcome()
    {
        var text = string.Join("\n", DecisionReport.All(ModelWithATrail()));

        StringAssert.Contains(text, "Kept (1):");
        StringAssert.Contains(text, "Rejected (1):");
        StringAssert.Contains(text, "Suggested (1):");
    }

    [TestMethod]
    public void All_SaysSoWhenTheTrailIsEmpty()
        => StringAssert.Contains(
            string.Join("\n", DecisionReport.All(new CandidateDomainModel())),
            "No decisions recorded.");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~DecisionReportTests`
Expected: FAIL to compile — `DecisionReport` does not exist.

- [ ] **Step 3: Write the formatter**

Create `CodeGenerator/App/DecisionReport.cs`:

```csharp
using System.Text;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace CodeGenerator.App;

/// <summary>
/// Renders the discovery decision trail for the review loop. Pure: it takes a model and
/// returns lines, touching no console. That is what makes the review loop's newest verb
/// testable without driving the loop.
/// </summary>
internal static class DecisionReport
{
    /// <summary>
    /// How many names one summary group shows before it is cut short. A real domain drops
    /// dozens of nouns, and an uncapped list pushes the model itself off the screen on every
    /// redraw — which is the thing the review loop exists to show.
    /// </summary>
    internal const int MaxNamesPerGroup = 8;

    /// <summary>
    /// The form two spellings of the same thing agree on. A record may be written under the
    /// article's spelling ("Cash register") while the user only ever saw the class name
    /// ("CashRegister"), so neither can be matched literally.
    /// </summary>
    internal static string Canonical(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    /// <summary>
    /// The grouped one-liners that sit under the model print: what was rejected, suggested or
    /// skipped, grouped by reason. Kept candidates are omitted — they are the model, printed
    /// immediately above.
    /// </summary>
    internal static IReadOnlyList<string> Summary(CandidateDomainModel model)
    {
        var lines = new List<string>();

        Append(DecisionOutcome.Rejected, "Not classes: ");
        Append(DecisionOutcome.Suggested, "Also found (add <name> to include): ");
        Append(DecisionOutcome.Skipped, "Ignored: ");

        return lines;

        void Append(DecisionOutcome outcome, string prefix)
        {
            var groups = model.Decisions
                .Where(d => d.Outcome == outcome)
                .GroupBy(d => d.Reason, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                var names = group.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var shown = string.Join(", ", names.Take(MaxNamesPerGroup));
                var overflow = names.Count > MaxNamesPerGroup
                    ? $"  (+{names.Count - MaxNamesPerGroup} more — try `why`)"
                    : string.Empty;

                lines.Add($"{prefix}{shown} — {group.Key}{overflow}");
            }
        }
    }

    /// <summary>The whole trail for one name, in the order the stages ran.</summary>
    internal static IReadOnlyList<string> ForName(CandidateDomainModel model, string name)
    {
        var wanted = Canonical(name);
        var records = model.Decisions.Where(d => Canonical(d.Name) == wanted).ToList();

        if (records.Count == 0)
        {
            var entity = model.Entities.FirstOrDefault(e => Canonical(e.Name) == wanted);
            return entity is null
                ? new[] { $"No decision recorded for '{name}'." }
                : new[] { $"{entity.Name} was added in the review loop, not discovered." };
        }

        var lines = new List<string> { records[0].Name };

        foreach (var record in records)
        {
            lines.Add($"  {Label(record.Stage),-16} {record.Outcome.ToString().ToLowerInvariant(),-10} {record.Reason}");
            if (!string.IsNullOrWhiteSpace(record.Evidence))
                lines.Add($"  {string.Empty,-16} {string.Empty,-10} {record.Evidence}");
        }

        // The last word on a name is the one worth acting on — a name rejected at shaping is
        // gone whatever seed extraction thought of it.
        var final = records[^1];

        // `add` takes a class name, but a record may be filed under the article's spelling
        // ("Cash register"), so the suggested command has to be given in the form the loop
        // will actually accept. The suggestion list already holds the converted name when the
        // candidate reached it; otherwise the same normalizer the pipeline used converts it.
        var className = model.Suggestions
            .FirstOrDefault(e => Canonical(e.Name) == wanted)?.Name
            ?? NameNormalizer.ToCompoundEntityName(final.Name);

        if (final.Outcome == DecisionOutcome.Rejected)
            lines.Add($"  Add it anyway with: add {className}");
        else if (final.Outcome == DecisionOutcome.Suggested)
            lines.Add($"  Include it with: add {className}");

        return lines;
    }

    /// <summary>Every decision, grouped by outcome — the answer to a bare <c>why</c>.</summary>
    internal static IReadOnlyList<string> All(CandidateDomainModel model)
    {
        if (model.Decisions.Count == 0)
            return new[] { "No decisions recorded." };

        var lines = new List<string> { $"Decisions ({model.Decisions.Count}):" };

        foreach (var outcome in new[]
                 {
                     DecisionOutcome.Kept, DecisionOutcome.Suggested,
                     DecisionOutcome.Rejected, DecisionOutcome.Skipped
                 })
        {
            var names = model.Decisions
                .Where(d => d.Outcome == outcome)
                .Select(d => d.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count > 0)
                lines.Add($"  {outcome} ({names.Count}): {string.Join(", ", names)}");
        }

        lines.Add("Ask `why <name>` for the detail on any one.");
        return lines;
    }

    private static string Label(DecisionStage stage) => stage switch
    {
        DecisionStage.SeedExtraction => "seed extraction",
        DecisionStage.Enrichment => "enrichment",
        DecisionStage.Shaping => "shaping",
        _ => stage.ToString().ToLowerInvariant()
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~DecisionReportTests`
Expected: PASS, 12 tests.

> `ForName_OffersTheActionThatTakesARejectedOrSuggestedNameBack` is the one to watch: it expects `add CashRegister` for a record filed under the article spelling "Cash register", which is what the `NameNormalizer.ToCompoundEntityName` fallback exists for. If it fails, fix the fallback — never weaken the assertion, because `add Cash register` is a command the review loop cannot execute.

- [ ] **Step 5: Verify the CI gate**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/DecisionReport.cs CodeGenerator.Tests/App/DecisionReportTests.cs
git commit -m "feat(cli): add the pure decision-report formatter"
```

---

### Task 6: Wire `why` into the review loop

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs:54-165` (the loop and its help line), `:221-263` (`PrintModel`)
- Test: `CodeGenerator.Tests/App/BuildAppWhyCommandTests.cs`

**Interfaces:**
- Consumes: `DecisionReport.{Summary, ForName, All}` from Task 5.
- Produces: `internal static IReadOnlyList<string> BuildAppCommand.WhyLines(CandidateDomainModel model, string[] parts)`.

**Why an internal static:** `RunReviewLoop` is private and reads `Console` directly, so it cannot be driven from a test. This codebase already solves that by lifting the decidable part of a loop verb into an `internal static` — `ResolveTarget` and `ResolvePersistence` are both tested that way by `BuildAppTargetSelectionTests`. `WhyLines` follows the same pattern.

**The split trap:** the loop parses with `line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries)`, so `why Cash register` arrives as three parts, not two. `WhyLines` must rejoin everything after the verb.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/BuildAppWhyCommandTests.cs`:

```csharp
using System.Linq;
using CodeGenerator.App;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppWhyCommandTests
{
    private static string[] Parts(params string[] parts) => parts;

    private static CandidateDomainModel Model()
    {
        var model = new CandidateDomainModel();
        model.Record("Cash register", DecisionStage.Enrichment, DecisionOutcome.Suggested,
            DecisionReasons.LeadDoesNotMentionIt, "lead of \"Point of sale\"");
        return model;
    }

    [TestMethod]
    public void BareWhy_ReportsEverything()
    {
        var text = string.Join("\n", BuildAppCommand.WhyLines(Model(), Parts("why")));

        StringAssert.Contains(text, "Decisions (1):");
    }

    [TestMethod]
    public void WhyWithAName_ReportsThatNamesTrail()
    {
        var text = string.Join("\n", BuildAppCommand.WhyLines(Model(), Parts("why", "CashRegister")));

        StringAssert.Contains(text, DecisionReasons.LeadDoesNotMentionIt);
    }

    [TestMethod]
    public void WhyWithAMultiWordName_RejoinsWhatTheSplitBrokeApart()
    {
        // `line.Split(' ', 3, ...)` hands the loop three parts for `why Cash register`.
        var text = string.Join("\n",
            BuildAppCommand.WhyLines(Model(), Parts("why", "Cash", "register")));

        StringAssert.Contains(text, DecisionReasons.LeadDoesNotMentionIt);
    }

    [TestMethod]
    public void WhyWithAnUnknownName_SaysSoRatherThanReportingEverything()
    {
        var text = string.Join("\n", BuildAppCommand.WhyLines(Model(), Parts("why", "Nonesuch")));

        StringAssert.Contains(text, "No decision recorded for 'Nonesuch'");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~BuildAppWhyCommandTests`
Expected: FAIL to compile — `WhyLines` does not exist.

- [ ] **Step 3: Add `WhyLines`**

Add to `BuildAppCommand`, next to `ResolvePersistence`:

```csharp
    /// <summary>
    /// The lines a <c>why [name]</c> line produces: the whole report when no name is given,
    /// that name's trail otherwise. Internal and pure for the same reason
    /// <see cref="ResolveTarget"/> is — the review loop itself reads the console and cannot be
    /// driven from a test, so the decidable part is lifted out of it.
    /// </summary>
    /// <remarks>
    /// The loop splits its input into at most three parts, so a two-word name arrives in
    /// pieces and has to be put back together before it can be matched.
    /// </remarks>
    internal static IReadOnlyList<string> WhyLines(CandidateDomainModel model, string[] parts)
    {
        var name = string.Join(' ', parts.Skip(1)).Trim();

        return name.Length == 0
            ? DecisionReport.All(model)
            : DecisionReport.ForName(model, name);
    }
```

- [ ] **Step 4: Add the verb and the help line**

In `RunReviewLoop`, add a case to the switch, after the `unfield` case:

```csharp
                case "why":
                    foreach (var whyLine in WhyLines(model, parts))
                        Console.WriteLine(whyLine);
                    break;
```

and extend the second help line:

```csharp
            Console.WriteLine("  field <entity> <name>[:<type>]   unfield <entity> <name>   why [name]   [c]ancel");
```

- [ ] **Step 5: Replace the bare rejected list with the grouped summary**

In `PrintModel`, delete the commented-out `model.Suggestions` block and replace the trailing `model.Rejected` block with:

```csharp
        // Rejections, demoted suggestions and skipped nouns, grouped by the reason each was
        // given. This replaces a bare list of rejected names that said nothing about why.
        foreach (var line in DecisionReport.Summary(model))
            Console.WriteLine(line);
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~BuildAppWhyCommandTests`
Expected: PASS, 4 tests.

- [ ] **Step 7: Check nothing asserted the old output**

Run: `grep -rn "Not classes" CodeGenerator.Tests DomainDiscovery.Tests`
Expected: no test asserts the old bare `Not classes:` line. If one does, update it to the new grouped form — the reason suffix is new, the prefix is not.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS, all three test projects.

- [ ] **Step 9: Verify the CI gate**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 10: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/BuildAppWhyCommandTests.cs
git commit -m "feat(cli): explain discovery decisions in the review loop with why [name]"
```

---

### Task 7: Verify against the real store

**Files:** none modified unless a defect is found.

**Interfaces:** none.

**Why this task exists:** a green suite has repeatedly hidden real defects in this repo, because tests enter the pipeline downstream of the bug. Driving the actual CLI over piped stdin has caught them every time. The fakes in Tasks 2-4 never exercise the real analyzer, the real dump store, or the real review loop.

- [ ] **Step 1: Confirm the store is present**

Run: `ls -l /d/SmartCodeGenerator-Repository/Dump/enwiki-full.db`
Expected: the file exists, roughly 20 GB. If it is missing, stop and report — the probe below cannot run, and the pipeline will silently fall back to the HTTP client.

- [ ] **Step 2: Rebuild the CLI**

Run: `dotnet build CodeGenerator -c Release`
Expected: Build succeeded. `dotnet test` does not refresh the CLI's own `bin`, so skipping this step verifies a stale binary and a real fix can look dead.

- [ ] **Step 3: Drive a real run**

Run:

```bash
printf 'build app: a library that tracks lending, borrowers and fines\nwhy\ncancel\nexit\n' \
  | dotnet run --project CodeGenerator -c Release --no-build
```

Expected, in the transcript:
- the candidate model prints as before;
- at least one grouped line beginning `Not classes:`, `Also found` or `Ignored:`, each ending in a reason;
- the bare `why` report listing `Decisions (N):` with counts by outcome.

Capture the transcript to the scratchpad for the next step.

- [ ] **Step 4: Ask why about a name that actually died in that run**

Pick a real name from the `Rejected` or `Suggested` group in the Step 3 transcript — do not invent one — and run:

```bash
printf 'build app: a library that tracks lending, borrowers and fines\nwhy <that name>\ncancel\nexit\n' \
  | dotnet run --project CodeGenerator -c Release --no-build
```

Expected: a trail with at least one stage line, its reason, and (for a rejection) the head noun that caused it, ending in an `add <name>` suggestion.

- [ ] **Step 5: Confirm generation is unchanged**

Run:

```bash
printf 'build app: a library that tracks lending, borrowers and fines\napprove console\nexit\n' \
  | dotnet run --project CodeGenerator -c Release --no-build
```

Expected: `Generated project at ...` exactly as before this branch. Decision records must not have changed what is scaffolded.

- [ ] **Step 6: Record the findings**

Write what the probe actually showed — including anything that came out wrong — into the branch's ledger at `.superpowers/sdd/2026-07-29-build-app-decision-analysis/progress.md`. If a step failed, fix it and re-run the probe rather than recording it as passed.

- [ ] **Step 7: Final full verification**

Run:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx
```

Expected: build clean with 0 warnings; the whole suite green. Report the actual test count.

- [ ] **Step 8: Commit any probe fixes**

```bash
git add -A
git commit -m "test(discovery): fixes found by the real-store decision-analysis probe"
```

(Skip if the probe found nothing to fix — do not create an empty commit.)

---

## Success Criteria

From the spec, each now traceable to a task:

1. Every death point produces exactly one `DecisionRecord` — Tasks 2, 3, 4.
2. `why <name>` explains any name the user saw, in any spelling — Tasks 5, 6.
3. Rejected names print with their reason — Tasks 4, 6.
4. Demoted suggestions are visible again — Tasks 3, 6.
5. `approve` output is unchanged — Task 7, Step 5.
6. Release `-warnaserror` clean and the whole suite green — Task 7, Step 7.
