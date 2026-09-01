# Resuming an Unfinished Build Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the browser tab, come back, and pick up where you left off — with the reviewed model exactly as you had edited it.

**Architecture:** A snapshot of the *answers* and the *reviewed model* is written to `.codegen/build-in-progress.json` after every applied answer and every model edit. It never holds the rows that were read, nor the connection string. `BuildSession` gains an opt-in `Autosave` callback so no existing test is affected; the Review panel calls `ModelEdited()` because its edits bypass `Change()` entirely. Resuming re-reads the source; a source that can no longer be read drops the workflow onto the Data stage rather than failing.

**Tech Stack:** .NET 10, `System.Text.Json`, xunit (DataIngestion.Tests), MSTest + bUnit (CodeGenerator.Tests).

**Spec:** `docs/superpowers/specs/2026-09-01-resume-an-unfinished-build-design.md`

## Global Constraints

- Target framework `net10.0`, `Nullable=enable`, `ImplicitUsings=enable` from `Directory.Build.props`. Do not add properties there.
- **No new package.** Everything here uses `System.Text.Json`, already referenced.
- **Test framework differs per project.** `CodeGenerator.Tests` is **MSTest** (`[TestClass]`, `[TestMethod]`, `Assert.AreEqual`) plus bUnit. `DataIngestion.Tests` is **xunit** (`[Fact]`, `Assert.Equal(expected, actual)`). Everything in this plan lives in `CodeGenerator.Tests`, so **MSTest throughout**.
- MSTest analyzer MSTEST0037 rejects `Assert.AreEqual(n, x.Count)` — use `Assert.HasCount(n, x)`. It also rejects `Assert.IsFalse(s.Contains(x))` — use `Assert.DoesNotContain(x, s)`.
- xUnit analyzer xUnit2031 rejects `.Where(...)` before `Assert.Single` — not applicable here, but the same class of analyzer error is a `-warnaserror` failure.
- CI runs exactly: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror` then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build`. A warning is an error there.
- **A catalog `Example` is printed on one line and must satisfy `Example.Length + 11 < 81`** — so ≤ 69 characters. A `Description` may wrap; an `Example` may not. This cost a red build on the previous branch.
- **`docs/commands.md` and the manual's chapter-18 appendix are BOTH generated from `CommandDictionary`.** Never hand-edit either. Regenerate with `UPDATE_COMMAND_DOCS=1 … --filter CommandReferenceDocTests` and `UPDATE_MANUAL_APPENDIX=1 … --filter ManualAppendix_MatchesTheCatalog`, then rebuild the `.docx`. Forgetting the second one reddened a green suite on the branch before last.
- When reading test output, grep `"^  Failed "` as well as `Failed!` — the summary line matches `Failed!` and the per-test line does not, so filtering on `Failed!` alone loses the name of what broke.
- **New types are `internal`**, matching `OverwritePrompt` and `BuildAppCommand`. The
  `CodeGenerator.csproj` has `<InternalsVisibleTo Include="CodeGenerator.Tests" />`, so tests
  reach them without anything being public.
- Branch is `resume-a-build`. Commit after every task.

---

### Task 1: The snapshot, and its round trip

**Files:**
- Create: `CodeGenerator/App/Resume/BuildSnapshot.cs`
- Test: `CodeGenerator.Tests/App/Resume/BuildSnapshotTests.cs`

**Interfaces:**
- Consumes: `BuildSession`, `AppCustomization`, `CandidateDomainModel`, `DataSourceSpec`.
- Produces, in namespace `CodeGenerator.App.Resume`:
  - `internal sealed class BuildSnapshot` with public settable properties (a JSON DTO, nullable throughout)
  - `public static BuildSnapshot Of(BuildSession session)`
  - `public CandidateDomainModel? ToModel()`
  - `public AppCustomization ToCustomization()`
  - `public const int CurrentSchemaVersion = 1`

**Why a DTO and not the model itself:** every collection on `CandidateDomainModel` is get-only (`public List<CandidateEntity> Entities { get; } = new();`) so `System.Text.Json` has nothing to deserialise into, and `CandidateEntity.Name`, `CandidateField.Name/Type` and `CandidateRelationship.Target/Role` are all `required`. Nullable throughout, so a file written by an older build fills a missing field from the default rather than with a null.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App.Resume;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App.Resume;

/// <summary>
/// What a snapshot carries, and — as much as anything here — what it must never carry.
/// </summary>
[TestClass]
public class BuildSnapshotTests
{
    [TestMethod]
    public void Carries_the_answers()
    {
        var snapshot = BuildSnapshot.Of(SessionWithModel());

        Assert.AreEqual("a shop", snapshot.Description);
        Assert.AreEqual("wpf", snapshot.AppType);
        Assert.AreEqual(BuildSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.AreEqual(nameof(BuildStage.Review), snapshot.Stage);
    }

    /// <summary>
    /// The edits are the whole point: discovery can be re-run, half an hour of renaming cannot.
    /// </summary>
    [TestMethod]
    public void Carries_the_model_with_its_edits()
    {
        var session = SessionWithModel();

        // The edits a person actually makes on the review stage.
        DomainDiscovery.Review.ModelEditor.RenameEntity(session.Model!, "Book", "Title");
        DomainDiscovery.Review.ModelEditor.AddField(session.Model!, "Title", "Isbn", "string");

        var restored = BuildSnapshot.Of(session).ToModel();

        Assert.IsNotNull(restored);
        var entity = restored!.Entities.Single(e => e.Name == "Title");
        Assert.IsNotEmpty(entity.Fields.Where(f => f.Name == "Isbn"));
    }

    [TestMethod]
    public void Carries_relationships_and_suggestions_and_the_decision_trail()
    {
        var session = SessionWithModel();
        var book = session.Model!.Entities.Single();
        book.Relationships.Add(new CandidateRelationship { Target = "Shelf", Role = "references" });
        session.Model.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept, "read");

        var restored = BuildSnapshot.Of(session).ToModel()!;

        Assert.IsNotEmpty(restored.Entities.Single().Relationships);
        Assert.IsNotEmpty(restored.Suggestions);
        Assert.IsNotEmpty(restored.Decisions);
    }

    [TestMethod]
    public void Carries_the_customization_including_the_screen_split()
    {
        var session = SessionWithModel();
        session.SetCustomization(AppCustomization.Default with
        {
            Company = "Codis Ltd", Masters = "OrderLine", Transactions = "Customer"
        });

        var restored = BuildSnapshot.Of(session).ToCustomization();

        Assert.AreEqual("Codis Ltd", restored.Company);
        Assert.AreEqual("OrderLine", restored.Masters);
        Assert.AreEqual("Customer", restored.Transactions);
    }

    [TestMethod]
    public void A_file_source_keeps_its_path()
    {
        var session = SessionWith(new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv"));

        var snapshot = BuildSnapshot.Of(session);

        Assert.AreEqual("Csv", snapshot.SourceKind);
        Assert.AreEqual(@"D:\data\orders.csv", snapshot.SourcePath);
        Assert.IsNull(snapshot.SourceDatabase);
    }

    /// <summary>
    /// The promise the manual makes as of the SQL Server branch: the connection string is never
    /// written to .codegen. A test, not an intention.
    /// </summary>
    [TestMethod]
    public void A_sql_source_keeps_the_database_name_and_never_the_connection_string()
    {
        var session = SessionWith(new DataSourceSpec(
            DataSourceKind.Sql,
            "Server=.;Database=Northwind;User ID=sa;Password=hunter2",
            SqlDatabase: "Northwind"));

        var snapshot = BuildSnapshot.Of(session);

        Assert.AreEqual("Sql", snapshot.SourceKind);
        Assert.AreEqual("Northwind", snapshot.SourceDatabase);
        Assert.IsNull(snapshot.SourcePath);

        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);

        Assert.DoesNotContain("hunter2", json);
        Assert.DoesNotContain("Server=", json);
    }

    /// <summary>
    /// The other promise: the file stays a few KB whatever the data size, because the rows are
    /// re-read rather than stored.
    /// </summary>
    [TestMethod]
    public void No_row_of_data_is_written_however_much_there_was()
    {
        var session = SessionWith(new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv"));

        var imported = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
        {
            ["Book"] = [.. Enumerable.Range(0, 25_000).Select(
                i => (IReadOnlyDictionary<string, string?>)
                    new Dictionary<string, string?> { ["Title"] = $"row {i}" })]
        };

        session.SetImportedData(new DataIngestion.Schema.ImportedData(imported));

        var json = System.Text.Json.JsonSerializer.Serialize(BuildSnapshot.Of(session));

        Assert.DoesNotContain("row 1000", json);
        Assert.IsLessThan(20_000, json.Length, $"the snapshot grew with the data: {json.Length}");
    }

    private static BuildSession SessionWithModel() => SessionWith(null);

    private static BuildSession SessionWith(DataSourceSpec? source)
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();

        if (source is null)
            session.SetNoData();
        else
            session.SetSource(source);

        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(AModel());

        while (session.Current != BuildStage.Review)
            session.Advance();

        return session;
    }

    private static CandidateDomainModel AModel()
    {
        var model = new CandidateDomainModel();

        var book = new CandidateEntity { Name = "Book", Provenance = Provenance.Wiki };
        book.Fields.Add(new CandidateField
        {
            Name = "Title", Type = "string", Provenance = Provenance.Wiki
        });

        model.Entities.Add(book);
        model.Suggestions.Add(new CandidateEntity { Name = "Shelf", Provenance = Provenance.Wiki });

        return model;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSnapshotTests"`
Expected: FAIL to compile — `BuildSnapshot` does not exist.

**If `ImportedData`'s constructor shape differs from the test above**, read `DataIngestion/Schema/ImportedData.cs` and adjust the fixture. Do not change the assertion: what matters is that a large `ImportedData` produces a small snapshot.

- [ ] **Step 3: Write minimal implementation**

```csharp
using AppScaffolding;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.App.Resume;

/// <summary>
/// An unfinished build, as it is written to disk.
/// </summary>
/// <remarks>
/// A DTO, not the session. Nullable throughout so a file written by an older build fills a missing
/// field from the default rather than with a null or a zero — the shape
/// <c>CustomizationDefaults.Profile</c> already takes, for the same reason.
/// <para>
/// <see cref="CandidateDomainModel"/> cannot be serialised directly: every collection on it is
/// get-only, so <c>System.Text.Json</c> has nothing to deserialise into, and its leaf types use
/// <c>required</c> members. Hence the parallel shapes below.
/// </para>
/// <para>
/// Two things are deliberately absent, and both are asserted by tests rather than left as
/// intentions: the ROWS that were read (re-read on resume, so the file stays small whatever the
/// data size) and the CONNECTION STRING (the manual promises it is never written to
/// <c>.codegen/</c>).
/// </para>
/// </remarks>
internal sealed class BuildSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? Description { get; set; }

    /// <summary>The target's KEY ("wpf"), not a type name — a type name in a file would break the
    /// moment a class moved.</summary>
    public string? AppType { get; set; }

    public string? Stage { get; set; }

    public string? Company { get; set; }
    public string? Theme { get; set; }
    public string? Accent { get; set; }
    public string? TitlePattern { get; set; }
    public string? IconPath { get; set; }
    public bool? Validation { get; set; }
    public bool? Logging { get; set; }
    public bool? SampleSeedData { get; set; }
    public bool? Login { get; set; }
    public string? Transactions { get; set; }
    public string? Masters { get; set; }
    public string? NamespacePrefix { get; set; }
    public string? Layout { get; set; }

    public string? SourceKind { get; set; }

    /// <summary>Set for every kind but SQL.</summary>
    public string? SourcePath { get; set; }

    /// <summary>Set for SQL alone, and the ONLY thing kept about a SQL connection.</summary>
    public string? SourceDatabase { get; set; }

    public List<Entity>? Entities { get; set; }
    public List<Entity>? Suggestions { get; set; }
    public List<string>? Rejected { get; set; }
    public List<Decision>? Decisions { get; set; }

    public sealed class Entity
    {
        public string? Name { get; set; }
        public string? Provenance { get; set; }
        public double? Confidence { get; set; }
        public string? SourceTitle { get; set; }
        public List<Field>? Fields { get; set; }
        public List<Relationship>? Relationships { get; set; }
    }

    public sealed class Field
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Provenance { get; set; }
    }

    public sealed class Relationship
    {
        public string? Target { get; set; }
        public string? Role { get; set; }
    }

    public sealed class Decision
    {
        public string? Name { get; set; }
        public string? Stage { get; set; }
        public string? Outcome { get; set; }
        public string? Reason { get; set; }
        public string? Evidence { get; set; }
    }

    public static BuildSnapshot Of(BuildSession session)
    {
        var customization = session.Customization;

        var snapshot = new BuildSnapshot
        {
            Description = session.Description,
            AppType = session.Target?.Key,
            Stage = session.Current.ToString(),

            Company = customization.Company,
            Theme = customization.Theme.ToString(),
            Accent = customization.Accent,
            TitlePattern = customization.TitlePattern,
            IconPath = customization.IconPath,
            Validation = customization.Validation,
            Logging = customization.Logging,
            SampleSeedData = customization.SampleSeedData,
            Login = customization.Login,
            Transactions = customization.Transactions,
            Masters = customization.Masters,
            NamespacePrefix = customization.NamespacePrefix,
            Layout = customization.Layout?.ToString(),
        };

        if (session.Source is { } source)
        {
            snapshot.SourceKind = source.Kind.ToString();

            // The one branch that matters. A SQL location IS a connection string, so only the
            // database name survives — see the class remarks.
            if (source.Kind == DataSourceKind.Sql)
                snapshot.SourceDatabase = source.SqlDatabase
                    ?? SqlConnectionInfo.DatabaseOf(source.Location);
            else
                snapshot.SourcePath = source.Location;
        }

        if (session.Model is { } model)
        {
            snapshot.Entities = [.. model.Entities.Select(ToDto)];
            snapshot.Suggestions = [.. model.Suggestions.Select(ToDto)];
            snapshot.Rejected = [.. model.Rejected];
            snapshot.Decisions = [.. model.Decisions.Select(d => new Decision
            {
                Name = d.Name,
                Stage = d.Stage.ToString(),
                Outcome = d.Outcome.ToString(),
                Reason = d.Reason,
                Evidence = d.Evidence,
            })];
        }

        return snapshot;
    }

    /// <summary>The customization this snapshot holds, with anything missing taken from the
    /// defaults.</summary>
    public AppCustomization ToCustomization()
    {
        var fallback = AppCustomization.Default;

        return new AppCustomization(
            Company: Company ?? fallback.Company,
            Theme: Enum.TryParse<AppTheme>(Theme, out var theme) ? theme : fallback.Theme,
            Accent: Accent ?? fallback.Accent,
            TitlePattern: TitlePattern ?? fallback.TitlePattern,
            IconPath: IconPath ?? fallback.IconPath,
            Validation: Validation ?? fallback.Validation,
            Logging: Logging ?? fallback.Logging,
            SampleSeedData: SampleSeedData ?? fallback.SampleSeedData,
            Login: Login ?? fallback.Login,
            Transactions: Transactions ?? fallback.Transactions,
            Masters: Masters ?? fallback.Masters,
            NamespacePrefix: NamespacePrefix ?? fallback.NamespacePrefix,
            Layout: Enum.TryParse<AppScaffolding.Layouts.AppLayout>(Layout, out var layout)
                ? layout
                : fallback.Layout);
    }

    /// <summary>The model this snapshot holds, or null when it was taken before discovery ran.</summary>
    public CandidateDomainModel? ToModel()
    {
        if (Entities is null)
            return null;

        var model = new CandidateDomainModel();

        foreach (var entity in Entities)
            if (FromDto(entity) is { } candidate)
                model.Entities.Add(candidate);

        foreach (var suggestion in Suggestions ?? [])
            if (FromDto(suggestion) is { } candidate)
                model.Suggestions.Add(candidate);

        foreach (var name in Rejected ?? [])
            model.Rejected.Add(name);

        foreach (var decision in Decisions ?? [])
        {
            if (decision.Name is null || decision.Reason is null)
                continue;

            model.Record(
                decision.Name,
                Enum.TryParse<DecisionStage>(decision.Stage, out var stage)
                    ? stage
                    : DecisionStage.SeedExtraction,
                Enum.TryParse<DecisionOutcome>(decision.Outcome, out var outcome)
                    ? outcome
                    : DecisionOutcome.Kept,
                decision.Reason,
                decision.Evidence);
        }

        return model;
    }

    private static Entity ToDto(CandidateEntity entity) => new()
    {
        Name = entity.Name,
        Provenance = entity.Provenance.ToString(),
        Confidence = entity.Confidence,
        SourceTitle = entity.SourceTitle,
        Fields = [.. entity.Fields.Select(f => new Field
        {
            Name = f.Name, Type = f.Type, Provenance = f.Provenance.ToString()
        })],
        Relationships = [.. entity.Relationships.Select(r => new Relationship
        {
            Target = r.Target, Role = r.Role
        })],
    };

    /// <summary>
    /// One entity back, or null when the file did not name it.
    /// </summary>
    /// <remarks>
    /// A nameless entity is dropped rather than defaulted: <c>CandidateEntity.Name</c> is
    /// <c>required</c>, and inventing a name would put a class in the model the user never had.
    /// </remarks>
    private static CandidateEntity? FromDto(Entity dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return null;

        var entity = new CandidateEntity
        {
            Name = dto.Name,
            Provenance = Enum.TryParse<Provenance>(dto.Provenance, out var provenance)
                ? provenance
                : Provenance.Wiki,
            Confidence = dto.Confidence ?? 0,
            SourceTitle = dto.SourceTitle,
        };

        foreach (var field in dto.Fields ?? [])
        {
            if (field.Name is null || field.Type is null)
                continue;

            entity.Fields.Add(new CandidateField
            {
                Name = field.Name,
                Type = field.Type,
                Provenance = Enum.TryParse<Provenance>(field.Provenance, out var p)
                    ? p
                    : Provenance.Wiki,
            });
        }

        foreach (var relationship in dto.Relationships ?? [])
        {
            if (relationship.Target is null || relationship.Role is null)
                continue;

            entity.Relationships.Add(new CandidateRelationship
            {
                Target = relationship.Target, Role = relationship.Role
            });
        }

        return entity;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSnapshotTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/Resume/BuildSnapshot.cs CodeGenerator.Tests/App/Resume/BuildSnapshotTests.cs
git commit -m "feat: an unfinished build has a shape on disk"
```

---

### Task 2: The store, and the ignore line

**Files:**
- Create: `CodeGenerator/App/Resume/BuildSnapshotStore.cs`
- Modify: `.gitignore`
- Test: `CodeGenerator.Tests/App/Resume/BuildSnapshotStoreTests.cs`

**Interfaces:**
- Consumes: `BuildSnapshot` (Task 1).
- Produces: `internal static class BuildSnapshotStore` with
  `PathFor(string workspaceRoot)`, `BuildSnapshot? Load(string workspaceRoot)`,
  `void Save(string workspaceRoot, BuildSnapshot snapshot)`, `void Clear(string workspaceRoot)`,
  and `public const string FileName = "build-in-progress.json"`.

**Never throws**, in either direction — `CustomizationDefaults` and `AiConfig.Load` set that precedent, because a broken config must leave the CLI working exactly as it does without one. Here it applies to saving too: an unwritable `.codegen` means no resume, not a dead command.

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerator.App.Resume;

namespace CodeGenerator.Tests.App.Resume;

[TestClass]
public class BuildSnapshotStoreTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "resume-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Nothing_saved_means_nothing_to_load()
        => Assert.IsNull(BuildSnapshotStore.Load(_root));

    [TestMethod]
    public void What_was_saved_comes_back()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot { Description = "a shop" });

        Assert.AreEqual("a shop", BuildSnapshotStore.Load(_root)!.Description);
    }

    [TestMethod]
    public void It_is_written_where_the_other_codegen_files_live()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot());

        var path = BuildSnapshotStore.PathFor(_root);

        Assert.IsTrue(File.Exists(path));
        StringAssert.Contains(path, ".codegen");
        StringAssert.Contains(path, BuildSnapshotStore.FileName);
    }

    [TestMethod]
    public void Clearing_removes_it()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot());
        BuildSnapshotStore.Clear(_root);

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }

    [TestMethod]
    public void Clearing_what_was_never_saved_is_not_an_error()
    {
        BuildSnapshotStore.Clear(_root);
        BuildSnapshotStore.Clear(_root);
    }

    /// <summary>
    /// A broken config must leave the CLI working exactly as it does without one — the rule
    /// CustomizationDefaults and AiConfig.Load already follow.
    /// </summary>
    [TestMethod]
    public void A_corrupt_file_reads_as_nothing_rather_than_throwing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BuildSnapshotStore.PathFor(_root))!);
        File.WriteAllText(BuildSnapshotStore.PathFor(_root), "{ this is not json");

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }

    /// <summary>
    /// A file from a NEWER build is refused whole rather than partially read. Half a model is
    /// worse than no offer, and someone running two versions must not have the older one quietly
    /// drop what the newer one saved.
    /// </summary>
    [TestMethod]
    public void A_snapshot_from_a_later_schema_is_refused()
    {
        BuildSnapshotStore.Save(_root, new BuildSnapshot
        {
            SchemaVersion = BuildSnapshot.CurrentSchemaVersion + 1,
            Description = "from the future",
        });

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }

    [TestMethod]
    public void Saving_over_an_unwritable_path_is_not_an_error()
    {
        // A file where the folder should be: creating .codegen must fail, and the command must
        // carry on regardless.
        File.WriteAllText(Path.Combine(_root, ".codegen"), "in the way");

        BuildSnapshotStore.Save(_root, new BuildSnapshot { Description = "a shop" });

        Assert.IsNull(BuildSnapshotStore.Load(_root));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSnapshotStoreTests"`
Expected: FAIL to compile — `BuildSnapshotStore` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGenerator.App.Resume;

/// <summary>
/// The one unfinished build a workspace can be holding, in
/// <c>.codegen/build-in-progress.json</c>.
/// </summary>
/// <remarks>
/// Beside <c>app-profile.json</c> and <c>learned.json</c>, and never throwing in either
/// direction. A broken file must leave the CLI working exactly as it does without one — the rule
/// <c>CustomizationDefaults</c> and <c>AiConfig.Load</c> already follow — and the same applies to
/// writing: an unwritable <c>.codegen</c> means no resume, not a dead command.
/// </remarks>
internal static class BuildSnapshotStore
{
    public const string FileName = "build-in-progress.json";
    private const string FolderName = ".codegen";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, FolderName, FileName);

    public static BuildSnapshot? Load(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            if (!File.Exists(path))
                return null;

            var snapshot = JsonSerializer.Deserialize<BuildSnapshot>(
                File.ReadAllText(path), Options);

            if (snapshot is null)
                return null;

            // Refused whole, not partially read: half a model is worse than no offer, and someone
            // running two versions of the tool must not have the older one quietly drop what the
            // newer one saved.
            return snapshot.SchemaVersion > BuildSnapshot.CurrentSchemaVersion ? null : snapshot;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or JsonException
                                      or NotSupportedException)
        {
            return null;
        }
    }

    public static void Save(string workspaceRoot, BuildSnapshot snapshot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No resume, rather than no build.
        }
    }

    public static void Clear(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale snapshot is offered again next time, which is recoverable. Throwing here
            // would fail a build that has already succeeded.
        }
    }
}
```

- [ ] **Step 4: Add the ignore line**

`.codegen/` is **not** ignored in this repository — `.codegen/learned.json` is tracked deliberately — so ignore the file, not the folder. In `.gitignore`, under the `# Smart Code Generator` heading beside `.codegen_history`:

```
# In-progress build state. The folder itself is NOT ignored: .codegen/learned.json is committed.
.codegen/build-in-progress.json
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSnapshotStoreTests"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/Resume/BuildSnapshotStore.cs CodeGenerator.Tests/App/Resume/BuildSnapshotStoreTests.cs .gitignore
git commit -m "feat: a workspace holds one unfinished build, and a broken file is no file"
```

---

### Task 3: The session saves itself

**Files:**
- Modify: `CodeGenerator/Web/Workflow/BuildSession.cs`
- Test: `CodeGenerator.Tests/Web/BuildSessionAutosaveTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces on `BuildSession`:
  - `public Action<BuildSession>? Autosave { get; set; }`
  - `public void ModelEdited()`

**Why a callback and not a call to the store:** `BuildSession` is in `CodeGenerator.Web.Workflow` and knows nothing about disk, and **2470 existing tests construct sessions**. A null-by-default callback leaves every one of them untouched; a store call inside the session would have all of them writing files.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// When the session offers itself up to be saved.
/// </summary>
[TestClass]
public class BuildSessionAutosaveTests
{
    [TestMethod]
    public void An_answer_asks_to_be_saved()
    {
        var saves = 0;
        var session = New();
        session.Autosave = _ => saves++;

        session.SetDescription("a library");

        Assert.IsGreaterThan(0, saves);
    }

    /// <summary>
    /// Re-answering with the same value is not a change — the rule that makes clicking back into
    /// a node harmless — so it is not worth a write either.
    /// </summary>
    [TestMethod]
    public void Re_answering_with_the_same_value_saves_nothing()
    {
        var session = New();
        session.SetDescription("a library");

        var saves = 0;
        session.Autosave = _ => saves++;

        session.SetDescription("a library");

        Assert.AreEqual(0, saves);
    }

    /// <summary>
    /// The wrinkle this whole feature turns on. ReviewStage edits the model IN PLACE through
    /// ModelEditor, without going through Change() — so an autosave hooked to answers alone would
    /// miss every rename, deletion and promotion, which is exactly the work being protected.
    /// </summary>
    [TestMethod]
    public void A_model_edit_asks_to_be_saved()
    {
        var session = New();
        session.SetModel(new CandidateDomainModel());

        var saves = 0;
        session.Autosave = _ => saves++;

        session.ModelEdited();

        Assert.AreEqual(1, saves);
    }

    /// <summary>Every existing test constructs a session without one of these, and none of them
    /// should start writing files.</summary>
    [TestMethod]
    public void A_session_with_no_autosave_is_untroubled()
    {
        var session = New();

        session.SetDescription("a library");
        session.SetModel(new CandidateDomainModel());
        session.ModelEdited();
    }

    private static BuildSession New()
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());

        return session;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSessionAutosaveTests"`
Expected: FAIL to compile — no `Autosave`, no `ModelEdited`.

- [ ] **Step 3: Write minimal implementation**

In `BuildSession`, beside the `Changed` event:

```csharp
    /// <summary>
    /// Invoked whenever this session has changed in a way worth persisting.
    /// </summary>
    /// <remarks>
    /// A callback rather than a call to a store, for two reasons. This type is in
    /// <c>Web.Workflow</c> and knows nothing about disk — the seam keeps it that way. And every
    /// existing test constructs a session; null-by-default means none of them starts writing
    /// files.
    /// </remarks>
    public Action<BuildSession>? Autosave { get; set; }

    /// <summary>
    /// Says that the model has been edited in place, so the session can be saved.
    /// </summary>
    /// <remarks>
    /// <see cref="Change"/> cannot see these. <c>ReviewStage</c> calls <c>ModelEditor</c> directly
    /// — rename, remove, add field, promote — and every one of them mutates the model object this
    /// session already holds. Without this method an autosave hooked to answers alone would save
    /// every stage transition and lose every edit, which is precisely the work worth keeping.
    /// <para>
    /// It deliberately does not raise <see cref="Changed"/>: the panel that made the edit has
    /// already re-rendered, and the other tab is repainted by the edit's own broadcast.
    /// </para>
    /// </remarks>
    public void ModelEdited() => Autosave?.Invoke(this);
```

And at the end of `Apply`, after the invalidation sweep:

```csharp
        Autosave?.Invoke(this);
```

**Placement matters:** inside `Apply`, not `Change`. `Change` returns early when it is holding a
pending discard, and a snapshot taken then would record an answer the session has not accepted.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSessionAutosaveTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Verify nothing else moved**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSessionTests"`
Expected: PASS, unchanged.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/Web/Workflow/BuildSession.cs CodeGenerator.Tests/Web/BuildSessionAutosaveTests.cs
git commit -m "feat: the session says when it is worth saving"
```

---

### Task 4: The Review panel says it edited the model

**Files:**
- Modify: `CodeGenerator/Web/Components/Stages/ReviewStage.razor`
- Test: `CodeGenerator.Tests/Web/ReviewStageTests.cs`

**Interfaces:**
- Consumes: `BuildSession.ModelEdited()` (Task 3).
- Produces: nothing new.

**This is the task the feature exists for.** Every other task is plumbing; this is the one that
makes a rename survive a closed tab.

- [ ] **Step 1: Write the failing test**

Append to `ReviewStageTests`:

```csharp
    /// <summary>
    /// The edits are what a closed tab loses, and they do not go through Change() — the panel
    /// calls ModelEditor directly and mutates the model in place. Asserted at the PANEL rather
    /// than on the session, because a test on the session alone passes happily while the panel
    /// bypasses it, which is the defect this exists to prevent.
    /// </summary>
    [TestMethod]
    public void Every_edit_asks_the_session_to_save()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        var saves = 0;
        session.Autosave = _ => saves++;

        rendered.Find("[data-entity=Book] button.remove").Click();
        Assert.IsGreaterThan(0, saves, "remove did not ask to be saved");

        saves = 0;
        rendered.Find("input.new-class").Change("Member");
        rendered.Find("button.add-class").Click();
        Assert.IsGreaterThan(0, saves, "add class did not ask to be saved");

        saves = 0;
        rendered.Find("button.promote").Click();
        Assert.IsGreaterThan(0, saves, "promoting a suggestion did not ask to be saved");
    }

    [TestMethod]
    public void Renaming_and_field_edits_ask_the_session_to_save()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        var saves = 0;
        session.Autosave = _ => saves++;

        rendered.Find("[data-entity=Book] input.field-name").Change("Isbn");
        rendered.Find("[data-entity=Book] button.add-field").Click();
        Assert.IsGreaterThan(0, saves, "add field did not ask to be saved");

        saves = 0;
        rendered.Find("[data-entity=Book] input.entity-name").Change("Volume");
        Assert.IsGreaterThan(0, saves, "rename did not ask to be saved");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~asks_the_session_to_save"`
Expected: FAIL — `saves` stays 0. If it fails to compile instead, Task 3 is not in place.

- [ ] **Step 3: Write minimal implementation**

In `ReviewStage.razor`, the one funnel every editor call already passes through:

```csharp
    private void Report(bool changed, string ok, string fail)
    {
        _message = changed ? ok : fail;

        // Here rather than at each caller, because this is the one place all of them pass
        // through — and an edit that is not saved is the work a closed tab loses. Called even
        // when the editor refused: `changed` false means the model is as it was, and a needless
        // save is far cheaper than a missed one.
        Session.ModelEdited();
    }
```

`AddClass` and `Promote` set `_message` directly rather than calling `Report`, so both need the
same line:

```csharp
    private void AddClass()
    {
        _message = BuildAppCommand.AddLine(Model, ["add", _newClass]);
        _newClass = string.Empty;
        Session.ModelEdited();
    }

    private void Promote(string name)
    {
        _message = BuildAppCommand.AddLine(Model, ["add", name]);
        Session.ModelEdited();
    }
```

`Choose` (the screen split) already goes through `Session.SetCustomization`, which reaches
`Apply` — so it is covered by Task 3 and needs nothing here.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ReviewStageTests"`
Expected: PASS, all of them.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Components/Stages/ReviewStage.razor CodeGenerator.Tests/Web/ReviewStageTests.cs
git commit -m "feat: a review edit asks to be saved"
```

---

### Task 5: Clearing, and what is deliberately kept

**Files:**
- Modify: `CodeGenerator/Web/Workflow/BuildSession.cs`
- Test: `CodeGenerator.Tests/Web/BuildSessionAutosaveTests.cs`

**Interfaces:**
- Produces on `BuildSession`: `public Action<BuildSession>? OnFinished { get; set; }`

Two callbacks rather than one with a flag: the caller wires `Autosave` to a save and `OnFinished`
to a clear, and neither has to inspect `Phase` to know which it is.

- [ ] **Step 1: Write the failing test**

```csharp
    [TestMethod]
    public void A_completed_build_is_not_offered_again()
    {
        var finished = 0;
        var session = New();
        session.OnFinished = _ => finished++;

        session.Complete(new BuildOutcome(SessionPhase.Generated, []));

        Assert.AreEqual(1, finished);
    }

    [TestMethod]
    public void A_cancelled_build_is_not_offered_again()
    {
        var finished = 0;
        var session = New();
        session.OnFinished = _ => finished++;

        session.Cancel();

        Assert.AreEqual(1, finished);
    }

    /// <summary>
    /// The closed tab, and the entire point of the feature. Abandoning must NOT clear.
    /// </summary>
    [TestMethod]
    public void An_abandoned_build_is_kept()
    {
        var finished = 0;
        var session = New();
        session.OnFinished = _ => finished++;

        session.Abandon();

        Assert.AreEqual(0, finished);
    }
```

**Check `BuildOutcome`'s constructor** in `CodeGenerator/Web/Workflow/` before writing this — if
its shape differs, adjust the call. `Complete` is what matters, not the outcome's members.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~offered_again|FullyQualifiedName~An_abandoned_build_is_kept"`
Expected: FAIL to compile — no `OnFinished`.

- [ ] **Step 3: Write minimal implementation**

In `BuildSession`, beside `Autosave`:

```csharp
    /// <summary>
    /// Invoked when this build is over for good — generated, or cancelled.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT invoked by <see cref="Abandon"/>. A closed tab is the case the snapshot
    /// exists for; clearing on abandon would delete the work at exactly the moment it becomes
    /// worth keeping.
    /// </remarks>
    public Action<BuildSession>? OnFinished { get; set; }
```

Then add `OnFinished?.Invoke(this);` as the last line of `Cancel()` and of `Complete(...)`, and
**not** to `Abandon()`. Put a comment on `Abandon` saying so, since its absence is the deliberate
part and the next reader will otherwise assume an omission.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildSessionAutosaveTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Workflow/BuildSession.cs CodeGenerator.Tests/Web/BuildSessionAutosaveTests.cs
git commit -m "feat: a finished build is cleared, an abandoned one is kept"
```

---

### Task 6: The question, as a pure thing

**Files:**
- Create: `CodeGenerator/App/Resume/ResumeOffer.cs`
- Test: `CodeGenerator.Tests/App/Resume/ResumeOfferTests.cs`

**Interfaces:**
- Consumes: `BuildSnapshot` (Task 1).
- Produces: `internal enum ResumeChoice { Resume, Fresh, Discard }` and
  `internal static class ResumeOffer` with
  `static ResumeChoice? Parse(string? answer)` and
  `static IReadOnlyList<string> Lines(BuildSnapshot snapshot, DateTimeOffset now)`.

Pure and tested, exactly as `OverwritePrompt` is, because the console loop that asks it cannot be
driven from a test.

**The safe answer is `Fresh`, and that is not the obvious choice** — see the spec. Nothing is
deleted either way, so the hazard is not lost work: it is someone pressing Enter without reading
and then generating an application from a stale model, or from a colleague's abandoned session.
Fresh is also exactly what the tool does today.

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerator.App.Resume;

namespace CodeGenerator.Tests.App.Resume;

[TestClass]
public class ResumeOfferTests
{
    [TestMethod]
    public void The_three_answers_and_their_letters()
    {
        Assert.AreEqual(ResumeChoice.Resume, ResumeOffer.Parse("r"));
        Assert.AreEqual(ResumeChoice.Resume, ResumeOffer.Parse("resume"));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse("f"));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse("fresh"));
        Assert.AreEqual(ResumeChoice.Discard, ResumeOffer.Parse("d"));
        Assert.AreEqual(ResumeChoice.Discard, ResumeOffer.Parse("discard"));
    }

    [TestMethod]
    public void An_answer_is_read_however_it_is_cased_or_spaced()
        => Assert.AreEqual(ResumeChoice.Resume, ResumeOffer.Parse("  RESUME  "));

    /// <summary>
    /// Blank and end-of-input start fresh. Nothing is deleted either way, so the hazard is not
    /// lost work — it is someone pressing Enter without reading and then generating an app from a
    /// stale model. Fresh is also what the tool did before any of this existed.
    /// </summary>
    [TestMethod]
    public void Enter_starts_fresh_and_deletes_nothing()
    {
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse(string.Empty));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse(null));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse("   "));
    }

    /// <summary>Unrecognised returns null so the caller re-asks. A default here would make a typo
    /// choose for the user — the reasoning OverwritePrompt already sets out.</summary>
    [TestMethod]
    public void Something_else_is_not_an_answer()
    {
        Assert.IsNull(ResumeOffer.Parse("yes"));
        Assert.IsNull(ResumeOffer.Parse("x"));
    }

    [TestMethod]
    public void The_offer_says_what_is_waiting()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop",
            AppType = "wpf",
            Stage = "Generate",
            SavedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            Entities = [new BuildSnapshot.Entity { Name = "Book" }],
        };

        var text = string.Join("\n", ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow));

        StringAssert.Contains(text, "a shop");
        StringAssert.Contains(text, "Generate");
        StringAssert.Contains(text, "1 class");
        StringAssert.Contains(text, "20 minutes ago");
    }

    /// <summary>A build abandoned before discovery ran has no classes, and must not claim "0
    /// classes reviewed" as though it had been through review.</summary>
    [TestMethod]
    public void An_offer_with_no_model_yet_does_not_mention_classes()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop", AppType = "wpf", Stage = "Data",
            SavedAt = DateTimeOffset.UtcNow,
        };

        var text = string.Join("\n", ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow));

        Assert.DoesNotContain("class", text);
        StringAssert.Contains(text, "a shop");
    }

    [TestMethod]
    public void A_build_with_no_description_is_still_describable()
    {
        var snapshot = new BuildSnapshot
        {
            Description = string.Empty, AppType = "wpf", Stage = "Describe",
            SavedAt = DateTimeOffset.UtcNow,
        };

        var text = string.Join("\n", ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow));

        Assert.IsNotEmpty(text);
        StringAssert.Contains(text, "Describe");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ResumeOfferTests"`
Expected: FAIL to compile — `ResumeOffer` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace CodeGenerator.App.Resume;

/// <summary>What to do about an unfinished build found in this workspace.</summary>
internal enum ResumeChoice
{
    /// <summary>Pick it up where it was left.</summary>
    Resume,

    /// <summary>Start over. The snapshot is left where it is, so nothing is lost.</summary>
    Fresh,

    /// <summary>Delete it.</summary>
    Discard
}

/// <summary>
/// The question put when a workspace is holding an unfinished build.
/// </summary>
/// <remarks>
/// Pure and tested, as <c>OverwritePrompt</c> is, because the console loop that asks it cannot be
/// driven from a test.
/// </remarks>
internal static class ResumeOffer
{
    /// <summary>
    /// The choice <paramref name="answer"/> names, or null if it names none and the question
    /// should be put again.
    /// </summary>
    /// <remarks>
    /// Blank and end-of-input start FRESH. That is not the same reasoning as
    /// <c>OverwritePrompt</c>, where the effortless answer had to be the one that destroyed
    /// nothing: here nothing is destroyed either way, since starting fresh leaves the snapshot on
    /// disk. The hazard is the other direction — someone pressing Enter without reading, resuming
    /// a build they did not know about, and generating an application from a stale model or from a
    /// colleague's abandoned session. Fresh is also exactly what this command did before any of
    /// this existed.
    /// <para>
    /// Anything unrecognised returns null rather than falling back to a default, because a default
    /// would let a typo choose.
    /// </para>
    /// </remarks>
    public static ResumeChoice? Parse(string? answer)
    {
        var text = (answer ?? string.Empty).Trim();

        if (text.Length == 0)
            return ResumeChoice.Fresh;

        return text.ToLowerInvariant() switch
        {
            "r" or "resume" => ResumeChoice.Resume,
            "f" or "fresh" => ResumeChoice.Fresh,
            "d" or "discard" => ResumeChoice.Discard,
            _ => null
        };
    }

    /// <summary>What is waiting, and what the answers do.</summary>
    public static IReadOnlyList<string> Lines(BuildSnapshot snapshot, DateTimeOffset now)
    {
        var what = new List<string> { Described(snapshot) };

        if (snapshot.AppType is { Length: > 0 } appType)
            what.Add(appType.ToUpperInvariant());

        // Only when there IS a model: "0 classes reviewed" on a build abandoned before discovery
        // ran claims a review that never happened.
        if (snapshot.Entities is { Count: > 0 } entities)
            what.Add($"{entities.Count} {(entities.Count == 1 ? "class" : "classes")} reviewed");

        return
        [
            "An unfinished build is waiting in this workspace:",
            "  " + string.Join("  —  ", what),
            $"  left off at {snapshot.Stage}, {Ago(snapshot.SavedAt, now)}",
            "",
            "  [r]esume   start [f]resh   [d]iscard",
        ];
    }

    private static string Described(BuildSnapshot snapshot)
        => snapshot.Description is { Length: > 0 } description
            ? $"\"{description}\""
            : "no description yet";

    /// <summary>
    /// How long ago, roughly. Rough on purpose: the question it answers is "is this mine, from
    /// just now, or someone else's from last week", and a timestamp to the second reads as
    /// precision nobody asked for.
    /// </summary>
    private static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var elapsed = now - then;

        if (elapsed < TimeSpan.FromMinutes(1))
            return "moments ago";

        if (elapsed < TimeSpan.FromHours(1))
            return $"{(int)elapsed.TotalMinutes} minutes ago";

        if (elapsed < TimeSpan.FromDays(1))
            return $"{(int)elapsed.TotalHours} hours ago";

        return $"{(int)elapsed.TotalDays} days ago";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ResumeOfferTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/Resume/ResumeOffer.cs CodeGenerator.Tests/App/Resume/ResumeOfferTests.cs
git commit -m "feat: the resume question, and why Enter starts fresh"
```

---

### Task 7: Asking, and picking up where it was left

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs`
- Create: `CodeGenerator/App/Resume/ResumedSession.cs`
- Test: `CodeGenerator.Tests/App/Resume/ResumedSessionTests.cs`

**Interfaces:**
- Consumes: `BuildSnapshot`, `BuildSnapshotStore`, `ResumeOffer` (Tasks 1, 2, 6).
- Produces: `internal static class ResumedSession` with
  `static BuildSession From(BuildSnapshot snapshot, string workspaceRoot, out string? notice)`.

**Restore order matters.** Build the session, replay the answers, then set the model, then walk to
the stage — and attach `Autosave` **after** all of it, so restoring does not write a dozen partial
snapshots over the good one.

**The source cannot always be honoured.** A path that has gone, or a SQL source whose connection
string was deliberately not saved, means the workflow must open on **Data** with a notice, rather
than resuming at Generate against a source it cannot read. `notice` carries that sentence.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using CodeGenerator.App.Resume;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.App.Resume;

[TestClass]
public class ResumedSessionTests
{
    [TestMethod]
    public void The_answers_and_the_model_come_back()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop",
            AppType = "wpf",
            Stage = nameof(BuildStage.Review),
            Company = "Codis Ltd",
            Masters = "OrderLine",
            Entities = [new BuildSnapshot.Entity
            {
                Name = "Book",
                Fields = [new BuildSnapshot.Field { Name = "Title", Type = "string" }],
            }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

        Assert.AreEqual("a shop", session.Description);
        Assert.AreEqual("wpf", session.Target!.Key);
        Assert.AreEqual("Codis Ltd", session.Customization.Company);
        Assert.AreEqual("OrderLine", session.Customization.Masters);
        Assert.HasCount(1, session.Model!.Entities);
        Assert.IsNull(notice);
    }

    /// <summary>Restoring must not overwrite the good snapshot with a dozen partial ones on its
    /// way through the setters.</summary>
    [TestMethod]
    public void Restoring_saves_nothing()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop", AppType = "wpf", Stage = nameof(BuildStage.Review),
            Entities = [new BuildSnapshot.Entity { Name = "Book" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out _);

        Assert.IsNull(session.Autosave, "autosave must be attached by the caller, after restoring");
    }

    /// <summary>
    /// The connection string is never saved — the manual promises it — so a SQL build cannot
    /// resume straight to Generate. It opens on Data, and says why.
    /// </summary>
    [TestMethod]
    public void A_sql_build_comes_back_needing_its_connection_again()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "trading",
            AppType = "wpf",
            Stage = nameof(BuildStage.Generate),
            SourceKind = nameof(DataSourceKind.Sql),
            SourceDatabase = "TRADING",
            Entities = [new BuildSnapshot.Entity { Name = "Trade" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

        Assert.AreEqual(BuildStage.Data, session.Current);
        Assert.IsNotNull(notice);
        StringAssert.Contains(notice!, "TRADING");
        Assert.HasCount(1, session.Model!.Entities);
    }

    [TestMethod]
    public void A_file_that_has_gone_comes_back_on_the_data_stage()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "orders",
            AppType = "wpf",
            Stage = nameof(BuildStage.Generate),
            SourceKind = nameof(DataSourceKind.Csv),
            SourcePath = Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid() + ".csv"),
            Entities = [new BuildSnapshot.Entity { Name = "Order" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

        Assert.AreEqual(BuildStage.Data, session.Current);
        Assert.IsNotNull(notice);
        StringAssert.Contains(notice!, "gone-");
    }

    [TestMethod]
    public void A_file_still_there_resumes_where_it_was()
    {
        var path = Path.Combine(Path.GetTempPath(), "resume-" + Guid.NewGuid() + ".csv");
        File.WriteAllText(path, "Title\nDune\n");

        try
        {
            var snapshot = new BuildSnapshot
            {
                Description = "orders",
                AppType = "wpf",
                Stage = nameof(BuildStage.Review),
                SourceKind = nameof(DataSourceKind.Csv),
                SourcePath = path,
                Entities = [new BuildSnapshot.Entity { Name = "Order" }],
            };

            var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

            Assert.AreEqual(BuildStage.Review, session.Current);
            Assert.IsNull(notice);
            Assert.AreEqual(path, session.Source!.Location);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A target key from a build that knew a type this one does not must not throw.</summary>
    [TestMethod]
    public void An_unknown_app_type_comes_back_unanswered_rather_than_throwing()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop", AppType = "hologram", Stage = nameof(BuildStage.AppType),
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out _);

        Assert.IsNull(session.Target);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ResumedSessionTests"`
Expected: FAIL to compile — `ResumedSession` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.App.Resume;

/// <summary>
/// A <see cref="BuildSession"/> rebuilt from a snapshot.
/// </summary>
/// <remarks>
/// The order is the whole of it: answers, then the model, then walk to the stage. And
/// <see cref="BuildSession.Autosave"/> is left null — the caller attaches it AFTER restoring, or
/// the walk through the setters writes a dozen partial snapshots over the good one.
/// </remarks>
internal static class ResumedSession
{
    /// <summary>
    /// The session <paramref name="snapshot"/> describes.
    /// </summary>
    /// <param name="notice">
    /// What could not be honoured, or null. A data source that cannot be read again is the only
    /// case: the workflow then opens on <see cref="BuildStage.Data"/> rather than resuming against
    /// a source it has no access to.
    /// </param>
    public static BuildSession From(
        BuildSnapshot snapshot, string workspaceRoot, out string? notice)
    {
        notice = null;

        var session = new BuildSession(
            snapshot.Description ?? string.Empty, workspaceRoot, snapshot.ToCustomization());

        // Unknown key rather than a throw: a snapshot from a build that knew a target this one
        // does not leaves the question unanswered, which the AppType stage then asks.
        if (snapshot.AppType is { Length: > 0 } key
            && TargetScaffolders.TryGet(key, out var target))
            session.SetTarget(target);

        var source = Restorable(snapshot, out notice);

        if (source is not null)
            session.SetSource(source);
        else if (snapshot.SourceKind is null)
            session.SetNoData();

        session.SetCustomization(snapshot.ToCustomization());

        if (snapshot.ToModel() is { } model)
            session.SetModel(model);

        session.GoTo(StageToResumeAt(snapshot, notice, session));

        return session;
    }

    /// <summary>
    /// The source this snapshot can be read from again, or null with a reason in
    /// <paramref name="notice"/>.
    /// </summary>
    private static DataSourceSpec? Restorable(BuildSnapshot snapshot, out string? notice)
    {
        notice = null;

        if (snapshot.SourceKind is null)
            return null;

        if (!Enum.TryParse<DataSourceKind>(snapshot.SourceKind, out var kind))
            return null;

        if (kind == DataSourceKind.Sql)
        {
            // Deliberate, and the manual says so: the connection string is never written to
            // .codegen, so there is nothing here to reconnect with.
            notice =
                $"This build read the {snapshot.SourceDatabase ?? "database"} database. The "
                + "connection string is never saved, so it needs giving again — the data stage is "
                + "open on it.";

            return null;
        }

        var path = snapshot.SourcePath;

        if (path is null)
            return null;

        if (!File.Exists(path))
        {
            notice = $"The file this build read is no longer at {path} — the data stage is open "
                   + "on it.";
            return null;
        }

        return new DataSourceSpec(kind, path);
    }

    /// <summary>
    /// Where to pick up: the stage that was left, unless something has to be answered first.
    /// </summary>
    private static BuildStage StageToResumeAt(
        BuildSnapshot snapshot, string? notice, BuildSession session)
    {
        // A source that cannot be read pulls the workflow back to Data whatever the snapshot
        // says. Resuming at Generate against a source with no connection would offer a step that
        // cannot run.
        if (notice is not null)
            return BuildStage.Data;

        if (!Enum.TryParse<BuildStage>(snapshot.Stage, out var stage))
            return BuildStage.Describe;

        // Generate and Run are not resumable positions: nothing has been written, so the honest
        // place to come back to is the last stage that still holds an answer.
        if (stage is BuildStage.Generate or BuildStage.Run)
            return session.Model is null ? BuildStage.Discover : BuildStage.Review;

        return stage;
    }
}
```

**If `BuildSession.GoTo` refuses a stage whose predecessors are not marked answered**, read
`StageNavigation`/`StageGraph` and use whatever the page itself uses to move between finished
nodes. Do not weaken a guard to make this work — resuming to an earlier stage is acceptable;
corrupting the stage state is not.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ResumedSessionTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Wire it into the command**

In `BuildAppCommand.Handle`, inside the interactive web branch, **before** the session is
constructed. The existing code reads:

```csharp
            var saved = CustomizationDefaults.Load(workspaceRoot);
            var webStart = Merge(saved, inline);

            WriteRememberedLayoutNotice(workspaceRoot, saved, inline.Layout);

            var session = new Web.Workflow.BuildSession(
                description, workspaceRoot, webStart, source);
```

Add before it:

```csharp
            // Asked here, in the console, because this command is dispatched OUTSIDE
            // CommandColour.Run (CodeGeneratorStarter) precisely so it can print an address and
            // block — which is why OverwritePrompt is visible too. A prompt inside a buffered
            // command would not be.
            var resumed = OfferToResume(workspaceRoot);
```

and use `resumed` when it is not null instead of building a fresh session. Then attach the
callbacks to whichever session is in play — resumed or fresh:

```csharp
            session.Autosave = s => BuildSnapshotStore.Save(
                workspaceRoot, Resume.BuildSnapshot.Of(s));
            session.OnFinished = _ => BuildSnapshotStore.Clear(workspaceRoot);
```

And the method, beside `WriteRememberedLayoutNotice`:

```csharp
    /// <summary>
    /// Offers an unfinished build, and returns the session to carry on with — or null to start
    /// fresh.
    /// </summary>
    /// <remarks>
    /// Interactive only. A redirected-stdin run neither writes a snapshot nor reads one: a
    /// scripted build must not have its output changed by a file it never mentioned, which is the
    /// same determinism <c>CustomizationDefaults</c> and <c>normalize</c> already protect.
    /// </remarks>
    private static Web.Workflow.BuildSession? OfferToResume(string workspaceRoot)
    {
        if (Console.IsInputRedirected)
            return null;

        var snapshot = Resume.BuildSnapshotStore.Load(workspaceRoot);

        if (snapshot is null)
            return null;

        while (true)
        {
            foreach (var line in Resume.ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow))
                Console.WriteLine(line);

            Console.Write("> ");

            var choice = Resume.ResumeOffer.Parse(Console.ReadLine());

            switch (choice)
            {
                case Resume.ResumeChoice.Resume:
                    var session = Resume.ResumedSession.From(
                        snapshot, workspaceRoot, out var notice);

                    if (notice is not null)
                        Output.Warning("  " + notice);

                    return session;

                case Resume.ResumeChoice.Fresh:
                    // Left on disk deliberately: starting fresh is not a decision to throw the
                    // old work away, and the next run can still offer it.
                    return null;

                case Resume.ResumeChoice.Discard:
                    Resume.BuildSnapshotStore.Clear(workspaceRoot);
                    Console.WriteLine("  Discarded.");
                    return null;

                default:
                    Console.WriteLine("  Answer r, f or d.");
                    continue;
            }
        }
    }
```

**Check `Output.Warning` exists** with that signature — it is used elsewhere in this file. If not,
use `Console.WriteLine`.

- [ ] **Step 6: Verify the piped path is untouched**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildAppDataSourceTests|FullyQualifiedName~BuildAppWizardTests"`
Expected: PASS, unchanged. These drive the command with redirected stdin, which must neither read
nor write a snapshot.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/App/Resume/ResumedSession.cs CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/Resume/ResumedSessionTests.cs
git commit -m "feat: build app offers the unfinished build it finds"
```

---

### Task 8: The documentation

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs`
- Regenerate: `docs/commands.md`
- Modify: `docs/user-manual-source.html`
- Regenerate: `docs/SmartCodeGenerator-User-Manual.docx`

**Both doc artefacts are generated from `CommandDictionary` and neither may be hand-edited.**

- [ ] **Step 1: Update the catalog**

Add to the `Build App` category, after the `build app` overview entry:

```csharp
        new()
        {
            Category = "Build App",
            Command = "build app (with an unfinished build in the workspace)",
            Description = "Offers the build you left. Closing the browser tab no longer loses "
                        + "the work: the answers and the reviewed model - renames, added and "
                        + "removed fields, promoted suggestions, the master/transaction split - "
                        + "are saved to .codegen/build-in-progress.json after every change, and "
                        + "the next interactive 'build app' in that workspace offers to resume, "
                        + "start fresh, or discard. Enter starts fresh and deletes nothing. Your "
                        + "DATA is not saved, only where it came from, so the file is re-read on "
                        + "resume; a file that has moved, or a SQL connection string (which is "
                        + "never saved), opens the data stage instead of failing. A finished or "
                        + "cancelled build clears the snapshot; an abandoned one keeps it. Piped "
                        + "runs neither write one nor read one.",
            Example = "build app"
        },
```

The `Example` is `build app` — 10 characters, well inside the 69-character budget.

- [ ] **Step 2: Regenerate both artefacts**

```
UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CommandReferenceDocTests"
UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ManualAppendix_MatchesTheCatalog"
```

Then confirm both are clean:

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ManualAppendixDocTests|FullyQualifiedName~CommandReferenceDocTests|FullyQualifiedName~CommandHelpRenderingTests"
```

- [ ] **Step 3: Write the manual section**

In `docs/user-manual-source.html`, at the end of the chapter covering the `build app` workflow
(the one whose stages are listed around the `<strong>Review</strong>` bullet):

```html
<h3>Picking up an unfinished build</h3>

<p>The workflow lives in the browser, and a browser tab is easy to close by accident. What that used
   to cost was everything the build was holding &mdash; and the part worth minding was never the
   discovery, which can be run again in a minute, but the edits: eight classes renamed, the invented
   fields deleted, three suggestions promoted, two classes moved across the master/transaction
   split. None of that has a command-line form, so the <em>same run as a command</em> line could
   never bring it back.</p>

<p>It is now saved as you work, after every answer and every edit, and the next
   <code>build app</code> in that workspace offers it:</p>

<pre>An unfinished build is waiting in this workspace:
  "a shop"  &mdash;  WPF  &mdash;  8 classes reviewed
  left off at Generate, 20 minutes ago

  [r]esume   start [f]resh   [d]iscard</pre>

<p><strong>Enter starts fresh</strong>, and deletes nothing &mdash; the build you left is still
   there to be offered next time. Only <code>discard</code> removes it. Starting fresh is the
   default deliberately: resuming a build you had forgotten about could have you generate an
   application from a stale model, or from a colleague's abandoned session.</p>

<h4>Your data is not saved &mdash; only where it came from</h4>

<p>The snapshot holds your answers and your model. It does not hold the rows that were read, so it
   stays a few kilobytes whether the source had ninety rows or forty million, and none of your data
   is copied into <code>.codegen</code>. Resuming re-reads the source instead.</p>

<p>That has two consequences worth knowing. A file that has moved since cannot be re-read, so the
   build resumes on the <strong>Data</strong> stage with the old path shown, rather than failing. And
   a SQL Server connection string is never saved at all &mdash; so a resumed database build shows
   the database name and asks for the connection again.</p>

<p>A build that finishes, or that you cancel, clears its snapshot. One you abandon by closing the
   tab keeps it, which is the whole point. Piped runs neither write a snapshot nor read one, so a
   script stays deterministic.</p>
```

- [ ] **Step 4: Rebuild and verify the document**

```
powershell -File tools/build-user-manual.ps1
powershell -File tools/verify-user-manual.ps1
```

Expected: a page count, then `All checks passed` with TOC links equal to heading bookmarks. If
`verify-user-manual.ps1` fails on an empty `$PSScriptRoot`, invoke it with an absolute path via
`&` in PowerShell rather than `-File`.

- [ ] **Step 5: Run the full suite as CI runs it**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: `Build succeeded.` and every project green. Grep `"^  Failed "` as well as `Failed!`.

- [ ] **Step 6: Look at it in a browser**

Not optional. Five consecutive branches have shipped a defect that compiled, passed its tests, and
was visible only in a running page — and the last one shipped with this step skipped.

There is no page to check for the offer itself, which is console-only. What needs looking at is
that a **resumed** workflow draws correctly: start a build, edit the model on Review, close the
tab, run `build app` again, answer `r`, and confirm the page comes back with the edits, the stage
diagram showing the right node, and no console errors. Drive it with patchright imported from
`file:///C:/Users/admin/.vscode/extensions/danielsanmedium.dscodegpt-*/standalone/node_modules/patchright/index.mjs`
and `channel: 'chromium'`; the browser-automation skill's `--script` flag is broken on Windows.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md docs/user-manual-source.html docs/SmartCodeGenerator-User-Manual.docx
git commit -m "docs: an unfinished build is offered back"
```

---

## Self-review notes

**Spec coverage.** Snapshot shape → 1. Store, never-throws, schemaVersion, `.gitignore` → 2.
Written after every answer → 3. Written after every model edit, the wrinkle → 4. Clearing on
Generated/Cancelled and kept on Abandoned → 5. The offer's parse and text → 6. The prompt, the
restore, the re-read and the Data-stage fallback, and the piped-run rule → 7. Docs → 8. Every spec
section has a task.

**Two places a task must read the code rather than trust this plan**, both flagged inline: the
`ImportedData` constructor shape in Task 1's fixture, and whether `BuildSession.GoTo` accepts an
arbitrary stage in Task 7. Neither changes what is being built; both would waste time as a
surprise.

**Deliberately not covered.** Nothing prevents a future panel from editing the model without
calling `ModelEdited()` — the spec names that as the primary risk. Closing it properly means the
session owning the model behind a method that cannot be bypassed, which would touch the console
review loop too, and is out of scope here.

**Ordering.** Tasks 1, 2 and 6 are pure and independent. Task 3 must precede 4 and 5. Task 7 needs
1, 2, 3, 6. Task 8 is last so the catalog's final wording is what gets generated.
