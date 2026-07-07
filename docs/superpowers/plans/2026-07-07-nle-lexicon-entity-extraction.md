# NLE Lexicon / Entity-Extraction Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make free-form `build app:` descriptions like "payroll application" extract seed entities instead of degrading to a 0-entity model.

**Architecture:** Two complementary changes in NaturalLanguageEngine — (1) `PosTagger` defaults unknown `Word`-type tokens to `Noun` (the "unknown word ≈ noun" baseline) so arbitrary lowercase common nouns flow through phrase detection into `Object` entities; (2) a curated business-noun vocabulary in `EnglishLanguageProvider` for precision and to rescue `-ing`/`-ed` nouns (billing, shipping, …) that the verb-suffix heuristic would otherwise mis-tag as verbs.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit (NaturalLanguageEngine.Tests, DomainDiscovery.Tests), MSTest (CodeGenerator.Tests).

## Global Constraints

- All projects target `net10.0`.
- `NaturalLanguageEngine.Tests` and `DomainDiscovery.Tests` use **xUnit**; `CodeGenerator.Tests` uses **MSTest**.
- Run all commands from the repository root: `D:\SmartCodeGenerator-Repository`.
- `EngineBuilder` is in the **global namespace** (no `using` needed to reference it).
- `SeedEntityExtractor` keeps only `EntityType` `Object` / `Person` / `Organization` / `Location`; entity names are normalized to PascalCase by `NameNormalizer.ToEntityName`.
- Adding a word already present in the lexicon is safe — `LanguageRepository` overwrites by key (`_words[entry.Word] = entry`), it does not throw.
- Commit after each task. Work stays on the current feature branch.

---

### Task 1: POS-tagger Word-Noun fallback

**Files:**
- Modify: `NaturalLanguageEngine/Syntax/PosTagger.cs` (method `InferFromPattern`, and add a `using`)
- Test: `NaturalLanguageEngine.Tests/Syntax/PosTaggerTests.cs`

**Interfaces:**
- Consumes: `TokenAnalysis.Token.Type` (`NaturalLanguageEngine.Models.Enums.TokenType`), `TokenType.Word`.
- Produces: `PosTagger.Tag` now assigns `PartOfSpeech.Noun` to unrecognized `Word` tokens and leaves non-`Word` tokens `PartOfSpeech.Unknown`.

- [ ] **Step 1: Update the obsolete test and add the guard test**

In `NaturalLanguageEngine.Tests/Syntax/PosTaggerTests.cs`, replace the existing test:

```csharp
    [Fact]
    public void Tag_UnknownLowercaseWord_FallsBackToUnknown()
    {
        var tokens = new List<TokenAnalysis> { Analysis("xylophonic") };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Unknown, tokens[0].PartOfSpeech);
    }
```

with these two tests:

```csharp
    [Fact]
    public void Tag_UnknownLowercaseWord_DefaultsToNoun()
    {
        // Unknown word-type tokens fall back to Noun so free-form nouns
        // (e.g. "payroll") reach the semantic layer as entities.
        var tokens = new List<TokenAnalysis> { Analysis("xylophonic") };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Noun, tokens[0].PartOfSpeech);
    }

    [Fact]
    public void Tag_UnknownNonWordToken_StaysUnknown()
    {
        // Punctuation/symbols must NOT become nouns.
        var tokens = new List<TokenAnalysis> { Analysis(".", TokenType.Punctuation) };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Unknown, tokens[0].PartOfSpeech);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj --filter "FullyQualifiedName~PosTaggerTests.Tag_UnknownLowercaseWord_DefaultsToNoun|FullyQualifiedName~PosTaggerTests.Tag_UnknownNonWordToken_StaysUnknown"`

Expected: `Tag_UnknownLowercaseWord_DefaultsToNoun` FAILS (actual `Unknown`, expected `Noun`). `Tag_UnknownNonWordToken_StaysUnknown` PASSES already (default is currently `Unknown`).

- [ ] **Step 3: Implement the fallback**

In `NaturalLanguageEngine/Syntax/PosTagger.cs`, add the enums `using` near the top with the other usings:

```csharp
using NaturalLanguageEngine.Models.Enums;
```

Then change the end of `InferFromPattern`. Replace:

```csharp
        // Default fallback
        return PartOfSpeech.Unknown;
```

with:

```csharp
        // Unknown word-type tokens default to Noun (the standard
        // "unknown word ≈ noun" baseline) so free-form common nouns reach
        // the semantic layer. Non-word tokens (punctuation, symbols) stay
        // Unknown so they never become entities.
        return tokens[index].Token.Type == TokenType.Word
            ? PartOfSpeech.Noun
            : PartOfSpeech.Unknown;
```

- [ ] **Step 4: Run the two tests to verify they pass**

Run: `dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj --filter "FullyQualifiedName~PosTaggerTests.Tag_UnknownLowercaseWord_DefaultsToNoun|FullyQualifiedName~PosTaggerTests.Tag_UnknownNonWordToken_StaysUnknown"`
Expected: both PASS.

- [ ] **Step 5: Run the full NaturalLanguageEngine.Tests suite (early regression check)**

Run: `dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj`
Expected: all PASS. If any test fails because it depended on unknown words being dropped, inspect it: if the new Noun behavior is correct for that input, update the assertion; if the test reveals a genuine problem, stop and reassess. Do not weaken tests just to make them pass.

- [ ] **Step 6: Commit**

```bash
git add NaturalLanguageEngine/Syntax/PosTagger.cs NaturalLanguageEngine.Tests/Syntax/PosTaggerTests.cs
git commit -m "Default unknown word tokens to Noun in PosTagger"
```

---

### Task 2: Curated business-noun vocabulary

**Files:**
- Modify: `NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs` (method `LoadWords`)
- Test: `NaturalLanguageEngine.Tests/Syntax/PosTaggerTests.cs`

**Interfaces:**
- Consumes: `PosTagger` (unchanged from Task 1), `ILanguageRepository.TryGetWord`.
- Produces: the built-in English lexicon now contains ~50 business nouns, including `-ing`/`-ed` forms tagged `noun`.

- [ ] **Step 1: Write the failing test**

Add to `NaturalLanguageEngine.Tests/Syntax/PosTaggerTests.cs`:

```csharp
    [Fact]
    public void Tag_BusinessNounEndingInIng_UsesLexiconNotVerbSuffix()
    {
        // "billing" ends in -ing; without a lexicon entry the verb-suffix
        // heuristic would mis-tag it as a verb. The lexicon must win.
        var tokens = new List<TokenAnalysis> { Analysis("billing") };

        _tagger.Tag(tokens);

        Assert.Equal(PartOfSpeech.Noun, tokens[0].PartOfSpeech);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj --filter "FullyQualifiedName~PosTaggerTests.Tag_BusinessNounEndingInIng_UsesLexiconNotVerbSuffix"`
Expected: FAIL (actual `Verb` from the `-ing` heuristic, expected `Noun`).

- [ ] **Step 3: Add the vocabulary**

In `NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs`, inside `LoadWords()`, immediately after the existing "Common nouns" `foreach` block (the one ending `... "ball", "fox" })` / `yield return Make(w, "noun");`), add:

```csharp
        // Business-domain nouns (so build app: descriptions extract entities).
        // Includes -ing/-ed forms that the POS verb-suffix heuristic would
        // otherwise mis-tag as verbs (billing, shipping, accounting, ...).
        foreach (var w in new[] { "payroll", "application", "system", "employee", "employer", "salary", "wage", "invoice", "payment", "customer", "client", "supplier", "vendor", "product", "account", "transaction", "department", "manager", "timesheet", "tax", "deduction", "bonus", "benefit", "expense", "budget", "ledger", "inventory", "warehouse", "shipment", "user", "role", "permission", "contract", "subscription", "refund", "receipt", "balance", "record", "dashboard", "notification", "billing", "shipping", "accounting", "invoicing", "onboarding", "booking", "reporting", "scheduling", "pricing", "listing", "training" })
            yield return Make(w, "noun");
```

- [ ] **Step 4: Run the new test to verify it passes**

Run: `dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj --filter "FullyQualifiedName~PosTaggerTests.Tag_BusinessNounEndingInIng_UsesLexiconNotVerbSuffix"`
Expected: PASS.

- [ ] **Step 5: Run the full NaturalLanguageEngine.Tests suite**

Run: `dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs NaturalLanguageEngine.Tests/Syntax/PosTaggerTests.cs
git commit -m "Add business-domain nouns to the English lexicon"
```

---

### Task 3: End-to-end seed extraction from free text

**Files:**
- Create: `DomainDiscovery.Tests/SeedExtraction/FreeTextSeedExtractionTests.cs`

**Interfaces:**
- Consumes: `EngineBuilder.BuildDefault().Analyzer` (`ISemanticAnalyzer`), `SeedEntityExtractor.Extract(SemanticResult)`, `CandidateDomainModel.Entities` (each has `.Name`).
- Produces: nothing downstream — this is a verification test.

- [ ] **Step 1: Write the test**

Create `DomainDiscovery.Tests/SeedExtraction/FreeTextSeedExtractionTests.cs`:

```csharp
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Abstractions.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.SeedExtraction;

public class FreeTextSeedExtractionTests
{
    [Fact]
    public void Extract_FromFreeText_PayrollApplication_YieldsSeedEntities()
    {
        // Real NLE analysis + seed extraction: the free-form description
        // must no longer degrade to a 0-entity model.
        ISemanticAnalyzer analyzer = EngineBuilder.BuildDefault().Analyzer;
        var analysis = analyzer.Analyze("payroll application");

        var model = new SeedEntityExtractor().Extract(analysis);

        Assert.NotEmpty(model.Entities);
        Assert.Contains(model.Entities, e => e.Name == "Payroll");
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~FreeTextSeedExtractionTests"`
Expected: PASS (model has ≥1 entity, including `Payroll`). If it fails with 0 entities, Tasks 1–2 are not both in place — verify the branch has both commits.

- [ ] **Step 3: Commit**

```bash
git add DomainDiscovery.Tests/SeedExtraction/FreeTextSeedExtractionTests.cs
git commit -m "Add end-to-end seed-extraction test for free-form descriptions"
```

---

### Task 4: Full regression gate

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Run the DomainDiscovery.Tests suite**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`
Expected: all PASS.

- [ ] **Step 2: Run the CodeGenerator.Tests suite (bridge consumers of NLE)**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: all PASS. The bridge routes commands through NLE, so the broadened POS behavior could shift an assertion here. If a test fails: confirm whether the new entity/intent behavior is actually correct for that input. If it is, update the test assertion to match; if the change produced a genuinely worse result for a real command, stop and reassess the fallback scope before proceeding.

- [ ] **Step 3: Final confirmation**

Run: `dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj`
Expected: all PASS. All three suites (NaturalLanguageEngine.Tests, DomainDiscovery.Tests, CodeGenerator.Tests) green = regression gate satisfied.

- [ ] **Step 4: Manual smoke check (optional but recommended)**

Run: `dotnet run --project CodeGenerator -- "build app: payroll application"`
Expected: the candidate model now lists ≥1 entity (e.g. `Payroll`, `Application`) instead of "Candidate model (0 entities)".

---

## Self-Review

**Spec coverage:**
- POS-tagger Word-Noun fallback → Task 1. ✓
- Non-word tokens stay Unknown → Task 1 (guard + `Tag_UnknownNonWordToken_StaysUnknown`). ✓
- Curated business vocabulary → Task 2. ✓
- Rescue `-ing`/`-ed` nouns → Task 2 (`billing` test + vocabulary includes billing/shipping/accounting/invoicing/onboarding/reporting/scheduling/pricing/listing/training/booking). ✓
- Update `Tag_UnknownLowercaseWord_FallsBackToUnknown` → Task 1, Step 1. ✓
- New unit tests (unknown Word→Noun, punctuation→Unknown, -ing noun→Noun) → Tasks 1–2. ✓
- DomainDiscovery-level "payroll application" ≥1 seed entity → Task 3. ✓
- Regression gate across NLE / DomainDiscovery / CodeGenerator suites → Task 1 Step 5, Task 2 Step 5, Task 4. ✓
- Scope boundaries (no WikipediaTopicRecognizer change, no code-gen, nouns only) → respected; no task touches them. ✓

**Placeholder scan:** No TBD/TODO/"similar to"/"add error handling". All code steps show full code. ✓

**Type consistency:** `TokenType.Word` / `TokenType.Punctuation` (Models.Enums); `PartOfSpeech.Noun`/`.Unknown`/`.Verb`; `Analysis(text, type)` helper exists in `PosTaggerTests` with `type` defaulting to `TokenType.Word`; `EngineBuilder.BuildDefault().Analyzer` returns `ISemanticAnalyzer`; `SeedEntityExtractor.Extract` returns `CandidateDomainModel` whose `Entities` have `.Name`. Consistent. ✓
