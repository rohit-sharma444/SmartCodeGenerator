# SLM Intelligence Layer — Phase A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the rule-based parser returns `IntentType.Unknown`, retrieve the nearest known commands with ONNX sentence embeddings, propose an intent, and execute it only after the user confirms.

**Architecture:** Four new projects. `CodeIntelligence.Abstractions` holds interfaces and records with zero dependencies; `CodeIntelligence` holds corpus/storage/retrieval; `CodeIntelligence.Onnx` is the only project allowed to reference `Microsoft.ML.OnnxRuntime`. `NaturalLanguageBridge` references Abstractions only. The seam is two optional parameters on `CommandFront.RunDetailed`, so with AI off the code path is byte-identical to today's.

**Tech Stack:** .NET 10, ONNX Runtime, `Microsoft.ML.Tokenizers`, `Microsoft.Data.Sqlite`, xunit 2.9.2, Git LFS.

**Spec:** `docs/superpowers/specs/2026-08-06-slm-intelligence-layer-design.md`

## Global Constraints

- Target framework `net10.0`, `Nullable` and `ImplicitUsings` enabled — inherited from `Directory.Build.props`. Do not restate them in new csproj files.
- File-scoped namespaces. This is the codebase convention everywhere.
- **New `IntentType` members MUST be appended at the very end of the enum.** Members are persisted as integers by the learned-intent store; inserting in the middle silently corrupts saved data.
- Every new project is added to `SmartCodeGenerator.slnx`. CI builds that solution in Release with `-warnaserror`.
- Only `CodeIntelligence.Onnx` may reference `Microsoft.ML.OnnxRuntime`. Task 1 makes this an executable test.
- `CodeIntelligence.Tests` uses xunit 2.9.2 (matching `DomainDiscovery.Tests`), not MSTest.
- Never mark a task complete on a red or unrun test. Run the command, read the output.
- Analyzer errors only surface on a non-incremental build. To verify a clean build use:
  `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
- All paths in this plan are relative to the repo root `D:\SmartCodeGenerator-Repository` (the parent of the `SmartCodeGenerator` folder).

---

## File Structure

**Created:**

```
CodeIntelligence.Abstractions/
  CodeIntelligence.Abstractions.csproj
  AiResolution.cs            AiResolution, RetrievedExample records
  CorpusDocument.cs          CorpusDocument record, CorpusSourceKind enum
  IIntentResolver.cs
  IEmbeddingProvider.cs      + ITokenizer
  IVectorStore.cs            + StoredVector, IndexStatus

CodeIntelligence/
  CodeIntelligence.csproj
  AiLayer.cs                 composition root; the only catch-all boundary
  AiConfig.cs                ai.json load/save
  Corpus/CorpusHarvester.cs  IntentRepository + LiteralCommandTable + CommandDictionary
  Storage/SqliteVectorStore.cs
  Storage/VectorSchema.cs    DDL + open/create
  Storage/DecisionLog.cs     ai_decision writes and last-decision read
  Retrieval/Similarity.cs    cosine, L2 normalize
  Retrieval/NeighbourVote.cs k-NN vote -> intent + confidence
  Resolution/RetrievalIntentResolver.cs

CodeIntelligence.Onnx/
  CodeIntelligence.Onnx.csproj
  BertTokenizerAdapter.cs
  OnnxEmbeddingProvider.cs   session, mean pooling, L2 normalize
  ModelRegistry.cs           id, dim, SHA-256, file resolution
  Models/all-MiniLM-L6-v2-int8/{model.onnx,vocab.txt}   (Git LFS)

CodeIntelligence.Tests/
  CodeIntelligence.Tests.csproj
  ArchitectureTests.cs  Fakes/FakeEmbeddingProvider.cs  StorageTests.cs
  HarvesterTests.cs  RetrievalTests.cs  ResolverTests.cs
  ConfigTests.cs  SeamTests.cs  GoldenSetTests.cs  Fixtures/golden-set.tsv
```

**Modified:**

- `SmartCodeGenerator/SmartCodeGenerator.slnx` — add four projects
- `CodeGenerationEngine/Models/IntentType.cs` — append 6 AI intents
- `NaturalLanguageBridge/NaturalLanguageBridge.csproj` — reference Abstractions
- `NaturalLanguageBridge/CommandFront.cs:214` — three optional parameters on `RunDetailed`
- `NaturalLanguageBridge/NaturalLanguagePipeline.cs:20` — thread the parameters through
- `NaturalLanguageBridge/LiteralCommandTable.cs:13` — six `ai ...` entries
- `CodeGenerationEngine/Service/CommandHandlingService.cs` — dispatch the AI intents
- `CodeGenerator/App/CodeGeneratorStarter.cs:92,180` — supply resolver and confirm delegate
- `CodeGenerator/CodeGenerator.csproj` — reference `CodeIntelligence` and `CodeIntelligence.Onnx`
- `.gitattributes` — LFS rule for `*.onnx`

---

### Task 1: Abstractions project and the dependency-isolation test

The contract every later task codes against, plus the test that makes "AI off means AI absent" structural.

**Files:**
- Create: `CodeIntelligence.Abstractions/CodeIntelligence.Abstractions.csproj`, `AiResolution.cs`, `CorpusDocument.cs`, `IIntentResolver.cs`, `IEmbeddingProvider.cs`, `IVectorStore.cs`
- Create: `CodeIntelligence.Tests/CodeIntelligence.Tests.csproj`, `CodeIntelligence.Tests/ArchitectureTests.cs`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Models.IntentType`
- Produces: `AiResolution`, `RetrievedExample`, `CorpusDocument`, `CorpusSourceKind`, `IIntentResolver`, `IEmbeddingProvider`, `ITokenizer`, `IVectorStore`, `StoredVector`, `IndexStatus`

- [ ] **Step 1: Create the Abstractions csproj**

`CodeIntelligence.Abstractions/CodeIntelligence.Abstractions.csproj` — note it references the engine for `IntentType` and nothing else:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Contracts only. This project must never gain a PackageReference:
       ArchitectureTests enforces it. Keeping it dependency-free is what lets
       NaturalLanguageBridge reference the AI contracts without dragging in
       ONNX Runtime or SQLite. -->
  <ItemGroup>
    <ProjectReference Include="..\CodeGenerationEngine\CodeGenerationEngine.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the contract types**

`CodeIntelligence.Abstractions/AiResolution.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace CodeIntelligence.Abstractions;

/// <summary>One corpus entry that supported a resolution, with its cosine score.</summary>
public sealed record RetrievedExample(long DocumentId, string Text, IntentType Intent, double Score);

/// <summary>An AI-proposed reading of input the rule-based parser could not recognize.
/// <paramref name="Confidence"/> is the neighbour-vote margin in [0,1]; Evidence is the
/// retrieved support, kept so `ai why` can explain the decision after the fact.</summary>
public sealed record AiResolution(
    IntentType Intent,
    double Confidence,
    IReadOnlyList<RetrievedExample> Evidence,
    string? Rationale);
```

`CodeIntelligence.Abstractions/CorpusDocument.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace CodeIntelligence.Abstractions;

public enum CorpusSourceKind { IntentRule, LiteralCommand, CommandCatalog, Documentation }

/// <summary>A phrasing to embed. <paramref name="ContentHash"/> is over Text+Intent so a
/// reindex can skip unchanged rows. Id is 0 until the store assigns one.</summary>
public sealed record CorpusDocument(
    string Text,
    IntentType Intent,
    CorpusSourceKind SourceKind,
    string SourceRef,
    string ContentHash)
{
    public long Id { get; init; }
}
```

`CodeIntelligence.Abstractions/IIntentResolver.cs`:

```csharp
namespace CodeIntelligence.Abstractions;

/// <summary>Proposes an intent for input the rule-based parser returned Unknown for.
/// Implementations MUST NOT throw: a failure is a null return, which callers treat as
/// "unrecognized" and print exactly what they print today.</summary>
public interface IIntentResolver
{
    AiResolution? Resolve(string input, CancellationToken ct);
}
```

`CodeIntelligence.Abstractions/IEmbeddingProvider.cs`:

```csharp
namespace CodeIntelligence.Abstractions;

public interface ITokenizer
{
    IReadOnlyList<int> Encode(string text);
}

/// <summary>Turns text into a unit-length vector. ModelId identifies the weights:
/// vectors from different ModelIds are NOT comparable, so it is stored per row.</summary>
public interface IEmbeddingProvider : IDisposable
{
    string ModelId { get; }
    int Dimension { get; }
    float[] Embed(string text);
}
```

`CodeIntelligence.Abstractions/IVectorStore.cs`:

```csharp
namespace CodeIntelligence.Abstractions;

public sealed record StoredVector(long DocumentId, string Text, IntentType Intent, float[] Vector);

/// <summary>What `ai status` reports. <paramref name="ReindexRequired"/> is true when the
/// index is empty or was built by a different model than the one now loaded.</summary>
public sealed record IndexStatus(
    int DocumentCount,
    string? IndexedModelId,
    int Dimension,
    bool ReindexRequired,
    DateTime? BuiltUtc);

public interface IVectorStore
{
    IndexStatus GetStatus(string currentModelId);
    IReadOnlyList<StoredVector> LoadAll(string modelId);
    void ReplaceAll(string modelId, int dimension, IReadOnlyList<(CorpusDocument Doc, float[] Vector)> rows);
}
```

Add `using CodeGenerationEngine.Models;` at the top of `IVectorStore.cs` for `IntentType`.

- [ ] **Step 3: Create the test project**

`CodeIntelligence.Tests/CodeIntelligence.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
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
    <ProjectReference Include="..\CodeIntelligence.Abstractions\CodeIntelligence.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Write the failing architecture test**

`CodeIntelligence.Tests/ArchitectureTests.cs`. It reads csproj XML rather than loaded assemblies, because a transitive reference can be present in the graph without being loaded — reading the project files is what actually pins the rule:

```csharp
using System.Xml.Linq;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class ArchitectureTests
{
    /// <summary>Repo root: climb from the test bin folder until a folder holding
    /// Directory.Build.props is found.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IReadOnlyList<string> PackageRefs(string csprojRelativePath)
    {
        var full = Path.Combine(RepoRoot(), csprojRelativePath);
        Assert.True(File.Exists(full), $"Missing project file: {full}");

        return XDocument.Load(full)
            .Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void Abstractions_has_no_package_references()
    {
        var refs = PackageRefs(@"CodeIntelligence.Abstractions\CodeIntelligence.Abstractions.csproj");
        Assert.Empty(refs);
    }

    [Fact]
    public void NaturalLanguageBridge_does_not_reference_onnx()
    {
        var refs = PackageRefs(@"NaturalLanguageBridge\NaturalLanguageBridge.csproj");
        Assert.DoesNotContain(refs, r => r.Contains("OnnxRuntime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CodeIntelligence_core_does_not_reference_onnx()
    {
        var refs = PackageRefs(@"CodeIntelligence\CodeIntelligence.csproj");
        Assert.DoesNotContain(refs, r => r.Contains("OnnxRuntime", StringComparison.OrdinalIgnoreCase));
    }
}
```

Note on assertion order: `Assert.DoesNotContain(collection, predicate)` takes the collection first. Getting this backwards is a known way to write a test that passes vacuously.

- [ ] **Step 5: Run the tests — expect two failures**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj
```

Expected: `Abstractions_has_no_package_references` and `NaturalLanguageBridge_does_not_reference_onnx` PASS; `CodeIntelligence_core_does_not_reference_onnx` FAILS with "Missing project file" because `CodeIntelligence` does not exist yet. That failure is correct and is fixed by Task 2.

- [ ] **Step 6: Register the two projects in the solution**

Add to `SmartCodeGenerator/SmartCodeGenerator.slnx`, keeping the existing alphabetical order:

```xml
  <Project Path="../CodeIntelligence.Abstractions/CodeIntelligence.Abstractions.csproj" />
  <Project Path="../CodeIntelligence.Tests/CodeIntelligence.Tests.csproj" />
```

- [ ] **Step 7: Commit**

```bash
git add CodeIntelligence.Abstractions CodeIntelligence.Tests SmartCodeGenerator/SmartCodeGenerator.slnx
git commit -m "feat(ai): add CodeIntelligence.Abstractions contracts and architecture tests"
```

---

### Task 2: SQLite vector store

**Files:**
- Create: `CodeIntelligence/CodeIntelligence.csproj`, `CodeIntelligence/Storage/VectorSchema.cs`, `CodeIntelligence/Storage/SqliteVectorStore.cs`
- Create: `CodeIntelligence.Tests/StorageTests.cs`
- Modify: `CodeIntelligence.Tests/CodeIntelligence.Tests.csproj`, `SmartCodeGenerator/SmartCodeGenerator.slnx`

**Interfaces:**
- Consumes: `IVectorStore`, `StoredVector`, `IndexStatus`, `CorpusDocument`
- Produces: `SqliteVectorStore(string databasePath)` implementing `IVectorStore`; `VectorSchema.EnsureCreated(SqliteConnection)`

- [ ] **Step 1: Create the CodeIntelligence csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\CodeIntelligence.Abstractions\CodeIntelligence.Abstractions.csproj" />
    <ProjectReference Include="..\CodeGenerationEngine\CodeGenerationEngine.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.9" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.3" />
  </ItemGroup>

</Project>
```

Add `<ProjectReference Include="..\CodeIntelligence\CodeIntelligence.csproj" />` to the test csproj, and both project lines to the slnx.

- [ ] **Step 2: Write the failing storage tests**

`CodeIntelligence.Tests/StorageTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Storage;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-store-" + Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_dir, "ai-index.db");

    public StorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static (CorpusDocument, float[]) Row(string text, IntentType intent, float a, float b)
        => (new CorpusDocument(text, intent, CorpusSourceKind.IntentRule, "test", text.GetHashCode().ToString()),
            [a, b]);

    [Fact]
    public void Empty_store_requires_reindex()
    {
        var store = new SqliteVectorStore(DbPath);
        var status = store.GetStatus("model-a");

        Assert.Equal(0, status.DocumentCount);
        Assert.True(status.ReindexRequired);
    }

    [Fact]
    public void ReplaceAll_then_LoadAll_round_trips_vectors_and_intents()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        var all = store.LoadAll("model-a");

        Assert.Single(all);
        Assert.Equal("list classes", all[0].Text);
        Assert.Equal(IntentType.ListClasses, all[0].Intent);
        Assert.Equal([1f, 0f], all[0].Vector);
    }

    [Fact]
    public void Status_reports_reindex_required_when_the_model_changed()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        Assert.False(store.GetStatus("model-a").ReindexRequired);
        Assert.True(store.GetStatus("model-b").ReindexRequired);
    }

    [Fact]
    public void LoadAll_returns_nothing_for_a_different_model()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        Assert.Empty(store.LoadAll("model-b"));
    }

    [Fact]
    public void ReplaceAll_leaves_the_previous_index_intact_when_the_build_throws()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        // A row whose vector length disagrees with the declared dimension is
        // rejected mid-build — the temp-then-swap must roll back, not truncate.
        Assert.ThrowsAny<Exception>(() =>
            store.ReplaceAll("model-a", 2,
            [
                Row("create class", IntentType.CreateClass, 0f, 1f),
                (new CorpusDocument("bad", IntentType.Unknown, CorpusSourceKind.IntentRule, "t", "h"),
                 new[] { 1f, 2f, 3f })
            ]));

        var survivors = store.LoadAll("model-a");
        Assert.Single(survivors);
        Assert.Equal("list classes", survivors[0].Text);
    }
}
```

- [ ] **Step 3: Run them — expect failure**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter StorageTests
```

Expected: FAIL, `SqliteVectorStore` does not exist.

- [ ] **Step 4: Implement the schema**

`CodeIntelligence/Storage/VectorSchema.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace CodeIntelligence.Storage;

/// <summary>DDL for the AI index. Vectors are stored as raw little-endian float32
/// BLOBs; at a few thousand rows a brute-force scan is microseconds, so there is no
/// ANN index to maintain.</summary>
internal static class VectorSchema
{
    internal const string ModelIdKey = "model_id";
    internal const string DimensionKey = "dimension";
    internal const string BuiltUtcKey = "built_utc";

    internal static void EnsureCreated(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS corpus_document(
              id          INTEGER PRIMARY KEY AUTOINCREMENT,
              text        TEXT    NOT NULL,
              intent      TEXT    NOT NULL,
              source_kind TEXT    NOT NULL,
              source_ref  TEXT    NOT NULL,
              content_hash TEXT   NOT NULL);

            CREATE TABLE IF NOT EXISTS embedding(
              document_id INTEGER PRIMARY KEY REFERENCES corpus_document(id),
              model_id    TEXT    NOT NULL,
              dim         INTEGER NOT NULL,
              vector      BLOB    NOT NULL);

            CREATE TABLE IF NOT EXISTS index_meta(
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS ai_decision(
              id           INTEGER PRIMARY KEY AUTOINCREMENT,
              created_utc  TEXT    NOT NULL,
              input        TEXT    NOT NULL,
              intent       TEXT    NOT NULL,
              confidence   REAL    NOT NULL,
              evidence_ids TEXT    NOT NULL,
              accepted     INTEGER);
            """;
        cmd.ExecuteNonQuery();
    }
}
```

- [ ] **Step 5: Implement the store**

`CodeIntelligence/Storage/SqliteVectorStore.cs`:

```csharp
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodeIntelligence.Storage;

/// <summary>SQLite-backed vector store. ReplaceAll is a single transaction, so a
/// crash or a rejected row leaves the previous index intact and serving — a killed
/// rebuild must never leave the store unusable.</summary>
public sealed class SqliteVectorStore : IVectorStore
{
    private readonly string _databasePath;

    public SqliteVectorStore(string databasePath)
    {
        _databasePath = databasePath;
        var folder = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        VectorSchema.EnsureCreated(connection);
        return connection;
    }

    public IndexStatus GetStatus(string currentModelId)
    {
        using var connection = Open();

        var indexedModel = ReadMeta(connection, VectorSchema.ModelIdKey);
        var dimension = int.TryParse(ReadMeta(connection, VectorSchema.DimensionKey), out var d) ? d : 0;

        // RoundtripKind is load-bearing: the value was written with "o" (UTC), and a
        // bare TryParse hands it back as DateTimeKind.Local, silently shifting
        // BuiltUtc by the machine's offset on a field the contract calls UTC.
        var built = DateTime.TryParse(
            ReadMeta(connection, VectorSchema.BuiltUtcKey), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var b)
            ? b : (DateTime?)null;

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM embedding WHERE model_id = $m";
        count.Parameters.AddWithValue("$m", currentModelId);
        var documents = Convert.ToInt32(count.ExecuteScalar() ?? 0);

        var reindex = documents == 0 ||
                      !string.Equals(indexedModel, currentModelId, StringComparison.Ordinal);

        return new IndexStatus(documents, indexedModel, dimension, reindex, built);
    }

    public IReadOnlyList<StoredVector> LoadAll(string modelId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT d.id, d.text, d.intent, e.vector
            FROM   embedding e JOIN corpus_document d ON d.id = e.document_id
            WHERE  e.model_id = $m
            ORDER  BY d.id
            """;
        cmd.Parameters.AddWithValue("$m", modelId);

        var results = new List<StoredVector>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var intent = Enum.TryParse<IntentType>(reader.GetString(2), out var parsed)
                ? parsed : IntentType.Unknown;

            results.Add(new StoredVector(
                reader.GetInt64(0), reader.GetString(1), intent, FromBlob((byte[])reader[3])));
        }

        return results;
    }

    public void ReplaceAll(
        string modelId, int dimension, IReadOnlyList<(CorpusDocument Doc, float[] Vector)> rows)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var wipe = connection.CreateCommand())
        {
            wipe.CommandText = "DELETE FROM embedding; DELETE FROM corpus_document;";
            wipe.ExecuteNonQuery();
        }

        foreach (var (doc, vector) in rows)
        {
            if (vector.Length != dimension)
                throw new InvalidOperationException(
                    $"Vector for '{doc.Text}' has length {vector.Length}, expected {dimension}.");

            using var insertDoc = connection.CreateCommand();
            insertDoc.CommandText = """
                INSERT INTO corpus_document(text, intent, source_kind, source_ref, content_hash)
                VALUES($t, $i, $k, $r, $h);
                SELECT last_insert_rowid();
                """;
            insertDoc.Parameters.AddWithValue("$t", doc.Text);
            insertDoc.Parameters.AddWithValue("$i", doc.Intent.ToString());
            insertDoc.Parameters.AddWithValue("$k", doc.SourceKind.ToString());
            insertDoc.Parameters.AddWithValue("$r", doc.SourceRef);
            insertDoc.Parameters.AddWithValue("$h", doc.ContentHash);
            var id = Convert.ToInt64(insertDoc.ExecuteScalar()!);

            using var insertVec = connection.CreateCommand();
            insertVec.CommandText =
                "INSERT INTO embedding(document_id, model_id, dim, vector) VALUES($d, $m, $n, $v)";
            insertVec.Parameters.AddWithValue("$d", id);
            insertVec.Parameters.AddWithValue("$m", modelId);
            insertVec.Parameters.AddWithValue("$n", dimension);
            insertVec.Parameters.AddWithValue("$v", ToBlob(vector));
            insertVec.ExecuteNonQuery();
        }

        WriteMeta(connection, VectorSchema.ModelIdKey, modelId);
        WriteMeta(connection, VectorSchema.DimensionKey, dimension.ToString());
        WriteMeta(connection, VectorSchema.BuiltUtcKey, DateTime.UtcNow.ToString("o"));

        transaction.Commit();
    }

    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM index_meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void WriteMeta(SqliteConnection connection, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO index_meta(key, value) VALUES($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBlob(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
```

- [ ] **Step 6: Run the storage tests — expect pass**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter StorageTests
```

Expected: 5 passed. `CodeIntelligence_core_does_not_reference_onnx` from Task 1 now passes too.

- [ ] **Step 7: Commit**

```bash
git add CodeIntelligence CodeIntelligence.Tests SmartCodeGenerator/SmartCodeGenerator.slnx
git commit -m "feat(ai): add the SQLite vector store with transactional reindex"
```

---

### Task 3: Corpus harvester

Turns the existing command knowledge into embeddable phrasings. `IntentRepository` supplies `Actions`, `Targets` and `Keywords` — not ready-made sentences — so the harvester composes action × target pairs.

**Files:**
- Create: `CodeIntelligence/Corpus/CorpusHarvester.cs`
- Create: `CodeIntelligence.Tests/HarvesterTests.cs`

**Interfaces:**
- Consumes: `IntentRepository.GetAll()`, `LiteralCommandTable`, `CommandDictionary.Commands`, `CorpusDocument`
- Produces: `CorpusHarvester.Harvest()` returning `IReadOnlyList<CorpusDocument>`

- [ ] **Step 1: Write the failing harvester tests**

`CodeIntelligence.Tests/HarvesterTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Corpus;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class HarvesterTests
{
    [Fact]
    public void Harvest_produces_documents()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.NotEmpty(docs);
    }

    [Fact]
    public void Harvest_never_emits_an_unknown_intent()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.DoesNotContain(docs, d => d.Intent == IntentType.Unknown);
    }

    [Fact]
    public void Harvest_includes_the_literal_command_table_phrasings()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.Contains(docs, d =>
            d.SourceKind == CorpusSourceKind.LiteralCommand &&
            d.Text.Equals("show learning", StringComparison.OrdinalIgnoreCase) &&
            d.Intent == IntentType.ShowLearning);
    }

    [Fact]
    public void Harvest_composes_action_and_target_pairs_from_intent_rules()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.Contains(docs, d => d.SourceKind == CorpusSourceKind.IntentRule);
    }

    [Fact]
    public void Harvest_deduplicates_identical_phrasings()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        var duplicates = docs
            .GroupBy(d => d.Text.ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Harvest_is_deterministic()
    {
        IntentRepository.Initialize();

        var first = CorpusHarvester.Harvest().Select(d => d.Text).ToList();
        var second = CorpusHarvester.Harvest().Select(d => d.Text).ToList();

        Assert.Equal(first, second);
    }
}
```

- [ ] **Step 2: Run them — expect failure**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter HarvesterTests
```

Expected: FAIL, `CorpusHarvester` does not exist.

- [ ] **Step 3: Implement the harvester**

`CodeIntelligence/Corpus/CorpusHarvester.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;

namespace CodeIntelligence.Corpus;

/// <summary>Builds the retrieval corpus from knowledge the platform already holds.
/// IntentRepository stores Actions/Targets/Keywords rather than sentences, so the
/// phrasings are composed here as action + target pairs — this is what finally puts
/// that catalog to work, since it drives none of the rule-based recognition.
/// Output is sorted and deduplicated so a reindex is reproducible.</summary>
public static class CorpusHarvester
{
    public static IReadOnlyList<CorpusDocument> Harvest()
    {
        var collected = new List<CorpusDocument>();

        collected.AddRange(FromIntentRules());
        collected.AddRange(FromLiteralTable());
        collected.AddRange(FromCommandCatalog());

        // Dedup on (Text, Intent), NOT Text alone. Two documents with the same text
        // but different intents are not duplicates — they are an ambiguous phrase, and
        // both must survive. "generate design document" composes from both
        // GenerateDocumentation and GenerateDesignDocument; "show project" from both
        // ListProjects and ShowProject. Keeping both makes the k-NN vote split, drops
        // confidence below the floor, and the resolver declines instead of guessing by
        // list order. ThenBy keeps ordering total now that text alone is not unique.
        return collected
            .Where(d => d.Intent != IntentType.Unknown && d.Text.Length > 0)
            .GroupBy(d => (Text: d.Text.ToLowerInvariant(), d.Intent))
            .Select(g => g.First())
            .OrderBy(d => d.Text, StringComparer.Ordinal)
            .ThenBy(d => d.Intent.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<CorpusDocument> FromIntentRules()
    {
        foreach (var rule in IntentRepository.GetAll().Where(r => r.IsEnabled))
        {
            // Actions alone are too generic to embed ("create" maps to a dozen
            // intents), so only the composed pairs and the keywords are emitted.
            foreach (var action in rule.Actions)
                foreach (var target in rule.Targets)
                    yield return Document($"{action} {target}", rule.Intent,
                        CorpusSourceKind.IntentRule, rule.Intent.ToString());

            foreach (var keyword in rule.Keywords)
                yield return Document(keyword, rule.Intent,
                    CorpusSourceKind.IntentRule, rule.Intent.ToString());
        }
    }

    private static IEnumerable<CorpusDocument> FromLiteralTable()
    {
        foreach (var intent in LiteralCommandTable.Intents)
            foreach (var phrase in LiteralCommandTable.PhrasesFor(intent))
                yield return Document(phrase, intent,
                    CorpusSourceKind.LiteralCommand, nameof(LiteralCommandTable));
    }

    private static IEnumerable<CorpusDocument> FromCommandCatalog()
    {
        foreach (var info in CommandDictionary.Commands)
        {
            var intent = CommandIntentLookup.Resolve(info.Command);
            if (intent == IntentType.Unknown)
                continue;

            yield return Document(StripPlaceholders(info.Command), intent,
                CorpusSourceKind.CommandCatalog, info.Command);

            if (!string.IsNullOrWhiteSpace(info.Example))
                yield return Document(StripPlaceholders(info.Example), intent,
                    CorpusSourceKind.CommandCatalog, info.Command);

            if (!string.IsNullOrWhiteSpace(info.Description))
                yield return Document(info.Description.TrimEnd('.'), intent,
                    CorpusSourceKind.Documentation, info.Command);
        }
    }

    /// <summary>Drops "&lt;placeholder&gt;" and "[option]" spans so the embedded text is
    /// the phrasing a user would actually type, not the help syntax.</summary>
    internal static string StripPlaceholders(string command)
    {
        // Two INDEPENDENT counters, not one shared depth. A single counter cannot tell
        // a mismatched closer from a real one: "<a]" would read as balanced, and a
        // stray ">" at depth 0 would be silently swallowed instead of kept. Make the
        // method internal (with InternalsVisibleTo for the test project) so both cases
        // can be unit-tested directly.
        var builder = new StringBuilder();
        var angleDepth = 0;
        var bracketDepth = 0;

        foreach (var ch in command)
        {
            if (ch is '<') angleDepth++;
            else if (ch is '[') bracketDepth++;
            else if (ch is '>' && angleDepth > 0) angleDepth--;
            else if (ch is ']' && bracketDepth > 0) bracketDepth--;
            else if (angleDepth == 0 && bracketDepth == 0) builder.Append(ch);
        }

        return string.Join(' ',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static CorpusDocument Document(
        string text, IntentType intent, CorpusSourceKind kind, string sourceRef)
    {
        var normalized = string.Join(' ',
            (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return new CorpusDocument(normalized, intent, kind, sourceRef, Hash(normalized, intent));
    }

    private static string Hash(string text, IntentType intent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{text}|{intent}"));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 4: Add the two lookups the harvester needs**

`LiteralCommandTable` exposes intents but not the phrases behind them. Add to `NaturalLanguageBridge/LiteralCommandTable.cs`, after the existing `Intents` property:

```csharp
    /// <summary>The exact phrases that resolve to <paramref name="intent"/>. Consumed by
    /// the AI corpus harvester, which embeds real user phrasings rather than enum names.</summary>
    public static IReadOnlyList<string> PhrasesFor(IntentType intent) =>
        [.. Exact.Where(pair => pair.Value == intent).Select(pair => pair.Key)];
```

Create `CodeIntelligence/Corpus/CommandIntentLookup.cs`. `CommandInfo` carries no `IntentType`, so the catalog is joined to intents by running each command name back through the existing parser — reusing the real recognizer rather than duplicating its rules:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeIntelligence.Corpus;

/// <summary>Maps a help-catalog command name to its intent by asking the real parser.
/// Duplicating the mapping here would let the corpus drift from recognition; this
/// cannot. Commands the parser does not recognize are skipped by the caller.</summary>
internal static class CommandIntentLookup
{
    internal static IntentType Resolve(string commandName)
    {
        try
        {
            return CommandFront.Parse(commandName, Directory.GetCurrentDirectory()).Intent;
        }
        catch (Exception)
        {
            return IntentType.Unknown;
        }
    }
}
```

Add the bridge reference to `CodeIntelligence/CodeIntelligence.csproj`:

```xml
    <ProjectReference Include="..\NaturalLanguageBridge\NaturalLanguageBridge.csproj" />
```

- [ ] **Step 5: Run the harvester tests — expect pass**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter HarvesterTests
```

Expected: 6 passed. If `Harvest_produces_documents` reports zero, `IntentRepository.Initialize()` wrote `intents.json` to the test bin folder and returned defaults — confirm `GetAll()` is non-empty before assuming the harvester is at fault.

- [ ] **Step 6: Commit**

```bash
git add CodeIntelligence CodeIntelligence.Tests NaturalLanguageBridge/LiteralCommandTable.cs
git commit -m "feat(ai): harvest the retrieval corpus from intents, literals and the help catalog"
```

---

### Task 4: Similarity and neighbour voting

Pure math, no ONNX. This is where the confidence floor is enforced.

**Files:**
- Create: `CodeIntelligence/Retrieval/Similarity.cs`, `CodeIntelligence/Retrieval/NeighbourVote.cs`
- Create: `CodeIntelligence.Tests/RetrievalTests.cs`

**Interfaces:**
- Produces: `Similarity.Cosine(float[], float[])`, `Similarity.Normalize(float[])`, `NeighbourVote.Decide(IReadOnlyList<StoredVector>, float[], int topK, double floor, double minTopScore)` returning `AiResolution?`

- [ ] **Step 1: Write the failing retrieval tests**

`CodeIntelligence.Tests/RetrievalTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Retrieval;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class RetrievalTests
{
    private static StoredVector V(string text, IntentType intent, params float[] vector)
        => new(text.GetHashCode(), text, intent, Similarity.Normalize(vector));

    [Fact]
    public void Cosine_of_identical_unit_vectors_is_one()
        => Assert.Equal(1.0, Similarity.Cosine([1f, 0f], [1f, 0f]), 6);

    [Fact]
    public void Cosine_of_orthogonal_vectors_is_zero()
        => Assert.Equal(0.0, Similarity.Cosine([1f, 0f], [0f, 1f]), 6);

    [Fact]
    public void Cosine_of_mismatched_lengths_is_zero()
        => Assert.Equal(0.0, Similarity.Cosine([1f, 0f], [1f, 0f, 0f]), 6);

    [Fact]
    public void Unanimous_neighbours_win_with_high_confidence()
    {
        var corpus = new[]
        {
            V("list classes", IntentType.ListClasses, 1f, 0f),
            V("show classes", IntentType.ListClasses, 0.99f, 0.01f),
            V("create class", IntentType.CreateClass, 0f, 1f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.1, minTopScore: 0.35);

        Assert.NotNull(result);
        Assert.Equal(IntentType.ListClasses, result!.Intent);
        Assert.True(result.Confidence > 0.9);
    }

    [Fact]
    public void A_split_vote_yields_low_confidence()
    {
        var corpus = new[]
        {
            V("list classes", IntentType.ListClasses, 1f, 0f),
            V("create class", IntentType.CreateClass, 1f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.0, minTopScore: 0.35);

        Assert.NotNull(result);
        Assert.True(result!.Confidence < 0.6);
    }

    [Fact]
    public void Below_the_floor_returns_null_rather_than_guessing()
    {
        var corpus = new[]
        {
            V("list classes", IntentType.ListClasses, 1f, 0f),
            V("create class", IntentType.CreateClass, 1f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.9, minTopScore: 0.35);

        Assert.Null(result);
    }

    [Fact]
    public void An_empty_corpus_returns_null()
        => Assert.Null(NeighbourVote.Decide([], [1f, 0f], topK: 5, floor: 0.1, minTopScore: 0.35));

    [Fact]
    public void Evidence_is_ordered_best_first_and_capped_at_topK()
    {
        var corpus = new[]
        {
            V("a", IntentType.ListClasses, 0.2f, 1f),
            V("b", IntentType.ListClasses, 1f, 0f),
            V("c", IntentType.ListClasses, 0.6f, 0.8f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.0, minTopScore: 0.35);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Evidence.Count);
        Assert.Equal("b", result.Evidence[0].Text);
        Assert.True(result.Evidence[0].Score >= result.Evidence[1].Score);
    }

    [Fact]
    public void A_lone_weak_match_returns_null_despite_100_percent_vote_share()
    {
        var corpus = new[]
        {
            V("weak", IntentType.ListClasses, 0.02f, 1f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 5, floor: 0.0, minTopScore: 0.35);

        Assert.Null(result);
    }

    [Fact]
    public void Floor_boundary_is_inclusive_minimum()
    {
        var corpus = new[]
        {
            V("a", IntentType.ListClasses, 1f, 0f),
            V("b", IntentType.CreateClass, 1f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.5, minTopScore: 0.0);

        Assert.NotNull(result);
        Assert.Equal(0.5, result!.Confidence, 6);
    }

    [Fact]
    public void Voting_is_score_weighted_not_count_based()
    {
        var corpus = new[]
        {
            V("strong", IntentType.ListClasses, 0.99f, 0.1414f),
            V("weak1", IntentType.CreateClass, 0.1f, 0.9949f),
            V("weak2", IntentType.CreateClass, 0.1f, 0.9949f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 3, floor: 0.0, minTopScore: 0.0);

        Assert.NotNull(result);
        Assert.Equal(IntentType.ListClasses, result!.Intent);
        Assert.True(result.Confidence > 0.8);
    }

    [Fact]
    public void Evidence_ordering_is_total()
    {
        // Listed ListClasses-first ON PURPOSE. LINQ's sort is stable, so a corpus in
        // the asserted order would pass with no intent tie-break at all — the test has
        // to fight the input order to prove the tie-break is what orders the output.
        var corpus = new[]
        {
            V("text", IntentType.ListClasses, 0.5f, 0f),
            V("text", IntentType.CreateClass, 0.5f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.0, minTopScore: 0.0);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Evidence.Count);
        Assert.Equal(IntentType.CreateClass, result.Evidence[0].Intent);
        Assert.Equal(IntentType.ListClasses, result.Evidence[1].Intent);
    }
}
```

The last four tests are the ones a first pass omits, and each pins a property that
would otherwise be free to regress silently:

- `A_lone_weak_match...` is the reason `minTopScore` exists. Share-of-vote alone reports
  confidence 1.0 for a single cosine-0.02 neighbour, because that neighbour is the whole
  vote. Written without this test, the gate looks redundant.
- `Floor_boundary_is_inclusive_minimum` pins `<` rather than `<=`, which is what makes the
  "floor must exceed 0.5" rule in the class remarks true rather than aspirational.
- `Voting_is_score_weighted_not_count_based` fails if `g.Sum(e => e.Score)` is ever
  simplified to `g.Count()` — two weak neighbours would then outvote one near-perfect one.
- `Evidence_ordering_is_total` must list the corpus in the OPPOSITE order to the assertion.
  LINQ's `OrderBy` is a stable sort, so a fixture already in the asserted order passes with
  the `ThenBy(e => e.Intent...)` deleted — a vacuous test that proves nothing.

- [ ] **Step 2: Run them — expect failure**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter RetrievalTests
```

Expected: FAIL, `Similarity` does not exist.

- [ ] **Step 3: Implement similarity**

`CodeIntelligence/Retrieval/Similarity.cs`:

```csharp
namespace CodeIntelligence.Retrieval;

public static class Similarity
{
    /// <summary>Cosine similarity. Mismatched lengths score 0 rather than throwing:
    /// a dimension mismatch means a stale index, which the caller reports as
    /// "reindex required" instead of crashing the REPL.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
            return 0.0;

        double dot = 0, normA = 0, normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0.0;

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>Returns a unit-length copy. A zero vector is returned unchanged.</summary>
    public static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector)
            sum += value * value;

        var magnitude = Math.Sqrt(sum);
        if (magnitude == 0)
            return vector;

        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
            result[i] = (float)(vector[i] / magnitude);

        return result;
    }
}
```

- [ ] **Step 4: Implement the vote**

`CodeIntelligence/Retrieval/NeighbourVote.cs`:

```csharp
using CodeIntelligence.Abstractions;

namespace CodeIntelligence.Retrieval;

/// <summary>Brute-force k-NN over the corpus, behind TWO independent gates.
///
/// `minTopScore` asks "is anything actually similar?" and `floor` asks "do the
/// neighbours agree?" — they must stay separate. Share-of-vote alone answers only the
/// second: if exactly one corpus entry scores positive (say cosine 0.02, noise), it
/// takes 100% of the vote and clears any floor. Multiplying the two together would
/// hide which gate rejected an input, and Task 10 calibrates them independently.
///
/// `floor` is INCLUSIVE (confidence == floor is accepted) and therefore must be set
/// ABOVE 0.5, or a two-way ambiguous phrase — which computes to exactly 0.5 — would be
/// accepted instead of declined. Declining is always better than a confident wrong
/// answer: the user's next step after a decline is the familiar "did you mean" line.</summary>
public static class NeighbourVote
{
    public static AiResolution? Decide(
        IReadOnlyList<StoredVector> corpus, float[] query, int topK, double floor, double minTopScore)
    {
        if (corpus.Count == 0 || query.Length == 0)
            return null;

        var neighbours = corpus
            .Select(v => new RetrievedExample(v.DocumentId, v.Text, v.Intent, Similarity.Cosine(v.Vector, query)))
            .Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Text, StringComparer.Ordinal)
            .ThenBy(e => e.Intent.ToString(), StringComparer.Ordinal)
            .Take(Math.Max(1, topK))
            .ToList();

        if (neighbours.Count == 0)
            return null;

        // Gate 1: is anything actually close? Without this, a single weak positive
        // match takes 100% of the vote share and reports confidence 1.0.
        if (neighbours[0].Score < minTopScore)
            return null;

        var totals = neighbours
            .GroupBy(e => e.Intent)
            .Select(g => (Intent: g.Key, Score: g.Sum(e => e.Score)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Intent.ToString(), StringComparer.Ordinal)
            .ToList();

        var overall = neighbours.Sum(e => e.Score);
        var winner = totals[0];
        var confidence = overall == 0 ? 0 : winner.Score / overall;

        // Gate 2: do the neighbours agree? Inclusive — see the class remarks on why
        // floor must be set above 0.5.
        if (confidence < floor)
            return null;

        var rationale =
            $"{neighbours.Count} nearest examples, {winner.Intent} holding " +
            $"{confidence:P0} of the vote (closest: \"{neighbours[0].Text}\").";

        return new AiResolution(winner.Intent, confidence, neighbours, rationale);
    }
}
```

- [ ] **Step 5: Run the retrieval tests — expect pass**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter RetrievalTests
```

Expected: 12 passed.

- [ ] **Step 6: Commit**

```bash
git add CodeIntelligence CodeIntelligence.Tests
git commit -m "feat(ai): add cosine similarity and k-NN intent voting with a confidence floor"
```

---

### Task 5: RetrievalIntentResolver

Composes store, embedder and vote, and is the component that must never throw.

**Files:**
- Create: `CodeIntelligence/Resolution/RetrievalIntentResolver.cs`
- Create: `CodeIntelligence.Tests/Fakes/FakeEmbeddingProvider.cs`, `CodeIntelligence.Tests/ResolverTests.cs`

**Interfaces:**
- Consumes: `IVectorStore`, `IEmbeddingProvider`, `NeighbourVote.Decide`
- Produces: `RetrievalIntentResolver(IVectorStore store, IEmbeddingProvider embedder, int topK, double floor, double minTopScore, TimeSpan budget)` implementing `IIntentResolver`

Note on the tests below: they pass `minTopScore: 0.0` deliberately. Task 4 owns the
gate's behavior and tests it directly; these tests exercise resolver plumbing (model
drift, throwing embedder, cancellation, blank input) and a live gate would only add a
second reason for them to fail.

- [ ] **Step 1: Write the fake embedder**

`CodeIntelligence.Tests/Fakes/FakeEmbeddingProvider.cs`. Deterministic bag-of-words vectors over a fixed vocabulary — enough to make retrieval behave sensibly without loading ONNX:

```csharp
using CodeIntelligence.Abstractions;

namespace CodeIntelligence.Tests.Fakes;

/// <summary>Deterministic stand-in for the ONNX embedder: a bag-of-words vector over a
/// fixed vocabulary. Lets resolver and seam tests run without model files.</summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    private static readonly string[] Vocabulary =
        ["list", "show", "create", "delete", "class", "classes", "project", "property", "method"];

    public string ModelId { get; init; } = "fake-v1";
    public int Dimension => Vocabulary.Length;
    public Func<string, float[]>? OnEmbed { get; init; }

    public float[] Embed(string text)
    {
        if (OnEmbed is not null)
            return OnEmbed(text);

        var words = (text ?? string.Empty)
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var vector = new float[Vocabulary.Length];
        for (var i = 0; i < Vocabulary.Length; i++)
            vector[i] = words.Count(w => w == Vocabulary[i]);

        return vector;
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Write the failing resolver tests**

`CodeIntelligence.Tests/ResolverTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Resolution;
using CodeIntelligence.Storage;
using CodeIntelligence.Tests.Fakes;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class ResolverTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-resolver-" + Guid.NewGuid().ToString("N"));

    public ResolverTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SqliteVectorStore SeededStore(FakeEmbeddingProvider embedder)
    {
        var store = new SqliteVectorStore(Path.Combine(_dir, "ai-index.db"));

        (string Text, IntentType Intent)[] seed =
        [
            ("list classes", IntentType.ListClasses),
            ("show classes", IntentType.ListClasses),
            ("create class", IntentType.CreateClass),
            ("delete property", IntentType.DeleteProperty)
        ];

        store.ReplaceAll(embedder.ModelId, embedder.Dimension,
        [
            .. seed.Select(s => (
                new CorpusDocument(s.Text, s.Intent, CorpusSourceKind.IntentRule, "test", s.Text),
                embedder.Embed(s.Text)))
        ]);

        return store;
    }

    [Fact]
    public void Resolves_a_paraphrase_to_the_neighbouring_intent()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        var result = resolver.Resolve("show me the classes", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(IntentType.ListClasses, result!.Intent);
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public void Returns_null_for_input_with_no_overlap()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        Assert.Null(resolver.Resolve("zzzz qqqq", CancellationToken.None));
    }

    [Fact]
    public void Returns_null_when_the_index_was_built_by_another_model()
    {
        var seeder = new FakeEmbeddingProvider { ModelId = "fake-v1" };
        var store = SeededStore(seeder);

        var other = new FakeEmbeddingProvider { ModelId = "fake-v2" };
        var resolver = new RetrievalIntentResolver(
            store, other, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        Assert.Null(resolver.Resolve("list classes", CancellationToken.None));
    }

    [Fact]
    public void A_throwing_embedder_yields_null_rather_than_propagating()
    {
        var embedder = new FakeEmbeddingProvider();
        var store = SeededStore(embedder);

        var broken = new FakeEmbeddingProvider
        {
            OnEmbed = _ => throw new InvalidOperationException("onnx exploded")
        };

        var resolver = new RetrievalIntentResolver(
            store, broken, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        var result = resolver.Resolve("list classes", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void A_cancelled_token_yields_null()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Null(resolver.Resolve("list classes", cts.Token));
    }

    [Fact]
    public void Blank_input_yields_null()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        Assert.Null(resolver.Resolve("   ", CancellationToken.None));
    }
}
```

- [ ] **Step 3: Run them — expect failure**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter ResolverTests
```

Expected: FAIL, `RetrievalIntentResolver` does not exist.

- [ ] **Step 4: Implement the resolver**

`CodeIntelligence/Resolution/RetrievalIntentResolver.cs`:

```csharp
using System.Diagnostics;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Retrieval;

namespace CodeIntelligence.Resolution;

/// <summary>Embeds the input and votes over its nearest corpus neighbours.
///
/// Every failure path returns null, which callers render as today's "Command not
/// recognized" message. This class must not throw: it runs inside the REPL loop, and
/// an escaping exception would turn an unrecognized command into a crash.
///
/// The corpus is loaded once and cached — it is a few thousand short strings, and
/// re-reading SQLite per keystroke-command would dominate the latency budget.</summary>
public sealed class RetrievalIntentResolver : IIntentResolver
{
    private readonly IVectorStore _store;
    private readonly IEmbeddingProvider _embedder;
    private readonly int _topK;
    private readonly double _floor;
    private readonly double _minTopScore;
    private readonly TimeSpan _budget;

    private IReadOnlyList<StoredVector>? _corpus;

    public RetrievalIntentResolver(
        IVectorStore store, IEmbeddingProvider embedder,
        int topK, double floor, double minTopScore, TimeSpan budget)
    {
        _store = store;
        _embedder = embedder;
        _topK = topK;
        _floor = floor;
        _minTopScore = minTopScore;
        _budget = budget;
    }

    /// <summary>Non-null when retrieval is unavailable, e.g. "reindex required".
    /// Surfaced by `ai status`; never printed per command.</summary>
    public string? Unavailable { get; private set; }

    public AiResolution? Resolve(string input, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input) || ct.IsCancellationRequested)
                return null;

            var status = _store.GetStatus(_embedder.ModelId);
            if (status.ReindexRequired)
            {
                Unavailable = status.DocumentCount == 0
                    ? "index is empty - run 'ai reindex'"
                    : $"index was built with '{status.IndexedModelId}' but '{_embedder.ModelId}' is loaded - run 'ai reindex'";
                return null;
            }

            _corpus ??= _store.LoadAll(_embedder.ModelId);
            if (_corpus.Count == 0)
                return null;

            var watch = Stopwatch.StartNew();
            var query = Similarity.Normalize(_embedder.Embed(input));

            if (ct.IsCancellationRequested || watch.Elapsed > _budget)
                return null;

            Unavailable = null;
            return NeighbourVote.Decide(_corpus, query, _topK, _floor, _minTopScore);
        }
        catch (Exception ex)
        {
            Unavailable = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }
}
```

- [ ] **Step 5: Run the resolver tests — expect pass**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter ResolverTests
```

Expected: 6 passed.

- [ ] **Step 6: Commit**

```bash
git add CodeIntelligence CodeIntelligence.Tests
git commit -m "feat(ai): add RetrievalIntentResolver with fail-to-null semantics"
```

---

### Task 6: ONNX embedding provider, tokenizer and model files

The only task that touches ONNX Runtime. Also sets up Git LFS.

**Files:**
- Create: `CodeIntelligence.Onnx/CodeIntelligence.Onnx.csproj`, `BertTokenizerAdapter.cs`, `OnnxEmbeddingProvider.cs`, `ModelRegistry.cs`
- Create: `CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8/model.onnx`, `vocab.txt`
- Create: `.gitattributes`
- Create: `CodeIntelligence.Tests/OnnxEmbeddingTests.cs`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx`, `CodeIntelligence.Tests/CodeIntelligence.Tests.csproj`

**Interfaces:**
- Consumes: `IEmbeddingProvider`, `ITokenizer`
- Produces: `OnnxEmbeddingProvider(string modelPath, string vocabPath, string modelId)`; `ModelRegistry.DefaultEmbeddingModel` returning `(string ModelPath, string VocabPath, string ModelId, int Dimension)`; `ModelRegistry.Sha256(string path)`

- [ ] **Step 1: Configure Git LFS and fetch the model**

```bash
git lfs install
```

Create `.gitattributes` at the repo root:

```
*.onnx filter=lfs diff=lfs merge=lfs -text
```

Download into `CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8/`:

```bash
mkdir -p CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8
curl -L -o CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8/model.onnx \
  https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model_quantized.onnx
curl -L -o CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8/vocab.txt \
  https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/vocab.txt
```

**Verify before continuing** — these upstream paths are not guaranteed stable, and a 404 saved as `model.onnx` is an HTML page that fails later with a confusing ONNX parse error:

```bash
ls -l CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8/
file CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8/model.onnx
```

Expected: `model.onnx` roughly 20–25 MB (not a few KB), `vocab.txt` roughly 230 KB. If the download 404s, find the current quantized ONNX export under `Xenova/all-MiniLM-L6-v2` or `sentence-transformers/all-MiniLM-L6-v2` and record the URL actually used in this plan file.

Record the checksum for Step 4:

```bash
sha256sum CodeIntelligence.Onnx/Models/all-MiniLM-L6-v2-int8/model.onnx
```

- [ ] **Step 2: Create the ONNX csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The ONLY project permitted to reference OnnxRuntime. ArchitectureTests
       enforces that the bridge and the CodeIntelligence core stay clear of it, which
       is what makes "AI disabled" mean "AI absent from the load path". -->
  <ItemGroup>
    <ProjectReference Include="..\CodeIntelligence.Abstractions\CodeIntelligence.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.20.1" />
    <PackageReference Include="Microsoft.ML.Tokenizers" Version="1.0.2" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Models\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

If either package version fails to restore, run `dotnet add CodeIntelligence.Onnx package Microsoft.ML.OnnxRuntime` and pin whatever stable version it resolves, then update this plan.

Add the project to the slnx and add a `ProjectReference` to it from the test csproj.

- [ ] **Step 3: Write the failing ONNX tests**

`CodeIntelligence.Tests/OnnxEmbeddingTests.cs`. These are unconditional — the model is committed, so CI has it:

```csharp
using CodeIntelligence.Onnx;
using CodeIntelligence.Retrieval;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class OnnxEmbeddingTests
{
    private static OnnxEmbeddingProvider Create()
    {
        var model = ModelRegistry.DefaultEmbeddingModel;
        return new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);
    }

    [Fact]
    public void Model_files_are_present_in_the_output()
    {
        var model = ModelRegistry.DefaultEmbeddingModel;

        Assert.True(File.Exists(model.ModelPath), $"missing {model.ModelPath}");
        Assert.True(File.Exists(model.VocabPath), $"missing {model.VocabPath}");
    }

    [Fact]
    public void Embedding_has_the_declared_dimension()
    {
        using var provider = Create();
        Assert.Equal(384, provider.Embed("list classes").Length);
    }

    [Fact]
    public void Embedding_is_unit_length()
    {
        using var provider = Create();
        var vector = provider.Embed("list classes");

        var magnitude = Math.Sqrt(vector.Sum(v => (double)v * v));
        Assert.Equal(1.0, magnitude, 3);
    }

    [Fact]
    public void Embedding_is_deterministic()
    {
        using var provider = Create();
        Assert.Equal(provider.Embed("list classes"), provider.Embed("list classes"));
    }

    [Fact]
    public void Paraphrases_score_higher_than_unrelated_text()
    {
        using var provider = Create();

        var anchor = provider.Embed("list the classes in the project");
        var near = provider.Embed("show me all classes");
        var far = provider.Embed("the weather in Paris is cold");

        Assert.True(Similarity.Cosine(anchor, near) > Similarity.Cosine(anchor, far),
            "a paraphrase must be nearer than unrelated text");
    }
}
```

- [ ] **Step 4: Run them — expect failure**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter OnnxEmbeddingTests
```

Expected: FAIL, `ModelRegistry` does not exist.

- [ ] **Step 5: Implement the registry**

`CodeIntelligence.Onnx/ModelRegistry.cs` — paste the checksum from Step 1 into `ExpectedSha256`:

```csharp
using System.Security.Cryptography;

namespace CodeIntelligence.Onnx;

public sealed record EmbeddingModelInfo(
    string ModelId, string ModelPath, string VocabPath, int Dimension, string ExpectedSha256);

/// <summary>Resolves model files relative to the running binary and identifies the
/// weights. The id ends up in the index: vectors from different models are not
/// comparable, so changing the model must invalidate the index rather than silently
/// return nonsense neighbours.</summary>
public static class ModelRegistry
{
    private const string FolderName = "all-MiniLM-L6-v2-int8";

    // Filled in from `sha256sum` after downloading. Empty disables the check.
    private const string ExpectedSha256 = "";

    public static EmbeddingModelInfo DefaultEmbeddingModel
    {
        get
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Models", FolderName);

            return new EmbeddingModelInfo(
                ModelId: FolderName,
                ModelPath: Path.Combine(root, "model.onnx"),
                VocabPath: Path.Combine(root, "vocab.txt"),
                Dimension: 384,
                ExpectedSha256: ExpectedSha256);
        }
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Null when usable, otherwise the reason — reported once by `ai status`,
    /// never per command.</summary>
    public static string? Validate(EmbeddingModelInfo model)
    {
        if (!File.Exists(model.ModelPath))
            return $"model file not found: {model.ModelPath}";

        if (!File.Exists(model.VocabPath))
            return $"vocab file not found: {model.VocabPath}";

        if (model.ExpectedSha256.Length > 0 &&
            !string.Equals(Sha256(model.ModelPath), model.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            return "model checksum mismatch - the file is corrupt or was replaced";

        return null;
    }
}
```

- [ ] **Step 6: Implement the tokenizer adapter**

`CodeIntelligence.Onnx/BertTokenizerAdapter.cs`:

```csharp
using CodeIntelligence.Abstractions;
using Microsoft.ML.Tokenizers;

namespace CodeIntelligence.Onnx;

/// <summary>Wraps the library WordPiece tokenizer behind our own interface. The
/// boundary is ours and stays swappable; the fiddly parts — unknown-token fallback,
/// max-chars-per-word, accent stripping — are not ours to get subtly wrong, and a
/// subtle tokenizer bug degrades retrieval silently rather than loudly.</summary>
public sealed class BertTokenizerAdapter : ITokenizer
{
    private readonly BertTokenizer _tokenizer;

    public BertTokenizerAdapter(string vocabPath)
        => _tokenizer = BertTokenizer.Create(vocabPath);

    public IReadOnlyList<int> Encode(string text)
        => _tokenizer.EncodeToIds(text ?? string.Empty);
}
```

If `BertTokenizer.Create(string)` does not exist in the resolved package version, check the available factory overloads with `dotnet build` errors as a guide — the type is `Microsoft.ML.Tokenizers.BertTokenizer` and it exposes a vocab-file factory plus `EncodeToIds`.

- [ ] **Step 7: Implement the embedding provider**

`CodeIntelligence.Onnx/OnnxEmbeddingProvider.cs`:

```csharp
using CodeIntelligence.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CodeIntelligence.Onnx;

/// <summary>Sentence embeddings from a MiniLM ONNX export.
///
/// The model emits one vector per token; a sentence vector is the attention-masked
/// mean of those, then L2-normalized so cosine similarity is a plain dot product.
/// This mean-pool-then-normalize recipe is what sentence-transformers does, and
/// skipping either step produces vectors that rank badly rather than obviously
/// wrongly — which is why the unit-length and paraphrase tests exist.
///
/// One session is created lazily and shared: InferenceSession.Run is thread-safe.</summary>
public sealed class OnnxEmbeddingProvider : IEmbeddingProvider
{
    private const int MaxTokens = 256;

    private readonly Lazy<InferenceSession> _session;
    private readonly Lazy<BertTokenizerAdapter> _tokenizer;

    public string ModelId { get; }
    public int Dimension => 384;

    public OnnxEmbeddingProvider(string modelPath, string vocabPath, string modelId)
    {
        ModelId = modelId;
        _session = new Lazy<InferenceSession>(() => new InferenceSession(modelPath));
        _tokenizer = new Lazy<BertTokenizerAdapter>(() => new BertTokenizerAdapter(vocabPath));
    }

    public float[] Embed(string text)
    {
        var ids = _tokenizer.Value.Encode(text ?? string.Empty).Take(MaxTokens).ToArray();
        if (ids.Length == 0)
            return new float[Dimension];

        var length = ids.Length;

        var inputIds = new DenseTensor<long>([1, length]);
        var attentionMask = new DenseTensor<long>([1, length]);
        var tokenTypeIds = new DenseTensor<long>([1, length]);

        for (var i = 0; i < length; i++)
        {
            inputIds[0, i] = ids[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        using var outputs = _session.Value.Run(inputs);
        var hidden = outputs.First().AsTensor<float>();

        // Mean-pool over the token axis. Every token is unmasked here because the
        // input is a single un-padded sequence, so this is a plain mean.
        var pooled = new float[Dimension];
        for (var token = 0; token < length; token++)
            for (var dim = 0; dim < Dimension; dim++)
                pooled[dim] += hidden[0, token, dim];

        for (var dim = 0; dim < Dimension; dim++)
            pooled[dim] /= length;

        double sum = 0;
        foreach (var value in pooled)
            sum += (double)value * value;

        var magnitude = (float)Math.Sqrt(sum);
        if (magnitude > 0)
            for (var dim = 0; dim < Dimension; dim++)
                pooled[dim] /= magnitude;

        return pooled;
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
            _session.Value.Dispose();
    }
}
```

If `Run` throws about an unexpected input name, print the actual names with `session.InputMetadata.Keys` — some MiniLM exports omit `token_type_ids`, in which case drop that entry from `inputs`.

- [ ] **Step 8: Run the ONNX tests — expect pass**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter OnnxEmbeddingTests
```

Expected: 5 passed. `Paraphrases_score_higher_than_unrelated_text` is the one that proves pooling and normalization are right — if it fails while the others pass, the pooling loop is the suspect, not the model.

- [ ] **Step 9: Commit**

```bash
git add .gitattributes CodeIntelligence.Onnx CodeIntelligence.Tests SmartCodeGenerator/SmartCodeGenerator.slnx
git commit -m "feat(ai): add the ONNX MiniLM embedding provider and commit model files via LFS"
```

Verify LFS actually took the file — a 23MB blob in normal git history is hard to undo later:

```bash
git lfs ls-files
```

Expected: `model.onnx` listed.

---

### Task 7: Config and the AiLayer composition root

**Files:**
- Create: `CodeIntelligence/AiConfig.cs`, `CodeIntelligence/AiLayer.cs`, `CodeIntelligence/Storage/DecisionLog.cs`
- Create: `CodeIntelligence.Tests/ConfigTests.cs`

**Interfaces:**
- Produces: `AiConfig` with `Enabled`, `ResolverMode`, `ConfidenceFloor`, `MinTopScore`, `TopK`, `EmbeddingModelId`, `RetrievalTimeoutMs`; `AiConfig.Load()`, `AiConfig.Save()`; `AiLayer.Create()` returning `AiLayer`; `AiLayer.Resolver` (nullable `IIntentResolver`); `AiLayer.StatusText()`; `AiLayer.Reindex()`; `AiLayer.Record(...)`; `DecisionLog.Append(...)`, `DecisionLog.Last()`

- [ ] **Step 1: Write the failing config tests**

`CodeIntelligence.Tests/ConfigTests.cs`:

```csharp
using CodeIntelligence;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-config-" + Guid.NewGuid().ToString("N"));

    public ConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Path_ => Path.Combine(_dir, "ai.json");

    [Fact]
    public void Defaults_are_disabled_retrieval_mode()
    {
        var config = AiConfig.Load(Path_);

        Assert.False(config.Enabled);
        Assert.Equal("Retrieval", config.ResolverMode);
        Assert.Equal(5, config.TopK);
        Assert.Equal(0.55, config.ConfidenceFloor, 3);
        Assert.Equal(0.35, config.MinTopScore, 3);
        Assert.True(config.ConfidenceFloor > 0.5,
            "an inclusive floor at or below 0.5 would accept two-way ambiguous phrases");
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var config = AiConfig.Load(Path_);
        config.Enabled = true;
        config.ConfidenceFloor = 0.72;
        config.Save(Path_);

        var reloaded = AiConfig.Load(Path_);

        Assert.True(reloaded.Enabled);
        Assert.Equal(0.72, reloaded.ConfidenceFloor, 3);
    }

    [Fact]
    public void A_corrupt_config_falls_back_to_defaults_instead_of_throwing()
    {
        File.WriteAllText(Path_, "{ this is not json");

        var config = AiConfig.Load(Path_);

        Assert.False(config.Enabled);
    }
}
```

- [ ] **Step 2: Run them — expect failure**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter ConfigTests
```

Expected: FAIL, `AiConfig` does not exist.

- [ ] **Step 3: Implement AiConfig**

`CodeIntelligence/AiConfig.cs`:

```csharp
using System.Text.Json;

namespace CodeIntelligence;

/// <summary>ai.json, kept beside intents.json in the binary folder — the same place
/// and pattern IntentRepository already uses. Defaults are OFF: phase A ships dark and
/// is switched on per machine once its decision log shows real accuracy.</summary>
public sealed class AiConfig
{
    public bool Enabled { get; set; }
    public string ResolverMode { get; set; } = "Retrieval";
    public double ConfidenceFloor { get; set; } = 0.55;

    /// <summary>Gate 1: the minimum absolute cosine the nearest neighbour must reach
    /// before any resolution is offered. Independent of ConfidenceFloor, which only
    /// measures neighbour agreement — see NeighbourVote. Provisional; Task 10 calibrates.</summary>
    public double MinTopScore { get; set; } = 0.35;

    public int TopK { get; set; } = 5;
    public string EmbeddingModelId { get; set; } = "all-MiniLM-L6-v2-int8";
    public int RetrievalTimeoutMs { get; set; } = 500;

    public static string DefaultPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai.json");

    /// <summary>Never throws. A missing or unreadable file yields defaults, because a
    /// broken config must leave the CLI working exactly as it does with AI off.</summary>
    public static AiConfig Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
                return new AiConfig();

            return JsonSerializer.Deserialize<AiConfig>(File.ReadAllText(path)) ?? new AiConfig();
        }
        catch (Exception)
        {
            return new AiConfig();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            File.WriteAllText(path,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Saving config must never take down the REPL.
        }
    }
}
```

- [ ] **Step 4: Implement the decision log**

`CodeIntelligence/Storage/DecisionLog.cs`:

```csharp
using CodeIntelligence.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodeIntelligence.Storage;

public sealed record LoggedDecision(
    DateTime CreatedUtc, string Input, string Intent, double Confidence,
    string EvidenceIds, bool? Accepted);

/// <summary>Append-only audit of every AI resolution and the user's answer. This is
/// both the traceability record behind `ai why` and the labelled data a later phase
/// needs to calibrate an auto-execute threshold from measurement instead of a guess.</summary>
public sealed class DecisionLog
{
    private readonly string _databasePath;

    public DecisionLog(string databasePath) => _databasePath = databasePath;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        VectorSchema.EnsureCreated(connection);
        return connection;
    }

    public void Append(string input, AiResolution resolution, bool? accepted)
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ai_decision(created_utc, input, intent, confidence, evidence_ids, accepted)
                VALUES($c, $i, $n, $f, $e, $a)
                """;
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$i", input);
            cmd.Parameters.AddWithValue("$n", resolution.Intent.ToString());
            cmd.Parameters.AddWithValue("$f", resolution.Confidence);
            cmd.Parameters.AddWithValue("$e", string.Join(',', resolution.Evidence.Select(x => x.DocumentId)));
            cmd.Parameters.AddWithValue("$a", accepted.HasValue ? accepted.Value ? 1 : 0 : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Audit is best-effort; losing a row must never fail the command.
        }
    }

    public LoggedDecision? Last()
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT created_utc, input, intent, confidence, evidence_ids, accepted
                FROM ai_decision ORDER BY id DESC LIMIT 1
                """;

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return new LoggedDecision(
                DateTime.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5) == 1);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public int Count()
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ai_decision";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
```

- [ ] **Step 5: Implement AiLayer**

`CodeIntelligence/AiLayer.cs`. This is the single place ONNX types are named, so add the `CodeIntelligence.Onnx` project reference to `CodeIntelligence.csproj`:

```xml
    <ProjectReference Include="..\CodeIntelligence.Onnx\CodeIntelligence.Onnx.csproj" />
```

**Divergence from the spec, resolved here.** The spec's dependency table lists `CodeIntelligence` as depending on Abstractions, Sqlite and the engine — but it also places the composition root in `CodeIntelligence/AiLayer.cs`, and composing an ONNX provider requires naming its type. The table and the composition point cannot both hold. The plan resolves it by letting `CodeIntelligence` reference the **Onnx project** while `ArchitectureTests` continues to forbid it a direct `Microsoft.ML.OnnxRuntime` **package** reference. The property that actually matters is untouched: `NaturalLanguageBridge` still never sees ONNX, so the rule-based path carries no AI dependency. Update the spec's table to match when this task lands.

```csharp
using CodeIntelligence.Abstractions;
using CodeIntelligence.Corpus;
using CodeIntelligence.Onnx;
using CodeIntelligence.Resolution;
using CodeIntelligence.Storage;

namespace CodeIntelligence;

/// <summary>Composition root and the single catch-all boundary for the AI layer.
///
/// Construction never throws: if the model is missing, the runtime fails to load, or
/// anything else goes wrong, Resolver stays null and Unavailable carries the reason.
/// A null Resolver is what the bridge already treats as "no AI", so every failure
/// degrades to exactly today's behavior.</summary>
public sealed class AiLayer
{
    private AiLayer(AiConfig config, string indexPath)
    {
        Config = config;
        IndexPath = indexPath;
        DecisionLog = new DecisionLog(indexPath);
    }

    public AiConfig Config { get; }
    public string IndexPath { get; }
    public DecisionLog DecisionLog { get; }
    public IIntentResolver? Resolver { get; private set; }
    public string? Unavailable { get; private set; }

    private IEmbeddingProvider? _embedder;
    private SqliteVectorStore? _store;

    public static AiLayer Create(string? configPath = null, string? indexPath = null)
    {
        var config = AiConfig.Load(configPath);
        indexPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai-index.db");

        var layer = new AiLayer(config, indexPath);
        layer.Initialize();
        return layer;
    }

    private void Initialize()
    {
        try
        {
            _store = new SqliteVectorStore(IndexPath);

            var model = ModelRegistry.DefaultEmbeddingModel;
            var problem = ModelRegistry.Validate(model);
            if (problem is not null)
            {
                Unavailable = problem;
                return;
            }

            _embedder = new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);

            if (!Config.Enabled)
            {
                Unavailable = "disabled - run 'ai enable'";
                return;
            }

            Resolver = new RetrievalIntentResolver(
                _store, _embedder, Config.TopK, Config.ConfidenceFloor, Config.MinTopScore,
                TimeSpan.FromMilliseconds(Config.RetrievalTimeoutMs));
        }
        catch (Exception ex)
        {
            // Typically a native load failure: wrong architecture, or a missing
            // Visual C++ runtime. Report it, do not rethrow.
            Unavailable = $"{ex.GetType().Name}: {ex.Message}";
            Resolver = null;
        }
    }

    /// <summary>Rebuilds the corpus and its embeddings. ReplaceAll is one transaction,
    /// so an interrupted reindex leaves the previous index intact and serving.</summary>
    public string Reindex()
    {
        try
        {
            if (_embedder is null || _store is null)
                return $"AI unavailable: {Unavailable ?? "not initialized"}";

            var documents = CorpusHarvester.Harvest();
            var rows = documents.Select(d => (d, _embedder.Embed(d.Text))).ToList();

            _store.ReplaceAll(_embedder.ModelId, _embedder.Dimension, rows);
            Initialize();

            return $"Indexed {rows.Count} phrasings with '{_embedder.ModelId}'.";
        }
        catch (Exception ex)
        {
            return $"Reindex failed [{ex.GetType().Name}]: {ex.Message}. Previous index left intact.";
        }
    }

    public string StatusText()
    {
        if (_store is null)
            return $"AI: unavailable ({Unavailable ?? "not initialized"})";

        var modelId = _embedder?.ModelId ?? Config.EmbeddingModelId;
        var status = _store.GetStatus(modelId);

        var state = Resolver is not null
            ? "enabled"
            : $"off ({Unavailable ?? "unknown reason"})";

        return $"""
            AI: {state}
            Mode: {Config.ResolverMode}   floor: {Config.ConfidenceFloor:0.00}   topK: {Config.TopK}
            Model: {modelId}
            Index: {status.DocumentCount} phrasings{(status.ReindexRequired ? " - REINDEX REQUIRED ('ai reindex')" : "")}
            Built: {(status.BuiltUtc.HasValue ? status.BuiltUtc.Value.ToString("u") : "never")}
            Decisions logged: {DecisionLog.Count()}
            """;
    }
}
```

- [ ] **Step 6: Run the config tests — expect pass**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter ConfigTests
```

Expected: 3 passed.

- [ ] **Step 7: Commit**

```bash
git add CodeIntelligence CodeIntelligence.Tests
git commit -m "feat(ai): add ai.json config, the decision log and the AiLayer composition root"
```

---

### Task 8: The CommandFront seam and the isolation invariant

The task that proves nothing existing broke.

**Files:**
- Modify: `NaturalLanguageBridge/NaturalLanguageBridge.csproj`, `NaturalLanguageBridge/CommandFront.cs:214`, `NaturalLanguageBridge/NaturalLanguagePipeline.cs:20`
- Create: `CodeIntelligence.Tests/SeamTests.cs`

**Interfaces:**
- Consumes: `IIntentResolver`, `AiResolution`
- Produces: `CommandFront.RunDetailed(..., IIntentResolver? aiResolver = null, Func<AiResolution,bool>? confirmAi = null, CancellationToken ct = default)`; the same three parameters on `NaturalLanguagePipeline.Handle`

- [ ] **Step 1: Reference Abstractions from the bridge**

Add to `NaturalLanguageBridge/NaturalLanguageBridge.csproj`:

```xml
    <ProjectReference Include="..\CodeIntelligence.Abstractions\CodeIntelligence.Abstractions.csproj" />
```

This must stay the only AI reference the bridge has — `ArchitectureTests.NaturalLanguageBridge_does_not_reference_onnx` guards it.

- [ ] **Step 2: Write the failing seam tests**

`CodeIntelligence.Tests/SeamTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using NaturalLanguageBridge;
using Xunit;

namespace CodeIntelligence.Tests;

/// <summary>Records every call so a test can assert the resolver was never reached.</summary>
public sealed class SpyResolver : IIntentResolver
{
    public List<string> Calls { get; } = [];
    public AiResolution? Next { get; init; }

    public AiResolution? Resolve(string input, CancellationToken ct)
    {
        Calls.Add(input);
        return Next;
    }
}

public sealed class SeamTests
{
    private static AiResolution Resolution(IntentType intent) =>
        new(intent, 0.9, [new RetrievedExample(1, "list classes", intent, 0.9)], "test");

    /// <summary>Every phrasing the rule-based path already recognizes today.</summary>
    public static TheoryData<string> RecognizedCommands()
    {
        var data = new TheoryData<string>();

        // ResetLearning phrasings are excluded deliberately: RunDetailed EXECUTES the
        // command, and "reset learning" would wipe the learned-intent store as a side
        // effect of an assertion about the resolver never being called. The invariant
        // is unaffected — these still route through the literal table, never the AI.
        foreach (var intent in LiteralCommandTable.Intents.Where(i => i != IntentType.ResetLearning))
            foreach (var phrase in LiteralCommandTable.PhrasesFor(intent))
                data.Add(phrase);

        data.Add("list classes in MyProject");
        data.Add("use project MyProject");
        data.Add("Order inherits AuditableBase");
        data.Add("rename property Name to Title in Customer");
        data.Add("add business validation Email must not be blank in Customer");
        data.Add("generate validator for Customer");

        return data;
    }

    [Theory]
    [MemberData(nameof(RecognizedCommands))]
    public void The_resolver_is_never_consulted_for_a_recognized_command(string input)
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListClasses) };
        ProjectMetaData? project = null;

        CommandFront.RunDetailed(
            input, ref project, [], Path.GetTempPath(), _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => true);

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public void Unrecognized_input_consults_the_resolver()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = null };
        ProjectMetaData? project = null;

        CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], Path.GetTempPath(), _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => true);

        Assert.Single(spy.Calls);
    }

    [Fact]
    public void Declining_the_confirmation_leaves_the_command_unrecognized()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListClasses) };
        ProjectMetaData? project = null;

        var result = CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], Path.GetTempPath(), _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => false);

        Assert.False(result.Recognized);
    }

    [Fact]
    public void Accepting_the_confirmation_marks_the_command_recognized()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListProjects) };
        ProjectMetaData? project = null;

        var result = CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], Path.GetTempPath(), _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: _ => true);

        Assert.True(result.Recognized);
    }

    [Fact]
    public void With_no_resolver_unrecognized_input_stays_unrecognized()
    {
        IntentRepository.Initialize();
        ProjectMetaData? project = null;

        var result = CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], Path.GetTempPath(), _ => { });

        Assert.False(result.Recognized);
    }

    [Fact]
    public void A_resolver_without_a_confirm_delegate_is_never_consulted()
    {
        IntentRepository.Initialize();

        var spy = new SpyResolver { Next = Resolution(IntentType.ListClasses) };
        ProjectMetaData? project = null;

        CommandFront.RunDetailed(
            "flurble the wotsit", ref project, [], Path.GetTempPath(), _ => { },
            onAnalyzed: null, aiResolver: spy, confirmAi: null);

        Assert.Empty(spy.Calls);
    }
}
```

Add to `CodeIntelligence.Tests/CodeIntelligence.Tests.csproj`:

```xml
    <ProjectReference Include="..\NaturalLanguageBridge\NaturalLanguageBridge.csproj" />
```

- [ ] **Step 3: Run them — expect failure**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter SeamTests
```

Expected: FAIL to compile — `RunDetailed` has no `aiResolver` parameter.

- [ ] **Step 4: Add the seam to CommandFront**

In `NaturalLanguageBridge/CommandFront.cs`, add `using CodeIntelligence.Abstractions;` at the top, then replace the body of `RunDetailed` (currently lines 214–237):

```csharp
    /// <summary>As <see cref="Run"/>, but also reports whether the input was
    /// recognized as a command (see <see cref="RunResult"/>).
    ///
    /// <paramref name="aiResolver"/> is consulted ONLY when rule-based parsing yields
    /// Unknown, and only when <paramref name="confirmAi"/> is also supplied — so the AI
    /// can never change, delay or degrade a command that already works today. With both
    /// null this method behaves exactly as it did before the AI layer existed.</summary>
    public static RunResult RunDetailed(
        string input,
        ref ProjectMetaData? project,
        List<PropertyRule> configuredRules,
        string workspace,
        Action<ProjectMetaData?> setLastCreatedProject,
        Action<SemanticResult?>? onAnalyzed = null,
        IIntentResolver? aiResolver = null,
        Func<AiResolution, bool>? confirmAi = null,
        CancellationToken ct = default)
    {
        if ((input ?? string.Empty).Trim().Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            var (demoCode, _) = CommandHandlingService.BuildDemo();
            Console.WriteLine(demoCode.Replace("//", string.Empty));
            onAnalyzed?.Invoke(null);
            return new RunResult(true, true);
        }

        if (onAnalyzed is not null)
            onAnalyzed(NlpActionProvider.Analyze(input ?? string.Empty));

        var command = Parse(input ?? string.Empty, workspace);

        if (command.Intent == IntentType.Unknown && aiResolver is not null && confirmAi is not null)
        {
            var resolution = aiResolver.Resolve(input ?? string.Empty, ct);

            if (resolution is not null &&
                resolution.Intent != IntentType.Unknown &&
                confirmAi(resolution))
            {
                command = CommandEntityExtractor.Extract(
                    input ?? string.Empty, resolution.Intent, workspace);
                command.Confidence = resolution.Confidence;
            }
        }

        var recognized = command.Intent != IntentType.Unknown;
        var handled = CommandHandlingService.Execute(command, ref project, configuredRules, workspace, setLastCreatedProject);
        return new RunResult(handled, recognized);
    }
```

- [ ] **Step 5: Thread the parameters through the pipeline**

Replace `NaturalLanguagePipeline.Handle` in `NaturalLanguageBridge/NaturalLanguagePipeline.cs`, adding `using CodeIntelligence.Abstractions;`:

```csharp
    public PipelineOutcome Handle(
        string input,
        ref ProjectMetaData? project,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        Action<SemanticResult?>? onAnalyzed = null,
        IIntentResolver? aiResolver = null,
        Func<AiResolution, bool>? confirmAi = null,
        CancellationToken ct = default)
    {
        SemanticResult? captured = null;
        var result = CommandFront.RunDetailed(
            input, ref project, configuredRules, workspaceRoot, setLastCreatedProject,
            a => { captured = a; onAnalyzed?.Invoke(a); },
            aiResolver, confirmAi, ct);

        return new PipelineOutcome(result.Handled, captured, result.Recognized);
    }
```

- [ ] **Step 6: Run the seam tests — expect pass**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter SeamTests
```

Expected: all pass, including one theory case per literal phrasing.

- [ ] **Step 7: Run the entire existing suite — this is the regression gate**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: the pre-existing 1657 tests still pass, plus the new ones. Any pre-existing test that now fails is a real regression from the seam — fix it before continuing rather than adjusting the test.

- [ ] **Step 8: Commit**

```bash
git add NaturalLanguageBridge CodeIntelligence.Tests
git commit -m "feat(ai): add the optional resolver seam to CommandFront and the pipeline"
```

---

### Task 9: The `ai` commands and CLI confirmation

**Files:**
- Modify: `CodeGenerationEngine/Models/IntentType.cs` (append only), `NaturalLanguageBridge/LiteralCommandTable.cs`, `CodeGenerationEngine/Service/CommandHandlingService.cs`, `CodeGenerator/App/CodeGeneratorStarter.cs:92,180`, `CodeGenerator/CodeGenerator.csproj`
- Create: `CodeGenerator/App/AiCommand.cs`, `CodeGenerator/App/AiConfirmPrompt.cs`

**Interfaces:**
- Consumes: `AiLayer.Create()`, `AiLayer.Resolver`, `AiLayer.StatusText()`, `AiLayer.Reindex()`, `AiLayer.DecisionLog`, `SelectionPrompt.Choose`
- Produces: `AiCommand.IsAiCommand(string)`, `AiCommand.Handle(string, AiLayer)`, `AiConfirmPrompt.Confirm(AiResolution)`

- [ ] **Step 1: Append the AI intents**

At the very END of `CodeGenerationEngine/Models/IntentType.cs`, after `ShowBusinessValidation`:

```csharp
    // AI layer. Appended last — IntentType members are persisted as integers by the
    // learned-intent store, so inserting anywhere above corrupts saved data.
    AiStatus,
    AiEnable,
    AiDisable,
    AiReindex,
    AiMode,
    AiWhy
```

Add a comma after `ShowBusinessValidation`.

- [ ] **Step 2: Handle the `ai` commands in the CLI**

`ai` commands are handled in the CLI before the pipeline, like `history` and `clear` — they are console-shaped meta-commands, and routing them through `CommandHandlingService` would push console formatting into the engine.

`CodeGenerator/App/AiCommand.cs`:

```csharp
using CodeIntelligence;

namespace CodeGenerator.App;

/// <summary>The `ai ...` meta-commands. Handled in the CLI beside `history` and
/// `clear`, because they are console reports rather than code-generation actions.</summary>
internal static class AiCommand
{
    internal static bool IsAiCommand(string input)
    {
        var text = (input ?? string.Empty).Trim();

        return text.Equals("ai", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("ai ", StringComparison.OrdinalIgnoreCase);
    }

    internal static string Handle(string input, AiLayer layer)
    {
        var parts = (input ?? string.Empty)
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";

        switch (verb)
        {
            case "status":
                return layer.StatusText();

            case "enable":
                layer.Config.Enabled = true;
                layer.Config.Save();
                return "AI enabled. Restart the CLI to load the model, then run 'ai reindex' if prompted.";

            case "disable":
                layer.Config.Enabled = false;
                layer.Config.Save();
                return "AI disabled. Command recognition is unchanged from the rule-based path.";

            case "reindex":
                return layer.Reindex();

            case "mode":
                if (parts.Length < 3)
                    return "Usage: ai mode <retrieval|generative|hybrid>";

                var mode = parts[2].ToLowerInvariant();
                if (mode is not ("retrieval" or "generative" or "hybrid"))
                    return $"Unknown mode '{parts[2]}'. Use retrieval, generative or hybrid.";

                if (mode is "generative" or "hybrid")
                    return $"Mode '{mode}' arrives in phase B. Only 'retrieval' is available today.";

                layer.Config.ResolverMode = "Retrieval";
                layer.Config.Save();
                return "Mode set to Retrieval.";

            case "why":
                var last = layer.DecisionLog.Last();
                if (last is null)
                    return "No AI decisions recorded yet.";

                var verdict = last.Accepted switch
                {
                    true => "accepted",
                    false => "declined",
                    null => "not answered"
                };

                return $"""
                    Input:      {last.Input}
                    Read as:    {last.Intent} ({last.Confidence:P0} confidence)
                    Evidence:   corpus documents {last.EvidenceIds}
                    You:        {verdict}
                    Recorded:   {last.CreatedUtc:u}
                    """;

            default:
                return $"Unknown ai command '{verb}'. Try: status, enable, disable, reindex, mode, why.";
        }
    }
}
```

- [ ] **Step 3: Add the confirmation prompt**

`CodeGenerator/App/AiConfirmPrompt.cs`:

```csharp
using CodeIntelligence.Abstractions;

namespace CodeGenerator.App;

/// <summary>Asks the user to confirm an AI reading before it executes. Confirmation is
/// mandatory in phase A: a wrong silent resolution could mutate code, and the y/n
/// answers are the labelled data a later phase needs to calibrate a threshold.</summary>
internal static class AiConfirmPrompt
{
    internal static bool Confirm(AiResolution resolution)
    {
        var closest = resolution.Evidence.Count > 0 ? resolution.Evidence[0].Text : "(none)";

        var title =
            $"Not a known command. Closest match \"{closest}\" suggests: " +
            $"{resolution.Intent} ({resolution.Confidence:P0} confidence). Run it?";

        var choice = SelectionPrompt.Choose(title, ["Yes, run it", "No, leave it"], defaultIndex: 1);

        return choice == 0;
    }
}
```

- [ ] **Step 4: Wire the CLI**

In `CodeGenerator/CodeGenerator.csproj` add:

```xml
    <ProjectReference Include="..\CodeIntelligence\CodeIntelligence.csproj" />
```

In `CodeGenerator/App/CodeGeneratorStarter.cs`, add `using CodeIntelligence;` and a field beside `_pipeline` (line 24):

```csharp
    private readonly AiLayer _ai = AiLayer.Create();
```

In `Run()`, add an `ai` branch beside the `history` branch (after line 158):

```csharp
            if (AiCommand.IsAiCommand(input))
            {
                Console.WriteLine(AiCommand.Handle(input, _ai));
                continue;
            }
```

Replace the `_pipeline.Handle` call in `Run()` (line 180). Console.IsInputRedirected is the gate: a piped or scripted run must never block on a prompt, which also keeps scripted E2E runs deterministic and model-free:

```csharp
                var outcome = _pipeline.Handle(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r,
                     onAnalyzed: null,
                     aiResolver: Console.IsInputRedirected ? null : _ai.Resolver,
                     confirmAi: Console.IsInputRedirected ? null : Confirm);
```

Add the confirm helper to the class, which is also where the decision is logged:

```csharp
    /// <summary>Prompts for an AI reading and records the answer. The decision log is
    /// written whichever way the user answers — a decline is as informative as an
    /// accept for calibrating a later auto-execute threshold.</summary>
    private bool Confirm(CodeIntelligence.Abstractions.AiResolution resolution)
    {
        var accepted = AiConfirmPrompt.Confirm(resolution);
        _ai.DecisionLog.Append(_lastInput, resolution, accepted);
        return accepted;
    }

    private string _lastInput = string.Empty;
```

Set `_lastInput = input;` immediately before the `_pipeline.Handle` call in `Run()`, and `_lastInput = command;` before the one in `RunOnce`.

`RunOnce` (line 92) is left passing no resolver — scripted single-shot invocations stay rule-based only.

**Honest note on cancellation.** The spec promises that `exit` cancels an in-flight resolve. In a synchronous REPL that guarantee is close to vacuous: `exit` is read at the prompt, so no resolve is running when it arrives. The `ct` parameter is threaded end-to-end and honored by the resolver, but the CLI passes `default` here — nothing wires a session token yet. Do not claim the cancellation behavior is delivered until something actually cancels. The parameter earns its place by bounding the phase-B generative path, where a resolve genuinely can run for seconds.

- [ ] **Step 5: Report AI state in the banner**

Find `PrintBanner` in `CodeGeneratorStarter.cs` and add a line beside the existing updater-service line, so a missing model is reported once per session rather than on every unrecognized command:

```csharp
        Console.WriteLine(_ai.Resolver is not null
            ? "AI: enabled (retrieval)"
            : $"AI: off ({_ai.Unavailable ?? "disabled"})");
```

- [ ] **Step 6: Build and exercise it by hand**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
```

Expected: clean. Then run the CLI and check the sequence:

```
ai status      -> AI: off (disabled - run 'ai enable')
ai enable      -> AI enabled...
```

Restart the CLI, then:

```
ai reindex     -> Indexed N phrasings with 'all-MiniLM-L6-v2-int8'.
ai status      -> AI: enabled, Index: N phrasings
show me every class      -> confirmation prompt
ai why                   -> the recorded decision
```

Note: rebuild `CodeGenerator` before trusting CLI behavior — `dotnet test` alone leaves its `bin` stale, and a working fix can look dead.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine CodeGenerator NaturalLanguageBridge
git commit -m "feat(ai): add the ai commands, confirmation prompt and CLI wiring"
```

---

### Task 10: Golden-set accuracy and calibrating the floor

Sets `ConfidenceFloor` from measurement rather than the provisional 0.55, and locks in a regression floor.

**Files:**
- Create: `CodeIntelligence.Tests/Fixtures/golden-set.tsv`, `CodeIntelligence.Tests/GoldenSetTests.cs`
- Modify: `CodeIntelligence/AiConfig.cs`, `CodeIntelligence.Tests/CodeIntelligence.Tests.csproj`
- Modify: `docs/superpowers/specs/2026-08-06-slm-intelligence-layer-design.md`

**Interfaces:**
- Consumes: `CorpusHarvester.Harvest()`, `OnnxEmbeddingProvider`, `SqliteVectorStore`, `RetrievalIntentResolver`

- [ ] **Step 1: Write the golden set**

`CodeIntelligence.Tests/Fixtures/golden-set.tsv` — tab-separated `phrasing<TAB>ExpectedIntent`. These must be **paraphrases users would plausibly type, not corpus entries**; a golden set drawn from the corpus measures nothing. Start with these 30 and extend toward 80:

```
show me every class	ListClasses
what classes exist	ListClasses
give me a list of the classes	ListClasses
i want to see the classes	ListClasses
make a new class called Invoice	CreateClass
i need a class for orders	CreateClass
add a class named Payment	CreateClass
get rid of the Customer class	DeleteClass
remove the class Invoice	DeleteClass
drop the Order class	DeleteClass
what projects do i have	ListProjects
show all my projects	ListProjects
which project am i in	CurrentProject
what project is active	CurrentProject
start a new project called Billing	CreateProject
set up a project named Shop	CreateProject
switch to the Billing project	UseProject
open project Shop	UseProject
what properties does Customer have	ListProperties
show the fields on Invoice	ListProperties
add a field called Total to Invoice	CreateProperty
put an Email property on Customer	CreateProperty
what methods are on Customer	ListMethods
list the functions in Invoice	ListMethods
check that the project builds	ValidateProject
does my project compile	ValidateProject
look at the Customer class for problems	AnalyzeClass
inspect the Invoice class	AnalyzeClass
find classes nobody uses	FindUnusedClasses
which classes are unused	FindUnusedClasses
```

Add to the test csproj:

```xml
  <ItemGroup>
    <None Update="Fixtures\golden-set.tsv" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the measurement test**

`CodeIntelligence.Tests/GoldenSetTests.cs`. It prints the accuracy sweep so Step 4 can read real numbers off it:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Onnx;
using CodeIntelligence.Resolution;
using CodeIntelligence.Storage;
using Xunit;
using Xunit.Abstractions;

namespace CodeIntelligence.Tests;

public sealed class GoldenSetTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-golden-" + Guid.NewGuid().ToString("N"));

    public GoldenSetTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static IReadOnlyList<(string Text, IntentType Expected)> GoldenSet()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-set.tsv");

        return File.ReadAllLines(path)
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Split('\t'))
            .Select(parts => (parts[0].Trim(), Enum.Parse<IntentType>(parts[1].Trim())))
            .ToList();
    }

    private RetrievalIntentResolver BuildResolver(double floor)
    {
        IntentRepository.Initialize();

        var model = ModelRegistry.DefaultEmbeddingModel;
        var embedder = new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);
        var store = new SqliteVectorStore(Path.Combine(_dir, "golden.db"));

        var documents = CorpusHarvester.Harvest();
        store.ReplaceAll(embedder.ModelId, embedder.Dimension,
            [.. documents.Select(d => (d, embedder.Embed(d.Text)))]);

        return new RetrievalIntentResolver(store, embedder, topK: 5, floor: floor,
            minTopScore: minTopScore, budget: TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Report_accuracy_across_candidate_floors()
    {
        var golden = GoldenSet();

        foreach (var floor in new[] { 0.0, 0.3, 0.4, 0.5, 0.55, 0.6, 0.7 })
        {
            var resolver = BuildResolver(floor);
            int correct = 0, wrongConfident = 0, declined = 0;

            foreach (var (text, expected) in golden)
            {
                var result = resolver.Resolve(text, CancellationToken.None);

                if (result is null) declined++;
                else if (result.Intent == expected) correct++;
                else wrongConfident++;
            }

            _output.WriteLine(
                $"floor {floor:0.00}: correct {correct}/{golden.Count}, " +
                $"wrong-and-confident {wrongConfident}, declined {declined}");
        }
    }

    [Fact]
    public void Accuracy_does_not_regress_below_the_calibrated_floor()
    {
        // CALIBRATE IN STEP 4: replace these three constants with the measured
        // numbers, minus a small margin. They are a regression gate, not a target.
        const double floor = 0.55;
        const int minimumCorrect = 0;
        const int maximumWrongConfident = int.MaxValue;

        var golden = GoldenSet();
        var resolver = BuildResolver(floor);

        int correct = 0, wrongConfident = 0;

        foreach (var (text, expected) in golden)
        {
            var result = resolver.Resolve(text, CancellationToken.None);
            if (result is null) continue;

            if (result.Intent == expected) correct++;
            else wrongConfident++;
        }

        Assert.True(correct >= minimumCorrect,
            $"top-1 correct fell to {correct}, below the calibrated {minimumCorrect}");
        Assert.True(wrongConfident <= maximumWrongConfident,
            $"wrong-and-confident rose to {wrongConfident}, above the calibrated {maximumWrongConfident}");
    }
}
```

- [ ] **Step 3: Run the sweep and read the numbers**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter GoldenSetTests --logger "console;verbosity=detailed"
```

Expected: the per-floor report prints. Both tests pass, but `Accuracy_does_not_regress_below_the_calibrated_floor` is vacuous until Step 4 — that is deliberate and temporary.

- [ ] **Step 4: Calibrate**

Pick the floor with the best correct count at a **wrong-and-confident count of roughly 5% of the set or lower** — declining is cheap, a confident wrong answer is not. Then:

1. Set `ConfidenceFloor`'s default in `CodeIntelligence/AiConfig.cs` to the chosen floor.
2. In `GoldenSetTests`, set `floor` to the same value, `minimumCorrect` to the measured count minus 2, and `maximumWrongConfident` to the measured count plus 1.
3. Record the measured numbers in the spec's "On thresholds" section, replacing the promise to measure with the measurement.

- [ ] **Step 5: Re-run and confirm the gate is real**

```
dotnet test CodeIntelligence.Tests/CodeIntelligence.Tests.csproj --filter GoldenSetTests
```

Expected: pass. Then confirm the gate bites — temporarily set `minimumCorrect` above the measured count, re-run, see it FAIL, and restore. A regression gate never observed failing is not known to be a gate.

- [ ] **Step 6: Full suite, clean build**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: clean build, all tests pass including the pre-existing 1657.

- [ ] **Step 7: Commit**

```bash
git add CodeIntelligence CodeIntelligence.Tests docs/superpowers/specs/2026-08-06-slm-intelligence-layer-design.md
git commit -m "test(ai): add the golden-set accuracy gate and calibrate the confidence floor"
```

---

## Done when

- `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror` is clean.
- The pre-existing 1657 tests still pass.
- `The_resolver_is_never_consulted_for_a_recognized_command` passes for every literal phrasing.
- With `Enabled=false`, an unrecognized command prints exactly today's message.
- With `Enabled=true` and an index built, an unrecognized paraphrase offers a confirmation, and `ai why` explains it afterwards.
- `git lfs ls-files` lists `model.onnx`.

## Deferred to phase B

Generative provider, prompt construction, `GenerativeIntentResolver`, and the `ai mode generative|hybrid` paths — which today return "arrives in phase B". Phase B's plan is written after phase A's decision log has real data in it.
