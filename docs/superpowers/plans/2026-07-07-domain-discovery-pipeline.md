# Domain Discovery Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `build app:` command that takes a free-form English description, uses the existing `NaturalLanguageEngine` to extract seed entities, recognizes the domain via a Wikipedia search, enriches the entity model by mining that article's prose, and lets the user review/approve the model — printing the approved model (code generation follows in a second plan).

**Architecture:** A new `DomainDiscovery` class library holds the pipeline: seed extraction (from an NLE `SemanticResult`), a Wikipedia access layer (`IWikipediaClient` + HTTP/disk-cache impl + test fake), topic recognition, prose harvesting with type inference, enrichment/merge, and pure model-edit operations for the review loop. The `CodeGenerator` CLI gets a thin `build app:` handler that wires these together and does console I/O. All logic lives behind interfaces so the test suite (xUnit) runs fully offline.

**Tech Stack:** .NET 10, C# (ImplicitUsings + Nullable enabled), xUnit for the new test project, `System.Net.Http` + `System.Text.Json` for Wikipedia, reuses `NaturalLanguageEngine` (`EngineBuilder.BuildDefault().Analyzer`).

## Global Constraints

- Target framework: `net10.0`; `ImplicitUsings` enable; `Nullable` enable — on every new project, copied from existing csproj files.
- No LLM / no third-party AI dependency. Intelligence = the existing 7-stage `NaturalLanguageEngine` + Wikipedia text mining only.
- New test project uses **xUnit** (matching `NaturalLanguageEngine.Tests`), `Microsoft.NET.Test.Sdk` 17.12.0, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2.
- **No network access in tests.** Anything touching HTTP is exercised only through a fake `IWikipediaClient`.
- New projects are added to `SmartCodeGenerator/SmartCodeGenerator.slnx` (paths relative to that file, i.e. `../DomainDiscovery/...`).
- Scope of THIS plan: spec sections 4–5 discovery pipeline + section 6 CLI trigger + section 8 error handling for discovery. Code generation (spec §5.6, §7, slices 3–5) is a separate follow-up plan.
- Reference signatures (do not redefine): `EngineBuilder.BuildDefault()` (global namespace) → `NaturalLanguageEngine.Core.EngineBuildResult { ISemanticAnalyzer Analyzer }`; `NaturalLanguageEngine.Abstractions.Semantics.ISemanticAnalyzer.Analyze(string) → SemanticResult`; `NaturalLanguageEngine.Models.Semantics.SemanticResult { IList<Entity> Entities; IList<Relationship> Relationships; Intent? Intent; }`; `Entity { string Text; EntityType Type; ConfidenceScore Confidence; }`; `EntityType { Unknown, Person, Organization, Location, Date, Time, Quantity, Action, Object, Attribute, Value }`; `Relationship { Entity Source; Entity Target; RelationshipType Type; }`; `ConfidenceScore` has an implicit conversion to `double`.

---

### Task 1: Scaffold `DomainDiscovery` project, model types, and test project

**Files:**
- Create: `DomainDiscovery/DomainDiscovery.csproj`
- Create: `DomainDiscovery/Model/CandidateDomainModel.cs`
- Create: `DomainDiscovery/Model/TopicResult.cs`
- Create: `DomainDiscovery/Model/SearchHit.cs`
- Create: `DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`
- Create: `DomainDiscovery.Tests/Model/CandidateDomainModelTests.cs`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx`

**Interfaces:**
- Produces: `DomainDiscovery.Model.Provenance` (enum `Seed`, `Wiki`); `CandidateField { string Name; string Type; Provenance Provenance; }`; `CandidateRelationship { string Target; string Role; }`; `CandidateEntity { string Name; List<CandidateField> Fields; List<CandidateRelationship> Relationships; Provenance Provenance; double Confidence; }`; `CandidateDomainModel { List<CandidateEntity> Entities; }`; `TopicResult(string Title, string ArticleUrl, double Confidence)` with `TopicResult.None`; `SearchHit(string Title, string Url, int Rank)`.

- [ ] **Step 1: Create the project files**

`DomainDiscovery/DomainDiscovery.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\NaturalLanguageEngine\NaturalLanguageEngine.csproj" />
  </ItemGroup>

</Project>
```

`DomainDiscovery/Model/CandidateDomainModel.cs`:

```csharp
namespace DomainDiscovery.Model;

/// <summary>Where a discovered entity or field came from.</summary>
public enum Provenance
{
    /// <summary>Extracted from the user's own text.</summary>
    Seed,

    /// <summary>Harvested from a fetched Wikipedia article.</summary>
    Wiki
}

public sealed class CandidateField
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public Provenance Provenance { get; init; }
}

public sealed class CandidateRelationship
{
    public required string Target { get; init; }
    public required string Role { get; init; }
}

public sealed class CandidateEntity
{
    public required string Name { get; set; }
    public List<CandidateField> Fields { get; } = new();
    public List<CandidateRelationship> Relationships { get; } = new();
    public Provenance Provenance { get; set; }
    public double Confidence { get; set; }
}

public sealed class CandidateDomainModel
{
    public List<CandidateEntity> Entities { get; } = new();
}
```

`DomainDiscovery/Model/TopicResult.cs`:

```csharp
namespace DomainDiscovery.Model;

/// <summary>The recognized domain: a Wikipedia article and a confidence in [0,1].</summary>
public sealed record TopicResult(string Title, string ArticleUrl, double Confidence)
{
    /// <summary>No topic could be recognized (e.g. no seed entities, or offline).</summary>
    public static readonly TopicResult None = new("(unknown)", string.Empty, 0d);

    public bool IsRecognized => !string.IsNullOrEmpty(ArticleUrl);
}
```

`DomainDiscovery/Model/SearchHit.cs`:

```csharp
namespace DomainDiscovery.Model;

/// <summary>One Wikipedia search result. <see cref="Rank"/> is 0-based; 0 = best.</summary>
public sealed record SearchHit(string Title, string Url, int Rank);
```

- [ ] **Step 2: Create the test project**

`DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DomainDiscovery\DomainDiscovery.csproj" />
    <ProjectReference Include="..\NaturalLanguageEngine\NaturalLanguageEngine.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write the model test**

`DomainDiscovery.Tests/Model/CandidateDomainModelTests.cs`:

```csharp
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class CandidateDomainModelTests
{
    [Fact]
    public void NewEntity_HasEmptyFieldAndRelationshipLists()
    {
        var entity = new CandidateEntity { Name = "Book" };

        Assert.Empty(entity.Fields);
        Assert.Empty(entity.Relationships);
    }

    [Fact]
    public void TopicResult_None_IsNotRecognized()
    {
        Assert.False(TopicResult.None.IsRecognized);
    }

    [Fact]
    public void TopicResult_WithUrl_IsRecognized()
    {
        var topic = new TopicResult("Library", "https://en.wikipedia.org/wiki/Library", 0.8);

        Assert.True(topic.IsRecognized);
    }
}
```

- [ ] **Step 4: Register both projects in the solution**

In `SmartCodeGenerator/SmartCodeGenerator.slnx`, add two `<Project>` lines inside `<Solution>` (paths are relative to the slnx file):

```xml
  <Project Path="../DomainDiscovery/DomainDiscovery.csproj" />
  <Project Path="../DomainDiscovery.Tests/DomainDiscovery.Tests.csproj" />
```

- [ ] **Step 5: Build and run the test**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`
Expected: build succeeds, 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery DomainDiscovery.Tests SmartCodeGenerator/SmartCodeGenerator.slnx
git commit -m "Add DomainDiscovery project skeleton and candidate model"
```

---

### Task 2: Field type-inference heuristics

**Files:**
- Create: `DomainDiscovery/Enrichment/TypeInference.cs`
- Create: `DomainDiscovery.Tests/Enrichment/TypeInferenceTests.cs`

**Interfaces:**
- Produces: `DomainDiscovery.Enrichment.TypeInference.Infer(string fieldName) → string` (returns a C# type name: `bool`/`decimal`/`DateTime`/`int`/`string`).

- [ ] **Step 1: Write the failing test**

`DomainDiscovery.Tests/Enrichment/TypeInferenceTests.cs`:

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class TypeInferenceTests
{
    [Theory]
    [InlineData("Price", "decimal")]
    [InlineData("TotalCost", "decimal")]
    [InlineData("LateFine", "decimal")]
    [InlineData("DueDate", "DateTime")]
    [InlineData("CreatedTime", "DateTime")]
    [InlineData("Quantity", "int")]
    [InlineData("StockCount", "int")]
    [InlineData("IsActive", "bool")]
    [InlineData("HasLateFee", "bool")]
    [InlineData("Title", "string")]
    [InlineData("Name", "string")]
    public void Infer_ReturnsExpectedType(string fieldName, string expected)
    {
        Assert.Equal(expected, TypeInference.Infer(fieldName));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~TypeInferenceTests"`
Expected: FAIL — `TypeInference` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`DomainDiscovery/Enrichment/TypeInference.cs`:

```csharp
namespace DomainDiscovery.Enrichment;

/// <summary>
/// Heuristic mapping from a field name to a C# type. Deliberately simple: the
/// review loop lets the user correct anything this gets wrong.
/// </summary>
public static class TypeInference
{
    public static string Infer(string fieldName)
    {
        var name = (fieldName ?? string.Empty).ToLowerInvariant();

        if (name.StartsWith("is", StringComparison.Ordinal)
            || name.StartsWith("has", StringComparison.Ordinal))
            return "bool";

        if (ContainsAny(name, "price", "cost", "amount", "total", "fee", "fine", "balance", "salary"))
            return "decimal";

        if (ContainsAny(name, "date", "time", "created", "updated", "due"))
            return "DateTime";

        if (ContainsAny(name, "quantity", "count", "number", "qty", "age", "stock"))
            return "int";

        return "string";
    }

    private static bool ContainsAny(string text, params string[] words)
        => words.Any(w => text.Contains(w, StringComparison.Ordinal));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~TypeInferenceTests"`
Expected: PASS (11 cases).

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/TypeInference.cs DomainDiscovery.Tests/Enrichment/TypeInferenceTests.cs
git commit -m "Add field type-inference heuristics"
```

---

### Task 3: Seed entity extraction from an NLE result

**Files:**
- Create: `DomainDiscovery/SeedExtraction/NameNormalizer.cs`
- Create: `DomainDiscovery/SeedExtraction/SeedEntityExtractor.cs`
- Create: `DomainDiscovery.Tests/SeedExtraction/SeedEntityExtractorTests.cs`

**Interfaces:**
- Consumes: `SemanticResult`, `Entity`, `EntityType` (from `NaturalLanguageEngine.Models.Semantics`).
- Produces: `DomainDiscovery.SeedExtraction.NameNormalizer.ToEntityName(string) → string` (PascalCase, last word); `SeedEntityExtractor.Extract(SemanticResult) → CandidateDomainModel` (entities of type Object/Person/Organization/Location, de-duplicated, `Provenance.Seed`).

- [ ] **Step 1: Write the failing test**

`DomainDiscovery.Tests/SeedExtraction/SeedEntityExtractorTests.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.SeedExtraction;

public class SeedEntityExtractorTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Extract_KeepsObjectPersonOrgLocation_AsPascalCase()
    {
        var result = new SemanticResult();
        result.Entities.Add(Ent("book", EntityType.Object));
        result.Entities.Add(Ent("member", EntityType.Person));
        result.Entities.Add(Ent("borrow", EntityType.Action));   // dropped: verb
        result.Entities.Add(Ent("late", EntityType.Attribute));  // dropped: attribute

        var model = new SeedEntityExtractor().Extract(result);

        Assert.Equal(new[] { "Book", "Member" }, model.Entities.Select(e => e.Name));
        Assert.All(model.Entities, e => Assert.Equal(Provenance.Seed, e.Provenance));
    }

    [Fact]
    public void Extract_DeduplicatesByName_CaseInsensitive()
    {
        var result = new SemanticResult();
        result.Entities.Add(Ent("Book", EntityType.Object));
        result.Entities.Add(Ent("book", EntityType.Object));

        var model = new SeedEntityExtractor().Extract(result);

        Assert.Single(model.Entities);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~SeedEntityExtractorTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write minimal implementation**

`DomainDiscovery/SeedExtraction/NameNormalizer.cs`:

```csharp
namespace DomainDiscovery.SeedExtraction;

/// <summary>Turns a raw entity phrase into a PascalCase C# identifier stub.</summary>
public static class NameNormalizer
{
    public static string ToEntityName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Take the head noun (last word) of a phrase like "library member".
        var word = text.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;

        // Keep only letters/digits.
        var cleaned = new string(word.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length == 0)
            return string.Empty;

        return char.ToUpperInvariant(cleaned[0]) + cleaned[1..].ToLowerInvariant();
    }
}
```

`DomainDiscovery/SeedExtraction/SeedEntityExtractor.cs`:

```csharp
using DomainDiscovery.Model;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.SeedExtraction;

/// <summary>
/// Turns an NLE <see cref="SemanticResult"/> into the seed entity model: the
/// nouns the user actually mentioned. Verbs (Action) and Attributes are ignored
/// here — they inform fields/relationships during enrichment, not entities.
/// </summary>
public sealed class SeedEntityExtractor
{
    private static readonly EntityType[] EntityTypes =
    {
        EntityType.Object,
        EntityType.Person,
        EntityType.Organization,
        EntityType.Location
    };

    public CandidateDomainModel Extract(SemanticResult result)
    {
        var model = new CandidateDomainModel();

        foreach (var entity in result.Entities.Where(e => EntityTypes.Contains(e.Type)))
        {
            var name = NameNormalizer.ToEntityName(entity.Text);
            if (name.Length == 0)
                continue;

            if (model.Entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            model.Entities.Add(new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Seed,
                Confidence = entity.Confidence
            });
        }

        return model;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~SeedEntityExtractorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/SeedExtraction DomainDiscovery.Tests/SeedExtraction
git commit -m "Add seed entity extraction from NLE result"
```

---

### Task 4: Wikipedia client interface + topic recognizer (with test fake)

**Files:**
- Create: `DomainDiscovery/Wikipedia/IWikipediaClient.cs`
- Create: `DomainDiscovery/Recognition/ITopicRecognizer.cs`
- Create: `DomainDiscovery/Recognition/WikipediaTopicRecognizer.cs`
- Create: `DomainDiscovery.Tests/Fakes/FakeWikipediaClient.cs`
- Create: `DomainDiscovery.Tests/Recognition/WikipediaTopicRecognizerTests.cs`

**Interfaces:**
- Consumes: `SearchHit`, `TopicResult`, `CandidateDomainModel` (Task 1).
- Produces: `DomainDiscovery.Wikipedia.IWikipediaClient` with `IReadOnlyList<SearchHit> Search(IEnumerable<string> terms)` and `string FetchExtract(string articleTitle)`; `DomainDiscovery.Recognition.ITopicRecognizer.Recognize(IEnumerable<string> seedEntityNames) → TopicResult`; `WikipediaTopicRecognizer(IWikipediaClient)`.
- Produces (tests): `DomainDiscovery.Tests.Fakes.FakeWikipediaClient` with settable `SearchHits` list and `Extracts` dictionary, recording the last search terms.

- [ ] **Step 1: Write the failing test**

`DomainDiscovery.Tests/Fakes/FakeWikipediaClient.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Tests.Fakes;

/// <summary>In-memory <see cref="IWikipediaClient"/> for offline tests.</summary>
public sealed class FakeWikipediaClient : IWikipediaClient
{
    public List<SearchHit> SearchHits { get; } = new();
    public Dictionary<string, string> Extracts { get; } = new();
    public List<string> LastSearchTerms { get; } = new();

    public IReadOnlyList<SearchHit> Search(IEnumerable<string> terms)
    {
        LastSearchTerms.Clear();
        LastSearchTerms.AddRange(terms);
        return SearchHits;
    }

    public string FetchExtract(string articleTitle)
        => Extracts.TryGetValue(articleTitle, out var text) ? text : string.Empty;
}
```

`DomainDiscovery.Tests/Recognition/WikipediaTopicRecognizerTests.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.Tests.Fakes;
using Xunit;

namespace DomainDiscovery.Tests.Recognition;

public class WikipediaTopicRecognizerTests
{
    [Fact]
    public void Recognize_PicksRank0Hit_AsTopic()
    {
        var client = new FakeWikipediaClient();
        client.SearchHits.Add(new SearchHit("Library", "https://en.wikipedia.org/wiki/Library", 0));
        client.SearchHits.Add(new SearchHit("Librarian", "https://en.wikipedia.org/wiki/Librarian", 1));

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "Book", "Member" });

        Assert.Equal("Library", topic.Title);
        Assert.True(topic.Confidence > 0);
        Assert.Contains("Book", client.LastSearchTerms);
    }

    [Fact]
    public void Recognize_NoHits_ReturnsNone()
    {
        var topic = new WikipediaTopicRecognizer(new FakeWikipediaClient())
            .Recognize(new[] { "Book" });

        Assert.False(topic.IsRecognized);
    }

    [Fact]
    public void Recognize_NoSeedEntities_ReturnsNone_WithoutSearching()
    {
        var client = new FakeWikipediaClient();
        client.SearchHits.Add(new SearchHit("X", "https://x", 0));

        var topic = new WikipediaTopicRecognizer(client).Recognize(Array.Empty<string>());

        Assert.False(topic.IsRecognized);
        Assert.Empty(client.LastSearchTerms);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~WikipediaTopicRecognizerTests"`
Expected: FAIL — `IWikipediaClient`, `ITopicRecognizer`, `WikipediaTopicRecognizer` do not exist.

- [ ] **Step 3: Write minimal implementation**

`DomainDiscovery/Wikipedia/IWikipediaClient.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery.Wikipedia;

/// <summary>Read-only access to Wikipedia: search and plain-text article extract.</summary>
public interface IWikipediaClient
{
    /// <summary>Best-first search hits for the given terms (may be empty).</summary>
    IReadOnlyList<SearchHit> Search(IEnumerable<string> terms);

    /// <summary>Plain-text extract of an article, or empty string if unavailable.</summary>
    string FetchExtract(string articleTitle);
}
```

`DomainDiscovery/Recognition/ITopicRecognizer.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery.Recognition;

public interface ITopicRecognizer
{
    TopicResult Recognize(IEnumerable<string> seedEntityNames);
}
```

`DomainDiscovery/Recognition/WikipediaTopicRecognizer.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;

namespace DomainDiscovery.Recognition;

/// <summary>
/// Recognizes the domain by searching Wikipedia for the seed entity names and
/// taking the best hit as the topic. Confidence falls off with rank.
/// </summary>
public sealed class WikipediaTopicRecognizer : ITopicRecognizer
{
    private readonly IWikipediaClient _client;

    public WikipediaTopicRecognizer(IWikipediaClient client)
        => _client = client;

    public TopicResult Recognize(IEnumerable<string> seedEntityNames)
    {
        var terms = seedEntityNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (terms.Count == 0)
            return TopicResult.None;

        var best = _client.Search(terms)
            .OrderBy(h => h.Rank)
            .FirstOrDefault();

        if (best is null)
            return TopicResult.None;

        var confidence = 1.0 / (1 + best.Rank);
        return new TopicResult(best.Title, best.Url, confidence);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~WikipediaTopicRecognizerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Wikipedia DomainDiscovery/Recognition DomainDiscovery.Tests/Fakes DomainDiscovery.Tests/Recognition
git commit -m "Add Wikipedia client interface and topic recognizer"
```

---

### Task 5: Entity harvester (mine entities/fields from prose analysis)

**Files:**
- Create: `DomainDiscovery/Enrichment/IEntityHarvester.cs`
- Create: `DomainDiscovery/Enrichment/EntityHarvester.cs`
- Create: `DomainDiscovery.Tests/Enrichment/EntityHarvesterTests.cs`

**Interfaces:**
- Consumes: `SemanticResult`, `Entity`, `EntityType`, `Relationship`, `NameNormalizer` (Task 3), `TypeInference` (Task 2).
- Produces: `DomainDiscovery.Enrichment.IEntityHarvester.Harvest(SemanticResult) → CandidateDomainModel`; `EntityHarvester : IEntityHarvester`. Harvested entities have `Provenance.Wiki`, frequency-based `Confidence`, and fields derived from `Attribute`-typed relationship targets.

- [ ] **Step 1: Write the failing test**

`DomainDiscovery.Tests/Enrichment/EntityHarvesterTests.cs`:

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class EntityHarvesterTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Harvest_RanksEntitiesByFrequency_Descending()
    {
        var r = new SemanticResult();
        r.Entities.Add(Ent("loan", EntityType.Object));
        r.Entities.Add(Ent("loan", EntityType.Object));
        r.Entities.Add(Ent("catalog", EntityType.Object));

        var model = new EntityHarvester().Harvest(r);

        Assert.Equal(new[] { "Loan", "Catalog" }, model.Entities.Select(e => e.Name));
        Assert.All(model.Entities, e => Assert.Equal(Provenance.Wiki, e.Provenance));
        Assert.True(model.Entities[0].Confidence >= model.Entities[1].Confidence);
    }

    [Fact]
    public void Harvest_AttachesAttributeRelationships_AsTypedFields()
    {
        var r = new SemanticResult();
        var loan = Ent("loan", EntityType.Object);
        var dueDate = Ent("due date", EntityType.Attribute);
        r.Entities.Add(loan);
        r.Entities.Add(dueDate);
        r.Relationships.Add(new Relationship { Source = loan, Target = dueDate });

        var model = new EntityHarvester().Harvest(r);

        var entity = Assert.Single(model.Entities);
        var field = Assert.Single(entity.Fields);
        Assert.Equal("Date", field.Name);        // NameNormalizer takes the head word
        Assert.Equal("DateTime", field.Type);
        Assert.Equal(Provenance.Wiki, field.Provenance);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~EntityHarvesterTests"`
Expected: FAIL — harvester types do not exist.

- [ ] **Step 3: Write minimal implementation**

`DomainDiscovery/Enrichment/IEntityHarvester.cs`:

```csharp
using DomainDiscovery.Model;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.Enrichment;

public interface IEntityHarvester
{
    CandidateDomainModel Harvest(SemanticResult analysis);
}
```

`DomainDiscovery/Enrichment/EntityHarvester.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Mines a domain model out of an NLE analysis of article prose: noun-type
/// entities become candidate entities (frequency-ranked); Attribute targets of
/// relationships become typed fields on their source entity.
/// </summary>
public sealed class EntityHarvester : IEntityHarvester
{
    private static readonly EntityType[] EntityTypes =
    {
        EntityType.Object,
        EntityType.Person,
        EntityType.Organization,
        EntityType.Location
    };

    public CandidateDomainModel Harvest(SemanticResult analysis)
    {
        var model = new CandidateDomainModel();

        // 1. Count noun mentions by normalized name.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in analysis.Entities.Where(e => EntityTypes.Contains(e.Type)))
        {
            var name = NameNormalizer.ToEntityName(entity.Text);
            if (name.Length == 0)
                continue;

            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        if (counts.Count == 0)
            return model;

        var max = counts.Values.Max();

        // 2. Frequency-ranked entities with normalized confidence.
        foreach (var (name, count) in counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            model.Entities.Add(new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Wiki,
                Confidence = (double)count / max
            });
        }

        // 3. Attribute targets of relationships become typed fields.
        foreach (var rel in analysis.Relationships)
        {
            if (rel.Source is null || rel.Target is null || rel.Target.Type != EntityType.Attribute)
                continue;

            var ownerName = NameNormalizer.ToEntityName(rel.Source.Text);
            var owner = model.Entities.FirstOrDefault(
                e => string.Equals(e.Name, ownerName, StringComparison.OrdinalIgnoreCase));
            if (owner is null)
                continue;

            var fieldName = NameNormalizer.ToEntityName(rel.Target.Text);
            if (fieldName.Length == 0
                || owner.Fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
                continue;

            owner.Fields.Add(new CandidateField
            {
                Name = fieldName,
                Type = TypeInference.Infer(fieldName),
                Provenance = Provenance.Wiki
            });
        }

        return model;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~EntityHarvesterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/IEntityHarvester.cs DomainDiscovery/Enrichment/EntityHarvester.cs DomainDiscovery.Tests/Enrichment/EntityHarvesterTests.cs
git commit -m "Add entity harvester for prose mining"
```

---

### Task 6: Domain enricher (fetch, analyze, merge)

**Files:**
- Create: `DomainDiscovery/Enrichment/IDomainEnricher.cs`
- Create: `DomainDiscovery/Enrichment/DomainEnricher.cs`
- Create: `DomainDiscovery.Tests/Fakes/StubAnalyzer.cs`
- Create: `DomainDiscovery.Tests/Enrichment/DomainEnricherTests.cs`

**Interfaces:**
- Consumes: `IWikipediaClient` (Task 4), `ISemanticAnalyzer` (`NaturalLanguageEngine.Abstractions.Semantics`), `IEntityHarvester` (Task 5), `TopicResult`, `CandidateDomainModel`.
- Produces: `DomainDiscovery.Enrichment.IDomainEnricher.Enrich(TopicResult topic, CandidateDomainModel seed) → CandidateDomainModel`; `DomainEnricher(IWikipediaClient, ISemanticAnalyzer, IEntityHarvester)`. Merge rule: start from seed; add harvested entities whose name is not already present; for existing entities, add harvested fields not already present; seed provenance/confidence win on conflict.

- [ ] **Step 1: Write the failing test**

`DomainDiscovery.Tests/Fakes/StubAnalyzer.cs`:

```csharp
using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.Tests.Fakes;

/// <summary>Returns a canned <see cref="SemanticResult"/> regardless of input.</summary>
public sealed class StubAnalyzer : ISemanticAnalyzer
{
    private readonly SemanticResult _result;
    public string? LastText { get; private set; }

    public StubAnalyzer(SemanticResult result) => _result = result;

    public SemanticResult Analyze(string text)
    {
        LastText = text;
        return _result;
    }
}
```

`DomainDiscovery.Tests/Enrichment/DomainEnricherTests.cs`:

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class DomainEnricherTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Enrich_AddsHarvestedEntities_KeepingSeed()
    {
        var seed = new CandidateDomainModel();
        seed.Entities.Add(new CandidateEntity { Name = "Book", Provenance = Provenance.Seed });

        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("loan", EntityType.Object));   // new, from wiki
        analysis.Entities.Add(Ent("book", EntityType.Object));   // dup of seed

        var client = new FakeWikipediaClient();
        client.Extracts["Library"] = "ignored - stub analyzer drives output";

        var enricher = new DomainEnricher(client, new StubAnalyzer(analysis), new EntityHarvester());
        var topic = new TopicResult("Library", "https://en.wikipedia.org/wiki/Library", 0.8);

        var model = enricher.Enrich(topic, seed);

        Assert.Equal(new[] { "Book", "Loan" }, model.Entities.Select(e => e.Name));
        Assert.Equal(Provenance.Seed, model.Entities.Single(e => e.Name == "Book").Provenance);
        Assert.Equal(Provenance.Wiki, model.Entities.Single(e => e.Name == "Loan").Provenance);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~DomainEnricherTests"`
Expected: FAIL — enricher types do not exist.

- [ ] **Step 3: Write minimal implementation**

`DomainDiscovery/Enrichment/IDomainEnricher.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery.Enrichment;

public interface IDomainEnricher
{
    CandidateDomainModel Enrich(TopicResult topic, CandidateDomainModel seed);
}
```

`DomainDiscovery/Enrichment/DomainEnricher.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;
using NaturalLanguageEngine.Abstractions.Semantics;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Fetches the recognized article, runs it through the NLE, harvests a candidate
/// model, and merges it into the seed. Seed entities/fields win on conflict.
/// </summary>
public sealed class DomainEnricher : IDomainEnricher
{
    private readonly IWikipediaClient _client;
    private readonly ISemanticAnalyzer _analyzer;
    private readonly IEntityHarvester _harvester;

    public DomainEnricher(IWikipediaClient client, ISemanticAnalyzer analyzer, IEntityHarvester harvester)
    {
        _client = client;
        _analyzer = analyzer;
        _harvester = harvester;
    }

    public CandidateDomainModel Enrich(TopicResult topic, CandidateDomainModel seed)
    {
        var text = _client.FetchExtract(topic.Title);
        if (string.IsNullOrWhiteSpace(text))
            return seed;

        var harvested = _harvester.Harvest(_analyzer.Analyze(text));
        return Merge(seed, harvested);
    }

    private static CandidateDomainModel Merge(CandidateDomainModel seed, CandidateDomainModel harvested)
    {
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

        return seed;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~DomainEnricherTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/IDomainEnricher.cs DomainDiscovery/Enrichment/DomainEnricher.cs DomainDiscovery.Tests/Fakes/StubAnalyzer.cs DomainDiscovery.Tests/Enrichment/DomainEnricherTests.cs
git commit -m "Add domain enricher with seed/harvest merge"
```

---

### Task 7: Model editor (pure review-loop operations)

**Files:**
- Create: `DomainDiscovery/Review/ModelEditor.cs`
- Create: `DomainDiscovery.Tests/Review/ModelEditorTests.cs`

**Interfaces:**
- Consumes: `CandidateDomainModel`, `CandidateEntity`, `Provenance`, `NameNormalizer`.
- Produces: static `DomainDiscovery.Review.ModelEditor` with `bool AddEntity(CandidateDomainModel, string name)`, `bool RemoveEntity(CandidateDomainModel, string name)`, `bool RenameEntity(CandidateDomainModel, string oldName, string newName)`. Each returns whether it changed the model. Added entities get `Provenance.Seed` (user-authored).

- [ ] **Step 1: Write the failing test**

`DomainDiscovery.Tests/Review/ModelEditorTests.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.Review;
using Xunit;

namespace DomainDiscovery.Tests.Review;

public class ModelEditorTests
{
    private static CandidateDomainModel ModelWith(params string[] names)
    {
        var m = new CandidateDomainModel();
        foreach (var n in names)
            m.Entities.Add(new CandidateEntity { Name = n });
        return m;
    }

    [Fact]
    public void AddEntity_AddsNormalizedName_Once()
    {
        var model = ModelWith("Book");

        Assert.True(ModelEditor.AddEntity(model, "loan"));
        Assert.False(ModelEditor.AddEntity(model, "Loan")); // duplicate

        Assert.Equal(new[] { "Book", "Loan" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void RemoveEntity_RemovesMatch_CaseInsensitive()
    {
        var model = ModelWith("Book", "Loan");

        Assert.True(ModelEditor.RemoveEntity(model, "book"));
        Assert.False(ModelEditor.RemoveEntity(model, "Missing"));

        Assert.Equal(new[] { "Loan" }, model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void RenameEntity_RenamesExisting_RejectsCollision()
    {
        var model = ModelWith("Book", "Loan");

        Assert.True(ModelEditor.RenameEntity(model, "Book", "Publication"));
        Assert.False(ModelEditor.RenameEntity(model, "Publication", "Loan")); // collides

        Assert.Equal(new[] { "Publication", "Loan" }, model.Entities.Select(e => e.Name));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~ModelEditorTests"`
Expected: FAIL — `ModelEditor` does not exist.

- [ ] **Step 3: Write minimal implementation**

`DomainDiscovery/Review/ModelEditor.cs`:

```csharp
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Review;

/// <summary>
/// Pure mutations behind the interactive review loop. Each method returns whether
/// it changed the model, so the CLI can report the outcome.
/// </summary>
public static class ModelEditor
{
    public static bool AddEntity(CandidateDomainModel model, string name)
    {
        var normalized = NameNormalizer.ToEntityName(name);
        if (normalized.Length == 0 || Find(model, normalized) is not null)
            return false;

        model.Entities.Add(new CandidateEntity { Name = normalized, Provenance = Provenance.Seed });
        return true;
    }

    public static bool RemoveEntity(CandidateDomainModel model, string name)
    {
        var target = Find(model, NameNormalizer.ToEntityName(name));
        if (target is null)
            return false;

        model.Entities.Remove(target);
        return true;
    }

    public static bool RenameEntity(CandidateDomainModel model, string oldName, string newName)
    {
        var target = Find(model, NameNormalizer.ToEntityName(oldName));
        var normalizedNew = NameNormalizer.ToEntityName(newName);
        if (target is null || normalizedNew.Length == 0 || Find(model, normalizedNew) is not null)
            return false;

        target.Name = normalizedNew;
        return true;
    }

    private static CandidateEntity? Find(CandidateDomainModel model, string name)
        => model.Entities.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~ModelEditorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Review DomainDiscovery.Tests/Review
git commit -m "Add pure model-edit operations for review loop"
```

---

### Task 8: Discovery pipeline facade + default factory

**Files:**
- Create: `DomainDiscovery/DiscoveryResult.cs`
- Create: `DomainDiscovery/DomainDiscoveryPipeline.cs`
- Create: `DomainDiscovery.Tests/DomainDiscoveryPipelineTests.cs`

**Interfaces:**
- Consumes: `ISemanticAnalyzer`, `SeedEntityExtractor`, `ITopicRecognizer`, `IDomainEnricher`, all model types.
- Produces: `DomainDiscovery.DiscoveryResult(TopicResult Topic, CandidateDomainModel Model)`; `DomainDiscovery.DomainDiscoveryPipeline` with ctor `(ISemanticAnalyzer, SeedEntityExtractor, ITopicRecognizer, IDomainEnricher)`, method `DiscoveryResult Discover(string freeText)`, and static factory `CreateDefault(string cacheDirectory) → DomainDiscoveryPipeline` (wires the real `HttpWikipediaClient` from Task 9 and `EngineBuilder.BuildDefault().Analyzer`).
- Note: `CreateDefault` depends on Task 9's `HttpWikipediaClient`; implement the instance ctor + `Discover` and the unit tests in this task, and add `CreateDefault` in Step 3 referencing Task 9 (do Task 9 first if executing strictly in order — the factory body is given here in full).

- [ ] **Step 1: Write the failing test**

`DomainDiscovery.Tests/DomainDiscoveryPipelineTests.cs`:

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests;

public class DomainDiscoveryPipelineTests
{
    private static Entity Ent(string text, EntityType type)
        => new() { Text = text, Type = type, Confidence = 0.9 };

    [Fact]
    public void Discover_ExtractsSeed_RecognizesTopic_AndEnriches()
    {
        // NLE sees the user's text and yields "book" (Object).
        var userAnalysis = new SemanticResult();
        userAnalysis.Entities.Add(Ent("book", EntityType.Object));

        // The article analysis (same stub, since Discover calls Analyze twice)
        // also yields "book"; enrichment adds nothing new but must not throw.
        var analyzer = new StubAnalyzer(userAnalysis);

        var client = new FakeWikipediaClient();
        client.SearchHits.Add(new SearchHit("Library", "https://en.wikipedia.org/wiki/Library", 0));
        client.Extracts["Library"] = "A library lends books.";

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()));

        var result = pipeline.Discover("track books that members borrow");

        Assert.Equal("Library", result.Topic.Title);
        Assert.Contains(result.Model.Entities, e => e.Name == "Book");
    }

    [Fact]
    public void Discover_NoTopic_ReturnsSeedOnly()
    {
        var analysis = new SemanticResult();
        analysis.Entities.Add(Ent("widget", EntityType.Object));
        var analyzer = new StubAnalyzer(analysis);

        var client = new FakeWikipediaClient(); // no search hits

        var pipeline = new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()));

        var result = pipeline.Discover("manage widgets");

        Assert.False(result.Topic.IsRecognized);
        Assert.Equal(new[] { "Widget" }, result.Model.Entities.Select(e => e.Name));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~DomainDiscoveryPipelineTests"`
Expected: FAIL — pipeline types do not exist.

- [ ] **Step 3: Write minimal implementation**

`DomainDiscovery/DiscoveryResult.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery;

/// <summary>The recognized topic plus the (possibly enriched) candidate model.</summary>
public sealed record DiscoveryResult(TopicResult Topic, CandidateDomainModel Model);
```

`DomainDiscovery/DomainDiscoveryPipeline.cs`:

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Wikipedia;
using NaturalLanguageEngine.Abstractions.Semantics;

namespace DomainDiscovery;

/// <summary>
/// Orchestrates discovery: NLE-extract seed entities from the user's text,
/// recognize the topic, then enrich from that article. No console I/O.
/// </summary>
public sealed class DomainDiscoveryPipeline
{
    private readonly ISemanticAnalyzer _analyzer;
    private readonly SeedEntityExtractor _seedExtractor;
    private readonly ITopicRecognizer _recognizer;
    private readonly IDomainEnricher _enricher;

    public DomainDiscoveryPipeline(
        ISemanticAnalyzer analyzer,
        SeedEntityExtractor seedExtractor,
        ITopicRecognizer recognizer,
        IDomainEnricher enricher)
    {
        _analyzer = analyzer;
        _seedExtractor = seedExtractor;
        _recognizer = recognizer;
        _enricher = enricher;
    }

    public DiscoveryResult Discover(string freeText)
    {
        var analysis = _analyzer.Analyze(freeText ?? string.Empty);
        var seed = _seedExtractor.Extract(analysis);

        var topic = _recognizer.Recognize(seed.Entities.Select(e => e.Name));
        if (!topic.IsRecognized)
            return new DiscoveryResult(topic, seed);

        var enriched = _enricher.Enrich(topic, seed);
        return new DiscoveryResult(topic, enriched);
    }

    /// <summary>
    /// Builds a pipeline wired to the real Wikipedia HTTP client (cached under
    /// <paramref name="cacheDirectory"/>) and the default NLE analyzer.
    /// </summary>
    public static DomainDiscoveryPipeline CreateDefault(string cacheDirectory)
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;
        IWikipediaClient client = new HttpWikipediaClient(cacheDirectory);
        var harvester = new EntityHarvester();

        return new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, harvester));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~DomainDiscoveryPipelineTests"`
Expected: PASS. (The two unit tests do not call `CreateDefault`, so Task 9's `HttpWikipediaClient` need only exist for the project to compile — implement Task 9 before this compiles, or temporarily comment `CreateDefault` until Task 9 lands. Recommended: execute Task 9 before Task 8's Step 4.)

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/DiscoveryResult.cs DomainDiscovery/DomainDiscoveryPipeline.cs DomainDiscovery.Tests/DomainDiscoveryPipelineTests.cs
git commit -m "Add domain discovery pipeline facade and factory"
```

---

### Task 9: Real Wikipedia HTTP client with disk cache

**Files:**
- Create: `DomainDiscovery/Wikipedia/WikipediaCache.cs`
- Create: `DomainDiscovery/Wikipedia/HttpWikipediaClient.cs`
- Create: `DomainDiscovery.Tests/Wikipedia/WikipediaCacheTests.cs`

**Interfaces:**
- Consumes: `IWikipediaClient`, `SearchHit`.
- Produces: `DomainDiscovery.Wikipedia.WikipediaCache(string directory)` with `string KeyToPath(string key)`, `bool TryGet(string key, out string value)`, `void Put(string key, string value)`; `HttpWikipediaClient(string cacheDirectory, HttpClient? http = null) : IWikipediaClient`.
- Note: only the **cache** is unit-tested (deterministic, offline). The HTTP calls are exercised manually in Task 10's verification, per the spec's "no network in tests" rule.

- [ ] **Step 1: Write the failing test (cache only)**

`DomainDiscovery.Tests/Wikipedia/WikipediaCacheTests.cs`:

```csharp
using DomainDiscovery.Wikipedia;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia;

public class WikipediaCacheTests
{
    [Fact]
    public void PutThenTryGet_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dd-cache-test-" + Guid.NewGuid());
        try
        {
            var cache = new WikipediaCache(dir);

            Assert.False(cache.TryGet("search:book member", out _));

            cache.Put("search:book member", "hello");

            Assert.True(cache.TryGet("search:book member", out var value));
            Assert.Equal("hello", value);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeyToPath_SanitizesInvalidCharacters()
    {
        var cache = new WikipediaCache(Path.GetTempPath());

        var path = cache.KeyToPath("extract:Library/Science?x");

        Assert.DoesNotContain('/', Path.GetFileName(path));
        Assert.DoesNotContain('?', Path.GetFileName(path));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~WikipediaCacheTests"`
Expected: FAIL — `WikipediaCache` does not exist.

- [ ] **Step 3: Write the cache**

`DomainDiscovery/Wikipedia/WikipediaCache.cs`:

```csharp
namespace DomainDiscovery.Wikipedia;

/// <summary>
/// A tiny file cache keyed by an arbitrary string. Makes repeated Wikipedia calls
/// (and re-runs) offline and deterministic.
/// </summary>
public sealed class WikipediaCache
{
    private readonly string _directory;

    public WikipediaCache(string directory) => _directory = directory;

    public string KeyToPath(string key)
    {
        var safe = new string(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        if (safe.Length > 120)
            safe = safe[..120];
        return Path.Combine(_directory, safe + ".cache");
    }

    public bool TryGet(string key, out string value)
    {
        var path = KeyToPath(key);
        if (File.Exists(path))
        {
            value = File.ReadAllText(path);
            return true;
        }

        value = string.Empty;
        return false;
    }

    public void Put(string key, string value)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(KeyToPath(key), value);
    }
}
```

- [ ] **Step 4: Run cache test to verify it passes**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --filter "FullyQualifiedName~WikipediaCacheTests"`
Expected: PASS.

- [ ] **Step 5: Write the HTTP client**

`DomainDiscovery/Wikipedia/HttpWikipediaClient.cs`:

```csharp
using System.Text.Json;
using DomainDiscovery.Model;

namespace DomainDiscovery.Wikipedia;

/// <summary>
/// Real <see cref="IWikipediaClient"/> over the MediaWiki API, backed by a disk
/// cache. Network failures degrade to empty results (never throw) so the pipeline
/// can fall back to seed-only discovery.
/// </summary>
public sealed class HttpWikipediaClient : IWikipediaClient
{
    private const string ApiBase = "https://en.wikipedia.org/w/api.php";

    private readonly WikipediaCache _cache;
    private readonly HttpClient _http;

    public HttpWikipediaClient(string cacheDirectory, HttpClient? http = null)
    {
        _cache = new WikipediaCache(cacheDirectory);
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "SmartCodeGenerator-DomainDiscovery/1.0");
    }

    public IReadOnlyList<SearchHit> Search(IEnumerable<string> terms)
    {
        var query = string.Join(' ', terms);
        var url = $"{ApiBase}?action=opensearch&limit=5&format=json&search={Uri.EscapeDataString(query)}";

        var json = GetCached($"search:{query}", url);
        if (json.Length == 0)
            return Array.Empty<SearchHit>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            // opensearch returns: [ term, [titles], [descriptions], [urls] ]
            var titles = doc.RootElement[1];
            var urls = doc.RootElement[3];

            var hits = new List<SearchHit>();
            for (var i = 0; i < titles.GetArrayLength(); i++)
            {
                hits.Add(new SearchHit(
                    titles[i].GetString() ?? string.Empty,
                    urls[i].GetString() ?? string.Empty,
                    i));
            }
            return hits;
        }
        catch (JsonException)
        {
            return Array.Empty<SearchHit>();
        }
    }

    public string FetchExtract(string articleTitle)
    {
        var url = $"{ApiBase}?action=query&prop=extracts&explaintext=1&redirects=1&format=json"
                + $"&titles={Uri.EscapeDataString(articleTitle)}";

        var json = GetCached($"extract:{articleTitle}", url);
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
        catch (JsonException)
        {
            // fall through
        }

        return string.Empty;
    }

    private string GetCached(string key, string url)
    {
        if (_cache.TryGet(key, out var cached))
            return cached;

        try
        {
            var body = _http.GetStringAsync(url).GetAwaiter().GetResult();
            _cache.Put(key, body);
            return body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build DomainDiscovery/DomainDiscovery.csproj`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add DomainDiscovery/Wikipedia/WikipediaCache.cs DomainDiscovery/Wikipedia/HttpWikipediaClient.cs DomainDiscovery.Tests/Wikipedia/WikipediaCacheTests.cs
git commit -m "Add cached Wikipedia HTTP client"
```

---

### Task 10: `build app:` CLI command, review loop, and wiring

**Files:**
- Create: `CodeGenerator/App/BuildAppCommand.cs`
- Modify: `CodeGenerator/CodeGenerator.csproj` (add `DomainDiscovery` project reference)
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (intercept `build app:` in `Run` and `RunOnce`)

**Interfaces:**
- Consumes: `DomainDiscoveryPipeline.CreateDefault(string)`, `DiscoveryResult`, `CandidateDomainModel`, `ModelEditor`.
- Produces: `CodeGenerator.App.BuildAppCommand` with static `bool IsBuildApp(string input)` and instance `void Handle(string input, string workspaceRoot)`.

- [ ] **Step 1: Add the project reference**

In `CodeGenerator/CodeGenerator.csproj`, add inside the existing `<ItemGroup>` of `ProjectReference`s:

```xml
    <ProjectReference Include="..\DomainDiscovery\DomainDiscovery.csproj">
      <Private>True</Private>
    </ProjectReference>
```

- [ ] **Step 2: Write the command handler**

`CodeGenerator/App/BuildAppCommand.cs`:

```csharp
using DomainDiscovery;
using DomainDiscovery.Model;
using DomainDiscovery.Review;

namespace CodeGenerator.App;

/// <summary>
/// Handles `build app: &lt;description&gt;`. Runs domain discovery, prints the
/// recognized topic and candidate model, and drives the review loop. Code
/// generation from the approved model is added in a later phase.
/// </summary>
internal sealed class BuildAppCommand
{
    private const string Prefix = "build app:";

    public static bool IsBuildApp(string input)
        => input.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public void Handle(string input, string workspaceRoot)
    {
        var description = input.TrimStart()[Prefix.Length..].Trim();
        if (description.Length == 0)
        {
            Console.WriteLine("Usage: build app: <description of the system you want>");
            return;
        }

        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");
        var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDir);

        Console.WriteLine("Discovering domain (this may fetch from Wikipedia the first time)...");
        DiscoveryResult result = pipeline.Discover(description);

        if (result.Topic.IsRecognized)
            Console.WriteLine($"Recognized topic: {result.Topic.Title} " +
                              $"(confidence {result.Topic.Confidence:P0}) — {result.Topic.ArticleUrl}");
        else
            Console.WriteLine("No topic recognized online — showing seed entities only. " +
                              "You can add entities by hand below.");

        RunReviewLoop(result.Model);
    }

    private static void RunReviewLoop(CandidateDomainModel model)
    {
        while (true)
        {
            PrintModel(model);
            Console.Write("[a]pprove  add <name>  remove <name>  rename <old> <new>  [c]ancel > ");
            var line = (Console.ReadLine() ?? string.Empty).Trim();

            if (line.Length == 0)
                continue;

            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var verb = parts[0].ToLowerInvariant();

            switch (verb)
            {
                case "a":
                case "approve":
                    Console.WriteLine($"Model approved with {model.Entities.Count} entit"
                                      + (model.Entities.Count == 1 ? "y." : "ies.")
                                      + " (Code generation will be added in the scaffolding phase.)");
                    return;

                case "c":
                case "cancel":
                    Console.WriteLine("Cancelled. Nothing was generated.");
                    return;

                case "add" when parts.Length >= 2:
                    Report(ModelEditor.AddEntity(model, parts[1]), $"Added '{parts[1]}'.", "Could not add (duplicate or invalid).");
                    break;

                case "remove" when parts.Length >= 2:
                    Report(ModelEditor.RemoveEntity(model, parts[1]), $"Removed '{parts[1]}'.", "No such entity.");
                    break;

                case "rename" when parts.Length >= 3:
                    Report(ModelEditor.RenameEntity(model, parts[1], parts[2]), $"Renamed to '{parts[2]}'.", "Could not rename (missing or name in use).");
                    break;

                default:
                    Console.WriteLine("Unrecognized command.");
                    break;
            }
        }
    }

    private static void Report(bool changed, string ok, string fail)
        => Console.WriteLine(changed ? ok : fail);

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
        Console.WriteLine();
    }
}
```

- [ ] **Step 3: Wire it into the CLI loop**

In `CodeGenerator/App/CodeGeneratorStarter.cs`, add a field next to `_pipeline`:

```csharp
    private readonly BuildAppCommand _buildApp = new();
```

In `Run()`, inside the `try` block, add the interception **before** `_pipeline.Handle(...)`:

```csharp
                if (BuildAppCommand.IsBuildApp(input))
                {
                    _buildApp.Handle(input, _workspaceRoot);
                    continue;
                }

                // NLE analyzes first, then CodeGenerator generates from the result.
                _pipeline.Handle(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);
```

In `RunOnce(string command)`, before the `_pipeline.Handle(...)` call:

```csharp
            if (BuildAppCommand.IsBuildApp(command))
            {
                _buildApp.Handle(command, _workspaceRoot);
                return true;
            }
```

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: build succeeds across all projects.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: all tests PASS (existing suites plus the new DomainDiscovery tests).

- [ ] **Step 6: Manual end-to-end verification (network)**

Run (pipe input so the review loop gets an `approve`):

```bash
printf 'build app: track books that members borrow and return with late fines\napprove\nexit\n' | dotnet run --project CodeGenerator
```

Expected: prints "Recognized topic: Library ..." (or a close article), lists candidate entities including `Book`/`Member`, then "Model approved with N entities." A `.dd-cache` folder appears in the workspace. Re-running the same command works offline (served from cache).

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator/CodeGenerator.csproj
git commit -m "Add build app: command driving domain discovery and review"
```

---

## Follow-up (next plan): code generation

Spec slices 3–5 (§5.6, §7) are a separate plan built on the approved `CandidateDomainModel`: `IApplicationScaffolder` generating a console project, one model class + validator per entity (reusing `CodeGenerationEngine`), a `System.Text.Json` per-entity store, CRUD services, and a console-menu `Program.cs` — verified by `dotnet build` on the generated output. The `BuildAppCommand` "approve" branch becomes the entry point that calls the scaffolder instead of printing.

## Self-Review Notes

- **Spec coverage:** §4 data flow → Tasks 3–8; §5.1 `IWikipediaClient` → Tasks 4/9; §5.2 `ITopicRecognizer` → Task 4; §5.3 enricher + harvester + type inference → Tasks 2/5/6; §5.4 `CandidateDomainModel` → Task 1; §5.5 review loop → Tasks 7/10; §6 `build app:` trigger → Task 10; §8 error handling (no seed / no topic / offline) → Tasks 4 (None), 8 (seed-only), 9 (empty on failure); §9 testing (fixtures, fake client, offline) → every task. §5.6/§7 generation intentionally deferred (stated above).
- **Type consistency:** `IWikipediaClient.Search/FetchExtract`, `ITopicRecognizer.Recognize`, `IEntityHarvester.Harvest`, `IDomainEnricher.Enrich`, `NameNormalizer.ToEntityName`, `TypeInference.Infer`, `ModelEditor.Add/Remove/RenameEntity`, and `DomainDiscoveryPipeline.Discover/CreateDefault` are used with identical signatures across tasks.
- **Ordering caveat:** Task 8's `CreateDefault` references Task 9's `HttpWikipediaClient`; execute Task 9 before Task 8's final build (noted in Task 8).
