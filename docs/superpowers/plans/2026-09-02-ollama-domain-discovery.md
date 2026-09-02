# Ollama Domain Discovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Discover stage build its class model by asking a local Ollama model, falling back to the existing Wikipedia path when Ollama is unreachable.

**Architecture:** A new `IDomainModelSource` seam sits in front of discovery. `WikipediaDomainModelSource` wraps today's pipeline unchanged; `OllamaDomainModelSource` asks a local model for schema-constrained JSON and hands it to a deterministic `LlmModelNormalizer`. The normaliser repairs identifiers and types and then filters every proposed relationship through the existing `EntityRoleRank.CanReference` rule, so the LLM proposes and the codebase's own rules dispose.

**Tech Stack:** C# / .NET 10, xUnit (DomainDiscovery.Tests), MSTest 4.0 (CodeGenerator.Tests), Ollama HTTP API at `localhost:11434`, `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-09-02-ollama-domain-discovery-design.md`

## Global Constraints

- Repository root is `D:\SmartCodeGenerator-Repository`. The solution is `SmartCodeGenerator/SmartCodeGenerator.slnx`. All commands below run from the repository root.
- CI builds `SmartCodeGenerator.slnx` in **Release** with `-warnaserror`. Analyzer errors only surface on a non-incremental build, so verification builds must pass `--no-incremental`.
- `DomainDiscovery.Tests` is **xUnit** (`[Fact]`, `Assert.Equal(expected, actual)`).
- `CodeGenerator.Tests` is **MSTest 4.0** (`[TestClass]`, `[TestMethod]`). MSTEST0037 forbids `Assert.AreEqual(n, collection.Count)` — use `Assert.HasCount(n, collection)`.
- `DomainDiscovery` must not gain a reference to `CodeIntelligence`. Its only project reference is `NaturalLanguageEngine`.
- Config defaults, verbatim: Source `Ollama`, Model `qwen2.5:3b`, Endpoint `http://localhost:11434`, TimeoutMinutes `10`.
- Config loading **never throws**. A missing or unreadable file yields defaults, matching `AiConfig.Load`.
- No test may require Ollama to be installed. The one integration test goes inert (returns early) when `/api/tags` does not answer, following the existing `SqlServerIntegrationTests` pattern. **No new NuGet package may be added** — this repo has already declined `Xunit.SkippableFact` for exactly this situation.
- Ollama measured on this machine takes 64–72 seconds for a small domain. Never set an `HttpClient` timeout below the configured value; the 100-second default would kill real requests.

---

### Task 1: The `IDomainModelSource` seam

**Files:**
- Create: `DomainDiscovery/IDomainModelSource.cs`
- Create: `DomainDiscovery/DiscoveryOutcome.cs`
- Create: `DomainDiscovery/WikipediaDomainModelSource.cs`
- Test: `DomainDiscovery.Tests/WikipediaDomainModelSourceTests.cs`

**Interfaces:**
- Consumes: existing `DomainDiscoveryPipeline.Discover(string) -> DiscoveryResult`, `CandidateDomainModel`, `TopicResult`.
- Produces: `IDomainModelSource.Discover(string description) -> DiscoveryOutcome`; `DiscoveryOutcome(CandidateDomainModel Model, string SourceLabel, bool Succeeded, string? Message)`; `WikipediaDomainModelSource(DomainDiscoveryPipeline)` and `WikipediaDomainModelSource.Label`.

Why a new record rather than reusing `DiscoveryResult`: `TopicResult.IsRecognized` is `!string.IsNullOrEmpty(ArticleUrl)`, a Wikipedia article URL. The Ollama path has no article, so reusing it would make every successful LLM discovery report itself as unrecognised.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery;
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;   // NOT `Recognition.X`: DomainDiscovery.Tests.Recognition
                                     // already exists and would win the name lookup.
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Tests.Fakes;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests;

public class WikipediaDomainModelSourceTests
{
    private static DomainDiscoveryPipeline PipelineOver(FakeWikipediaClient client)
    {
        var analyzer = new StubAnalyzer(new SemanticResult());
        return new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, new EntityHarvester()),
            new EntityShaper(client),
            client);
    }

    [Fact]
    public void ReportsFailureAndAMessageWhenNoTopicIsRecognized()
    {
        var source = new WikipediaDomainModelSource(PipelineOver(new FakeWikipediaClient()));

        var outcome = source.Discover("something with no article");

        Assert.False(outcome.Succeeded);
        Assert.Equal(WikipediaDomainModelSource.Label, outcome.SourceLabel);
        Assert.NotNull(outcome.Message);
    }

    [Fact]
    public void CarriesTheModelThroughUnchanged()
    {
        var source = new WikipediaDomainModelSource(PipelineOver(new FakeWikipediaClient()));

        var outcome = source.Discover("orders and customers");

        Assert.NotNull(outcome.Model);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WikipediaDomainModelSourceTests"`
Expected: FAIL — `WikipediaDomainModelSource` and `DiscoveryOutcome` do not exist (CS0246).

- [ ] **Step 3: Write the implementation**

`DomainDiscovery/DiscoveryOutcome.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery;

/// <summary>
/// What a discovery source produced, and which source produced it.
/// </summary>
/// <remarks>
/// Deliberately NOT <see cref="DiscoveryResult"/>. That type carries a <see cref="TopicResult"/>
/// whose <c>IsRecognized</c> means "has a Wikipedia article URL" — a question the LLM path can
/// never answer yes to, which would make every successful discovery report itself as a failure.
/// <para>
/// <see cref="SourceLabel"/> is here rather than inferred by the caller because the Discover panel
/// must state which source produced the classes, on success and on fallback alike.
/// </para>
/// </remarks>
public sealed record DiscoveryOutcome(
    CandidateDomainModel Model,
    string SourceLabel,
    bool Succeeded,
    string? Message);
```

`DomainDiscovery/IDomainModelSource.cs`:

```csharp
namespace DomainDiscovery;

/// <summary>Where the Discover stage gets a candidate class model from.</summary>
public interface IDomainModelSource
{
    /// <summary>
    /// Builds a candidate model from the user's description. Never throws: a source that cannot
    /// answer returns an outcome with <c>Succeeded</c> false and a message saying why, because
    /// the caller's job is to fall back, not to catch.
    /// </summary>
    DiscoveryOutcome Discover(string description);
}
```

`DomainDiscovery/WikipediaDomainModelSource.cs`:

```csharp
namespace DomainDiscovery;

/// <summary>The original path — NLE seeds, a Wikipedia article, prose mining — behind the
/// <see cref="IDomainModelSource"/> seam. Behaviour is unchanged; only the return shape is.</summary>
public sealed class WikipediaDomainModelSource : IDomainModelSource
{
    public const string Label = "Wikipedia";

    private readonly DomainDiscoveryPipeline _pipeline;

    public WikipediaDomainModelSource(DomainDiscoveryPipeline pipeline) => _pipeline = pipeline;

    public DiscoveryOutcome Discover(string description)
    {
        var result = _pipeline.Discover(description);

        return new DiscoveryOutcome(
            result.Model,
            Label,
            result.Topic.IsRecognized,
            result.Topic.IsRecognized
                ? null
                : "No topic recognized online — showing seed classes only. "
                  + "You can add classes by hand in the review.");
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WikipediaDomainModelSourceTests"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/IDomainModelSource.cs DomainDiscovery/DiscoveryOutcome.cs DomainDiscovery/WikipediaDomainModelSource.cs DomainDiscovery.Tests/WikipediaDomainModelSourceTests.cs
git commit -m "Add IDomainModelSource seam with the Wikipedia path behind it"
```

---

### Task 2: `DiscoveryConfig`

**Files:**
- Create: `DomainDiscovery/DiscoveryConfig.cs`
- Test: `DomainDiscovery.Tests/DiscoveryConfigTests.cs`

**Interfaces:**
- Produces: `DiscoveryConfig` with settable `Source`, `Model`, `Endpoint`, `TimeoutMinutes`; constants `OllamaSource`, `WikipediaSource`, `DefaultModel`, `DefaultEndpoint`, `DefaultTimeoutMinutes`; `DiscoveryConfig.Load(string? path = null) -> DiscoveryConfig`; `DiscoveryConfig.DefaultPath`.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery;
using Xunit;

namespace DomainDiscovery.Tests;

public class DiscoveryConfigTests
{
    [Fact]
    public void DefaultsToOllamaWhenThereIsNoFile()
    {
        var config = DiscoveryConfig.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        Assert.Equal(DiscoveryConfig.OllamaSource, config.Source);
        Assert.Equal(DiscoveryConfig.DefaultModel, config.Model);
        Assert.Equal(DiscoveryConfig.DefaultEndpoint, config.Endpoint);
        Assert.Equal(DiscoveryConfig.DefaultTimeoutMinutes, config.TimeoutMinutes);
    }

    [Fact]
    public void ReadsTheFileWhenItIsThere()
    {
        var path = Path.Combine(Path.GetTempPath(), $"discovery-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Source":"Wikipedia","Model":"llama3.2:3b","TimeoutMinutes":3}""");

        var config = DiscoveryConfig.Load(path);

        Assert.Equal("Wikipedia", config.Source);
        Assert.Equal("llama3.2:3b", config.Model);
        Assert.Equal(3, config.TimeoutMinutes);
        Assert.Equal(DiscoveryConfig.DefaultEndpoint, config.Endpoint);

        File.Delete(path);
    }

    [Fact]
    public void YieldsDefaultsRatherThanThrowingOnBrokenJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not json");

        var config = DiscoveryConfig.Load(path);

        Assert.Equal(DiscoveryConfig.OllamaSource, config.Source);

        File.Delete(path);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DiscoveryConfigTests"`
Expected: FAIL — `DiscoveryConfig` does not exist (CS0246).

- [ ] **Step 3: Write the implementation**

`DomainDiscovery/DiscoveryConfig.cs`:

```csharp
using System.Text.Json;

namespace DomainDiscovery;

/// <summary>
/// discovery.json, beside the binary — the same place and pattern <c>AiConfig</c> already uses.
/// </summary>
/// <remarks>
/// Never throws. A missing or unreadable file yields defaults, because a broken config must
/// leave the tool working rather than break the stage it configures.
/// </remarks>
public sealed class DiscoveryConfig
{
    public const string OllamaSource = "Ollama";
    public const string WikipediaSource = "Wikipedia";

    /// <summary>Chosen over llama3.2:3b because it emitted valid C# identifiers and type names
    /// where llama3.2 emitted <c>customer_id</c> and <c>datetime</c>, leaving the normalizer
    /// less to repair.</summary>
    public const string DefaultModel = "qwen2.5:3b";

    public const string DefaultEndpoint = "http://localhost:11434";

    /// <summary>Ten minutes, not HttpClient's 100 seconds. A small domain measured 64-72s on a
    /// CPU-only machine, so the framework default would kill ordinary requests.</summary>
    public const int DefaultTimeoutMinutes = 10;

    public string Source { get; set; } = OllamaSource;
    public string Model { get; set; } = DefaultModel;
    public string Endpoint { get; set; } = DefaultEndpoint;
    public int TimeoutMinutes { get; set; } = DefaultTimeoutMinutes;

    public static string DefaultPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discovery.json");

    public static DiscoveryConfig Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
                return new DiscoveryConfig();

            return JsonSerializer.Deserialize<DiscoveryConfig>(
                       File.ReadAllText(path),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new DiscoveryConfig();
        }
        catch (Exception)
        {
            return new DiscoveryConfig();
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DiscoveryConfigTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/DiscoveryConfig.cs DomainDiscovery.Tests/DiscoveryConfigTests.cs
git commit -m "Add DiscoveryConfig: source, model, endpoint and timeout from discovery.json"
```

---

### Task 3: New `Provenance` and `DecisionStage` members

**Files:**
- Modify: `DomainDiscovery/Model/CandidateDomainModel.cs` (the `Provenance` enum)
- Modify: `DomainDiscovery/Model/DecisionRecord.cs` (the `DecisionStage` enum)
- Modify: `DomainDiscovery/Model/DecisionReasons.cs`
- Modify: `CodeGenerator/App/DecisionReport.cs:186-192` (the `Label` switch)
- Test: `DomainDiscovery.Tests/Model/ModelProvenanceTests.cs`

**Interfaces:**
- Produces: `Provenance.Model`, `DecisionStage.ModelProposal`, `DecisionReasons.ProposedByTheModel`, `DecisionReasons.TheModelGaveNoUsableName`.

Both enums are safe to extend: `DecisionReport.Label` already has a `_ =>` default arm, and `BuildSnapshot` round-trips provenance through `Enum.TryParse` with a fallback. `ReviewStage.razor:35` renders `@entity.Provenance` directly, so the new member appears on screen as "Model" — which is the honest label, and the reason for not reusing `Provenance.Wiki`.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class ModelProvenanceTests
{
    [Fact]
    public void ProvenanceHasAMemberForAnLlmProposal()
        => Assert.Equal("Model", Provenance.Model.ToString());

    [Fact]
    public void DecisionStageHasAMemberForAnLlmProposal()
        => Assert.Equal("ModelProposal", DecisionStage.ModelProposal.ToString());

    [Fact]
    public void ProvenanceModelRoundTripsThroughItsName()
    {
        Assert.True(Enum.TryParse<Provenance>(Provenance.Model.ToString(), out var parsed));
        Assert.Equal(Provenance.Model, parsed);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ModelProvenanceTests"`
Expected: FAIL — `Provenance.Model` and `DecisionStage.ModelProposal` do not exist (CS0117).

- [ ] **Step 3: Write the implementation**

In `DomainDiscovery/Model/CandidateDomainModel.cs`, add to the `Provenance` enum after `Data`:

```csharp
    /// <summary>
    /// Proposed by a language model asked directly for a data model. Distinct from
    /// <see cref="Wiki"/> because the review page prints this verbatim, and calling a model's
    /// proposal "Wiki" would name a source that was never read.
    /// </summary>
    Model
```

In `DomainDiscovery/Model/DecisionRecord.cs`, add to the `DecisionStage` enum after `Normalization`:

```csharp
    /// <summary>
    /// Asking a language model for the domain's classes outright. Distinct from
    /// <see cref="Enrichment"/>, which mines candidates out of an article's prose.
    /// </summary>
    ModelProposal,
```

In `DomainDiscovery/Model/DecisionReasons.cs`, add a new section before `// --- Review loop ---`:

```csharp
    // --- Model proposal ---
    public const string ProposedByTheModel = "proposed by the model";
    public const string TheModelGaveNoUsableName = "the model gave it no usable class name";
```

In `CodeGenerator/App/DecisionReport.cs`, add an arm to the `Label` switch before the `_ =>` default:

```csharp
        DecisionStage.ModelProposal => "model proposal",
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ModelProvenanceTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Run the whole suite to prove the new enum members broke nothing**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS, no new failures. Record the total count for comparison in later tasks.

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery/Model/CandidateDomainModel.cs DomainDiscovery/Model/DecisionRecord.cs DomainDiscovery/Model/DecisionReasons.cs CodeGenerator/App/DecisionReport.cs DomainDiscovery.Tests/Model/ModelProvenanceTests.cs
git commit -m "Name a model proposal as its own provenance, stage and reasons"
```

---

### Task 4: The Ollama client

**Files:**
- Create: `DomainDiscovery/Llm/LlmDomainModel.cs`
- Create: `DomainDiscovery/Llm/IOllamaClient.cs`
- Create: `DomainDiscovery/Llm/OllamaClient.cs`
- Test: `DomainDiscovery.Tests/Llm/OllamaClientTests.cs`

**Interfaces:**
- Consumes: `DiscoveryConfig` (Task 2).
- Produces: `LlmDomainModel { List<LlmClass> Classes }`, `LlmClass { string Name, Role, Purpose; List<LlmProperty> Properties; List<LlmRelationship> Relationships }`, `LlmProperty { string Name, Type }`, `LlmRelationship { string Target, Role }`; `IOllamaClient { bool IsAvailable(); LlmDomainModel? Ask(string description); }`; `OllamaClient(DiscoveryConfig config, HttpClient? http = null)`.

The request shape below is the one verified working against both installed 3B models: `POST /api/chat`, `stream:false`, `temperature:0`, and a JSON schema in `format`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Text;
using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>Answers every request with a canned body, so the client is testable with no Ollama.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }

    public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;

        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json")
        };
    }
}

public class OllamaClientTests
{
    private const string ChatResponse = """
        {"message":{"content":"{\"classes\":[{\"name\":\"Customer\",\"role\":\"Party\",\"purpose\":\"someone who buys\",\"properties\":[{\"name\":\"Name\",\"type\":\"string\"}],\"relationships\":[]}]}"}}
        """;

    [Fact]
    public void ParsesTheModelsJsonOutOfTheChatEnvelope()
    {
        var client = new OllamaClient(new DiscoveryConfig(), new HttpClient(new StubHandler(ChatResponse)));

        var model = client.Ask("a shop");

        Assert.NotNull(model);
        var only = Assert.Single(model!.Classes);
        Assert.Equal("Customer", only.Name);
        Assert.Equal("Party", only.Role);
        Assert.Equal("someone who buys", only.Purpose);
        Assert.Equal("Name", Assert.Single(only.Properties).Name);
    }

    [Fact]
    public void AsksTheConfiguredModelAtTheConfiguredEndpoint()
    {
        var handler = new StubHandler(ChatResponse);
        var config = new DiscoveryConfig { Model = "llama3.2:3b", Endpoint = "http://localhost:11434" };

        new OllamaClient(config, new HttpClient(handler)).Ask("a shop");

        Assert.Equal("http://localhost:11434/api/chat", handler.LastRequestUri!.ToString());
        Assert.Contains("llama3.2:3b", handler.LastRequestBody!);
        Assert.Contains("\"stream\":false", handler.LastRequestBody!);
        Assert.Contains("\"format\"", handler.LastRequestBody!);
    }

    [Fact]
    public void ReturnsNullRatherThanThrowingWhenOllamaErrors()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("nope", HttpStatusCode.InternalServerError)));

        Assert.Null(client.Ask("a shop"));
    }

    [Fact]
    public void ReturnsNullRatherThanThrowingOnUnparseableContent()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("""{"message":{"content":"not json"}}""")));

        Assert.Null(client.Ask("a shop"));
    }

    [Fact]
    public void IsAvailableWhenTagsAnswers()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("""{"models":[{"name":"qwen2.5:3b"}]}""")));

        Assert.True(client.IsAvailable());
    }

    [Fact]
    public void IsNotAvailableWhenTagsFails()
    {
        var client = new OllamaClient(
            new DiscoveryConfig(), new HttpClient(new StubHandler("", HttpStatusCode.ServiceUnavailable)));

        Assert.False(client.IsAvailable());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~OllamaClientTests"`
Expected: FAIL — `OllamaClient` and `LlmDomainModel` do not exist (CS0246).

- [ ] **Step 3: Write the DTOs**

`DomainDiscovery/Llm/LlmDomainModel.cs`:

```csharp
using System.Text.Json.Serialization;

namespace DomainDiscovery.Llm;

/// <summary>One property as the model described it — before any repair. See
/// <see cref="LlmModelNormalizer"/> for why nothing here is trusted as-is.</summary>
public sealed class LlmProperty
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
}

public sealed class LlmRelationship
{
    [JsonPropertyName("target")] public string Target { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
}

public sealed class LlmClass
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>The model's guess at an <c>EntityRole</c> name. Parsed leniently; an
    /// unrecognized value becomes <c>EntityRole.None</c>.</summary>
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;

    /// <summary>One line saying what the class is for. Becomes the evidence on the class's
    /// <c>DecisionRecord</c>, so <c>why Customer</c> has a real answer on this path.</summary>
    [JsonPropertyName("purpose")] public string Purpose { get; set; } = string.Empty;

    [JsonPropertyName("properties")] public List<LlmProperty> Properties { get; set; } = new();
    [JsonPropertyName("relationships")] public List<LlmRelationship> Relationships { get; set; } = new();
}

public sealed class LlmDomainModel
{
    [JsonPropertyName("classes")] public List<LlmClass> Classes { get; set; } = new();
}
```

`DomainDiscovery/Llm/IOllamaClient.cs`:

```csharp
namespace DomainDiscovery.Llm;

/// <summary>Talks to a local Ollama server. Never throws — see <see cref="Ask"/>.</summary>
public interface IOllamaClient
{
    /// <summary>Whether the server answers at all. A millisecond call, used to decide whether
    /// to run this source or fall back.</summary>
    bool IsAvailable();

    /// <summary>The model's proposed domain, or null when the call failed or its output could
    /// not be parsed. Null rather than an exception because the caller's job is to fall back.</summary>
    LlmDomainModel? Ask(string description);
}
```

- [ ] **Step 4: Write the client**

`DomainDiscovery/Llm/OllamaClient.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace DomainDiscovery.Llm;

/// <summary>
/// The local Ollama server over its HTTP API.
/// </summary>
/// <remarks>
/// Uses schema-constrained output (<c>format</c>), which Ollama applies during decoding — that is
/// what makes a 3B model's JSON reliably parseable. <c>temperature:0</c> for as much run-to-run
/// stability as a sampled model can offer.
/// </remarks>
public sealed class OllamaClient : IOllamaClient
{
    private const string SystemPrompt =
        "You are a domain modelling assistant. Given a description of a business system, return "
        + "the data model: the classes, their properties with C# types, and the relationships "
        + "between classes. Each class must carry a role, one of: Party (a person or organisation), "
        + "Device, Place, Item (a thing bought or held), Document (a record of something that "
        + "happened), Transaction (value changing hands), Event (something scheduled). "
        + "Return only JSON matching the schema.";

    private readonly DiscoveryConfig _config;
    private readonly HttpClient _http;

    public OllamaClient(DiscoveryConfig config, HttpClient? http = null)
    {
        _config = config;

        // The framework's 100-second default would kill an ordinary request: a small domain
        // measured 64-72 seconds on a CPU-only machine.
        _http = http ?? new HttpClient();

        if (http is null)
            _http.Timeout = TimeSpan.FromMinutes(Math.Max(1, config.TimeoutMinutes));
    }

    public bool IsAvailable()
    {
        try
        {
            using var response = _http.GetAsync($"{_config.Endpoint}/api/tags").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public LlmDomainModel? Ask(string description)
    {
        try
        {
            var request = new
            {
                model = _config.Model,
                stream = false,
                options = new { temperature = 0 },
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = description }
                },
                format = Schema()
            };

            var body = new StringContent(
                JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            using var response = _http
                .PostAsync($"{_config.Endpoint}/api/chat", body)
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return null;

            var envelope = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            using var document = JsonDocument.Parse(envelope);

            var content = document.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return null;

            return JsonSerializer.Deserialize<LlmDomainModel>(content);
        }
        catch (Exception)
        {
            // Unreachable server, timeout, or output the schema did not in fact constrain.
            // Null is the contract; the caller falls back.
            return null;
        }
    }

    /// <summary>The JSON schema Ollama constrains decoding to.</summary>
    private static object Schema() => new
    {
        type = "object",
        properties = new
        {
            classes = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        role = new { type = "string" },
                        purpose = new { type = "string" },
                        properties = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    type = new { type = "string" }
                                },
                                required = new[] { "name", "type" }
                            }
                        },
                        relationships = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    target = new { type = "string" },
                                    role = new { type = "string" }
                                },
                                required = new[] { "target", "role" }
                            }
                        }
                    },
                    required = new[] { "name", "role", "properties" }
                }
            }
        },
        required = new[] { "classes" }
    };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~OllamaClientTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery/Llm/ DomainDiscovery.Tests/Llm/OllamaClientTests.cs
git commit -m "Add OllamaClient: schema-constrained chat, ten-minute timeout, null on failure"
```

---

### Task 5: `LlmModelNormalizer`

**Files:**
- Create: `DomainDiscovery/Llm/LlmModelNormalizer.cs`
- Test: `DomainDiscovery.Tests/Llm/LlmModelNormalizerTests.cs`

**Interfaces:**
- Consumes: `LlmDomainModel` (Task 4), `Provenance.Model` / `DecisionStage.ModelProposal` / `DecisionReasons.ProposedByTheModel` / `DecisionReasons.TheModelGaveNoUsableName` (Task 3), existing `NameNormalizer.ToUserTypedEntityName`, `NameNormalizer.ToFieldName`, `NameNormalizer.IsValidIdentifier`, `EntityRole`, `EntityRoleRank.CanReference`, `CandidateDomainModel`.
- Produces: `LlmModelNormalizer.Normalize(LlmDomainModel raw) -> CandidateDomainModel`.

This is the deterministic heart of the feature and carries the real test coverage. Every repair below is driven by measured output: `llama3.2:3b` emitted `customer_id`, `order_date` and the type `datetime`; `qwen2.5:3b` emitted a `SaleItem` with no key.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

public class LlmModelNormalizerTests
{
    private static LlmClass Class(string name, string role, params LlmProperty[] properties)
        => new() { Name = name, Role = role, Purpose = "a thing", Properties = properties.ToList() };

    private static LlmProperty Property(string name, string type)
        => new() { Name = name, Type = type };

    [Fact]
    public void PascalCasesPropertyNamesTheModelWroteInSnakeCase()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("Order", "Transaction", Property("order_date", "datetime")) }
        };

        var model = LlmModelNormalizer.Normalize(raw);

        var order = model.Entities.Single(e => e.Name == "Order");
        Assert.Contains(order.Fields, f => f.Name == "OrderDate");
    }

    [Fact]
    public void MapsModelTypeNamesOntoRealCsharpTypes()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("Order", "Transaction",
                Property("Placed", "datetime"),
                Property("Total", "money"),
                Property("Note", "text")) }
        };

        var order = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Order");

        Assert.Equal("DateTime", order.Fields.Single(f => f.Name == "Placed").Type);
        Assert.Equal("decimal", order.Fields.Single(f => f.Name == "Total").Type);
        Assert.Equal("string", order.Fields.Single(f => f.Name == "Note").Type);
    }

    [Fact]
    public void AddsAKeyToAClassTheModelGaveNone()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("SaleItem", "Document", Property("Quantity", "int")) }
        };

        var item = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "SaleItem");

        var id = item.Fields.Single(f => f.Name == "Id");
        Assert.Equal("int", id.Type);
    }

    [Fact]
    public void TurnsAForeignKeyColumnIntoARelationship()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Order", "Transaction", Property("customer_id", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var order = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Order");

        Assert.Contains(order.Relationships, r => r.Target == "Customer");
        Assert.DoesNotContain(order.Fields, f => f.Name == "CustomerId");
    }

    /// <summary>
    /// The defect EntityRoleRank.CanReference exists to stop. A real run once produced
    /// Restaurant.CustomerId; a 3B model proposes exactly the same thing, and the rule must
    /// still hold on this path.
    /// </summary>
    [Fact]
    public void RefusesAForeignKeyOnAClassThatMayNotCarryOne()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Restaurant", "Place", Property("customer_id", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var restaurant = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Restaurant");

        Assert.DoesNotContain(restaurant.Relationships, r => r.Target == "Customer");
    }

    /// <summary>A refused relationship must not silently delete the property it came from.</summary>
    [Fact]
    public void KeepsARefusedForeignKeyAsAPlainScalar()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Restaurant", "Place", Property("customer_id", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var restaurant = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Restaurant");

        Assert.Contains(restaurant.Fields, f => f.Name == "CustomerId" && f.Type == "int");
    }

    /// <summary>
    /// A class the model returned with no properties would otherwise scaffold a grid holding
    /// nothing but a read-only Id. EntityShaper, DataModelBuilder and ModelEditor all guarantee
    /// against that on their own paths; this path bypasses EntityShaper, so it must do it here.
    /// </summary>
    [Fact]
    public void GivesAnEditableFieldToAClassTheModelLeftEmpty()
    {
        var raw = new LlmDomainModel { Classes = { Class("Cash", "None") } };

        var entity = LlmModelNormalizer.Normalize(raw).Entities.Single();

        Assert.Contains(entity.Fields, f => f.Name == DefaultFields.NameField);
    }

    /// <summary>A class the model DID give fields to keeps exactly those — an Order has no Name.</summary>
    [Fact]
    public void DoesNotInventANameFieldForAClassThatAlreadyHasFields()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("Order", "Transaction", Property("Total", "decimal")) }
        };

        var order = LlmModelNormalizer.Normalize(raw).Entities.Single();

        Assert.DoesNotContain(order.Fields, f => f.Name == DefaultFields.NameField);
    }

    /// <summary>
    /// Both measured models name compound classes SaleItem and OrderItem. ToEntityName would
    /// reduce those to "Saleitem" and "Orderitem" — a class name no user typed and no scaffolder
    /// should emit. This is why the normalizer uses ToUserTypedEntityName.
    /// </summary>
    [Fact]
    public void KeepsACompoundClassNameAsTheModelWroteIt()
    {
        var raw = new LlmDomainModel
        {
            Classes = { Class("SaleItem", "Document", Property("Quantity", "int")) }
        };

        Assert.Equal("SaleItem", LlmModelNormalizer.Normalize(raw).Entities.Single().Name);
    }

    /// <summary>
    /// The default model writes SaleID, not SaleId. Case-sensitive suffix checks would miss
    /// every foreign key it produces and then add a duplicate key beside the one it gave.
    /// </summary>
    [Fact]
    public void RecognizesTheAllCapsIdSpellingTheDefaultModelWrites()
    {
        var raw = new LlmDomainModel
        {
            Classes =
            {
                Class("Sale", "Transaction", Property("SaleID", "int"), Property("CustomerID", "int")),
                Class("Customer", "Party", Property("Name", "string"))
            }
        };

        var sale = LlmModelNormalizer.Normalize(raw).Entities.Single(e => e.Name == "Sale");

        // Its own key was kept, and no second one was invented beside it.
        Assert.Contains(sale.Fields, f => f.Name == "SaleID");
        Assert.DoesNotContain(sale.Fields, f => f.Name == "Id");

        // The reference to Customer became a relationship despite the all-caps spelling.
        Assert.Contains(sale.Relationships, r => r.Target == "Customer");
    }

    [Fact]
    public void DropsAClassWhoseNameCouldNeverCompile()
    {
        var raw = new LlmDomainModel { Classes = { Class("2nd course", "Item", Property("Name", "string")) } };

        var model = LlmModelNormalizer.Normalize(raw);

        Assert.Empty(model.Entities);
        Assert.Contains(model.Rejected, r => r.Contains("2nd course"));
    }

    [Fact]
    public void RecordsWhyEachClassIsThereSoWhyCanAnswer()
    {
        var raw = new LlmDomainModel { Classes = { Class("Customer", "Party", Property("Name", "string")) } };
        raw.Classes[0].Purpose = "someone who buys things";

        var model = LlmModelNormalizer.Normalize(raw);

        var decision = model.Decisions.Single(d => d.Name == "Customer");
        Assert.Equal(DecisionStage.ModelProposal, decision.Stage);
        Assert.Equal(DecisionOutcome.Kept, decision.Outcome);
        Assert.Equal(DecisionReasons.ProposedByTheModel, decision.Reason);
        Assert.Equal("someone who buys things", decision.Evidence);
    }

    [Fact]
    public void MarksEveryClassAsComingFromTheModel()
    {
        var raw = new LlmDomainModel { Classes = { Class("Customer", "Party", Property("Name", "string")) } };

        var customer = LlmModelNormalizer.Normalize(raw).Entities.Single();

        Assert.Equal(Provenance.Model, customer.Provenance);
        Assert.Equal(EntityRole.Party, customer.Role);
    }

    [Fact]
    public void FallsBackToNoRoleWhenTheModelInventsOne()
    {
        var raw = new LlmDomainModel { Classes = { Class("Widget", "Doohickey", Property("Name", "string")) } };

        Assert.Equal(EntityRole.None, LlmModelNormalizer.Normalize(raw).Entities.Single().Role);
    }

    [Fact]
    public void DropsARelationshipToAClassThatIsNotInTheModel()
    {
        var raw = new LlmDomainModel { Classes = { Class("Order", "Transaction", Property("Total", "decimal")) } };
        raw.Classes[0].Relationships.Add(new LlmRelationship { Target = "Ghost", Role = "has" });

        Assert.Empty(LlmModelNormalizer.Normalize(raw).Entities.Single().Relationships);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~LlmModelNormalizerTests"`
Expected: FAIL — `LlmModelNormalizer` does not exist (CS0246).

- [ ] **Step 3: Write the implementation**

`DomainDiscovery/Llm/LlmModelNormalizer.cs`:

```csharp
using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Llm;

/// <summary>
/// Turns what a language model said into something the rest of the tool can compile.
/// </summary>
/// <remarks>
/// Nothing the model returns is trusted. Measured on the two installed 3B models:
/// llama3.2:3b emitted <c>customer_id</c>, <c>order_date</c> and the type <c>datetime</c>;
/// qwen2.5:3b emitted a <c>SaleItem</c> with no key at all.
/// <para>
/// This class is deterministic, which is the point: the LLM call cannot carry test coverage,
/// so every rule worth asserting lives here instead.
/// </para>
/// </remarks>
public static class LlmModelNormalizer
{
    public static CandidateDomainModel Normalize(LlmDomainModel raw)
    {
        var model = new CandidateDomainModel();

        // Pass 1: names and roles. Relationships need every class's role before any of them can
        // be judged, so no relationship can be settled until this pass has finished.
        var accepted = new List<(LlmClass Raw, CandidateEntity Entity)>();

        foreach (var llmClass in raw.Classes)
        {
            // ToUserTypedEntityName, NOT ToEntityName. ToEntityName reduces a phrase to its head
            // noun and lowercases everything after the first character, which turns SaleItem into
            // "Saleitem" and OrderItem into "Orderitem" — and those are the exact class names both
            // measured models produce. This one preserves a compound the way it was written, which
            // is what its own summary says it exists for. It still rejects "2nd course".
            var name = NameNormalizer.ToUserTypedEntityName(llmClass.Name);

            if (!NameNormalizer.IsValidIdentifier(name))
            {
                model.Rejected.Add(llmClass.Name);
                model.Record(
                    llmClass.Name,
                    DecisionStage.ModelProposal,
                    DecisionOutcome.Rejected,
                    DecisionReasons.TheModelGaveNoUsableName);
                continue;
            }

            if (accepted.Any(a => a.Entity.Name == name))
                continue;

            var entity = new CandidateEntity
            {
                Name = name,
                Provenance = Provenance.Model,
                Confidence = 1d,
                Role = ParseRole(llmClass.Role)
            };

            model.Record(
                name,
                DecisionStage.ModelProposal,
                DecisionOutcome.Kept,
                DecisionReasons.ProposedByTheModel,
                string.IsNullOrWhiteSpace(llmClass.Purpose) ? null : llmClass.Purpose.Trim());

            accepted.Add((llmClass, entity));
        }

        var byName = accepted.ToDictionary(a => a.Entity.Name, a => a.Entity);

        // Pass 2: properties and relationships, now that every role is known.
        foreach (var (llmClass, entity) in accepted)
        {
            foreach (var property in llmClass.Properties)
                AddProperty(entity, property, byName);

            foreach (var relationship in llmClass.Relationships)
            {
                // The same normalizer the class names went through, or the lookup misses.
                var target = NameNormalizer.ToUserTypedEntityName(relationship.Target);

                if (target == entity.Name || !byName.TryGetValue(target, out var targetEntity))
                    continue;

                Link(entity, targetEntity, string.IsNullOrWhiteSpace(relationship.Role)
                    ? "has"
                    : relationship.Role.Trim());
            }

            EnsureKey(entity);

            // Every other path into CandidateDomainModel guarantees an editable grid:
            // EntityShaper does it for a discovered class, DataModelBuilder for an imported one,
            // ModelEditor for one the user adds by hand. This path bypasses EntityShaper, so it
            // is the only one that could hand scaffolding a class whose grid shows nothing but a
            // read-only Id — exactly what DefaultFields was written to prevent. Applied only when
            // the model actually left the class empty; a class with real fields keeps them alone,
            // because an Order legitimately has no Name.
            if (!entity.Fields.Any(f => !IsKeyOf(f.Name, entity.Name)))
                DefaultFields.ApplyTo(entity, Provenance.Model);

            model.Entities.Add(entity);
        }

        return model;
    }

    /// <summary>
    /// Adds one property — unless it is a foreign key naming another class, in which case it
    /// becomes a relationship instead of a stray int sitting beside one that means the same thing.
    /// </summary>
    private static void AddProperty(
        CandidateEntity entity,
        LlmProperty property,
        IReadOnlyDictionary<string, CandidateEntity> byName)
    {
        var name = NameNormalizer.ToFieldName(property.Name);

        if (!NameNormalizer.IsValidIdentifier(name) || name == entity.Name)
            return;

        if (entity.Fields.Any(f => f.Name == name))
            return;

        if (TryForeignKeyTarget(name, byName, out var target)
            && target.Name != entity.Name
            && Link(entity, target, "has"))
        {
            // Became a relationship; the scalar would now be a duplicate of it.
            return;
        }

        entity.Fields.Add(new CandidateField
        {
            Name = name,
            Type = ToCsharpType(property.Type),
            Provenance = Provenance.Model
        });
    }

    /// <summary>
    /// Whether <paramref name="fieldName"/> is a <c>CustomerId</c>-shaped reference to a class
    /// the model also defined. "Id" alone is a key, not a reference.
    /// </summary>
    private static bool TryForeignKeyTarget(
        string fieldName,
        IReadOnlyDictionary<string, CandidateEntity> byName,
        out CandidateEntity target)
    {
        target = null!;

        // OrdinalIgnoreCase, not Ordinal: the DEFAULT model writes SaleID and ProductID, not
        // SaleId. An exact-case check would miss every foreign key qwen2.5:3b produces.
        if (!fieldName.EndsWith("id", StringComparison.OrdinalIgnoreCase) || fieldName.Length <= 2)
            return false;

        return byName.TryGetValue(fieldName[..^2], out target!);
    }

    /// <summary>
    /// Whether <paramref name="fieldName"/> is this class's own key. Both spellings a model
    /// actually produces count: a bare "Id", and the "SaleID" form qwen2.5:3b writes on class Sale.
    /// </summary>
    private static bool IsKeyOf(string fieldName, string entityName)
        => fieldName.Equals("Id", StringComparison.OrdinalIgnoreCase)
           || fieldName.Equals($"{entityName}Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a relationship if — and only if — the existing role rules allow it.
    /// </summary>
    /// <remarks>
    /// <see cref="EntityRoleRank.CanReference"/> is the gate a real run needed: relying on rank
    /// alone once produced Restaurant.CustomerId and CashRegister.CustomerId. A language model
    /// proposes the same shapes, so the rule is applied to its output rather than replaced by it.
    /// </remarks>
    /// <returns>True when the relationship was added.</returns>
    private static bool Link(CandidateEntity from, CandidateEntity to, string role)
    {
        if (!EntityRoleRank.CanReference(from.Role))
            return false;

        if (EntityRoleRank.RankOf(from.Role) <= EntityRoleRank.RankOf(to.Role))
            return false;

        if (from.Relationships.Any(r => r.Target == to.Name))
            return false;

        from.Relationships.Add(new CandidateRelationship { Target = to.Name, Role = role });
        return true;
    }

    private static void EnsureKey(CandidateEntity entity)
    {
        // IsKeyOf, not f.Name == "Id": qwen2.5:3b names Sale's key "SaleID", and an exact match
        // would add a second key beside the one the model already gave us.
        if (entity.Fields.Any(f => IsKeyOf(f.Name, entity.Name)))
            return;

        entity.Fields.Insert(0, new CandidateField
        {
            Name = "Id",
            Type = "int",
            Provenance = Provenance.Model
        });
    }

    private static EntityRole ParseRole(string role)
        => Enum.TryParse<EntityRole>(role?.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : EntityRole.None;

    /// <summary>
    /// A real C# type for whatever the model called it. Anything unrecognized becomes
    /// <c>string</c> rather than an invented type name that would not compile.
    /// </summary>
    private static string ToCsharpType(string type) => (type ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "int" or "integer" or "int32" or "number" => "int",
        "long" or "int64" or "bigint" => "long",
        "decimal" or "money" or "currency" or "numeric" => "decimal",
        "double" or "float" or "real" => "double",
        "bool" or "boolean" or "bit" => "bool",
        "datetime" or "date" or "timestamp" or "datetime2" => "DateTime",
        "guid" or "uuid" or "uniqueidentifier" => "Guid",
        _ => "string"
    };
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~LlmModelNormalizerTests"`
Expected: PASS, 15 tests.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Llm/LlmModelNormalizer.cs DomainDiscovery.Tests/Llm/LlmModelNormalizerTests.cs
git commit -m "Normalize LLM output into a compilable model, with CanReference still in force"
```

---

### Task 6: `OllamaDomainModelSource`

**Files:**
- Create: `DomainDiscovery/Llm/OllamaDomainModelSource.cs`
- Test: `DomainDiscovery.Tests/Llm/OllamaDomainModelSourceTests.cs`

**Interfaces:**
- Consumes: `IOllamaClient` (Task 4), `LlmModelNormalizer.Normalize` (Task 5), `DiscoveryOutcome` (Task 1), `DiscoveryConfig` (Task 2).
- Produces: `OllamaDomainModelSource(IOllamaClient client, string modelName)` implementing `IDomainModelSource`.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

internal sealed class FakeOllamaClient : IOllamaClient
{
    public LlmDomainModel? Answer { get; set; }
    public bool Available { get; set; } = true;
    public string? LastDescription { get; private set; }

    public bool IsAvailable() => Available;

    public LlmDomainModel? Ask(string description)
    {
        LastDescription = description;
        return Answer;
    }
}

public class OllamaDomainModelSourceTests
{
    private static LlmDomainModel OneClass() => new()
    {
        Classes =
        {
            new LlmClass
            {
                Name = "Customer",
                Role = "Party",
                Purpose = "someone who buys",
                Properties = { new LlmProperty { Name = "Name", Type = "string" } }
            }
        }
    };

    [Fact]
    public void SucceedsAndLabelsTheModelThatAnswered()
    {
        var client = new FakeOllamaClient { Answer = OneClass() };

        var outcome = new OllamaDomainModelSource(client, "qwen2.5:3b").Discover("a shop");

        Assert.True(outcome.Succeeded);
        Assert.Equal("qwen2.5:3b (Ollama)", outcome.SourceLabel);
        Assert.Contains(outcome.Model.Entities, e => e.Name == "Customer");
    }

    [Fact]
    public void FailsWithAMessageWhenTheModelAnswersNothing()
    {
        var outcome = new OllamaDomainModelSource(new FakeOllamaClient { Answer = null }, "qwen2.5:3b")
            .Discover("a shop");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Message);
        Assert.Empty(outcome.Model.Entities);
    }

    [Fact]
    public void FailsWhenTheModelReturnsNoClassesAtAll()
    {
        var outcome = new OllamaDomainModelSource(
            new FakeOllamaClient { Answer = new LlmDomainModel() }, "qwen2.5:3b").Discover("a shop");

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public void PassesTheDescriptionStraightThrough()
    {
        var client = new FakeOllamaClient { Answer = OneClass() };

        new OllamaDomainModelSource(client, "qwen2.5:3b").Discover("a shop that sells bicycles");

        Assert.Equal("a shop that sells bicycles", client.LastDescription);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~OllamaDomainModelSourceTests"`
Expected: FAIL — `OllamaDomainModelSource` does not exist (CS0246).

- [ ] **Step 3: Write the implementation**

`DomainDiscovery/Llm/OllamaDomainModelSource.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery.Llm;

/// <summary>Asks a local model for the domain, then repairs what it says.</summary>
public sealed class OllamaDomainModelSource : IDomainModelSource
{
    private readonly IOllamaClient _client;
    private readonly string _modelName;

    public OllamaDomainModelSource(IOllamaClient client, string modelName)
    {
        _client = client;
        _modelName = modelName;
    }

    /// <summary>Named so the Discover panel can say which model produced the classes.</summary>
    public string Label => $"{_modelName} (Ollama)";

    public DiscoveryOutcome Discover(string description)
    {
        var raw = _client.Ask(description);

        if (raw is null)
            return new DiscoveryOutcome(
                new CandidateDomainModel(),
                Label,
                false,
                $"{_modelName} did not answer. Check that Ollama is running and the model is pulled.");

        var model = LlmModelNormalizer.Normalize(raw);

        if (model.Entities.Count == 0)
            return new DiscoveryOutcome(
                model,
                Label,
                false,
                $"{_modelName} proposed no usable classes for that description.");

        return new DiscoveryOutcome(model, Label, true, null);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~OllamaDomainModelSourceTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Llm/OllamaDomainModelSource.cs DomainDiscovery.Tests/Llm/OllamaDomainModelSourceTests.cs
git commit -m "Add OllamaDomainModelSource over the client and the normalizer"
```

---

### Task 7: `DomainModelSourceFactory` and its fallback

**Files:**
- Create: `DomainDiscovery/DomainModelSourceFactory.cs`
- Test: `DomainDiscovery.Tests/DomainModelSourceFactoryTests.cs`

**Interfaces:**
- Consumes: `DiscoveryConfig` (Task 2), `IOllamaClient` (Task 4), `OllamaDomainModelSource` (Task 6), `WikipediaDomainModelSource` (Task 1).
- Produces: `DomainModelSourceFactory.Create(DiscoveryConfig config, Func<IDomainModelSource> wikipedia, IOllamaClient? client = null) -> IDomainModelSource`.

`wikipedia` is a factory rather than an instance because building the Wikipedia pipeline opens the dump store, and the Ollama path must not pay that cost.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery;
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using DomainDiscovery.Tests.Llm;   // FakeOllamaClient, defined in Task 6's test file
using Xunit;

namespace DomainDiscovery.Tests;

internal sealed class StubSource : IDomainModelSource
{
    public DiscoveryOutcome Discover(string description)
        => new(new CandidateDomainModel(), "Wikipedia", true, null);
}

public class DomainModelSourceFactoryTests
{
    private static IDomainModelSource Wikipedia() => new StubSource();

    [Fact]
    public void UsesOllamaWhenItIsConfiguredAndAvailable()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), Wikipedia, new FakeOllamaClient { Available = true });

        Assert.IsType<OllamaDomainModelSource>(source);
    }

    [Fact]
    public void FallsBackToWikipediaWhenOllamaIsNotRunning()
    {
        var source = DomainModelSourceFactory.Create(
            new DiscoveryConfig(), Wikipedia, new FakeOllamaClient { Available = false });

        Assert.IsType<StubSource>(source);
    }

    [Fact]
    public void UsesWikipediaWhenItIsTheConfiguredSource()
    {
        var config = new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource };

        var source = DomainModelSourceFactory.Create(
            config, Wikipedia, new FakeOllamaClient { Available = true });

        Assert.IsType<StubSource>(source);
    }

    [Fact]
    public void NeverBuildsTheWikipediaPipelineWhenOllamaAnswers()
    {
        var built = 0;

        DomainModelSourceFactory.Create(
            new DiscoveryConfig(),
            () => { built++; return new StubSource(); },
            new FakeOllamaClient { Available = true });

        Assert.Equal(0, built);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainModelSourceFactoryTests"`
Expected: FAIL — `DomainModelSourceFactory` does not exist (CS0246).

- [ ] **Step 3: Write the implementation**

`DomainDiscovery/DomainModelSourceFactory.cs`:

```csharp
using DomainDiscovery.Llm;

namespace DomainDiscovery;

/// <summary>Chooses the discovery source, and falls back rather than failing.</summary>
public static class DomainModelSourceFactory
{
    /// <param name="wikipedia">
    /// A factory, not an instance: building the Wikipedia pipeline opens the dump store, and the
    /// Ollama path must not pay that cost merely to have a fallback available.
    /// </param>
    /// <param name="client">Injected by tests. Built from <paramref name="config"/> otherwise.</param>
    public static IDomainModelSource Create(
        DiscoveryConfig config,
        Func<IDomainModelSource> wikipedia,
        IOllamaClient? client = null)
    {
        if (!string.Equals(config.Source, DiscoveryConfig.OllamaSource, StringComparison.OrdinalIgnoreCase))
            return wikipedia();

        client ??= new OllamaClient(config);

        // A millisecond call against /api/tags. Asked before committing to a source, so an
        // unreachable Ollama costs one probe rather than a ten-minute timeout.
        return client.IsAvailable()
            ? new OllamaDomainModelSource(client, config.Model)
            : wikipedia();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainModelSourceFactoryTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/DomainModelSourceFactory.cs DomainDiscovery.Tests/DomainModelSourceFactoryTests.cs
git commit -m "Choose the discovery source, probing Ollama before committing to it"
```

---

### Task 8: Wire the Blazor Discover stage

**Files:**
- Modify: `CodeGenerator/Web/Workflow/DiscoveryStep.cs:104-118` (the `Discover` method)
- Modify: `CodeGenerator/Web/Workflow/BuildSession.cs:218` (`SetModel`)
- Modify: `CodeGenerator/Web/Components/Stages/DiscoverStage.razor:83-105`
- Modify: `CodeGenerator/Web/Components/Stages/ReviewStage.razor` (a source line above the entity list)
- Test: `CodeGenerator.Tests/Web/DiscoveryStepSourceTests.cs`

**Interfaces:**
- Consumes: `DomainModelSourceFactory.Create` (Task 7), `DiscoveryConfig` (Task 2), `DiscoveryOutcome` (Task 1).
- Produces: `DiscoveryStep.Discover(string description, string workspaceRoot) -> DiscoveryOutcome` — the return type changes from `(CandidateDomainModel Model, bool TopicRecognized)`; `BuildSession.ModelSource` (string); `BuildSession.SetModel(CandidateDomainModel, string? sourceLabel = null)`.

`CodeGenerator.Tests` is MSTest. Use `Assert.HasCount`, never `Assert.AreEqual(n, x.Count)` — MSTEST0037 is an error under `-warnaserror`.

> **Why this task does not just set `_label`.**
> `BuildWorkflow.razor` renders exactly ONE stage, chosen by a `switch` on the current stage. The moment `DiscoverStage` calls `Session.Advance()`, the page swaps to `ReviewStage` and the Discover panel is gone. **Anything assigned to `_label` after `_busy = false` is never rendered** — the existing "No topic recognized online" message on line 97 is already dead code that no user has ever seen. The spec requires the user be told which source produced their classes, so the label has to live somewhere that outlives the Discover stage: on the session, rendered by Review.
> `SetModel` has 25 call sites, so it gains an **optional** parameter rather than a changed signature — no existing caller breaks.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using CodeGenerator.Web.Workflow;
using DomainDiscovery;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// That the step returns a labelled outcome, and that the label survives to the stage that
/// actually renders it. The source itself is covered in DomainDiscovery.Tests.
/// </summary>
[TestClass]
public class DiscoveryStepSourceTests
{
    [TestMethod]
    public void DiscoverReturnsAnOutcomeCarryingItsSourceLabel()
    {
        var outcome = DiscoveryStep.Discover("a shop that sells bicycles", Path.GetTempPath());

        Assert.IsNotNull(outcome);
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.SourceLabel));
        Assert.IsNotNull(outcome.Model);
    }

    /// <summary>
    /// The Discover panel is torn down by Advance() the instant discovery finishes, so the source
    /// label cannot live on that component. It has to reach Review through the session.
    /// </summary>
    [TestMethod]
    public void TheSourceLabelSurvivesOnTheSession()
    {
        var session = new BuildSession(
            "a shop that sells bicycles", Path.GetTempPath(), AppCustomization.Default);

        session.SetModel(new CandidateDomainModel(), "qwen2.5:3b (Ollama)");

        Assert.AreEqual("qwen2.5:3b (Ollama)", session.ModelSource);
    }

    [TestMethod]
    public void ASetModelCallerThatNamesNoSourceLeavesItEmpty()
    {
        var session = new BuildSession(
            "a shop that sells bicycles", Path.GetTempPath(), AppCustomization.Default);

        session.SetModel(new CandidateDomainModel());

        Assert.AreEqual(string.Empty, session.ModelSource);
    }
}
```

`BuildSession`'s constructor is `(string description, string workspaceRoot, AppCustomization start, DataSourceSpec? source = null)` — the third argument has **no default**, so it must be passed. `AppCustomization.Default` lives in `AppScaffolding`. This matches the construction in `CodeGenerator.Tests/Web/BuildSessionTests.cs:47`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DiscoveryStepSourceTests"`
Expected: FAIL — `DiscoveryStep.Discover` returns a tuple, which has no `SourceLabel` (CS1061).

- [ ] **Step 3: Change `DiscoveryStep.Discover`**

Replace the `Discover` method in `CodeGenerator/Web/Workflow/DiscoveryStep.cs` with:

```csharp
    /// <summary>
    /// The description-driven path. The source is whatever <c>discovery.json</c> configures —
    /// a local model by default, the Wikipedia pipeline when Ollama is not running.
    /// </summary>
    /// <remarks>
    /// Slow either way, which is why the component runs it on a background task: a local model
    /// measured 64-72 seconds for a small domain, and a first Wikipedia fetch is no quicker.
    /// </remarks>
    public static DiscoveryOutcome Discover(string description, string workspaceRoot)
    {
        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");

        var source = DomainModelSourceFactory.Create(
            DiscoveryConfig.Load(),
            () => new WikipediaDomainModelSource(DomainDiscoveryPipeline.CreateDefault(cacheDir)));

        return source.Discover(description);
    }
```

- [ ] **Step 4: Carry the source label on the session**

In `CodeGenerator/Web/Workflow/BuildSession.cs`, add the property and give `SetModel` an **optional** second parameter, so none of its 25 existing callers change:

```csharp
    /// <summary>
    /// Which source produced <see cref="Model"/> — "qwen2.5:3b (Ollama)", "Wikipedia", or a
    /// fallback note. Empty when nothing said.
    /// </summary>
    /// <remarks>
    /// It lives here rather than on the Discover panel because that panel does not survive its
    /// own success: discovery ends with <see cref="Advance"/>, and BuildWorkflow renders one
    /// stage at a time, so the component is torn down before anything it set could be painted.
    /// Review is the first surface the user actually sees afterwards.
    /// </remarks>
    public string ModelSource { get; private set; } = string.Empty;

    public bool SetModel(CandidateDomainModel model, string? sourceLabel = null)
        => Change(BuildStage.Discover, changed: true, () =>
        {
            Model = model;
            ModelSource = sourceLabel ?? string.Empty;
        });
```

- [ ] **Step 5: Change the component to use it**

In `CodeGenerator/Web/Components/Stages/DiscoverStage.razor`, replace the block from `var discovered = await Task.Run(` through `await OnAdvance.InvokeAsync();` with:

```csharp
        var outcome = await Task.Run(
            () => DiscoveryStep.Discover(Session.Description, Session.WorkspaceRoot));

        _busy = false;
        StopClock();

        // Which source produced these classes, always — not only when something went wrong. A
        // customer whose Ollama service has stopped must be able to see that, rather than watch
        // the tool quietly get worse. It goes on the SESSION, not on _label: Advance() below tears
        // this panel down, so anything set on the component here is never painted.
        Session.SetModel(
            outcome.Model,
            outcome.Succeeded
                ? $"Classes from {outcome.SourceLabel}."
                : $"{outcome.Message} (Source: {outcome.SourceLabel})");

        Session.Advance();

        await OnAdvance.InvokeAsync();
```

Also change the busy label set just above `StartClock()` to name the wait honestly:

```csharp
        _label = "Discovering the domain — a local model can take a minute or two";
```

- [ ] **Step 6: Show it where the user will actually see it**

In `CodeGenerator/Web/Components/Stages/ReviewStage.razor`, inside `<div class="review">` and immediately **above** the `<RelationshipDiagram .../>` line, add:

```razor
    @* Where these classes came from. The Discover panel cannot say it — Advance() removes that
       panel the instant discovery finishes — so this is the first surface that can. *@
    @if (!string.IsNullOrWhiteSpace(Session.ModelSource))
    {
        <p class="model-source">@Session.ModelSource</p>
    }
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DiscoveryStepSourceTests"`
Expected: PASS, 3 tests.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS, **3786 tests plus the ones this task added**. That baseline was measured at Task 3. A total BELOW it means something was silently dropped — say so rather than reporting the number alone.

- [ ] **Step 9: Commit**

```bash
git add CodeGenerator/Web/Workflow/DiscoveryStep.cs CodeGenerator/Web/Workflow/BuildSession.cs CodeGenerator/Web/Components/Stages/DiscoverStage.razor CodeGenerator/Web/Components/Stages/ReviewStage.razor CodeGenerator.Tests/Web/DiscoveryStepSourceTests.cs
git commit -m "Discover stage picks its source, and Review says which one produced the classes"
```

---

### Task 9: Wire the console `build app` path

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs:268-292` (inside `HandleFromDescription`, which ends at line 294)

**Interfaces:**
- Consumes: `DomainModelSourceFactory.Create` (Task 7), `DiscoveryOutcome` (Task 1).
- Produces: no new API. The console prints the same information the web panel does.

- [ ] **Step 1: Replace the discovery block**

In `CodeGenerator/App/BuildAppCommand.cs`, replace from `var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDir);` down to the end of the `else` that prints "No topic recognized online" with:

```csharp
        var source = DomainModelSourceFactory.Create(
            DiscoveryConfig.Load(),
            () => new WikipediaDomainModelSource(DomainDiscoveryPipeline.CreateDefault(cacheDir)));

        // The longest silence in the tool. A local model measured 64-72 seconds for a small
        // domain, and a first Wikipedia fetch is no quicker; an unmoving line reads as hung.
        DiscoveryOutcome outcome;

        Console.WriteLine("\n");
        using (Spinner.Start("Discovering the domain (this can take a minute or two)"))
        {
            outcome = source.Discover(description);
        }

        // Which source answered, always — the command line says exactly what the web panel says.
        if (outcome.Succeeded)
            Console.WriteLine($"Classes from {outcome.SourceLabel}.");
        else
            Console.WriteLine($"{outcome.Message} (Source: {outcome.SourceLabel})");
```

- [ ] **Step 2: Fix the references to the old `result` variable**

Exactly one reference survives the replacement: the `RunReviewLoop(result.Model, ...)` call at line 292 (line numbers as they were before your edit). Change that one to `outcome.Model`.

**Do not do a blanket `result.` rename.** `BuildAppCommand.cs` contains a SECOND, unrelated `result` variable further down the file — around lines 1051 and 1074, `result.TimedOut` and `result.StandardOutput`, which belong to a process-run result and have nothing to do with discovery. Renaming those breaks the build.

Confirm with: `grep -n "result\.Model\|result\.Topic" CodeGenerator/App/BuildAppCommand.cs`
Expected after your change: no matches. Any remaining `result.Topic` use must be deleted — the LLM path has no topic, which is the whole reason `DiscoveryOutcome` exists.

For reference, the block you are replacing ends with this `else`, whose `if` half has a body that is **entirely commented out**, so deleting it loses no behaviour:

```csharp
        else
            Console.WriteLine("No topic recognized online — showing seed classes only. " +
                              "You can add classes by hand below.");
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`
Expected: Build succeeded, 0 errors. `--no-incremental` is required — analyzer errors do not resurface on an incremental build.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS, matching the Task 8 count.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs
git commit -m "Console build app uses the same discovery source as the web stage"
```

---

### Task 10: The one test that talks to real Ollama

**Files:**
- Create: `DomainDiscovery.Tests/Llm/OllamaIntegrationTests.cs`

**Interfaces:**
- Consumes: `OllamaClient`, `DiscoveryConfig`, `LlmModelNormalizer`.
- Produces: no new API.

This test must skip, not fail, when Ollama is absent — CI and other machines have no Ollama, and a red suite there would be a false alarm.

- [ ] **Step 1: Write the test**

```csharp
using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>
/// The only test that needs a running Ollama.
/// </summary>
/// <remarks>
/// Everything else on this path is covered by unit tests over a fake client; this is the only
/// thing that proves a real model's output survives normalization. It does nothing when Ollama
/// is not answering — CI has no Ollama installed, and a red suite there would be a false alarm.
/// <para>
/// A plain <c>[Fact]</c> that returns early rather than a skippable one, following
/// <c>DataIngestion.Tests.Schema.Readers.Sql.SqlServerIntegrationTests</c>: expressing the skip
/// properly would need Xunit.SkippableFact, and this repository has already declined to add that
/// package for exactly this situation. The cost is that a machine with no Ollama reports this
/// test as passed rather than skipped; the alternative is a new dependency for one test.
/// </para>
/// </remarks>
public class OllamaIntegrationTests
{
    private static bool OllamaIsRunning(DiscoveryConfig config)
        => new OllamaClient(config).IsAvailable();

    [Fact]
    public void ProposesAModelForAPointOfSaleSystem()
    {
        var config = new DiscoveryConfig();

        if (!OllamaIsRunning(config))
            return;   // no local model server — see the class remarks

        var outcome = new OllamaDomainModelSource(new OllamaClient(config), config.Model)
            .Discover("A point of sale system for a retail shop");

        Assert.True(outcome.Succeeded, outcome.Message);

        // Deliberately weak: the model is sampled, so asserting exact class names would make
        // this test flaky. What is asserted is that the normalizer produced something usable.
        Assert.NotEmpty(outcome.Model.Entities);

        foreach (var entity in outcome.Model.Entities)
        {
            Assert.True(
                DomainDiscovery.SeedExtraction.NameNormalizer.IsValidIdentifier(entity.Name),
                $"'{entity.Name}' would not compile as a class name.");

            // A KEY, not a field literally named "Id". The default model names Sale's key
            // "SaleID", and LlmModelNormalizer.EnsureKey deliberately accepts that rather than
            // adding a second one — so asserting the exact string here would fail against the
            // real model's actual output.
            Assert.Contains(entity.Fields, f =>
                f.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || f.Name.Equals($"{entity.Name}Id", StringComparison.OrdinalIgnoreCase));

            // The rule a 3B model reproduces the mistakes of: only a Document, Transaction or
            // Event may carry a foreign key.
            if (entity.Relationships.Count > 0)
                Assert.True(
                    DomainDiscovery.Enrichment.EntityRoleRank.CanReference(entity.Role),
                    $"'{entity.Name}' ({entity.Role}) must not carry a relationship.");
        }
    }
}
```

- [ ] **Step 2: Add no package**

`DomainDiscovery.Tests.csproj` is **not** modified by this task. The early-return pattern above needs nothing beyond xUnit, which is the whole reason for using it — see the class remarks.

- [ ] **Step 3: Run it with Ollama running**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~OllamaIntegrationTests"`
Expected: PASS. It may take one to three minutes — that is the model generating, not a hang.

- [ ] **Step 4: Prove it returns early when Ollama is absent**

Temporarily change the test's config line to `new DiscoveryConfig { Endpoint = "http://localhost:1" }` — a dead port — and run the filter again. Confirm it passes **in well under a second**, which is the observable proof it took the early return rather than talking to a model. Then revert that edit.

Do **not** stop the Ollama service to test this.

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~OllamaIntegrationTests"`
Expected: PASS, 0 failed, and a run time far below the one-to-three minutes a real model call takes.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery.Tests/Llm/OllamaIntegrationTests.cs
git commit -m "Add an integration test against a real local model, inert without one"
```

---

### Task 11: Verify against a running tool

Tests do not catch the defects that matter most here — this codebase's record is that a page can compile, pass every test, and still be wrong on screen. This task is looking at it.

**Files:**
- Modify: `docs/` manual section covering the Discover stage, if the run shows the wording is now wrong.

- [ ] **Step 1: Full clean build as CI runs it**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`
Expected: 0 errors, 0 warnings.

`--no-incremental` is not optional: without it MSBuild prints "Build succeeded" without re-running analyzers, so a `-warnaserror` check on an unchanged tree proves nothing.

- [ ] **Step 2: Full suite in BOTH configurations**

Every task up to here ran the tests in Debug, but CI runs them in **Release** — and this repository has hit failures visible in only one configuration in each direction (Release-only analyzer errors, and a Debug-only timing failure in `ProcessRunnerTests` under load). Passing one configuration is not evidence about the other.

Run Debug: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Run Release, exactly as CI does: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build`

Expected: PASS in both. Record both counts. If the two totals differ, say so explicitly rather than reporting the larger one.

- [ ] **Step 3: Run a real build through the console path**

`CodeGenerator/Program.cs` joins any arguments into one command and runs it non-interactively, so this form works:

Run: `dotnet run --project CodeGenerator -- "build app: a point of sale system for a retail shop"`

**This writes a real project to disk**, under `D:\SampleProjects\` — not the working directory. Before running, list that directory. Afterwards, report the exact path written.

**Never overwrite or delete anything already there.** If the build would land on an existing project, stop and report it rather than proceeding — a non-interactive run cannot reach the overwrite prompt, so the guard has to be you. At the time of writing, `D:\SampleProjects\Generated` held only a stray `Employee.cs` and no app folder, so a collision is not expected.

Confirm on screen: the spinner appears; a line names the source (`Classes from qwen2.5:3b (Ollama).`); the classes listed are plausible; no class name starts with a digit; every class has an `Id`.

Expect this to take one to three minutes at the discovery step — that is the local model generating, not a hang.

**Guard against a prompt stalling the run.** `HandleFromDescription` ends in `RunReviewLoop`, which is interactive. Feed it end-of-input and cap the wall clock so a waiting prompt cannot hang you for the full timeout:

```bash
echo "" | timeout 600 dotnet run --project CodeGenerator -- "build app: a point of sale system for a retail shop" 2>&1 | tee /tmp/build-app-run.log
```

If it exits at the review loop without generating, that is fine and expected — the thing being verified here is the discovery output and the source line, both printed before the loop starts. Say so in your report rather than treating it as a failure.

- [ ] **Step 4: Run the same description through the web stage**

Start the chat web host, walk to the Discover node with the same description, and confirm the panel shows the source line and that the Review page's provenance column reads **Model** for the discovered classes.

Use the `browser-automation` skill for this rather than asking the user to look.

- [ ] **Step 5: Prove the fallback with your own eyes**

**Do NOT stop the Ollama service.** The machine's owner has explicitly ruled that out. Point the config at a dead port instead, which exercises the identical code path — `IsAvailable()` returns false either way.

Write `discovery.json` beside the CodeGenerator binary with a dead endpoint:

```json
{ "Source": "Ollama", "Model": "qwen2.5:3b", "Endpoint": "http://localhost:1", "TimeoutMinutes": 10 }
```

Repeat Step 3 and confirm the run falls back to Wikipedia and *says so* on screen, rather than failing or silently producing different classes.

Delete `discovery.json` afterwards so the default configuration is restored.

- [ ] **Step 6: Commit any documentation the run proved wrong**

```bash
git add docs/
git commit -m "Update the manual for the Discover stage's new source"
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| 1. `IDomainModelSource` / `DiscoveryOutcome` | 1 |
| 2. Talking to Ollama (schema, timeout) | 4 |
| 3. Normalisation | 5 |
| 4. `CanReference` in force | 5 (unit), 10 (live) |
| 5. Source selection and fallback | 2, 7, 8, 9, 11 |
| 6. Keeping `why` answerable | 3, 5 |
| 7. Testing | 1–10 |

**Known deviations from the spec, deliberate:**

- The spec's **warm-up request** during panel paint is not implemented. It is an optimisation that adds a background call and a failure mode, and the panel already counts seconds through the wait. Fold it in only if Step 3 of Task 11 shows the first-call delay is actually intolerable.
- `Link` applies the rank comparison as well as `CanReference`, matching how `RelationshipMiner` uses the pair. The spec named only `CanReference`; the rank check is what stops a Document pointing at another Document.
