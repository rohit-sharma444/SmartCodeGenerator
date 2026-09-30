# Bundled Domains Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `build app:` gives a sensible starting model for 25 common business systems with no Ollama, no Wikipedia store and no network. The Wikipedia store's location stops being a hard-coded `D:\` path.

**Architecture:** Each domain is an embedded JSON file in the same shape Ollama returns (`LlmDomainModel`) plus weighted match terms. A new `BundledDomainSource` scores the description against every domain, runs the winner through the existing `LlmModelNormalizer` (tagged as built-in, not "model"), and adds the classes the user named. `DomainModelSourceFactory` puts it in the agreed order. A new `WikipediaStoreLocation` resolves the store path for every program that uses it.

**Tech Stack:** .NET 10, C#, System.Text.Json, xUnit (`DomainDiscovery.Tests`), MSTest (`CodeGenerator.Tests`), Roslyn (`Compiler.CompileSources`).

**Spec:** `docs/superpowers/specs/2026-09-30-bundled-domains-design.md`

## Global Constraints

- Order: Ollama → Wikipedia store (if installed) → bundled domain (if one matches) → online Wikipedia → classes from the description.
- Match threshold: a domain needs a score of **at least 3**. Weight 3 means a term names the domain on its own; weight 1 or 2 means a supporting word.
- Ties: the domain with a weight-3 hit wins, then catalog order (file name, alphabetical).
- Message on a match: `Matched: <words as typed, comma-separated>.` with SourceLabel `the built-in <Name> domain`.
- Default store path: `%ProgramData%\SmartCodeGenerator\Wikipedia\enwiki-full.db`. Override: `StorePath` in `discovery.json`. `DomainDiscoveryPipeline.DefaultDumpDatabasePath` is deleted.
- Links in domain files are written as `<Target>Id` fields and only on classes whose role is Document, Transaction or Event (`EntityRoleRank.CanReference`). A `relationships` entry goes through a rank check that can drop it silently.
- Types in domain files: only `string int long decimal double bool DateTime Guid`. Anything else silently becomes `string` in `ToCsharpType`.
- New enum members are appended at the end (`DecisionStage` is saved by name in resume snapshots, but append anyway).
- Every commit ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Write messages to a file and use `git commit -F`: backticks in `-m` are command substitution.
- The repo is CRLF with `* text=auto`. Python edits must read and write bytes and keep the file's own line ending and BOM (see memory *scripted-edits-corrupt-files-quietly*).

**Refinement of the spec, stated here so the reviewer sees it:** the spec says the store-missing message "says so in one line". Startup's rule is "silence means healthy", so under the default Ollama source a missing store is now *healthy*: startup prints nothing, and `status` shows the one line. Pinned to Wikipedia, it is still a startup warning. The spec also says the `wikipedia` factory "splits in two". The plan achieves the same order with one factory plus a `wikipediaIsLocal` flag, which leaves every existing caller and test signature intact.

## Review Focus

1. **The domain's own name comes back as a class.** "a library that lends books" gives the seed `Library`. It names the system, not a class in it, so it is dropped with a `why` reason. The test is in Task 4.
2. **Word forms and repeats.** `Books`, `lends`, `borrowing` and `BOOKS BOOKS` must match like `book`/`lend`/`borrow`, and count once. `bookkeeping` must not hit `book`. Tests are in Task 3.
3. **Ties between domains** must be deterministic and name the winner. The test is in Task 3.
4. **A typo'd type in a domain file** (`decmal`) silently becomes `string`. Task 6's catalog test rejects any type outside the allowed list.
5. **A link on a class whose role can't hold one** silently becomes a plain `int` column. Task 6's catalog test asserts every `<Target>Id` naming a class in the domain becomes a relationship.

---

## File structure

| File | Responsibility |
|---|---|
| `DomainDiscovery/WikipediaStoreLocation.cs` (new) | Where the store is: config → default. |
| `DomainDiscovery/DiscoveryConfig.cs` | + `StorePath`. |
| `DomainDiscovery/Llm/ModelOrigin.cs` (new) | Provenance, stage and kept-reason for a normalised model. |
| `DomainDiscovery/Llm/LlmModelNormalizer.cs` | `Normalize(raw, origin?)`. |
| `DomainDiscovery/Model/DecisionRecord.cs`, `DecisionReasons.cs` | `BuiltInDomain` stage; two reasons. |
| `CodeGenerator/App/DecisionReport.cs` | Label for the new stage. |
| `DomainDiscovery/Bundled/WordStem.cs` (new) | Lower-case words and their stems. |
| `DomainDiscovery/Bundled/BundledDomain.cs` (new) | One parsed domain file. |
| `DomainDiscovery/Bundled/BundledDomainCatalog.cs` (new) | Loads embedded domains; `Match`. |
| `DomainDiscovery/Bundled/BundledDomainSource.cs` (new) | `IDomainModelSource`: match, normalise, merge seeds. |
| `DomainDiscovery/Bundled/Domains/*.json` (new, 25) | The catalog. |
| `DomainDiscovery/DomainModelSourceFactory.cs` | Chain order. |
| `CodeGenerator/App/BuildAppCommand.cs`, `CodeGenerator/Web/Workflow/DiscoveryStep.cs` | Pass bundled + store flag. |
| `CodeGenerator/App/CodeGeneratorStarter.cs` | Store report from the resolver; new text. |
| `WikipediaDumpImporter/Program.cs`, `WikipediaUpdateService/ServiceOptions.cs` | Use the resolver. |

---

### Task 1: Store location

**Files:**
- Create: `DomainDiscovery/WikipediaStoreLocation.cs`
- Modify: `DomainDiscovery/DiscoveryConfig.cs` (add property after `Phases`)
- Modify: `DomainDiscovery/DomainDiscoveryPipeline.cs:116-145` (delete the const and its doc comment; `CreateDefault` defaults to the resolver)
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs:475`, `WikipediaDumpImporter/Program.cs:34-41`, `WikipediaUpdateService/ServiceOptions.cs:26`, `WikipediaUpdateService/appsettings.json:2`, `artifacts/updater-service/appsettings.json:2`, `WikipediaDumpImporter/README.md:33`, `DomainDiscovery/README.md:72`
- Test: `DomainDiscovery.Tests/WikipediaStoreLocationTests.cs`

**Interfaces:**
- Produces: `WikipediaStoreLocation.DefaultPath : string`, `WikipediaStoreLocation.Resolve(DiscoveryConfig? config = null) : string`, `WikipediaStoreLocation.IsInstalled(DiscoveryConfig? config = null) : bool`, `DiscoveryConfig.StorePath : string?`.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery;
using Xunit;

namespace DomainDiscovery.Tests;

public class WikipediaStoreLocationTests
{
    [Fact]
    public void DefaultsUnderLocalAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartCodeGenerator", "Wikipedia", "enwiki-full.db");

        Assert.Equal(expected, WikipediaStoreLocation.Resolve(new DiscoveryConfig()));
        Assert.Equal(expected, WikipediaStoreLocation.DefaultPath);
    }

    [Fact]
    public void BlankStorePathMeansTheDefault()
        => Assert.Equal(WikipediaStoreLocation.DefaultPath,
            WikipediaStoreLocation.Resolve(new DiscoveryConfig { StorePath = "  " }));

    [Fact]
    public void StorePathWinsAndExpandsEnvironmentVariables()
    {
        var config = new DiscoveryConfig { StorePath = @"%TEMP%\store.db" };

        Assert.Equal(Path.Combine(Path.GetTempPath().TrimEnd('\\'), "store.db"),
            WikipediaStoreLocation.Resolve(config));
    }

    [Fact]
    public void IsInstalledOnlyWhenTheFileExists()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.True(WikipediaStoreLocation.IsInstalled(new DiscoveryConfig { StorePath = file }));
            Assert.False(WikipediaStoreLocation.IsInstalled(new DiscoveryConfig { StorePath = file + ".missing" }));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void StorePathIsReadFromDiscoveryJson()
    {
        var json = Path.GetTempFileName();
        try
        {
            File.WriteAllText(json, "{ \"StorePath\": \"C:\\\\x\\\\s.db\" }");
            Assert.Equal(@"C:\x\s.db", DiscoveryConfig.Load(json).StorePath);
        }
        finally { File.Delete(json); }
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~WikipediaStoreLocationTests"` (from `D:\SmartCodeGenerator-Repository`)
Expected: build error, because `WikipediaStoreLocation` and `StorePath` do not exist yet.

- [ ] **Step 3: Implement**

`DiscoveryConfig.cs`, after `public int Phases ...`:

```csharp
    /// <summary>
    /// Where the Wikipedia store is. Blank means <see cref="WikipediaStoreLocation.DefaultPath"/>.
    /// Environment variables (%ProgramData%) are expanded.
    /// </summary>
    public string? StorePath { get; set; }
```

`WikipediaStoreLocation.cs`:

```csharp
namespace DomainDiscovery;

/// <summary>
/// Where the optional Wikipedia store lives, for every program that reads or writes it: the CLI,
/// the web page, WikipediaDumpImporter and WikipediaUpdateService.
/// </summary>
/// <remarks>
/// It used to be a constant naming D:\SmartCodeGenerator-Repository\Dump, a folder that exists on
/// no machine but the one this was written on — and on that one it was emptied on 2026-09-24.
/// </remarks>
public static class WikipediaStoreLocation
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartCodeGenerator", "Wikipedia", "enwiki-full.db");

    public static string Resolve(DiscoveryConfig? config = null)
    {
        var configured = (config ?? DiscoveryConfig.Load()).StorePath;

        return string.IsNullOrWhiteSpace(configured)
            ? DefaultPath
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
    }

    public static bool IsInstalled(DiscoveryConfig? config = null) => File.Exists(Resolve(config));
}
```

`DomainDiscoveryPipeline.cs`: delete `DefaultDumpDatabasePath` and its summary. In `CreateDefault` replace `dumpDatabasePath ?? DefaultDumpDatabasePath` with `dumpDatabasePath ?? WikipediaStoreLocation.Resolve()`. In its `<param name="dumpDatabasePath">` doc change the cref to `WikipediaStoreLocation.Resolve`.

Callers:
- `CodeGeneratorStarter.cs:475`: `var databasePath = WikipediaStoreLocation.Resolve();`
- `ServiceOptions.cs:26`: `? WikipediaStoreLocation.Resolve()`
- `WikipediaDumpImporter/Program.cs`: work out `databasePath` first (`positional.Length > 1 ? positional[1] : WikipediaStoreLocation.Resolve()`), then `dumpPath = positional.Length > 0 ? positional[0] : Path.Combine(Path.GetDirectoryName(databasePath)!, "enwiki-latest-pages-articles.xml.bz2");`. Move the `dumpPath` declaration below `databasePath`.
- Both `appsettings.json` `"//"` comments: replace `DomainDiscoveryPipeline.DefaultDumpDatabasePath` with `WikipediaStoreLocation.Resolve() (%ProgramData%\\SmartCodeGenerator\\Wikipedia\\enwiki-full.db unless discovery.json sets StorePath)`.
- The two READMEs: the same wording in place of the old constant and path.

Then check nothing still names the constant:
Run: `grep -rn "DefaultDumpDatabasePath" --include=*.cs --include=*.json --include=*.md . | grep -v "/bin/\|/obj/\|docs/superpowers\|\.superpowers"`
Expected: no output.

- [ ] **Step 4: Run tests and watch them pass**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~WikipediaStoreLocationTests|FullyQualifiedName~DiscoveryConfigTests"`
Expected: PASS.
Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit** (`feat(discovery): the Wikipedia store's location comes from discovery.json, not a D:\ constant`) with the files above.

---

### Task 2: A normalised model can say it came from a built-in domain

**Files:**
- Create: `DomainDiscovery/Llm/ModelOrigin.cs`
- Modify: `DomainDiscovery/Llm/LlmModelNormalizer.cs` (signature; every `Provenance.Model` and `DecisionStage.ModelProposal` and `DecisionReasons.ProposedByTheModel` inside it)
- Modify: `DomainDiscovery/Model/DecisionRecord.cs` (append `BuiltInDomain` after `ReviewLoop`), `DomainDiscovery/Model/DecisionReasons.cs`
- Modify: `CodeGenerator/App/DecisionReport.cs:186-193`
- Test: `DomainDiscovery.Tests/Llm/ModelOriginTests.cs`

**Interfaces:**
- Produces: `ModelOrigin(Provenance Provenance, DecisionStage Stage, string KeptReason)`, `ModelOrigin.LanguageModel`, `ModelOrigin.BuiltIn(string domainName)`, `LlmModelNormalizer.Normalize(LlmDomainModel raw, ModelOrigin? origin = null)`, `DecisionStage.BuiltInDomain`, `DecisionReasons.FromTheBuiltInDomain(string domain) : string`, `DecisionReasons.NamesTheWholeSystem : string`.

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

public class ModelOriginTests
{
    private static LlmDomainModel Raw() => new()
    {
        Classes =
        {
            new LlmClass { Name = "Member", Role = "Party",
                Properties = { new LlmProperty { Name = "Name", Type = "string" } } },
            new LlmClass { Name = "Loan", Role = "Transaction",
                Properties = { new LlmProperty { Name = "MemberId", Type = "int" },
                               new LlmProperty { Name = "DueDate", Type = "date" } } },
            new LlmClass { Name = "2nd", Role = "Item" }
        }
    };

    [Fact]
    public void WithNoOriginTheModelIsTheLanguageModelAsBefore()
    {
        var model = LlmModelNormalizer.Normalize(Raw());

        Assert.All(model.Entities, e => Assert.Equal(Provenance.Model, e.Provenance));
        Assert.Contains(model.Decisions, d => d.Name == "Member"
            && d.Stage == DecisionStage.ModelProposal && d.Reason == DecisionReasons.ProposedByTheModel);
    }

    [Fact]
    public void ABuiltInOriginTagsEveryEntityFieldAndDecision()
    {
        var model = LlmModelNormalizer.Normalize(Raw(), ModelOrigin.BuiltIn("Library"));

        Assert.All(model.Entities, e =>
        {
            Assert.Equal(Provenance.Data, e.Provenance);
            Assert.All(e.Fields, f => Assert.Equal(Provenance.Data, f.Provenance));
        });
        Assert.All(model.Decisions, d => Assert.Equal(DecisionStage.BuiltInDomain, d.Stage));
        Assert.Contains(model.Decisions, d => d.Name == "Member"
            && d.Reason == "from the built-in Library domain");
        // The link still forms: origin changes labels, not rules.
        Assert.Contains(model.Entities.Single(e => e.Name == "Loan").Relationships,
            r => r.Target == "Member");
    }
}
```

`Provenance.Data` is the closest existing member ("the tool supplied it, not the user and not a model"). A new `Provenance` member would ripple into every switch over it, and nothing downstream needs to tell a bundled field from an imported one. `why` tells them apart through the stage.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~ModelOriginTests"`
Expected: build error, because `ModelOrigin` and `DecisionStage.BuiltInDomain` do not exist yet.

- [ ] **Step 3: Implement**

`ModelOrigin.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery.Llm;

/// <summary>
/// Where a model in the language model's JSON shape came from, so `why` does not say "proposed by
/// the model" about a class that shipped with the tool.
/// </summary>
public sealed record ModelOrigin(Provenance Provenance, DecisionStage Stage, string KeptReason)
{
    public static ModelOrigin LanguageModel { get; } =
        new(Provenance.Model, DecisionStage.ModelProposal, DecisionReasons.ProposedByTheModel);

    public static ModelOrigin BuiltIn(string domainName) =>
        new(Provenance.Data, DecisionStage.BuiltInDomain, DecisionReasons.FromTheBuiltInDomain(domainName));
}
```

`LlmModelNormalizer.Normalize(LlmDomainModel raw, ModelOrigin? origin = null)`: first line `origin ??= ModelOrigin.LanguageModel;`. Replace each `DecisionStage.ModelProposal` with `origin.Stage`, `DecisionReasons.ProposedByTheModel` with `origin.KeptReason`, and every `Provenance.Model` with `origin.Provenance`. That includes `DefaultFields.ApplyTo(entity, …)`, `AddProperty`'s new field and `EnsureKey`'s key. Thread `origin` into `AddProperty` and `EnsureKey` as a parameter. Check with `grep -n "Provenance.Model\|ModelProposal\|ProposedByTheModel" DomainDiscovery/Llm/LlmModelNormalizer.cs`: the only hits left must be in comments.

`DecisionRecord.cs`, after `ReviewLoop`:

```csharp
    ReviewLoop,

    /// <summary>A class that shipped with the tool, from the built-in domain the description matched.</summary>
    BuiltInDomain
```

`DecisionReasons.cs`, beside the other constants:

```csharp
    public const string NamesTheWholeSystem = "it names the whole system, not a class in it";

    public static string FromTheBuiltInDomain(string domain) => $"from the built-in {domain} domain";
```

`DecisionReport.cs` label switch: add `DecisionStage.BuiltInDomain => "built-in domain",` before `_ =>`.

- [ ] **Step 4: Run tests and watch them pass**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~Llm"`
Expected: PASS, and the existing normaliser tests are unchanged.

- [ ] **Step 5: Commit** (`feat(discovery): a normalised model can say it came from a built-in domain`).

---

### Task 3: Words, stems, the catalog and matching

**Files:**
- Create: `DomainDiscovery/Bundled/WordStem.cs`, `DomainDiscovery/Bundled/BundledDomain.cs`, `DomainDiscovery/Bundled/BundledDomainCatalog.cs`
- Create: `DomainDiscovery/Bundled/Domains/Library.json`, `Hotel.json`, `Clinic.json` (full content in Task 6's table. Write these three now.)
- Modify: `DomainDiscovery/DomainDiscovery.csproj`: `<ItemGroup><EmbeddedResource Include="Bundled\Domains\*.json" /></ItemGroup>`
- Test: `DomainDiscovery.Tests/Bundled/WordStemTests.cs`, `DomainDiscovery.Tests/Bundled/BundledDomainCatalogTests.cs`

**Interfaces:**
- Produces:
  - `WordStem.Of(string word) : string`
  - `WordStem.Words(string text) : IReadOnlyList<(string Word, string Stem)>`
  - `BundledDomain(string Name, IReadOnlyDictionary<string,int> Terms, LlmDomainModel Model)`, where `Terms` keys are stems
  - `BundledDomain.Parse(string json) : BundledDomain` (throws `FormatException` naming the problem)
  - `DomainMatch(BundledDomain Domain, int Score, IReadOnlyList<string> MatchedWords)`
  - `BundledDomainCatalog(IReadOnlyList<BundledDomain> domains)`
  - `BundledDomainCatalog.Default : BundledDomainCatalog` (lazy, embedded)
  - `.Domains : IReadOnlyList<BundledDomain>`
  - `.Match(string? description) : DomainMatch?`
  - `const int MatchThreshold = 3`

- [ ] **Step 1: Write the failing tests**

`WordStemTests.cs`:

```csharp
using DomainDiscovery.Bundled;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

public class WordStemTests
{
    [Theory]
    [InlineData("books", "book")] [InlineData("Book", "book")] [InlineData("booking", "book")]
    [InlineData("booked", "book")] [InlineData("lends", "lend")] [InlineData("borrowing", "borrow")]
    [InlineData("enrolments", "enrolment")] [InlineData("invoice", "invoic")] [InlineData("invoicing", "invoic")]
    [InlineData("properties", "property")] [InlineData("classes", "class")] [InlineData("shopping", "shop")]
    [InlineData("billing", "bill")] [InlineData("hire", "hir")] [InlineData("hiring", "hir")]
    [InlineData("status", "status")] [InlineData("business", "business")] [InlineData("gym", "gym")]
    [InlineData("vet", "vet")] [InlineData("leases", "leas")] [InlineData("lease", "leas")]
    public void Stems(string word, string stem) => Assert.Equal(stem, WordStem.Of(word));

    [Fact]
    public void WordsSplitOnAnythingButLettersAndKeepTheWordAsTyped()
    {
        var words = WordStem.Words("A library, that LENDS books-to members!");

        Assert.Equal(new[] { "A", "library", "that", "LENDS", "books", "to", "members" },
            words.Select(w => w.Word).ToArray());
        Assert.Equal("lend", words[3].Stem);
    }
}
```

`BundledDomainCatalogTests.cs`:

```csharp
using DomainDiscovery.Bundled;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

public class BundledDomainCatalogTests
{
    private static BundledDomain Domain(string name, string terms) => BundledDomain.Parse(
        "{ \"name\": \"" + name + "\", \"terms\": {" + terms + "}, "
        + "\"classes\": [ { \"name\": \"Thing\", \"role\": \"Item\", \"properties\": [] } ] }");

    private static readonly BundledDomainCatalog Two = new([
        Domain("Hotel", "\"hotel\": 3, \"room\": 1, \"booking\": 1"),
        Domain("Library", "\"library\": 3, \"book\": 1, \"lend\": 1, \"member\": 1")
    ]);

    [Fact]
    public void ADomainNameMatchesOnItsOwn()
    {
        var match = Two.Match("a library");
        Assert.Equal("Library", match!.Domain.Name);
        Assert.Equal(3, match.Score);
    }

    [Fact]
    public void ThreeSupportingWordsMatchAndTheWordsAreReportedAsTyped()
    {
        var match = Two.Match("lends Books to members");
        Assert.Equal("Library", match!.Domain.Name);
        Assert.Equal(new[] { "lends", "Books", "members" }, match.MatchedWords);
    }

    [Theory]
    [InlineData("a book club")]            // 1 point
    [InlineData("bookkeeping for a firm")] // not "book"
    [InlineData("")]
    [InlineData(null)]
    public void BelowTheThresholdNothingMatches(string? description) => Assert.Null(Two.Match(description));

    [Fact]
    public void ARepeatedWordCountsOnce() => Assert.Null(Two.Match("books books books"));

    [Fact]
    public void TheDomainNameBeatsASharedSupportingWord()
        => Assert.Equal("Hotel", Two.Match("a hotel booking system")!.Domain.Name);

    [Fact]
    public void ATieGoesToTheDomainWithANameHitThenToCatalogOrder()
    {
        var tied = new BundledDomainCatalog([
            Domain("Alpha", "\"x\": 1, \"y\": 1, \"z\": 1"),
            Domain("Beta", "\"beta\": 3")
        ]);
        Assert.Equal("Beta", tied.Match("x y z beta")!.Domain.Name);   // 3 vs 3: Beta has the name hit

        var plain = new BundledDomainCatalog([Domain("Alpha", "\"a\": 3"), Domain("Beta", "\"b\": 3")]);
        Assert.Equal("Alpha", plain.Match("a b")!.Domain.Name);
    }

    [Fact]
    public void ParseRejectsATermWithASpaceOrAWeightOutOfRange()
    {
        Assert.Throws<FormatException>(() => Domain("X", "\"point of sale\": 3"));
        Assert.Throws<FormatException>(() => Domain("X", "\"till\": 4"));
        Assert.Throws<FormatException>(() => Domain("X", "\"till\": 0"));
    }

    [Fact]
    public void WordsSharingAStemKeepTheStrongerWeight()
    {
        var domain = Domain("Purchasing", "\"purchasing\": 3, \"purchase\": 1");
        Assert.Equal(3, domain.Terms[WordStem.Of("purchase")]);
    }

    [Fact]
    public void TheDefaultCatalogLoadsTheEmbeddedFiles()
        => Assert.Contains(BundledDomainCatalog.Default.Domains, d => d.Name == "Library");
}
```

Catalog order is resource (file-name) order; Task 6's catalog test holds each file name to its domain name.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~Bundled"`
Expected: build error, because the types do not exist yet.

- [ ] **Step 3: Implement**

`WordStem.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DomainDiscovery.Bundled;

/// <summary>
/// A deliberately small stemmer for matching a description against the built-in domains' terms.
/// Terms are stemmed the same way when a domain file loads, so a file says "invoicing" and a user
/// saying "invoice" or "invoices" still hits it. It never has to produce a real word: only equal
/// stems for the forms people type.
/// </summary>
public static class WordStem
{
    private static readonly Regex Letters = new("[A-Za-z]+", RegexOptions.Compiled);

    public static IReadOnlyList<(string Word, string Stem)> Words(string text) =>
        Letters.Matches(text ?? string.Empty).Select(m => (m.Value, Of(m.Value))).ToList();

    public static string Of(string word)
    {
        var w = (word ?? string.Empty).ToLowerInvariant();
        if (w.Length <= 3)
            return w;

        // Plurals.
        if (w.EndsWith("ies") && w.Length > 4)
            w = w[..^3] + "y";
        else if (w.EndsWith("es") && (w[..^2].EndsWith('s') || w[..^2].EndsWith('x')
                 || w[..^2].EndsWith("ch") || w[..^2].EndsWith("sh")))
            w = w[..^2];
        else if (w.EndsWith('s') && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is"))
            w = w[..^1];

        // Verb forms, then the doubled consonant they leave: shopping -> shopp -> shop.
        if (w.EndsWith("ing") && w.Length - 3 >= 3)
            w = Undouble(w[..^3]);
        else if (w.EndsWith("ed") && w.Length - 2 >= 3)
            w = Undouble(w[..^2]);

        // A final e, so hire/hiring and invoice/invoicing meet.
        if (w.EndsWith('e') && w.Length > 3)
            w = w[..^1];

        return w;
    }

    // bill and class keep their doubled letter; shopp and plann lose one.
    private static string Undouble(string w) =>
        w.Length >= 2 && w[^1] == w[^2] && w[^1] is not ('l' or 's' or 'z') ? w[..^1] : w;
}
```

Check the theory rows against these rules before running. Two of them: `leases` → the `es` rule needs `s/x/ch/sh` before `es`, and `leas` ends in `s`, so it becomes `leas`; `lease` → drop `e` → `leas`. `booked` → `book`. `classes` → `class`. `enrolments` → `enrolment`. `business` → ends in `ss`, unchanged. `status` → ends in `us`, unchanged. If a row fails, fix the rule, not the row. The rows are the forms people type.

`BundledDomain.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainDiscovery.Llm;

namespace DomainDiscovery.Bundled;

/// <summary>One built-in domain: its name, its weighted match terms (as stems) and its classes.</summary>
public sealed record BundledDomain(string Name, IReadOnlyDictionary<string, int> Terms, LlmDomainModel Model)
{
    private sealed class File
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("terms")] public Dictionary<string, int> Terms { get; set; } = new();
        [JsonPropertyName("classes")] public List<LlmClass> Classes { get; set; } = new();
    }

    public static BundledDomain Parse(string json)
    {
        File file;
        try
        {
            file = JsonSerializer.Deserialize<File>(json) ?? throw new FormatException("empty domain file");
        }
        catch (JsonException ex)
        {
            throw new FormatException("not a domain file: " + ex.Message, ex);
        }

        if (string.IsNullOrWhiteSpace(file.Name))
            throw new FormatException("a domain file has no name");

        var terms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (term, weight) in file.Terms)
        {
            if (term.Any(c => !char.IsLetter(c)))
                throw new FormatException($"{file.Name}: term \"{term}\" must be one word of letters");
            if (weight is < 1 or > 3)
                throw new FormatException($"{file.Name}: term \"{term}\" has weight {weight}; use 1, 2 or 3");
            // Two words can share a stem (purchasing/purchase): the stronger weight stands, or a
            // later supporting word would quietly demote the word that names the domain.
            var stem = WordStem.Of(term);
            terms[stem] = terms.TryGetValue(stem, out var existing) ? Math.Max(existing, weight) : weight;
        }

        return new BundledDomain(file.Name.Trim(), terms, new LlmDomainModel { Classes = file.Classes });
    }
}
```

`BundledDomainCatalog.cs`:

```csharp
namespace DomainDiscovery.Bundled;

public sealed record DomainMatch(BundledDomain Domain, int Score, IReadOnlyList<string> MatchedWords);

/// <summary>The built-in domains, and which one (if any) a description is about.</summary>
public sealed class BundledDomainCatalog
{
    public const int MatchThreshold = 3;
    private const int NameWeight = 3;
    private const string ResourcePrefix = "DomainDiscovery.Bundled.Domains.";

    private static readonly Lazy<BundledDomainCatalog> Embedded = new(LoadEmbedded);

    public BundledDomainCatalog(IReadOnlyList<BundledDomain> domains) => Domains = domains;

    public static BundledDomainCatalog Default => Embedded.Value;

    public IReadOnlyList<BundledDomain> Domains { get; }

    public DomainMatch? Match(string? description)
    {
        // Distinct stems: "books books books" is one piece of evidence, not three.
        var words = WordStem.Words(description ?? string.Empty)
            .GroupBy(w => w.Stem)
            .Select(g => g.First())
            .ToList();

        DomainMatch? best = null;
        var bestHasName = false;

        foreach (var domain in Domains)   // catalog order, so the first of equals stays
        {
            var hits = words.Where(w => domain.Terms.ContainsKey(w.Stem)).ToList();
            var score = hits.Sum(w => domain.Terms[w.Stem]);
            var hasName = hits.Any(w => domain.Terms[w.Stem] >= NameWeight);

            if (best is null || score > best.Score || (score == best.Score && hasName && !bestHasName))
            {
                best = new DomainMatch(domain, score, hits.Select(w => w.Word).ToList());
                bestHasName = hasName;
            }
        }

        return best is not null && best.Score >= MatchThreshold ? best : null;
    }

    private static BundledDomainCatalog LoadEmbedded()
    {
        var assembly = typeof(BundledDomainCatalog).Assembly;

        var domains = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return BundledDomain.Parse(reader.ReadToEnd());
            })
            .ToList();

        return new BundledDomainCatalog(domains);
    }
}
```

Write `Library.json`, `Hotel.json` and `Clinic.json` from Task 6's table and conversion rule.

- [ ] **Step 4: Run tests and watch them pass**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~Bundled"`
Expected: PASS.

- [ ] **Step 5: Commit** (`feat(discovery): built-in domain catalog and matching`).

---

### Task 4: `BundledDomainSource`

**Files:**
- Create: `DomainDiscovery/Bundled/BundledDomainSource.cs`
- Test: `DomainDiscovery.Tests/Bundled/BundledDomainSourceTests.cs`

**Interfaces:**
- Consumes: `BundledDomainCatalog.Match`, `LlmModelNormalizer.Normalize(raw, ModelOrigin.BuiltIn(name))`, `DecisionReasons.NamesTheWholeSystem`, `SeedEntityExtractor.Extract(SemanticResult)`.
- Produces:
  - `BundledDomainSource(BundledDomainCatalog catalog, Func<string, CandidateDomainModel> seeds)`
  - `BundledDomainSource.CreateDefault() : BundledDomainSource`
  - `const string NoMatchLabel = "the built-in domains"`
  - Outcome on a match: `SourceLabel = "the built-in <Name> domain"`, `Succeeded = true`, `Message = "Matched: a, b, c."`
  - Outcome with no match: the seeds' model, `SourceLabel = NoMatchLabel`, `Succeeded = false`, `Message = "No built-in domain matches that description."`

- [ ] **Step 1: Write the failing test**

```csharp
using DomainDiscovery.Bundled;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

public class BundledDomainSourceTests
{
    private static readonly BundledDomainCatalog Catalog = new([BundledDomain.Parse("""
        { "name": "Library", "terms": { "library": 3, "book": 1, "lend": 1, "member": 1 },
          "classes": [
            { "name": "Book", "role": "Item", "properties": [ { "name": "Title", "type": "string" } ] },
            { "name": "Member", "role": "Party", "properties": [ { "name": "Name", "type": "string" } ] },
            { "name": "Loan", "role": "Transaction", "properties": [
                { "name": "BookId", "type": "int" }, { "name": "MemberId", "type": "int" } ] } ] }
        """)]);

    private static CandidateDomainModel Seeds(params string[] names)
    {
        var model = new CandidateDomainModel();
        foreach (var name in names)
        {
            model.Entities.Add(new CandidateEntity { Name = name, Provenance = Provenance.Seed });
            model.Record(name, DecisionStage.SeedExtraction, DecisionOutcome.Kept, DecisionReasons.YouNamedIt);
        }
        return model;
    }

    [Fact]
    public void AMatchGivesTheDomainsClassesAndSaysWhy()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds())
            .Discover("a library that lends books to members");

        Assert.True(outcome.Succeeded);
        Assert.Equal("the built-in Library domain", outcome.SourceLabel);
        Assert.Equal("Matched: library, lends, books, members.", outcome.Message);
        Assert.Equal(new[] { "Book", "Member", "Loan" }, outcome.Model.Entities.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void ClassesTheUserNamedAreAddedAndDuplicatesAreNot()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Book", "Fine"))
            .Discover("a library that lends books and tracks fines");

        var names = outcome.Model.Entities.Select(e => e.Name).ToList();
        Assert.Single(names, n => n == "Book");
        Assert.Contains("Fine", names);
        Assert.Equal(Provenance.Seed, outcome.Model.Entities.Single(e => e.Name == "Fine").Provenance);
        Assert.Contains(outcome.Model.Decisions, d => d.Name == "Fine" && d.Stage == DecisionStage.SeedExtraction);
    }

    [Fact]
    public void ASeedNamingTheSystemItselfIsDroppedWithAReason()   // Review Focus 1
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Library", "Libraries"))
            .Discover("a library that lends books");

        Assert.DoesNotContain(outcome.Model.Entities, e => e.Name.StartsWith("Librar"));
        Assert.Contains(outcome.Model.Decisions, d => d.Name == "Library"
            && d.Outcome == DecisionOutcome.Rejected && d.Reason == DecisionReasons.NamesTheWholeSystem);
    }

    [Fact]
    public void NoMatchReturnsTheSeedsUnsuccessfullySoTheChainMovesOn()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Pattern"))
            .Discover("a knitting pattern tracker");

        Assert.False(outcome.Succeeded);
        Assert.Equal(BundledDomainSource.NoMatchLabel, outcome.SourceLabel);
        Assert.Equal("No built-in domain matches that description.", outcome.Message);
        Assert.Equal("Pattern", Assert.Single(outcome.Model.Entities).Name);
    }

    [Fact]
    public void TheDefaultSourceMatchesTheRealLibraryDomain()
    {
        var outcome = BundledDomainSource.CreateDefault().Discover("a library that lends books to members");

        Assert.True(outcome.Succeeded);
        Assert.Contains(outcome.Model.Entities, e => e.Name == "Book");
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~BundledDomainSourceTests"`
Expected: build error, because `BundledDomainSource` does not exist yet.

- [ ] **Step 3: Implement**

```csharp
using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Core;   // EngineBuilder: copy the using DomainDiscoveryPipeline.cs uses

namespace DomainDiscovery.Bundled;

/// <summary>
/// Discovery with nothing installed and no network: the built-in domain the description is about,
/// plus any class the user named that the domain lacks.
/// </summary>
public sealed class BundledDomainSource : IDomainModelSource
{
    public const string NoMatchLabel = "the built-in domains";

    private readonly BundledDomainCatalog _catalog;
    private readonly Func<string, CandidateDomainModel> _seeds;

    public BundledDomainSource(BundledDomainCatalog catalog, Func<string, CandidateDomainModel> seeds)
    {
        _catalog = catalog;
        _seeds = seeds;
    }

    /// <summary>The embedded catalog, with seeds from the same analyzer and extractor the
    /// Wikipedia pipeline uses.</summary>
    public static BundledDomainSource CreateDefault()
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;
        var extractor = new SeedEntityExtractor();
        return new BundledDomainSource(BundledDomainCatalog.Default, d => extractor.Extract(analyzer.Analyze(d)));
    }

    public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
    {
        var seeds = _seeds(description ?? string.Empty);
        var match = _catalog.Match(description);

        if (match is null)
            return new DiscoveryOutcome(seeds, NoMatchLabel, false, "No built-in domain matches that description.");

        var model = LlmModelNormalizer.Normalize(match.Domain.Model, ModelOrigin.BuiltIn(match.Domain.Name));
        MergeSeeds(model, seeds, match.Domain);

        return new DiscoveryOutcome(
            model,
            $"the built-in {match.Domain.Name} domain",
            true,
            $"Matched: {string.Join(", ", match.MatchedWords)}.");
    }

    // The domain's version of a class wins, because it has fields. A seed that is one of the
    // domain's own name words ("library" in "a library that…") names the system, not a class in it.
    private static void MergeSeeds(CandidateDomainModel model, CandidateDomainModel seeds, BundledDomain domain)
    {
        foreach (var seed in seeds.Entities)
        {
            if (model.Entities.Any(e => e.Name.Equals(seed.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (domain.Terms.TryGetValue(WordStem.Of(seed.Name), out var weight) && weight >= 3)
            {
                model.Rejected.Add(seed.Name);
                model.Record(seed.Name, DecisionStage.BuiltInDomain, DecisionOutcome.Rejected,
                    DecisionReasons.NamesTheWholeSystem);
                continue;
            }

            model.Entities.Add(seed);
        }

        // The seeds' own trail, so `why Fine` still says "you named it".
        model.Decisions.AddRange(seeds.Decisions);
    }
}
```

Check the `EngineBuilder` and `Analyze` usings against `DomainDiscoveryPipeline.cs` (which has `using NaturalLanguageEngine.Abstractions.Semantics;` and calls `EngineBuilder.BuildDefault().Analyzer`). Copy whatever it uses.

- [ ] **Step 4: Run tests and watch them pass**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~Bundled"`
Expected: PASS.

- [ ] **Step 5: Commit** (`feat(discovery): BundledDomainSource: a matched built-in domain plus the classes you named`).

---

### Task 5: The chain, and both callers

**Files:**
- Modify: `DomainDiscovery/DomainModelSourceFactory.cs`
- Modify: `CodeGenerator/App/BuildAppCommand.cs:304-307`, `CodeGenerator/Web/Workflow/DiscoveryStep.cs:122-126`
- Test: `DomainDiscovery.Tests/DomainModelSourceFactoryTests.cs` (append), and check `CodeGenerator.Tests/Web/DiscoveryStepSourceTests.cs`

**Interfaces:**
- Consumes: `BundledDomainSource.CreateDefault()`, `WikipediaStoreLocation.Resolve/IsInstalled`.
- Produces: `DomainModelSourceFactory.Create(DiscoveryConfig config, Func<IDomainModelSource> wikipedia, IOllamaClient? client = null, Func<IDomainModelSource>? bundled = null, bool wikipediaIsLocal = false)`. With `bundled == null` it behaves exactly as today.

- [ ] **Step 1: Write the failing tests** (append to `DomainModelSourceFactoryTests`)

```csharp
    private sealed class Recording : IDomainModelSource
    {
        private readonly string _label; private readonly bool _succeeds; private readonly List<string> _log;
        public Recording(string label, bool succeeds, List<string> log) { _label = label; _succeeds = succeeds; _log = log; }
        public DiscoveryOutcome Discover(string d, Action<DiscoveryProgress>? p = null)
        {
            _log.Add(_label);
            return new(new CandidateDomainModel(), _label, _succeeds, _succeeds ? null : $"{_label} found nothing.");
        }
    }

    private static readonly FakeOllamaClient Down = new() { Available = false };

    [Fact]
    public void WithNoStoreABundledMatchIsUsedBeforeOnlineWikipedia()
    {
        var log = new List<string>();
        var outcome = DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", true, log), Down,
            bundled: () => new Recording("Bundled", true, log), wikipediaIsLocal: false).Discover("x");

        Assert.Equal(new[] { "Bundled" }, log);
        Assert.StartsWith("Bundled", outcome.SourceLabel);
        Assert.Contains("did not answer. Falling back to the built-in domains.", outcome.Message);
    }

    [Fact]
    public void WithNoStoreAndNoBundledMatchOnlineWikipediaRunsNext()
    {
        var log = new List<string>();
        var outcome = DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", true, log), Down,
            bundled: () => new Recording("Bundled", false, log), wikipediaIsLocal: false).Discover("x");

        Assert.Equal(new[] { "Bundled", "Wikipedia" }, log);
        Assert.Contains("Bundled found nothing. Falling back to Wikipedia.", outcome.Message);
    }

    [Fact]
    public void AnInstalledStoreIsTriedBeforeTheBundledDomains()
    {
        var log = new List<string>();
        var outcome = DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", false, log), Down,
            bundled: () => new Recording("Bundled", true, log), wikipediaIsLocal: true).Discover("x");

        Assert.Equal(new[] { "Wikipedia", "Bundled" }, log);
        Assert.Contains("Wikipedia found nothing. Falling back to the built-in domains.", outcome.Message);
    }

    [Fact]
    public void OllamaThatAnswersIsNeverFollowedByTheOthers()
    {
        var log = new List<string>();
        DomainModelSourceFactory.Create(new DiscoveryConfig(),
            () => new Recording("Wikipedia", true, log),
            new FakeOllamaClient { Available = true, Roster = OneClass(), Detail = OneClass() },
            bundled: () => new Recording("Bundled", true, log)).Discover("a shop");

        Assert.Empty(log);
    }

    [Fact]
    public void PinnedToWikipediaTheBundledDomainsStillCatchAFailure()
    {
        var log = new List<string>();
        DomainModelSourceFactory.Create(new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource },
            () => new Recording("Wikipedia", false, log), Down,
            bundled: () => new Recording("Bundled", true, log)).Discover("x");

        Assert.Equal(new[] { "Wikipedia", "Bundled" }, log);
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~DomainModelSourceFactoryTests"`
Expected: build error, because the `bundled` parameter does not exist yet.

- [ ] **Step 3: Implement**

In `DomainModelSourceFactory`:
- Add the two parameters.
- Build the offline part once:

```csharp
        // Everything after Ollama, in the agreed order: an installed store first, then the built-in
        // domains, then online Wikipedia. The one wikipedia factory still decides store-or-online
        // for itself; this only decides where the built-in domains go around it.
        var (offline, offlineName) = bundled is null
            ? (wikipedia, "Wikipedia")
            : wikipediaIsLocal
                ? (() => new FallbackWhenUnsuccessfulSource(wikipedia(), bundled, "the built-in domains"), "Wikipedia")
                : (Func<IDomainModelSource>)(() => new FallbackWhenUnsuccessfulSource(bundled(), wikipedia, "Wikipedia")),
                  "the built-in domains");
```

(Write it as plain `if`/`else` if the tuple-with-lambdas does not infer. Clarity wins.)
- The pinned-Wikipedia branch returns `offline` when `bundled` is null and `wikipediaIsLocal` is false. Otherwise it returns `new FallbackWhenUnsuccessfulSource(wikipedia(), bundled, "the built-in domains")` whenever `bundled` is not null, because pinned means Wikipedia goes first. Keep today's `return wikipedia();` when `bundled` is null.
- Everywhere else `wikipedia` was used (unrecognised source, Ollama available, Ollama down), use `offline`. Every hard-coded `"Falling back to Wikipedia."` becomes `$"Falling back to {offlineName}."`.
- `FallbackWhenUnsuccessfulSource` takes `IDomainModelSource preferred, Func<IDomainModelSource> fallback, string fallbackName` and writes `$"{outcome.Message} Falling back to {fallbackName}."`. Update its existing construction for Ollama to pass `offline, offlineName`.

Callers. In `BuildAppCommand.cs` and `DiscoveryStep.cs`, replace the `Create(...)` call with:

```csharp
        var config = DiscoveryConfig.Load();
        var storePath = WikipediaStoreLocation.Resolve(config);
        var source = DomainModelSourceFactory.Create(
            config,
            () => new WikipediaDomainModelSource(DomainDiscoveryPipeline.CreateDefault(cacheDir, storePath)),
            bundled: BundledDomainSource.CreateDefault,
            wikipediaIsLocal: File.Exists(storePath));
```

Add `using DomainDiscovery.Bundled;`.

- [ ] **Step 4: Run tests and watch them pass**

Run: `dotnet test DomainDiscovery.Tests`
Expected: all pass, and every existing factory test is unchanged.
Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~DiscoveryStep|FullyQualifiedName~BuildApp"`
Expected: pass. If `DiscoveryStepSourceTests` asserts `"Falling back to Wikipedia."` for a path that now reaches the built-in domains first, read the test. If it drives the real factory with no store, the new expected text is `"Falling back to the built-in domains."` because the order changed on purpose. Update it and say so in the commit. If it injects its own sources, it should be unaffected, and a failure there is a bug to fix in the factory.

- [ ] **Step 5: Commit** (`feat(discovery): built-in domains in the fallback chain, between an installed store and online Wikipedia`).

---

### Task 6: The 25 domains, and tests that hold every file to the rules

**Files:**
- Create: the remaining 22 files in `DomainDiscovery/Bundled/Domains/`
- Test: `DomainDiscovery.Tests/Bundled/CatalogContentTests.cs`, `CodeGenerator.Tests/App/BundledDomainsCompileTests.cs`

**Conversion rule** from the table to JSON. The table row `Loan (Transaction): BookId, MemberId, LoanDate:DateTime` becomes:

```json
{ "name": "Loan", "role": "Transaction", "purpose": "",
  "properties": [ { "name": "BookId", "type": "int" }, { "name": "MemberId", "type": "int" },
                  { "name": "LoanDate", "type": "DateTime" } ],
  "relationships": [] }
```

A field with no `:type` is `string`. `XId` is `int` and is a link to class `X`. `relationships` is always `[]` (see Global Constraints). A file is named after the domain with its spaces removed (`PointOfSale.json`), and has `"name"`, `"terms"` and `"classes"`. `Library.json` in full:

```json
{
  "name": "Library",
  "terms": { "library": 3, "librarian": 3, "book": 1, "lend": 1, "loan": 1, "borrow": 1, "member": 1, "catalogue": 1 },
  "classes": [
    { "name": "Book", "role": "Item", "purpose": "", "relationships": [], "properties": [
      { "name": "Title", "type": "string" }, { "name": "Isbn", "type": "string" },
      { "name": "Author", "type": "string" }, { "name": "Publisher", "type": "string" },
      { "name": "PublishedYear", "type": "int" }, { "name": "CopiesOwned", "type": "int" } ] },
    { "name": "Member", "role": "Party", "purpose": "", "relationships": [], "properties": [
      { "name": "Name", "type": "string" }, { "name": "Email", "type": "string" },
      { "name": "Phone", "type": "string" }, { "name": "JoinedOn", "type": "DateTime" } ] },
    { "name": "Loan", "role": "Transaction", "purpose": "", "relationships": [], "properties": [
      { "name": "BookId", "type": "int" }, { "name": "MemberId", "type": "int" },
      { "name": "LoanDate", "type": "DateTime" }, { "name": "DueDate", "type": "DateTime" },
      { "name": "Returned", "type": "bool" } ] },
    { "name": "Reservation", "role": "Transaction", "purpose": "", "relationships": [], "properties": [
      { "name": "BookId", "type": "int" }, { "name": "MemberId", "type": "int" },
      { "name": "ReservedOn", "type": "DateTime" }, { "name": "Status", "type": "string" } ] }
  ]
}
```

**The catalog** (terms: weight 3 unless marked `:1`/`:2`):

| File / Name | Terms | Classes |
|---|---|---|
| Library | library, librarian, book:1, lend:1, loan:1, borrow:1, member:1, catalogue:1 | *(above)* |
| Clinic | clinic, hospital, patient:1, doctor:1, appointment:1, prescription:1, nurse:1 | Patient (Party): Name, DateOfBirth:DateTime, Phone, Email, InsuranceNumber · Doctor (Party): Name, Specialty, Phone · Appointment (Event): PatientId, DoctorId, StartsAt:DateTime, Reason, Status · Prescription (Document): AppointmentId, Medicine, Dosage, IssuedOn:DateTime |
| School | school, college, student:1, course:1, teacher:1, enrol:1, enrolment:1, grade:1, pupil:1 | Student (Party): Name, DateOfBirth:DateTime, Email · Teacher (Party): Name, Email, Subject · Course (Item): Title, Code, Credits:int · Enrolment (Transaction): StudentId, CourseId, EnrolledOn:DateTime, Grade · Lesson (Event): CourseId, TeacherId, StartsAt:DateTime, Room |
| OnlineShop / Online shop | shop, ecommerce, webshop, online:1, product:1, cart:1, basket:1, order:1, customer:1, checkout:1 | Customer (Party): Name, Email, Phone, Address · Product (Item): Name, Sku, Price:decimal, StockQuantity:int, Description · Order (Transaction): CustomerId, OrderDate:DateTime, Status, Total:decimal · OrderLine (Transaction): OrderId, ProductId, Quantity:int, UnitPrice:decimal · Payment (Transaction): OrderId, Amount:decimal, PaidOn:DateTime, Method |
| PointOfSale / Point of sale | till, pos, cashier, checkout:1, receipt:1, sale:1, barcode:1 | Product (Item): Name, Barcode, Price:decimal, StockQuantity:int · Cashier (Party): Name, EmployeeNumber · Sale (Transaction): CashierId, SoldAt:DateTime, Total:decimal, PaymentMethod · SaleLine (Transaction): SaleId, ProductId, Quantity:int, UnitPrice:decimal |
| Invoicing | invoice, invoicing, billing, bill:1, client:1, payment:1, quote:1 | Client (Party): Name, Email, Address, VatNumber · Invoice (Document): ClientId, Number, IssuedOn:DateTime, DueOn:DateTime, Status, Total:decimal · InvoiceLine (Document): InvoiceId, Description, Quantity:decimal, UnitPrice:decimal · Payment (Transaction): InvoiceId, Amount:decimal, PaidOn:DateTime, Method |
| Inventory | inventory, warehouse, stock:1, item:1, location:1, reorder:1 | Product (Item): Name, Sku, ReorderLevel:int, UnitCost:decimal · Warehouse (Place): Name, Address · StockLevel (Document): ProductId, WarehouseId, Quantity:int, CountedOn:DateTime · StockMovement (Transaction): ProductId, WarehouseId, Quantity:int, MovedOn:DateTime, Reason |
| Hotel | hotel, guesthouse, motel, room:1, guest:1, booking:1, reservation:1, stay:1 | Guest (Party): Name, Email, Phone · Room (Place): Number, RoomType, NightlyRate:decimal, Floor:int · Booking (Transaction): GuestId, RoomId, CheckIn:DateTime, CheckOut:DateTime, Status · RoomCharge (Transaction): BookingId, Description, Amount:decimal, ChargedOn:DateTime |
| Restaurant | restaurant, cafe, bistro, menu:1, table:1, dish:1, waiter:1, kitchen:1 | MenuItem (Item): Name, Category, Price:decimal · DiningTable (Place): Number:int, Seats:int · Order (Transaction): DiningTableId, PlacedAt:DateTime, Status, Total:decimal · OrderLine (Transaction): OrderId, MenuItemId, Quantity:int, Notes · TableReservation (Event): DiningTableId, CustomerName, PartySize:int, StartsAt:DateTime |
| HR / HR | hr, personnel, employee:1, department:1, leave:1, holiday:1, staff:1 | Employee (Party): Name, Email, JobTitle, StartDate:DateTime · Department (Party): Name, CostCentre · LeaveRequest (Document): EmployeeId, StartsOn:DateTime, EndsOn:DateTime, LeaveType, Status · PerformanceReview (Document): EmployeeId, ReviewedOn:DateTime, Rating:int, Notes |
| Payroll | payroll, payslip, salary:1, wage:1, tax:1, pay:1 | Employee (Party): Name, TaxCode, BankAccount, AnnualSalary:decimal · PayPeriod (Event): StartsOn:DateTime, EndsOn:DateTime, PayDate:DateTime · Payslip (Document): EmployeeId, PayPeriodId, GrossPay:decimal, Tax:decimal, NetPay:decimal · Deduction (Document): PayslipId, Description, Amount:decimal |
| CRM / CRM | crm, lead:1, contact:1, opportunity:1, deal:1, pipeline:1, prospect:1 | Company (Party): Name, Industry, Website · Contact (Party): Name, Email, Phone, JobTitle · Opportunity (Document): CompanyId, Title, Value:decimal, Stage, ExpectedClose:DateTime · Activity (Event): OpportunityId, Kind, OccurredAt:DateTime, Notes |
| Helpdesk | helpdesk, servicedesk, ticket:1, support:1, issue:1, agent:1 | Customer (Party): Name, Email · Agent (Party): Name, Email, Team · Ticket (Document): CustomerId, AgentId, Subject, Priority, Status, OpenedAt:DateTime · TicketComment (Document): TicketId, Author, Body, PostedAt:DateTime |
| ProjectsAndTasks / Projects and tasks | kanban, project:2, task:2, milestone:1, sprint:1, timesheet:1 | Project (Item): Name, Description, StartsOn:DateTime, DueOn:DateTime · TeamMember (Party): Name, Email, Role · ProjectTask (Document): ProjectId, TeamMemberId, Title, Status, DueOn:DateTime · TimeEntry (Document): ProjectTaskId, TeamMemberId, Hours:decimal, WorkedOn:DateTime |
| Events | conference, festival, concert, event:1, attendee:1, ticket:1, venue:1, speaker:1 | Venue (Place): Name, Address, Capacity:int · ScheduledEvent (Event): VenueId, Title, StartsAt:DateTime, EndsAt:DateTime · Attendee (Party): Name, Email · Ticket (Transaction): ScheduledEventId, AttendeeId, Price:decimal, PurchasedOn:DateTime, SeatNumber |
| Gym | gym, fitness, membership:1, member:1, trainer:1, workout:1 | Member (Party): Name, Email, Phone, JoinedOn:DateTime · Trainer (Party): Name, Specialty · Membership (Document): MemberId, Plan, StartsOn:DateTime, EndsOn:DateTime, MonthlyFee:decimal · FitnessClass (Event): TrainerId, Title, StartsAt:DateTime, Capacity:int · ClassBooking (Transaction): FitnessClassId, MemberId, BookedOn:DateTime |
| CarRental / Car rental | car:2, hire:1, rent:1, rental:1, vehicle:1, mileage:1 | Customer (Party): Name, Email, DrivingLicence · Car (Item): Registration, Make, Model, DailyRate:decimal, Mileage:int · Rental (Transaction): CarId, CustomerId, PicksUpOn:DateTime, ReturnsOn:DateTime, TotalCost:decimal · DamageReport (Document): RentalId, Description, ReportedOn:DateTime, RepairCost:decimal |
| PropertyRental / Property rental | landlord, tenant, letting, lease:1, rent:1, flat:1, apartment:1 | RentalProperty (Place): Address, Bedrooms:int, MonthlyRent:decimal · Tenant (Party): Name, Email, Phone · Lease (Document): RentalPropertyId, TenantId, StartsOn:DateTime, EndsOn:DateTime, MonthlyRent:decimal, Deposit:decimal · RentPayment (Transaction): LeaseId, Amount:decimal, PaidOn:DateTime · MaintenanceRequest (Document): RentalPropertyId, Description, ReportedOn:DateTime, Status |
| Fleet | fleet, vehicle:1, driver:1, trip:1, fuel:1, van:1, truck:1 | Vehicle (Device): Registration, Make, Model, Year:int · Driver (Party): Name, LicenceNumber, Phone · Trip (Event): VehicleId, DriverId, StartedAt:DateTime, EndedAt:DateTime, DistanceKm:decimal · FuelPurchase (Transaction): VehicleId, Litres:decimal, Cost:decimal, PurchasedOn:DateTime · ServiceRecord (Document): VehicleId, ServicedOn:DateTime, Description, Cost:decimal |
| Banking | bank, banking, account:1, deposit:1, withdrawal:1, transfer:1 | Customer (Party): Name, Email, DateOfBirth:DateTime · BankAccount (Document): CustomerId, AccountNumber, AccountType, OpenedOn:DateTime, Balance:decimal · AccountTransaction (Transaction): BankAccountId, Amount:decimal, Kind, PostedAt:DateTime, Reference |
| Expenses | expense, reimbursement, receipt:1, claim:1, mileage:1 | Employee (Party): Name, Email, Department · ExpenseClaim (Document): EmployeeId, SubmittedOn:DateTime, Status, Total:decimal · ExpenseItem (Document): ExpenseClaimId, Category, Amount:decimal, SpentOn:DateTime, Description · Approval (Event): ExpenseClaimId, ApprovedBy, DecidedOn:DateTime, Decision |
| Recruitment | recruitment, recruiting, candidate:1, vacancy:1, job:1, applicant:1, interview:1 | JobOpening (Document): Title, Department, ClosesOn:DateTime, Salary:decimal · Candidate (Party): Name, Email, Phone, ResumeUrl · JobApplication (Document): JobOpeningId, CandidateId, AppliedOn:DateTime, Status · Interview (Event): JobApplicationId, Interviewer, StartsAt:DateTime, Outcome |
| Purchasing | purchasing, procurement, supplier:1, vendor:1, purchase:1, order:1 | Supplier (Party): Name, Email, Phone, PaymentTerms · Product (Item): Name, Sku, UnitCost:decimal · PurchaseOrder (Document): SupplierId, OrderedOn:DateTime, Status, Total:decimal · PurchaseOrderLine (Document): PurchaseOrderId, ProductId, Quantity:int, UnitCost:decimal · GoodsReceipt (Document): PurchaseOrderId, ReceivedOn:DateTime, Notes |
| Blog | blog, post:1, article:1, comment:1, author:1, tag:1 | Author (Party): Name, Email, Bio · Post (Document): AuthorId, Title, Body, Slug, PublishedOn:DateTime · Comment (Document): PostId, AuthorName, Body, PostedAt:DateTime · Tag (Item): Name |
| Veterinary | vet, veterinary, pet:1, animal:1, owner:1, vaccination:1 | Owner (Party): Name, Phone, Email, Address · Pet (Item): Name, Species, Breed, DateOfBirth:DateTime · Visit (Event): PetId, OwnerId, VisitedOn:DateTime, Reason, Notes · Vaccination (Document): PetId, Vaccine, GivenOn:DateTime, DueAgainOn:DateTime |

Words to check against the stemmer and the unique-weight-3 rule when writing: `recruitment`/`recruiting` stem to `recruitment`/`recruit`, which are distinct. `pos`, `hr` and `crm` are 3 letters or fewer, so they are left unstemmed. `checkout` is 1 in both Online shop and Point of sale, which is allowed; only weight-3 terms must be unique.

- [ ] **Step 1: Write the failing catalog tests**

`DomainDiscovery.Tests/Bundled/CatalogContentTests.cs`:

```csharp
using System.Text.RegularExpressions;
using DomainDiscovery.Bundled;
using DomainDiscovery.Enrichment;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

public class CatalogContentTests
{
    private static readonly string[] Types = ["string", "int", "long", "decimal", "double", "bool", "DateTime", "Guid"];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var d in BundledDomainCatalog.Default.Domains) data.Add(d.Name);
        return data;
    }

    private static BundledDomain Get(string name) => BundledDomainCatalog.Default.Domains.Single(d => d.Name == name);

    [Fact]
    public void ThereAreTwentyFive() => Assert.Equal(25, BundledDomainCatalog.Default.Domains.Count);

    [Theory, MemberData(nameof(Names))]
    public void NormalisesWithNothingRejected(string name)
    {
        var model = LlmModelNormalizer.Normalize(Get(name).Model, ModelOrigin.BuiltIn(name));
        Assert.Empty(model.Rejected);
        Assert.Equal(Get(name).Model.Classes.Count, model.Entities.Count);
    }

    [Theory, MemberData(nameof(Names))]
    public void EveryTypeIsOneTheNormaliserKnows(string name)   // Review Focus 4
    {
        foreach (var c in Get(name).Model.Classes)
            foreach (var p in c.Properties)
                Assert.True(Types.Contains(p.Type), $"{name}.{c.Name}.{p.Name}: type \"{p.Type}\"");
    }

    [Theory, MemberData(nameof(Names))]
    public void EveryIdFieldBecomesARelationship(string name)   // Review Focus 5
    {
        var raw = Get(name).Model;
        var model = LlmModelNormalizer.Normalize(raw, ModelOrigin.BuiltIn(name));
        var classNames = raw.Classes.Select(c => c.Name).ToHashSet();

        foreach (var c in raw.Classes)
            foreach (var p in c.Properties.Where(p => p.Name.EndsWith("Id") && classNames.Contains(p.Name[..^2])))
                Assert.True(model.Entities.Single(e => e.Name == c.Name).Relationships.Any(r => r.Target == p.Name[..^2]),
                    $"{name}.{c.Name}.{p.Name} did not become a link: role {c.Role} cannot hold one");
    }

    [Theory, MemberData(nameof(Names))]
    public void RolesAreRealAndRelationshipsAreLeftEmpty(string name)
    {
        foreach (var c in Get(name).Model.Classes)
        {
            Assert.True(Enum.TryParse<EntityRole>(c.Role, out _), $"{name}.{c.Name}: role \"{c.Role}\"");
            Assert.Empty(c.Relationships);
        }
    }

    [Fact]
    public void NoTwoDomainsShareAWeightThreeTerm()
    {
        var owners = BundledDomainCatalog.Default.Domains
            .SelectMany(d => d.Terms.Where(t => t.Value == 3).Select(t => (t.Key, d.Name)))
            .GroupBy(x => x.Key).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Name))}");
        Assert.Empty(owners);
    }

    [Fact]
    public void EveryFileIsNamedAfterItsDomain()
    {
        var resources = typeof(BundledDomainCatalog).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("DomainDiscovery.Bundled.Domains.")).OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => n["DomainDiscovery.Bundled.Domains.".Length..^".json".Length]).ToList();
        // PointOfSale.json holds "Point of sale": spaces removed, case ignored.
        Assert.Equal(resources.Select(r => r.ToLowerInvariant()),
            BundledDomainCatalog.Default.Domains.Select(d => Regex.Replace(d.Name, @"\s", "").ToLowerInvariant()));
    }

    [Theory]
    [InlineData("a library that lends books to members", "Library")]
    [InlineData("a clinic that books patients with doctors", "Clinic")]
    [InlineData("a school with students enrolled on courses", "School")]
    [InlineData("an online shop selling products to customers", "Online shop")]
    [InlineData("a till for a corner shop cashier", "Point of sale")]
    [InlineData("invoicing for my clients", "Invoicing")]
    [InlineData("a warehouse stock system", "Inventory")]
    [InlineData("a hotel booking system", "Hotel")]
    [InlineData("a restaurant with tables and a menu", "Restaurant")]
    [InlineData("an HR system for employees and leave", "HR")]
    [InlineData("payroll for our staff", "Payroll")]
    [InlineData("a CRM for leads and deals", "CRM")]
    [InlineData("a helpdesk where customers raise tickets", "Helpdesk")]
    [InlineData("track projects and tasks", "Projects and tasks")]
    [InlineData("a conference with speakers and attendees", "Events")]
    [InlineData("a gym with members and trainers", "Gym")]
    [InlineData("a car hire business", "Car rental")]
    [InlineData("a landlord managing tenants", "Property rental")]
    [InlineData("manage a fleet of vans and drivers", "Fleet")]
    [InlineData("a bank with accounts and deposits", "Banking")]
    [InlineData("staff expense claims with receipts", "Expenses")]
    [InlineData("recruitment of candidates for vacancies", "Recruitment")]
    [InlineData("purchasing from suppliers", "Purchasing")]
    [InlineData("a blog with posts and comments", "Blog")]
    [InlineData("a vet practice for pets and their owners", "Veterinary")]
    public void EachDomainIsFoundByAnOrdinaryDescription(string description, string domain)
        => Assert.Equal(domain, BundledDomainCatalog.Default.Match(description)?.Domain.Name);

    [Theory]
    [InlineData("a book club")]
    [InlineData("a knitting pattern tracker")]
    public void NearMissesDoNotMatch(string description) => Assert.Null(BundledDomainCatalog.Default.Match(description));
}
```

`CodeGenerator.Tests/App/BundledDomainsCompileTests.cs` (MSTest):

```csharp
using AppScaffolding;
using CodeGenerationEngine.Core;
using DomainDiscovery.Bundled;
using DomainDiscovery.Llm;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BundledDomainsCompileTests
{
    public static IEnumerable<object[]> Domains() =>
        BundledDomainCatalog.Default.Domains.Select(d => new object[] { d.Name });

    [TestMethod]
    [DynamicData(nameof(Domains), DynamicDataSourceType.Method)]
    public void EveryDomainsClassesCompile(string name)
    {
        var domain = BundledDomainCatalog.Default.Domains.Single(d => d.Name == name);
        var model = LlmModelNormalizer.Normalize(domain.Model, ModelOrigin.BuiltIn(name));

        var sources = new List<(string Path, string Code)>
        {
            ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;")
        };
        foreach (var definition in DomainModelMapper.ToClassDefinitions(model))
            sources.Add(("Models/" + definition.Name + ".cs", RoslynClassGenerator.Generate(definition, "Acme.Models")));

        var errors = Compiler.CompileSources(sources).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();

        Assert.IsEmpty(errors, name + ":\n" + string.Join("\n", errors));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~CatalogContentTests"`
Expected: FAIL. `ThereAreTwentyFive` sees 3, and the per-domain match rows fail for the 22 missing domains.

- [ ] **Step 3: Write the 22 files**

Follow the table and conversion rule exactly. No `purpose` text is needed (use `""`).

- [ ] **Step 4: Run tests and watch them pass**

Run: `dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~Bundled"`
Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BundledDomainsCompileTests"`
Expected: PASS. If a match row fails, adjust that domain's **terms**, never the description. The descriptions are what people type. If a compile row fails, rename the class in the table (as `Task`→`ProjectTask` and `Application`→`JobApplication` already were) and note the reason in the commit.

- [ ] **Step 5: Commit** (`feat(discovery): the 25 built-in domains`).

---

### Task 7: Startup and `status` tell the truth about the store

**Files:**
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs:473-516` (split `StoreReport`)
- Test: `CodeGenerator.Tests/App/StoreReportTests.cs`

**Interfaces:**
- Produces: `internal static (IReadOnlyList<string> Lines, bool Healthy) StoreReport(string databasePath, string source)`. The existing parameterless `StoreReport()` calls it with `WikipediaStoreLocation.Resolve(config)` and `config.Source`.

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerator.App;
using DomainDiscovery;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreReportTests
{
    private const string Missing = @"C:\nowhere\enwiki-full.db";

    [TestMethod]
    public void AMissingStoreUnderOllamaIsHealthyAndOneLine()
    {
        var (lines, healthy) = CodeGeneratorStarter.StoreReport(Missing, DiscoveryConfig.OllamaSource);

        Assert.IsTrue(healthy, "an optional store being absent is not a fault");
        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], Missing);
        StringAssert.Contains(lines[0], "built-in domains");
        Assert.DoesNotContain("zero classes", lines[0]);
    }

    [TestMethod]
    public void AMissingStorePinnedToWikipediaStillWarns()
    {
        var (lines, healthy) = CodeGeneratorStarter.StoreReport(Missing, DiscoveryConfig.WikipediaSource);

        Assert.IsFalse(healthy);
        Assert.IsTrue(lines.Any(l => l.Contains("online Wikipedia")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("dotnet run --project WikipediaDumpImporter")));
    }
}
```

Check that `CodeGeneratorStarter`'s namespace and accessibility let the test call it (`InternalsVisibleTo` already covers `CodeGenerator.Tests`). Fix the `using` to the real namespace.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~StoreReportTests"`
Expected: build error, because there is no two-argument overload yet.

- [ ] **Step 3: Implement**

Move the body into the two-argument overload. The not-found branch becomes:

```csharp
        if (!File.Exists(databasePath))
        {
            if (string.Equals(source, DiscoveryConfig.WikipediaSource, StringComparison.OrdinalIgnoreCase))
            {
                return
                ([
                    $"Wikipedia store: not installed ({databasePath}).",
                    "  discovery.json pins discovery to Wikipedia, so 'build app:' uses the online",
                    "  Wikipedia API (slower, rate-limited, needs a connection), then the built-in domains.",
                    "  To import the store (a one-time job), run:",
                    string.Empty,
                    "      dotnet run --project WikipediaDumpImporter -c Release",
                ], false);
            }

            // The normal state for anyone who installed the tool: silence at startup, one line in
            // `status`. Before the built-in domains this warned at every launch and claimed 'build
            // app:' would give zero classes, which the code never did (it went online instead).
            return ([
                $"Wikipedia store: not installed ({databasePath}) — 'build app:' uses Ollama, then the built-in domains, then online Wikipedia."
            ], true);
        }
```

The parameterless `StoreReport()`:

```csharp
    internal static (IReadOnlyList<string> Lines, bool Healthy) StoreReport()
    {
        var config = DiscoveryConfig.Load();
        return StoreReport(WikipediaStoreLocation.Resolve(config), config.Source);
    }
```

Update the comment above `ReportWikipediaStoreStatus`'s `Output.Warning` loop: "a missing store falls back to the online API" becomes "…to the built-in domains and the online API".

- [ ] **Step 4: Run tests and watch them pass**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~StoreReportTests|FullyQualifiedName~StatusReport"`
Expected: PASS.

- [ ] **Step 5: Commit** (`fix(cli): the store report stops promising zero classes; a missing store is normal now`).

---

### Task 8: Docs, the full suite, and running it for real

**Files:**
- Modify: `docs/user-manual-source.html` (wherever discovery sources or the store are described: find them with `grep -n "Wikipedia store\|fall back\|falls back\|Ollama" docs/user-manual-source.html`), `docs/SmartCodeGenerator-User-Manual.docx` (rebuilt), `DomainDiscovery/README.md` (source table: add the built-in domains row)

- [ ] **Step 1: Manual.** Describe the order in the discovery chapter in plain words, list the 25 domains, say how to set `StorePath`, and say that `why <Class>` shows "built-in domain". Rebuild: `powershell -File tools\build-user-manual.ps1`. Verify: `powershell -File tools\verify-user-manual.ps1 -Contains 'built-in domains','StorePath','Veterinary'`. Expected: `All checks passed.` and the same chapter and section counts as before, unless a section was added.

- [ ] **Step 2: CI's own commands** (from `D:\SmartCodeGenerator-Repository`):

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: `Build succeeded.`; every project `Passed!`, 0 failed (last full run: 4,660 passed, 1 skipped).

- [ ] **Step 3: Live, with Ollama pointed at a dead port** (never stop his service). Find the `discovery.json` the Release CLI reads (`CodeGenerator/bin/Release/net10.0/discovery.json`, and whichever project file is copied there: `git ls-files | grep discovery.json`). Copy it to `C:\ac\discovery.json.bak` and set `"Endpoint": "http://localhost:1"`. Then:

```bash
rm -rf /c/ac/bd && mkdir -p /c/ac/bd
printf 'build app: a library that lends books to members\nn\nexit\n' | PROJECTPATH='C:\ac\bd' \
  /d/SmartCodeGenerator-Repository/CodeGenerator/bin/Release/net10.0/CodeGenerator.exe > /c/ac/bd-out.txt 2>&1
grep -n "built-in\|Book\|Member\|Loan\|Falling back" /c/ac/bd-out.txt
```

Expected: "Classes from the built-in Library domain. … did not answer. Falling back to the built-in domains. Matched: library, lends, books, members." and the classes Book, Member, Loan, Reservation. Also run once with `a knitting pattern tracker`, and expect the online fallback message or the seed classes, not a crash.

- [ ] **Step 4: app-check** with the same dead endpoint: `powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1 -Only library`, then `-Only clinic`. If `run.ps1`'s rebuild overwrites the edited `discovery.json` in `bin`, edit the source copy instead and `git checkout` it afterwards. Open each `report.html` **and look at the screenshots**. Expected: both pass, and the "Classes" line names the built-in domain.

- [ ] **Step 5: Restore `discovery.json`** from `C:\ac\discovery.json.bak`, confirm with `git status --short` that no config is left modified, and commit the docs (`docs: the built-in domains and StorePath in the manual`).

---

## Self-review (done)

- **Spec coverage.** Catalog (Tasks 3 and 6), matching (3), your own words kept (4), plug-in and order (5), store location (1), startup message (7), catalog, compile, matching and chain tests (6 and 5), live run (8). All covered.
- **Placeholders.** None. Task 6's 22 files are specified fully by the table and conversion rule.
- **Types.** `ModelOrigin.BuiltIn`, `BundledDomainCatalog.Default/Match`, `DomainMatch.MatchedWords`, `BundledDomainSource.NoMatchLabel` and `Create(…, bundled, wikipediaIsLocal)` are named the same in every task.
- **Review Focus.** All five are pinned: 1 → Task 4, 2 and 3 → Task 3, 4 and 5 → Task 6.
