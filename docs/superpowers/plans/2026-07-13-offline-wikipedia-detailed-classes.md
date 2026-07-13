# Offline Wikipedia and Detailed Classes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the live MediaWiki API with a local Wikipedia dump, and spend the rate-limit budget that frees on reading each discovered class's own article to mine the properties it states.

**Architecture:** A one-time importer streams the 25 GB single-stream bz2 dump, strips wikitext to prose, and writes a SQLite store. `DumpWikipediaClient` implements the existing `IWikipediaClient` against that store, so nothing downstream changes. With article reads now free, `EntityShaper` gains a `PropertyMiner` that reads each candidate's full article, and `DomainModelMapper` finally uses the relationships `RelationshipMiner` already produces.

**Tech Stack:** C# / .NET 10, xunit, Roslyn (`Microsoft.CodeAnalysis` 5.3.0), SharpZipLib 1.4.2 (bz2), Microsoft.Data.Sqlite 10.0.9.

## Global Constraints

- **Target framework:** `net10.0`. `ImplicitUsings` and `Nullable` are `enable` in every project here.
- **Test framework:** **xunit** (`[Fact]`, `[Theory]`, `Assert.Equal`). `DomainDiscovery.Tests` already references xunit 2.9.2. Do **not** use MSTest — that is a different test project in this repo.
- **Solution:** every new project must be added to `SmartCodeGenerator/SmartCodeGenerator.slnx`.
- **Security:** `Microsoft.Data.Sqlite` 10.0.9 transitively pulls `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which has a **high-severity advisory (NU1903, GHSA-2m69-gcr7-jv3q)**. Every project that references `Microsoft.Data.Sqlite` MUST also pin `SQLitePCLRaw.bundle_e_sqlite3` version `3.0.3`, which resolves it. Verified: restore is warning-free with the pin.
- **Dump location:** `D:\SmartCodeGenerator-Repository\Dump\enwiki-latest-pages-articles.xml.bz2`. Never modify, move, or delete it. It is already gitignored.
- **Store location:** `D:\SmartCodeGenerator-Repository\Dump\enwiki.db`. Also gitignored.
- **Never-throw contract:** `IWikipediaClient` methods must never throw. Every failure degrades to an empty result. `DumpWikipediaClient` inherits this contract exactly.
- **No-op contract:** `PropertyMiner` must never make a model worse. An article that yields no evidence leaves the entity exactly as `EntityShaper` already shaped it.
- **CS0542:** a property may never share its class's name (`class Order { int Order; }` does not compile). Never emit a property named `Id` from mining — `DomainModelMapper` generates it.

---

## File Structure

**New — `DomainDiscovery/Wikipedia/Dump/`** (pure logic + the store; no bz2 dependency):
- `StrippedArticle.cs` — the record the stripper produces.
- `WikitextStripper.cs` — wikitext → prose + links. Pure string work, no dependencies. **This is where quality lives.**
- `WikipediaStore.cs` — SQLite schema, batched writes, indexed reads. Owns `Microsoft.Data.Sqlite`.
- `DumpWikipediaClient.cs` — `IWikipediaClient` over `WikipediaStore`.

**New — `WikipediaDumpImporter/`** (console exe, run once; owns SharpZipLib):
- `DumpPageReader.cs` — bz2 → `XmlReader` → `IEnumerable<RawPage>`.
- `ImportRunner.cs` — orchestration, resume, progress.
- `Program.cs` — entry point.

**New — `DomainDiscovery/Enrichment/`:**
- `RoleTemplate.cs` — the fuller per-`EntityRole` baseline.
- `PropertyMiner.cs` — mines properties from a candidate's own article.

**Modified:**
- `DomainDiscovery/Enrichment/EntityShaper.cs` — call `RoleTemplate` and `PropertyMiner`.
- `DomainDiscovery/DomainDiscoveryPipeline.cs:87-100` — `CreateDefault` prefers the dump.
- `CodeGenerationEngine/Models/PropertyDefinition.cs` — add `Attributes`.
- `CodeGenerationEngine/Models/ClassDefinition.cs` — add `Usings`.
- `CodeGenerationEngine/Core/RoslynClassGenerator.cs:19-43,75-133` — emit usings and attributes.
- `AppScaffolding/DomainModelMapper.cs:37-65` — audit fields and navigation properties.
- `AppScaffolding/MainWindowTemplate.cs:67-71` — cancel non-display columns.
- `AppScaffolding/MainViewModelTemplate.cs` — call the resolver.
- `AppScaffolding/AppScaffolder.cs` — emit `Data/RelationshipResolver.cs`.

**New — `AppScaffolding/RelationshipResolverTemplate.cs`** — the generated app's FK → nav-prop resolver.

---

## Task 1: WikitextStripper

The single highest-risk component: every consumer downstream reads the prose it produces. It replaces what the API's `explaintext=1` did for us, and unlike the API it **preserves `* ` list markers**, because that is where an article states a concept's properties.

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/StrippedArticle.cs`
- Create: `DomainDiscovery/Wikipedia/Dump/WikitextStripper.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/WikitextStripperTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record StrippedArticle(string Body, IReadOnlyList<string> Links)`; `WikitextStripper.Parse(string wikitext) → StrippedArticle`; `WikitextStripper.LeadOf(string body) → string`.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/WikitextStripperTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public class WikitextStripperTests
{
    [Fact]
    public void Unwraps_a_piped_link_to_its_display_text()
    {
        var result = WikitextStripper.Parse("A [[cash register|till]] prints a receipt.");
        Assert.Equal("A till prints a receipt.", result.Body);
    }

    [Fact]
    public void Unwraps_a_bare_link_to_its_target()
    {
        var result = WikitextStripper.Parse("A [[receipt]] is issued.");
        Assert.Equal("A receipt is issued.", result.Body);
    }

    [Fact]
    public void Collects_link_targets_including_the_piped_form()
    {
        var result = WikitextStripper.Parse("[[Cash register|till]] and [[Receipt]].");
        Assert.Equal(new[] { "Cash register", "Receipt" }, result.Links);
    }

    [Fact]
    public void Excludes_file_category_and_image_links_from_both_prose_and_links()
    {
        var result = WikitextStripper.Parse("[[File:A.png|thumb]][[Category:Retail]]Text.");
        Assert.Equal("Text.", result.Body);
        Assert.Empty(result.Links);
    }

    [Fact]
    public void Removes_nested_templates()
    {
        var result = WikitextStripper.Parse("{{Infobox|a={{nested|x}}|b=2}}An invoice.");
        Assert.Equal("An invoice.", result.Body);
    }

    [Fact]
    public void Removes_tables_refs_and_comments()
    {
        var result = WikitextStripper.Parse(
            "{|class=\"x\"\n|a\n|}<ref>Smith 2020</ref><!-- hide -->Kept.");
        Assert.Equal("Kept.", result.Body);
    }

    [Fact]
    public void Strips_bold_and_italic_markup()
    {
        var result = WikitextStripper.Parse("An '''invoice''' is a ''document''.");
        Assert.Equal("An invoice is a document.", result.Body);
    }

    // The reason this class exists rather than reusing explaintext: PropertyMiner
    // reads these markers, and flat plain text destroys them.
    [Fact]
    public void Preserves_list_markers_as_a_line_prefix()
    {
        var result = WikitextStripper.Parse(
            "An invoice contains:\n* invoice number\n** sub item\n* date of issue");

        Assert.Equal(
            "An invoice contains:\n* invoice number\n* sub item\n* date of issue",
            result.Body);
    }

    [Fact]
    public void Drops_heading_markup_but_keeps_the_heading_text_on_its_own_line()
    {
        var result = WikitextStripper.Parse("== Contents ==\nText.");
        Assert.Equal("Contents\nText.", result.Body);
    }

    [Fact]
    public void LeadOf_returns_the_first_sentence()
    {
        Assert.Equal(
            "An invoice is a commercial document.",
            WikitextStripper.LeadOf("An invoice is a commercial document. It is issued by a seller."));
    }

    [Fact]
    public void LeadOf_is_empty_for_empty_input()
    {
        Assert.Equal(string.Empty, WikitextStripper.LeadOf(""));
    }

    [Fact]
    public void Parse_never_throws_on_unbalanced_markup()
    {
        var result = WikitextStripper.Parse("{{unclosed [[also unclosed");
        Assert.NotNull(result.Body);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter WikitextStripperTests`
Expected: FAIL — `WikitextStripper` does not exist (CS0246).

- [ ] **Step 3: Implement**

Create `DomainDiscovery/Wikipedia/Dump/StrippedArticle.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>An article's wikitext reduced to prose, with its outgoing links kept aside.</summary>
/// <param name="Body">
/// Plain prose. List items survive as a leading <c>"* "</c> on their own line — deliberately,
/// unlike the API's explaintext, because <see cref="Enrichment.PropertyMiner"/> reads them.
/// </param>
/// <param name="Links">Mainspace link targets, in order of first appearance.</param>
public sealed record StrippedArticle(string Body, IReadOnlyList<string> Links);
```

Create `DomainDiscovery/Wikipedia/Dump/WikitextStripper.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Reduces wikitext to prose, and collects the article's outgoing links on the way.
/// </summary>
/// <remarks>
/// This is the job the MediaWiki API did for us with <c>explaintext=1</c>, and it is the
/// quality-critical step of the whole offline path: every consumer downstream — HypernymExtractor,
/// EntityHarvester, RelationshipMiner, PropertyMiner — reads only what this class emits.
/// <para>
/// It deviates from explaintext in exactly one way, on purpose: a wikitext list item survives as a
/// <c>"* "</c> line prefix. Flat plain text throws that structure away, and an article states a
/// concept's properties precisely there ("An invoice typically contains: * invoice number * ...").
/// </para>
/// </remarks>
public static class WikitextStripper
{
    /// <summary>Link namespaces that are not article references and must not reach either output.</summary>
    private static readonly string[] NonArticlePrefixes =
        ["File:", "Image:", "Category:", "Media:", "Template:", "Help:", "Wikipedia:", "Portal:"];

    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RefTags = new(@"<ref[^>]*?/>|<ref[^>]*?>.*?</ref>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OtherTags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Emphasis = new(@"'{2,5}", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s*={2,6}\s*(.*?)\s*={2,6}\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^[*#:;]+\s*", RegexOptions.Compiled);
    private static readonly Regex BlankRun = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?])\s", RegexOptions.Compiled);

    /// <summary>
    /// Prose and links for <paramref name="wikitext"/>. Never throws: unbalanced markup degrades
    /// to whatever prose can still be recovered, because one malformed article out of seven
    /// million must not stop an import that takes two hours.
    /// </summary>
    public static StrippedArticle Parse(string wikitext)
    {
        if (string.IsNullOrWhiteSpace(wikitext))
            return new StrippedArticle(string.Empty, Array.Empty<string>());

        try
        {
            var text = Comments.Replace(wikitext, string.Empty);
            text = RefTags.Replace(text, string.Empty);
            text = RemoveNested(text, "{{", "}}");   // templates, possibly nested
            text = RemoveNested(text, "{|", "|}");   // tables, possibly nested

            var links = new List<string>();
            text = UnwrapLinks(text, links);

            text = OtherTags.Replace(text, string.Empty);
            text = Emphasis.Replace(text, string.Empty);
            text = Heading.Replace(text, "$1");

            return new StrippedArticle(NormalizeLines(text), links);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return new StrippedArticle(string.Empty, Array.Empty<string>());
        }
    }

    /// <summary>The first sentence of <paramref name="body"/>, or empty.</summary>
    public static string LeadOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var firstParagraph = body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !line.StartsWith("* ", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(firstParagraph))
            return string.Empty;

        var sentence = SentenceEnd.Split(firstParagraph).FirstOrDefault() ?? string.Empty;
        return sentence.Trim();
    }

    /// <summary>
    /// Removes every <paramref name="open"/>..<paramref name="close"/> span, counting depth so a
    /// nested template ({{Infobox|a={{nested}}}}) is removed whole rather than leaving its tail
    /// behind. A span left unclosed at end-of-input is dropped along with the rest.
    /// </summary>
    private static string RemoveNested(string text, string open, string close)
    {
        var result = new StringBuilder(text.Length);
        var depth = 0;
        var i = 0;

        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, open, 0, open.Length) == 0)
            {
                depth++;
                i += open.Length;
            }
            else if (depth > 0 && string.CompareOrdinal(text, i, close, 0, close.Length) == 0)
            {
                depth--;
                i += close.Length;
            }
            else
            {
                if (depth == 0)
                    result.Append(text[i]);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Replaces every [[target|display]] with its display text and [[target]] with its target,
    /// appending each mainspace target to <paramref name="links"/>. A File/Category/Image link is
    /// neither prose nor a concept, so it contributes to neither output.
    /// </summary>
    private static string UnwrapLinks(string text, List<string> links)
    {
        var result = new StringBuilder(text.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, "[[", 0, 2) != 0)
            {
                result.Append(text[i]);
                i++;
                continue;
            }

            var end = text.IndexOf("]]", i + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                // Unclosed link: keep the remaining prose rather than dropping the article's tail.
                result.Append(text, i + 2, text.Length - i - 2);
                break;
            }

            var inner = text.Substring(i + 2, end - i - 2);
            i = end + 2;

            var pipe = inner.IndexOf('|');
            var target = (pipe < 0 ? inner : inner[..pipe]).Trim();
            var display = (pipe < 0 ? inner : inner[(pipe + 1)..]).Trim();

            if (NonArticlePrefixes.Any(p => target.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (target.Length > 0 && seen.Add(target))
                links.Add(target);

            result.Append(display);
        }

        return result.ToString();
    }

    /// <summary>
    /// Trims each line, rewrites any list bullet to a single "* " marker, drops blank lines, and
    /// collapses the result. Nesting depth is deliberately flattened: PropertyMiner cares that an
    /// item IS a list item, not how deeply indented it was.
    /// </summary>
    private static string NormalizeLines(string text)
    {
        var lines = new List<string>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (ListItem.IsMatch(line))
            {
                var content = ListItem.Replace(line, string.Empty).Trim();
                if (content.Length > 0)
                    lines.Add("* " + content);
                continue;
            }

            lines.Add(line);
        }

        return BlankRun.Replace(string.Join('\n', lines), "\n\n").Trim();
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests --filter WikitextStripperTests`
Expected: PASS — 12 passed.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Wikipedia/Dump/ DomainDiscovery.Tests/Wikipedia/Dump/
git commit -m "Strip wikitext to prose, and keep the list markers explaintext threw away"
```

---

## Task 2: WikipediaStore

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/WikipediaStore.cs`
- Modify: `DomainDiscovery/DomainDiscovery.csproj`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/WikipediaStoreTests.cs`

**Interfaces:**
- Consumes: `StrippedArticle` (Task 1).
- Produces:
  - `WikipediaStore(string databasePath)`
  - `void CreateSchema()`
  - `void WriteArticles(IEnumerable<ArticleRow> rows)` where `record ArticleRow(string Title, string Lead, string Body, IReadOnlyList<string> Links)`
  - `void WriteRedirects(IEnumerable<(string From, string To)> redirects)`
  - `string? ResolveTitle(string term)` — exact → redirect → normalised → prefix
  - `ArticleRow? Read(string title)` — follows redirects
  - `IReadOnlyList<string> PrefixSearch(string term, int limit)`
  - `void SetLastPageId(long id)` / `long GetLastPageId()` — resume support
  - `long ArticleCount { get; }`

- [ ] **Step 1: Add the packages**

```bash
cd D:/SmartCodeGenerator-Repository
dotnet add DomainDiscovery/DomainDiscovery.csproj package Microsoft.Data.Sqlite --version 10.0.9
dotnet add DomainDiscovery/DomainDiscovery.csproj package SQLitePCLRaw.bundle_e_sqlite3 --version 3.0.3
dotnet restore DomainDiscovery/DomainDiscovery.csproj
```

Expected: restore succeeds with **no NU1903 warning**. If NU1903 appears, the `SQLitePCLRaw.bundle_e_sqlite3` pin is missing or wrong — fix it before continuing; do not proceed with a known high-severity advisory.

- [ ] **Step 2: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/WikipediaStoreTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class WikipediaStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"wikistore-{Guid.NewGuid():N}.db");

    private WikipediaStore NewStore()
    {
        var store = new WikipediaStore(_path);
        store.CreateSchema();
        return store;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void Reads_back_an_article_it_wrote()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Invoice", "An invoice is a document.",
            "An invoice is a document.\n* invoice number", ["Receipt"])]);

        var row = store.Read("Invoice");

        Assert.NotNull(row);
        Assert.Equal("An invoice is a document.", row!.Lead);
        Assert.Contains("* invoice number", row.Body);
        Assert.Equal(new[] { "Receipt" }, row.Links);
    }

    [Fact]
    public void Read_is_case_insensitive_on_the_title()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Point of sale", "A point of sale.", "Body.", [])]);

        Assert.NotNull(store.Read("point of sale"));
    }

    [Fact]
    public void Read_follows_a_redirect_to_the_target_article()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Point of sale", "A point of sale.", "Body.", [])]);
        store.WriteRedirects([("POS", "Point of sale")]);

        var row = store.Read("POS");

        Assert.NotNull(row);
        Assert.Equal("Point of sale", row!.Title);
    }

    [Fact]
    public void Read_is_null_for_an_unknown_title()
    {
        Assert.Null(NewStore().Read("Nonexistent"));
    }

    [Fact]
    public void ResolveTitle_prefers_an_exact_match_over_a_prefix_match()
    {
        var store = NewStore();
        store.WriteArticles([
            new ArticleRow("Payment", "A payment.", "Body.", []),
            new ArticleRow("Payment terminal", "A terminal.", "Body.", [])]);

        Assert.Equal("Payment", store.ResolveTitle("Payment"));
    }

    [Fact]
    public void ResolveTitle_falls_back_to_a_prefix_match()
    {
        var store = NewStore();
        store.WriteArticles([new ArticleRow("Payment terminal", "A terminal.", "Body.", [])]);

        Assert.Equal("Payment terminal", store.ResolveTitle("Payment ter"));
    }

    [Fact]
    public void Remembers_the_last_page_id_so_an_import_can_resume()
    {
        var store = NewStore();
        Assert.Equal(0, store.GetLastPageId());

        store.SetLastPageId(4242);

        Assert.Equal(4242, store.GetLastPageId());
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter WikipediaStoreTests`
Expected: FAIL — `WikipediaStore` does not exist (CS0246).

- [ ] **Step 4: Implement**

Create `DomainDiscovery/Wikipedia/Dump/WikipediaStore.cs`:

```csharp
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>One article as it is stored and read back.</summary>
public sealed record ArticleRow(
    string Title, string Lead, string Body, IReadOnlyList<string> Links);

/// <summary>
/// The SQLite store the dump is imported into, and the only thing
/// <see cref="DumpWikipediaClient"/> reads.
/// </summary>
/// <remarks>
/// The dump on disk is the SINGLE-STREAM variant, which is one continuous bz2 stream: there is no
/// way to seek to an article without decompressing from byte zero. That is why this store exists
/// at all, rather than the client reading the dump directly.
/// <para>
/// Body and links are held as one Deflate-compressed blob. The redirect table is not an
/// optimisation — it is the search index, and an editor-curated synonym list ("POS" -> "Point of
/// sale") is a better answer than fuzzy string matching ever gave us.
/// </para>
/// </remarks>
public sealed class WikipediaStore
{
    private readonly string _connectionString;

    public WikipediaStore(string databasePath)
        => _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void CreateSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        // NOCASE on the keys is what makes every lookup case-insensitive without a second index.
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS pages (
                title   TEXT PRIMARY KEY COLLATE NOCASE,
                lead    TEXT NOT NULL,
                payload BLOB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS redirects (
                source  TEXT PRIMARY KEY COLLATE NOCASE,
                target  TEXT NOT NULL COLLATE NOCASE
            );

            CREATE TABLE IF NOT EXISTS import_state (
                key     TEXT PRIMARY KEY,
                value   INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public long ArticleCount
    {
        get
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pages";
            return (long)(command.ExecuteScalar() ?? 0L);
        }
    }

    /// <summary>Writes a batch in one transaction. Re-importing a title overwrites it.</summary>
    public void WriteArticles(IEnumerable<ArticleRow> rows)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.CommandText =
            "INSERT OR REPLACE INTO pages (title, lead, payload) VALUES ($title, $lead, $payload)";

        var title = command.Parameters.Add("$title", SqliteType.Text);
        var lead = command.Parameters.Add("$lead", SqliteType.Text);
        var payload = command.Parameters.Add("$payload", SqliteType.Blob);

        foreach (var row in rows)
        {
            title.Value = row.Title;
            lead.Value = row.Lead;
            payload.Value = Pack(row.Body, row.Links);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void WriteRedirects(IEnumerable<(string From, string To)> redirects)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.CommandText =
            "INSERT OR REPLACE INTO redirects (source, target) VALUES ($source, $target)";

        var source = command.Parameters.Add("$source", SqliteType.Text);
        var target = command.Parameters.Add("$target", SqliteType.Text);

        foreach (var (from, to) in redirects)
        {
            source.Value = from;
            target.Value = to;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// The article <paramref name="title"/> names, following at most one redirect hop. Null when
    /// nothing matches.
    /// </summary>
    public ArticleRow? Read(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        using var connection = Open();

        var row = ReadExact(connection, title);
        if (row is not null)
            return row;

        var target = ReadRedirect(connection, title);
        return target is null ? null : ReadExact(connection, target);
    }

    /// <summary>
    /// The title <paramref name="term"/> most likely means: exact, then redirect, then a
    /// prefix match. Deterministic, unlike the API's fuzzy opensearch ranking.
    /// </summary>
    public string? ResolveTitle(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return null;

        using var connection = Open();

        if (ReadExact(connection, term) is { } exact)
            return exact.Title;

        if (ReadRedirect(connection, term) is { } redirected)
            return redirected;

        return PrefixSearch(term, 1).FirstOrDefault();
    }

    public IReadOnlyList<string> PrefixSearch(string term, int limit)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Array.Empty<string>();

        using var connection = Open();
        using var command = connection.CreateCommand();

        // ORDER BY length keeps "Payment" ahead of "Payment terminal in Japan": the shortest
        // title carrying the prefix is the most general concept, which is the one wanted.
        command.CommandText = """
            SELECT title FROM pages
            WHERE title LIKE $prefix ESCAPE '\'
            ORDER BY LENGTH(title), title
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$prefix", EscapeLike(term) + "%");
        command.Parameters.AddWithValue("$limit", limit);

        var titles = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            titles.Add(reader.GetString(0));

        return titles;
    }

    public void SetLastPageId(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO import_state (key, value) VALUES ('last_page_id', $id)";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public long GetLastPageId()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM import_state WHERE key = 'last_page_id'";
        return command.ExecuteScalar() is long value ? value : 0L;
    }

    private static ArticleRow? ReadExact(SqliteConnection connection, string title)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title, lead, payload FROM pages WHERE title = $title";
        command.Parameters.AddWithValue("$title", title);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var (body, links) = Unpack((byte[])reader["payload"]);
        return new ArticleRow(reader.GetString(0), reader.GetString(1), body, links);
    }

    private static string? ReadRedirect(SqliteConnection connection, string source)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT target FROM redirects WHERE source = $source";
        command.Parameters.AddWithValue("$source", source);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Body and links in one Deflate blob: two reads become one, and the text compresses
    /// about four to one, which is the difference between a 40 GB store and a 12 GB one.</summary>
    /// <summary>
    /// Separates the body from the link list inside the blob. U+0001 cannot occur in
    /// wikitext-derived prose, so it can never appear inside a body and split it early. A space or
    /// a newline would NOT do: both are all over the body, and either would truncate an article at
    /// its first word.
    /// </summary>
    private const char Separator = '\u0001';

    private static byte[] Pack(string body, IReadOnlyList<string> links)
    {
        var payload = body + Separator + string.Join('\n', links);

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            deflate.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    private static (string Body, IReadOnlyList<string> Links) Unpack(byte[] payload)
    {
        using var input = new MemoryStream(payload);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var text = new StreamReader(deflate, Encoding.UTF8);

        var unpacked = text.ReadToEnd();
        var separator = unpacked.IndexOf(Separator);

        if (separator < 0)
            return (unpacked, Array.Empty<string>());

        var body = unpacked[..separator];
        var linkText = unpacked[(separator + 1)..];

        var links = linkText.Length == 0
            ? Array.Empty<string>()
            : linkText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return (body, links);
    }

    /// <summary>LIKE treats % and _ as wildcards; an article title may contain either.</summary>
    private static string EscapeLike(string term)
        => term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests --filter WikipediaStoreTests`
Expected: PASS — 7 passed.

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery/DomainDiscovery.csproj DomainDiscovery/Wikipedia/Dump/WikipediaStore.cs DomainDiscovery.Tests/Wikipedia/Dump/WikipediaStoreTests.cs
git commit -m "Store the imported dump in SQLite, with redirects as the search index"
```

---

## Task 3: DumpWikipediaClient

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/DumpWikipediaClient.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/DumpWikipediaClientTests.cs`

**Interfaces:**
- Consumes: `WikipediaStore`, `ArticleRow` (Task 2).
- Produces: `DumpWikipediaClient(WikipediaStore store) : IWikipediaClient` — implements `Search`, `FetchExtract`, `FetchLinks`, `FetchLead`, `FetchLeads`.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/DumpWikipediaClientTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class DumpWikipediaClientTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"wikiclient-{Guid.NewGuid():N}.db");
    private readonly DumpWikipediaClient _client;

    public DumpWikipediaClientTests()
    {
        var store = new WikipediaStore(_path);
        store.CreateSchema();
        store.WriteArticles([
            new ArticleRow("Point of sale", "A point of sale is where a sale is completed.",
                "A point of sale is where a sale is completed.\nThe customer pays the merchant.",
                ["Cash register", "Receipt"]),
            new ArticleRow("Receipt", "A receipt is a document acknowledging payment.",
                "A receipt is a document acknowledging payment.", [])]);
        store.WriteRedirects([("POS", "Point of sale")]);

        _client = new DumpWikipediaClient(store);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void Search_finds_an_article_by_its_exact_title()
    {
        var hits = _client.Search("Receipt");

        Assert.Single(hits);
        Assert.Equal("Receipt", hits[0].Title);
    }

    [Fact]
    public void Search_resolves_a_redirect_to_its_target()
    {
        var hits = _client.Search("POS");

        Assert.Single(hits);
        Assert.Equal("Point of sale", hits[0].Title);
    }

    [Fact]
    public void Search_is_empty_for_an_unknown_term()
    {
        Assert.Empty(_client.Search("Nonexistent topic"));
    }

    [Fact]
    public void FetchExtract_returns_the_whole_body()
    {
        Assert.Contains("The customer pays the merchant.", _client.FetchExtract("Point of sale"));
    }

    [Fact]
    public void FetchExtract_is_empty_for_an_unknown_title()
    {
        Assert.Equal(string.Empty, _client.FetchExtract("Nonexistent"));
    }

    [Fact]
    public void FetchLinks_returns_the_articles_outgoing_links()
    {
        Assert.Equal(new[] { "Cash register", "Receipt" }, _client.FetchLinks("Point of sale"));
    }

    [Fact]
    public void FetchLead_returns_the_first_sentence()
    {
        Assert.Equal(
            "A receipt is a document acknowledging payment.", _client.FetchLead("Receipt"));
    }

    [Fact]
    public void FetchLeads_returns_a_lead_per_known_title_and_omits_the_unknown()
    {
        var leads = _client.FetchLeads(["Receipt", "Nonexistent"]);

        Assert.True(leads.ContainsKey("Receipt"));
        Assert.False(leads.ContainsKey("Nonexistent"));
    }

    [Fact]
    public void FetchLeads_is_keyed_by_the_title_the_caller_asked_for_not_the_redirect_target()
    {
        var leads = _client.FetchLeads(["POS"]);

        Assert.True(leads.ContainsKey("POS"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter DumpWikipediaClientTests`
Expected: FAIL — `DumpWikipediaClient` does not exist (CS0246).

- [ ] **Step 3: Implement**

Create `DomainDiscovery/Wikipedia/Dump/DumpWikipediaClient.cs`:

```csharp
using DomainDiscovery.Model;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// <see cref="IWikipediaClient"/> over the imported dump. No network, so no throttle, no HTTP 429,
/// no timeout, and no cache directory.
/// </summary>
/// <remarks>
/// The never-throw contract is inherited unchanged from <see cref="IWikipediaClient"/>, but it now
/// protects against a corrupt store rather than a flaky network.
/// <para>
/// <see cref="FetchLeads"/> keeps its batch signature purely so callers need not change. The
/// rationale in the interface doc — batching to dodge the API's rate limit — no longer applies
/// here: offline, a batch is just a loop over local reads, and it is not faster than N calls.
/// </para>
/// </remarks>
public sealed class DumpWikipediaClient : IWikipediaClient
{
    /// <summary>How many prefix matches a search may return, mirroring the old opensearch limit=5.</summary>
    private const int MaxHits = 5;

    private readonly WikipediaStore _store;

    public DumpWikipediaClient(WikipediaStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public IReadOnlyList<SearchHit> Search(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return Array.Empty<SearchHit>();

        try
        {
            var resolved = _store.ResolveTitle(term.Trim());
            if (resolved is null)
                return Array.Empty<SearchHit>();

            var titles = new List<string> { resolved };

            foreach (var title in _store.PrefixSearch(term.Trim(), MaxHits))
            {
                if (!titles.Contains(title, StringComparer.OrdinalIgnoreCase))
                    titles.Add(title);
            }

            return titles
                .Take(MaxHits)
                .Select((title, rank) => new SearchHit(title, UrlFor(title), rank))
                .ToList();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            return Array.Empty<SearchHit>();
        }
    }

    public string FetchExtract(string articleTitle) => Read(articleTitle)?.Body ?? string.Empty;

    public IReadOnlyList<string> FetchLinks(string articleTitle)
        => Read(articleTitle)?.Links ?? Array.Empty<string>();

    public string FetchLead(string articleTitle) => Read(articleTitle)?.Lead ?? string.Empty;

    public IReadOnlyDictionary<string, string> FetchLeads(IReadOnlyList<string> articleTitles)
    {
        var leads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var title in articleTitles)
        {
            if (string.IsNullOrWhiteSpace(title) || leads.ContainsKey(title))
                continue;

            // Keyed by the title the CALLER asked for, even when a redirect resolved it to another
            // article -- the interface promises exactly that, and EntityShaper looks its own
            // SourceTitle back up in this map.
            var lead = Read(title)?.Lead;
            if (!string.IsNullOrEmpty(lead))
                leads[title] = lead;
        }

        return leads;
    }

    private ArticleRow? Read(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        try
        {
            return _store.Read(title);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The article's public URL. The store holds no URLs — every one is derivable.</summary>
    private static string UrlFor(string title)
        => "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests --filter DumpWikipediaClientTests`
Expected: PASS — 9 passed.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Wikipedia/Dump/DumpWikipediaClient.cs DomainDiscovery.Tests/Wikipedia/Dump/DumpWikipediaClientTests.cs
git commit -m "Answer IWikipediaClient from the local store instead of the API"
```

---

## Task 4: DumpPageReader

**Files:**
- Create: `WikipediaDumpImporter/WikipediaDumpImporter.csproj`
- Create: `WikipediaDumpImporter/DumpPageReader.cs`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx`
- Modify: `DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/DumpPageReaderTests.cs`
- Create: `DomainDiscovery.Tests/Fixtures/sample-dump.xml`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `record RawPage(long Id, string Title, int Namespace, string? RedirectTo, string Wikitext)`; `DumpPageReader.ReadXml(Stream xml) → IEnumerable<RawPage>` (lazy); `DumpPageReader.ReadBz2(string path) → IEnumerable<RawPage>` (lazy).

- [ ] **Step 1: Create the project and wire it up**

```bash
cd D:/SmartCodeGenerator-Repository
dotnet new console -o WikipediaDumpImporter -n WikipediaDumpImporter --framework net10.0
dotnet add WikipediaDumpImporter/WikipediaDumpImporter.csproj package SharpZipLib --version 1.4.2
dotnet add WikipediaDumpImporter/WikipediaDumpImporter.csproj reference DomainDiscovery/DomainDiscovery.csproj
dotnet sln SmartCodeGenerator/SmartCodeGenerator.slnx add WikipediaDumpImporter/WikipediaDumpImporter.csproj
dotnet add DomainDiscovery.Tests/DomainDiscovery.Tests.csproj reference WikipediaDumpImporter/WikipediaDumpImporter.csproj
```

Confirm `WikipediaDumpImporter/WikipediaDumpImporter.csproj` has `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`; add them if `dotnet new` omitted them.

- [ ] **Step 2: Create the test fixture**

Create `DomainDiscovery.Tests/Fixtures/sample-dump.xml` — a hand-cut sample in the dump's real schema (`export-0.11`), containing an article, a redirect, and a non-mainspace page that must be skipped:

```xml
<mediawiki xmlns="http://www.mediawiki.org/xml/export-0.11/" version="0.11" xml:lang="en">
  <siteinfo>
    <sitename>Wikipedia</sitename>
    <dbname>enwiki</dbname>
  </siteinfo>
  <page>
    <title>Invoice</title>
    <ns>0</ns>
    <id>101</id>
    <revision>
      <id>900</id>
      <text xml:space="preserve">An '''invoice''' is a [[commercial document]].

An invoice typically contains:
* invoice number
* date of issue
* total amount</text>
    </revision>
  </page>
  <page>
    <title>Bill (payment)</title>
    <ns>0</ns>
    <id>102</id>
    <redirect title="Invoice" />
    <revision>
      <id>901</id>
      <text xml:space="preserve">#REDIRECT [[Invoice]]</text>
    </revision>
  </page>
  <page>
    <title>Talk:Invoice</title>
    <ns>1</ns>
    <id>103</id>
    <revision>
      <id>902</id>
      <text xml:space="preserve">Discussion about the invoice article.</text>
    </revision>
  </page>
</mediawiki>
```

Add to `DomainDiscovery.Tests/DomainDiscovery.Tests.csproj`, inside a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <None Update="Fixtures\sample-dump.xml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/DumpPageReaderTests.cs`:

```csharp
using WikipediaDumpImporter;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public class DumpPageReaderTests
{
    private static List<RawPage> ReadFixture()
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-dump.xml"));
        return DumpPageReader.ReadXml(stream).ToList();
    }

    [Fact]
    public void Reads_every_page_including_the_non_mainspace_one()
    {
        // Filtering by namespace is ImportRunner's job, not the reader's: the reader
        // reports what the dump says, so the filter stays testable on its own.
        Assert.Equal(3, ReadFixture().Count);
    }

    [Fact]
    public void Reads_a_pages_id_title_and_namespace()
    {
        var invoice = ReadFixture().Single(p => p.Title == "Invoice");

        Assert.Equal(101, invoice.Id);
        Assert.Equal(0, invoice.Namespace);
    }

    [Fact]
    public void Reads_the_wikitext_body()
    {
        var invoice = ReadFixture().Single(p => p.Title == "Invoice");

        Assert.Contains("* invoice number", invoice.Wikitext);
    }

    [Fact]
    public void Reports_a_redirects_target()
    {
        var bill = ReadFixture().Single(p => p.Title == "Bill (payment)");

        Assert.Equal("Invoice", bill.RedirectTo);
    }

    [Fact]
    public void An_article_has_no_redirect_target()
    {
        Assert.Null(ReadFixture().Single(p => p.Title == "Invoice").RedirectTo);
    }

    [Fact]
    public void Reports_a_non_mainspace_namespace_as_itself()
    {
        Assert.Equal(1, ReadFixture().Single(p => p.Title == "Talk:Invoice").Namespace);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter DumpPageReaderTests`
Expected: FAIL — `DumpPageReader` does not exist (CS0246).

- [ ] **Step 5: Implement**

Create `WikipediaDumpImporter/DumpPageReader.cs`:

```csharp
using System.Xml;
using ICSharpCode.SharpZipLib.BZip2;

namespace WikipediaDumpImporter;

/// <summary>One page exactly as the dump states it, before any filtering or stripping.</summary>
public sealed record RawPage(
    long Id, string Title, int Namespace, string? RedirectTo, string Wikitext);

/// <summary>
/// Streams pages out of a MediaWiki XML export.
/// </summary>
/// <remarks>
/// Lazy on purpose. The dump expands to roughly 97 GB of XML (measured: 3.83x on the real file),
/// so nothing here may materialise the document — an <see cref="XmlReader"/> forward-only walk is
/// the only shape that fits in memory.
/// <para>
/// bz2 decoding is the hard ceiling on the whole import: 23.6 MB/s of output, single-threaded and
/// CPU-bound, which puts a full pass at about 69 minutes. Disk reads at 126 MB/s, so it is not the
/// constraint. Do not try to make this faster by reading ahead.
/// </para>
/// </remarks>
public static class DumpPageReader
{
    /// <summary>Pages from a bz2-compressed dump, decompressed as they are read.</summary>
    public static IEnumerable<RawPage> ReadBz2(string dumpPath)
    {
        using var file = File.OpenRead(dumpPath);
        using var bz2 = new BZip2InputStream(file);

        foreach (var page in ReadXml(bz2))
            yield return page;
    }

    /// <summary>Pages from an already-decompressed XML stream.</summary>
    public static IEnumerable<RawPage> ReadXml(Stream xml)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = false,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Ignore
        };

        using var reader = XmlReader.Create(xml, settings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "page")
                continue;

            var page = ReadPage(reader);
            if (page is not null)
                yield return page;
        }
    }

    /// <summary>
    /// Consumes one &lt;page&gt; element. Returns null for a page with no title or no text, which
    /// the dump does contain — a deleted revision leaves the element behind.
    /// </summary>
    private static RawPage? ReadPage(XmlReader reader)
    {
        long id = 0;
        var ns = 0;
        string? title = null;
        string? redirectTo = null;
        string? wikitext = null;
        var idSeen = false;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "page")
                break;

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            switch (reader.LocalName)
            {
                case "title":
                    title = reader.ReadElementContentAsString();
                    break;

                case "ns":
                    int.TryParse(reader.ReadElementContentAsString(), out ns);
                    break;

                // <page> and <revision> both carry an <id>; the first one is the page's.
                case "id" when !idSeen:
                    long.TryParse(reader.ReadElementContentAsString(), out id);
                    idSeen = true;
                    break;

                case "redirect":
                    redirectTo = reader.GetAttribute("title");
                    break;

                case "text":
                    wikitext = reader.ReadElementContentAsString();
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        return new RawPage(id, title, ns, redirectTo, wikitext ?? string.Empty);
    }
}
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests --filter DumpPageReaderTests`
Expected: PASS — 6 passed.

- [ ] **Step 7: Commit**

```bash
git add WikipediaDumpImporter/ SmartCodeGenerator/SmartCodeGenerator.slnx DomainDiscovery.Tests/
git commit -m "Stream pages out of the dump XML, lazily"
```

---

## Task 5: ImportRunner

**Files:**
- Create: `WikipediaDumpImporter/ImportRunner.cs`
- Modify: `WikipediaDumpImporter/Program.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/ImportRunnerTests.cs`

**Interfaces:**
- Consumes: `DumpPageReader.ReadXml`, `RawPage` (Task 4); `WikipediaStore`, `ArticleRow` (Task 2); `WikitextStripper.Parse`, `LeadOf` (Task 1).
- Produces: `ImportRunner(WikipediaStore store, Action<string>? progress = null)`; `ImportStats Run(IEnumerable<RawPage> pages)` where `record ImportStats(long Articles, long Redirects, long Skipped)`.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/ImportRunnerTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using WikipediaDumpImporter;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class ImportRunnerTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"wikiimport-{Guid.NewGuid():N}.db");
    private readonly WikipediaStore _store;

    public ImportRunnerTests()
    {
        _store = new WikipediaStore(_path);
        _store.CreateSchema();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static IEnumerable<RawPage> Fixture()
    {
        using var stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-dump.xml"));
        return DumpPageReader.ReadXml(stream).ToList();
    }

    [Fact]
    public void Imports_the_mainspace_article_and_skips_the_talk_page()
    {
        var stats = new ImportRunner(_store).Run(Fixture());

        Assert.Equal(1, stats.Articles);
        Assert.Equal(1, stats.Redirects);
        Assert.Equal(1, stats.Skipped);
    }

    [Fact]
    public void The_imported_article_is_stripped_to_prose()
    {
        new ImportRunner(_store).Run(Fixture());

        var row = _store.Read("Invoice");

        Assert.NotNull(row);
        Assert.Equal("An invoice is a commercial document.", row!.Lead);
        Assert.DoesNotContain("'''", row.Body);
    }

    [Fact]
    public void The_imported_article_keeps_its_list_markers()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Contains("* invoice number", _store.Read("Invoice")!.Body);
    }

    [Fact]
    public void The_imported_article_keeps_its_links()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Equal(new[] { "commercial document" }, _store.Read("Invoice")!.Links);
    }

    [Fact]
    public void A_redirect_resolves_to_its_target_after_import()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Equal("Invoice", _store.Read("Bill (payment)")!.Title);
    }

    [Fact]
    public void Records_the_last_page_id_so_a_rerun_can_resume()
    {
        new ImportRunner(_store).Run(Fixture());

        Assert.Equal(103, _store.GetLastPageId());
    }

    [Fact]
    public void A_resumed_run_skips_pages_already_committed()
    {
        _store.SetLastPageId(101);

        var stats = new ImportRunner(_store).Run(Fixture());

        // 101 (Invoice) is already in; only the redirect and the talk page are still to come.
        Assert.Equal(0, stats.Articles);
        Assert.Equal(1, stats.Redirects);
    }

    [Fact]
    public void A_page_with_unparseable_wikitext_does_not_stop_the_import()
    {
        var pages = new[]
        {
            new RawPage(1, "Broken", 0, null, "{{unclosed [[also unclosed"),
            new RawPage(2, "Good", 0, null, "A good article.")
        };

        var stats = new ImportRunner(_store).Run(pages);

        Assert.NotNull(_store.Read("Good"));
        Assert.Equal(2, stats.Articles);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter ImportRunnerTests`
Expected: FAIL — `ImportRunner` does not exist (CS0246).

- [ ] **Step 3: Implement**

Create `WikipediaDumpImporter/ImportRunner.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaDumpImporter;

/// <summary>What one import pass did.</summary>
public sealed record ImportStats(long Articles, long Redirects, long Skipped);

/// <summary>
/// Turns a stream of <see cref="RawPage"/> into the store: strips wikitext to prose, keeps
/// mainspace only, and records progress so an interrupted run can resume.
/// </summary>
/// <remarks>
/// The full import takes 1.5-2 hours (bz2 decode is the ceiling at ~69 minutes). Resumability is
/// therefore a requirement, not a nicety: a crash 90 minutes in must not cost the whole run. The
/// page id is monotonically increasing in the dump, which is what makes "skip anything at or below
/// the last committed id" a correct resume rule.
/// </remarks>
public sealed class ImportRunner
{
    /// <summary>Rows per transaction. Large enough that commit overhead disappears, small enough
    /// that a crash loses seconds of work rather than minutes.</summary>
    private const int BatchSize = 2000;

    /// <summary>Pages between progress reports.</summary>
    private const int ProgressInterval = 50_000;

    private readonly WikipediaStore _store;
    private readonly Action<string>? _progress;

    public ImportRunner(WikipediaStore store, Action<string>? progress = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _progress = progress;
    }

    public ImportStats Run(IEnumerable<RawPage> pages)
    {
        var resumeFrom = _store.GetLastPageId();

        var articles = new List<ArticleRow>(BatchSize);
        var redirects = new List<(string From, string To)>(BatchSize);

        long articleCount = 0, redirectCount = 0, skipped = 0, seen = 0, lastId = resumeFrom;

        foreach (var page in pages)
        {
            seen++;

            if (page.Id <= resumeFrom)
                continue;

            lastId = page.Id;

            if (page.Namespace != 0)
            {
                skipped++;
            }
            else if (page.RedirectTo is { Length: > 0 } target)
            {
                redirects.Add((page.Title, target));
                redirectCount++;
            }
            else
            {
                var stripped = WikitextStripper.Parse(page.Wikitext);

                // An article that strips to nothing (a stub that was pure template) is not an
                // error -- it simply has no prose to offer, and storing it would only make
                // FetchExtract return "".
                if (stripped.Body.Length > 0)
                {
                    articles.Add(new ArticleRow(
                        page.Title,
                        WikitextStripper.LeadOf(stripped.Body),
                        stripped.Body,
                        stripped.Links));
                    articleCount++;
                }
                else
                {
                    skipped++;
                }
            }

            if (articles.Count + redirects.Count >= BatchSize)
                Flush(articles, redirects, lastId);

            if (seen % ProgressInterval == 0)
                _progress?.Invoke($"{seen:N0} pages read, {articleCount:N0} articles stored");
        }

        Flush(articles, redirects, lastId);

        return new ImportStats(articleCount, redirectCount, skipped);
    }

    /// <summary>
    /// Commits a batch and advances the resume point. The id is written LAST and in the same call
    /// as the rows, so a crash between the two can only ever re-import work, never lose it.
    /// </summary>
    private void Flush(List<ArticleRow> articles, List<(string From, string To)> redirects, long lastId)
    {
        if (articles.Count > 0)
        {
            _store.WriteArticles(articles);
            articles.Clear();
        }

        if (redirects.Count > 0)
        {
            _store.WriteRedirects(redirects);
            redirects.Clear();
        }

        if (lastId > 0)
            _store.SetLastPageId(lastId);
    }
}
```

Replace `WikipediaDumpImporter/Program.cs` entirely:

```csharp
using System.Diagnostics;
using DomainDiscovery.Wikipedia.Dump;
using WikipediaDumpImporter;

var dumpPath = args.Length > 0
    ? args[0]
    : @"D:\SmartCodeGenerator-Repository\Dump\enwiki-latest-pages-articles.xml.bz2";

var databasePath = args.Length > 1
    ? args[1]
    : @"D:\SmartCodeGenerator-Repository\Dump\enwiki.db";

if (!File.Exists(dumpPath))
{
    Console.Error.WriteLine($"Dump not found: {dumpPath}");
    return 1;
}

Console.WriteLine($"Dump:  {dumpPath}");
Console.WriteLine($"Store: {databasePath}");
Console.WriteLine();
Console.WriteLine("bz2 decode runs at ~23.6 MB/s single-threaded, so expect 1.5-2 hours.");
Console.WriteLine("The run is resumable: re-running after a crash continues where it stopped.");
Console.WriteLine();

var store = new WikipediaStore(databasePath);
store.CreateSchema();

if (store.GetLastPageId() > 0)
    Console.WriteLine($"Resuming after page id {store.GetLastPageId():N0}.");

var clock = Stopwatch.StartNew();

var stats = new ImportRunner(store, message =>
        Console.WriteLine($"[{clock.Elapsed:hh\\:mm\\:ss}] {message}"))
    .Run(DumpPageReader.ReadBz2(dumpPath));

clock.Stop();

var sizeGb = new FileInfo(databasePath).Length / 1024d / 1024d / 1024d;

Console.WriteLine();
Console.WriteLine($"Done in {clock.Elapsed:hh\\:mm\\:ss}.");
Console.WriteLine($"  articles:  {stats.Articles:N0}");
Console.WriteLine($"  redirects: {stats.Redirects:N0}");
Console.WriteLine($"  skipped:   {stats.Skipped:N0}");
Console.WriteLine($"  store:     {sizeGb:F1} GB");

return 0;
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests --filter ImportRunnerTests`
Expected: PASS — 8 passed.

- [ ] **Step 5: Commit**

```bash
git add WikipediaDumpImporter/ DomainDiscovery.Tests/
git commit -m "Import the dump into the store, resumably"
```

---

## Task 6: Run the real import, and make the pipeline prefer the dump

This task is half operational. **The import takes 1.5-2 hours** — start it, then do Task 7 while it runs.

**Files:**
- Modify: `DomainDiscovery/DomainDiscoveryPipeline.cs:83-100`
- Test: `DomainDiscovery.Tests/DomainDiscoveryPipelineTests.cs` (add one test)

**Interfaces:**
- Consumes: `DumpWikipediaClient` (Task 3), `WikipediaStore` (Task 2).
- Produces: `DomainDiscoveryPipeline.CreateDefault(string cacheDirectory, string? dumpDatabasePath = null)` — unchanged signature for existing callers.

- [ ] **Step 1: Start the import (it runs for ~2 hours)**

```bash
cd D:/SmartCodeGenerator-Repository
dotnet run --project WikipediaDumpImporter -c Release
```

Expected: progress lines every 50,000 pages, then a summary. Leave it running.

- [ ] **Step 2: Write the failing test**

Add to `DomainDiscovery.Tests/DomainDiscoveryPipelineTests.cs`:

```csharp
[Fact]
public void CreateDefault_falls_back_to_the_http_client_when_no_dump_store_exists()
{
    var cacheDirectory = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}");
    var missingStore = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.db");

    // Must not throw, and must not create a store: an unbuilt dump is a reason to use the
    // network, not a reason to fail.
    var pipeline = DomainDiscoveryPipeline.CreateDefault(cacheDirectory, missingStore);

    Assert.NotNull(pipeline);
    Assert.False(File.Exists(missingStore));
}
```

- [ ] **Step 3: Run it and confirm it fails**

Run: `dotnet test DomainDiscovery.Tests --filter CreateDefault_falls_back`
Expected: FAIL — `CreateDefault` takes one argument (CS1501).

- [ ] **Step 4: Implement**

In `DomainDiscovery/DomainDiscoveryPipeline.cs`, add `using DomainDiscovery.Wikipedia.Dump;` and replace `CreateDefault` (lines 83-100):

```csharp
    /// <summary>
    /// The dump store the pipeline uses when the caller names none. It is built once by
    /// WikipediaDumpImporter and is the normal source: local, deterministic, and free of the
    /// API's rate limit.
    /// </summary>
    public const string DefaultDumpDatabasePath =
        @"D:\SmartCodeGenerator-Repository\Dump\enwiki.db";

    /// <summary>
    /// Builds a pipeline over the local Wikipedia dump when its store exists, and over the
    /// MediaWiki API when it does not.
    /// </summary>
    /// <remarks>
    /// The dump is the intended source. <see cref="HttpWikipediaClient"/> survives only as the
    /// answer to "the store has not been imported yet", because failing outright would leave a
    /// fresh clone with no way to run `build app:` at all.
    /// </remarks>
    /// <param name="cacheDirectory">Disk cache for the HTTP fallback. Unused by the dump client.</param>
    /// <param name="dumpDatabasePath">
    /// The SQLite store to read. Defaults to <see cref="DefaultDumpDatabasePath"/>.
    /// </param>
    public static DomainDiscoveryPipeline CreateDefault(
        string cacheDirectory, string? dumpDatabasePath = null)
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;
        var databasePath = dumpDatabasePath ?? DefaultDumpDatabasePath;

        IWikipediaClient client = File.Exists(databasePath)
            ? new DumpWikipediaClient(new WikipediaStore(databasePath))
            : new HttpWikipediaClient(cacheDirectory);

        var harvester = new EntityHarvester();

        return new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, harvester),
            new EntityShaper(client),
            client);
    }
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests`
Expected: PASS — the whole suite, including the new test.

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery/DomainDiscoveryPipeline.cs DomainDiscovery.Tests/DomainDiscoveryPipelineTests.cs
git commit -m "Read Wikipedia from the dump, and keep the API only as a fallback"
```

- [ ] **Step 7: When the import finishes, record what it produced**

Note the reported article count, redirect count, elapsed time, and store size. The spec estimated 10-15 GB; if the real figure differs materially, say so rather than quietly accepting it.

---

## Task 7: RoleTemplate

**Files:**
- Create: `DomainDiscovery/Enrichment/RoleTemplate.cs`
- Test: `DomainDiscovery.Tests/Enrichment/RoleTemplateTests.cs`

**Interfaces:**
- Consumes: `EntityRole` (existing, in `DomainDiscovery.Enrichment`); `DomainFieldLexicon.FieldShape` (existing `readonly record struct FieldShape(string Name, string Type)`).
- Produces: `RoleTemplate.For(EntityRole role) → IReadOnlyList<FieldShape>`.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Enrichment/RoleTemplateTests.cs`:

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class RoleTemplateTests
{
    [Fact]
    public void A_document_gets_the_fields_every_document_has()
    {
        var names = RoleTemplate.For(EntityRole.Document).Select(f => f.Name).ToList();

        Assert.Contains("Number", names);
        Assert.Contains("IssuedOn", names);
        Assert.Contains("Status", names);
    }

    [Fact]
    public void A_transaction_carries_an_amount_and_a_currency()
    {
        var fields = RoleTemplate.For(EntityRole.Transaction);

        Assert.Contains(fields, f => f.Name == "Amount" && f.Type == "decimal");
        Assert.Contains(fields, f => f.Name == "Currency" && f.Type == "string");
    }

    [Fact]
    public void A_party_is_a_person_or_an_organisation_we_can_contact()
    {
        var names = RoleTemplate.For(EntityRole.Party).Select(f => f.Name).ToList();

        Assert.Contains("FullName", names);
        Assert.Contains("Email", names);
        Assert.Contains("Phone", names);
    }

    [Theory]
    [InlineData(EntityRole.Device)]
    [InlineData(EntityRole.Place)]
    [InlineData(EntityRole.Item)]
    public void Every_shaped_role_offers_at_least_three_fields(EntityRole role)
    {
        Assert.True(RoleTemplate.For(role).Count >= 3);
    }

    [Fact]
    public void None_offers_nothing_because_it_is_not_a_kind_of_thing()
    {
        Assert.Empty(RoleTemplate.For(EntityRole.None));
    }

    [Fact]
    public void No_template_declares_the_same_field_twice()
    {
        foreach (var role in Enum.GetValues<EntityRole>())
        {
            var names = RoleTemplate.For(role).Select(f => f.Name).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    // Id is generated by DomainModelMapper, and a property may never share its class's name
    // (CS0542). Neither may ever come from a template.
    [Fact]
    public void No_template_declares_a_field_named_Id()
    {
        foreach (var role in Enum.GetValues<EntityRole>())
            Assert.DoesNotContain(RoleTemplate.For(role), f => f.Name == "Id");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter RoleTemplateTests`
Expected: FAIL — `RoleTemplate` does not exist (CS0246).

- [ ] **Step 3: Implement**

Create `DomainDiscovery/Enrichment/RoleTemplate.cs`:

```csharp
using FieldShape = DomainDiscovery.Enrichment.DomainFieldLexicon.FieldShape;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// The baseline properties every class of a given <see cref="EntityRole"/> carries.
/// </summary>
/// <remarks>
/// <see cref="ArchetypeLexicon"/> used to answer two questions at once: what KIND of thing a
/// candidate is, and what fields it therefore has. It answered the first well and the second
/// stingily — three fields, the same three for every device in the world.
/// <para>
/// So the two are split. ArchetypeLexicon keeps the question it is good at (the role, and the
/// Shaped/Abstract/Unknown verdict); this table answers the other one, more generously. It is
/// deliberately generic: what makes a class specific is <see cref="PropertyMiner"/>, which reads
/// the class's own article. This is only the floor beneath that.
/// </para>
/// </remarks>
public static class RoleTemplate
{
    private static FieldShape Text(string name) => new(name, "string");
    private static FieldShape Money(string name) => new(name, "decimal");
    private static FieldShape When(string name) => new(name, "DateTime");
    private static FieldShape Flag(string name) => new(name, "bool");

    private static readonly Dictionary<EntityRole, FieldShape[]> Templates = new()
    {
        [EntityRole.Party] =
            [Text("FullName"), Text("Email"), Text("Phone"), Text("Address"), Flag("Active")],

        [EntityRole.Place] =
            [Text("Name"), Text("Address"), Text("City"), Text("PostCode"), Text("Phone")],

        [EntityRole.Device] =
            [Text("Name"), Text("Model"), Text("SerialNumber"),
             Text("Manufacturer"), When("InstalledOn")],

        [EntityRole.Item] =
            [Text("Name"), Text("Sku"), Text("Description"), Money("Price"), Text("Unit")],

        [EntityRole.Document] =
            [Text("Number"), When("IssuedOn"), Text("Status"),
             Money("Total"), Text("Currency"), Text("Notes")],

        [EntityRole.Transaction] =
            [Money("Amount"), Text("Currency"), When("OccurredOn"),
             Text("Method"), Text("Reference"), Text("Status")],

        // Not a kind of thing another class references, and so not a kind of thing with a shape.
        [EntityRole.None] = [],
    };

    /// <summary>The baseline fields of <paramref name="role"/>, or empty for a role with no shape.</summary>
    public static IReadOnlyList<FieldShape> For(EntityRole role)
        => Templates.TryGetValue(role, out var fields) ? fields : Array.Empty<FieldShape>();
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests --filter RoleTemplateTests`
Expected: PASS — 9 passed (the `[Theory]` counts as 3).

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/RoleTemplate.cs DomainDiscovery.Tests/Enrichment/RoleTemplateTests.cs
git commit -m "Give each role a fuller baseline than three fields"
```

---

## Task 8: PropertyMiner

The payoff for the whole offline path: with article reads free, a class's own article can be read for the properties it states.

**Files:**
- Create: `DomainDiscovery/Enrichment/PropertyMiner.cs`
- Test: `DomainDiscovery.Tests/Enrichment/PropertyMinerTests.cs`

**Interfaces:**
- Consumes: `DomainFieldLexicon.FieldShape`; `NameNormalizer.ToFieldName(string) → string` and `NameNormalizer.IsValidIdentifier(string) → bool` (existing, `DomainDiscovery.SeedExtraction`); `TypeInference.Infer(string fieldName) → string` (existing); `StopWords.Contains(string) → bool` (existing).
- Produces: `PropertyMiner.Mine(string articleBody, string entityName) → IReadOnlyList<FieldShape>`.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Enrichment/PropertyMinerTests.cs`:

```csharp
using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class PropertyMinerTests
{
    [Fact]
    public void Mines_a_bulleted_list_that_follows_a_containment_lead_in()
    {
        var article = """
            An invoice is a commercial document.
            An invoice typically contains:
            * invoice number
            * date of issue
            * total amount
            """;

        var names = PropertyMiner.Mine(article, "Invoice").Select(f => f.Name).ToList();

        Assert.Contains("InvoiceNumber", names);
        Assert.Contains("DateOfIssue", names);
        Assert.Contains("TotalAmount", names);
    }

    [Fact]
    public void Ignores_a_bulleted_list_with_no_containment_lead_in()
    {
        // A "See also" list is not a property list. Without the lead-in rule, every article's
        // navigation furniture becomes fields.
        var article = """
            An invoice is a commercial document.
            See also:
            * Receipt
            * Purchase order
            """;

        Assert.Empty(PropertyMiner.Mine(article, "Invoice"));
    }

    [Fact]
    public void Mines_a_prose_enumeration_after_a_containment_verb()
    {
        var article = "A receipt includes the date of sale, the amount paid and the payment method.";

        var names = PropertyMiner.Mine(article, "Receipt").Select(f => f.Name).ToList();

        Assert.Contains("DateOfSale", names);
        Assert.Contains("AmountPaid", names);
        Assert.Contains("PaymentMethod", names);
    }

    [Fact]
    public void Infers_a_type_for_each_mined_property()
    {
        var article = """
            An invoice contains:
            * total amount
            * date of issue
            """;

        var fields = PropertyMiner.Mine(article, "Invoice");

        Assert.Contains(fields, f => f.Name == "TotalAmount" && f.Type == "decimal");
        Assert.Contains(fields, f => f.Name == "DateOfIssue" && f.Type == "DateTime");
    }

    [Fact]
    public void Never_mines_a_property_named_Id()
    {
        var article = """
            A record contains:
            * id
            * name
            """;

        Assert.DoesNotContain(PropertyMiner.Mine(article, "Record"), f => f.Name == "Id");
    }

    // class Invoice { public string Invoice { get; set; } } is CS0542.
    [Fact]
    public void Never_mines_a_property_named_after_its_own_class()
    {
        var article = """
            An invoice contains:
            * invoice
            * total amount
            """;

        Assert.DoesNotContain(PropertyMiner.Mine(article, "Invoice"), f => f.Name == "Invoice");
    }

    [Fact]
    public void Rejects_a_phrase_too_long_to_be_a_property_name()
    {
        var article = """
            An invoice contains:
            * a detailed explanation of every term and condition that may apply
            * total amount
            """;

        var names = PropertyMiner.Mine(article, "Invoice").Select(f => f.Name).ToList();

        Assert.Equal(new[] { "TotalAmount" }, names);
    }

    [Fact]
    public void Does_not_mine_the_same_property_twice()
    {
        var article = """
            An invoice contains:
            * total amount
            * total amount
            """;

        Assert.Single(PropertyMiner.Mine(article, "Invoice"));
    }

    [Fact]
    public void Caps_how_many_properties_one_article_may_contribute()
    {
        var items = string.Join('\n', Enumerable.Range(1, 30).Select(n => $"* field number {n}"));
        var article = "A record contains:\n" + items;

        Assert.True(PropertyMiner.Mine(article, "Record").Count <= PropertyMiner.MaxMinedFields);
    }

    // The no-op contract: an article with nothing to say must leave the class exactly as
    // EntityShaper already shaped it, never worse.
    [Fact]
    public void Mines_nothing_from_an_article_with_no_property_evidence()
    {
        Assert.Empty(PropertyMiner.Mine("A point of sale is where a sale is completed.", "PointOfSale"));
    }

    [Fact]
    public void Mines_nothing_from_empty_input()
    {
        Assert.Empty(PropertyMiner.Mine("", "Invoice"));
        Assert.Empty(PropertyMiner.Mine(null!, "Invoice"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter PropertyMinerTests`
Expected: FAIL — `PropertyMiner` does not exist (CS0246).

- [ ] **Step 3: Implement**

Create `DomainDiscovery/Enrichment/PropertyMiner.cs`:

```csharp
using System.Text.RegularExpressions;
using DomainDiscovery.SeedExtraction;
using FieldShape = DomainDiscovery.Enrichment.DomainFieldLexicon.FieldShape;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Mines a class's properties from the article the class was discovered from.
/// </summary>
/// <remarks>
/// <para>
/// This is what the offline dump buys. Reading each candidate's own article costs one article
/// fetch per candidate, and twenty candidates is twenty fetches — exactly what the MediaWiki API's
/// rate limit forbade, and the reason <see cref="EntityShaper"/> settled for guessing a shape from
/// a single lead sentence. Against a local store that read is a seek.
/// </para>
/// <para>
/// Two sources, in confidence order. A <b>bulleted list introduced by a containment phrase</b>
/// ("An invoice typically contains:") is the strong one: an encyclopedia states a concept's parts
/// exactly there, and it is the reason <see cref="Wikipedia.Dump.WikitextStripper"/> goes out of
/// its way to keep list markers the API's explaintext destroyed. A <b>prose enumeration</b> after
/// a containment verb is the weaker one, and is only consulted for what the lists did not give.
/// </para>
/// <para>
/// The lead-in requirement is not a nicety. Without it, every "See also" and "References" list in
/// Wikipedia becomes a property list, and the class fills with navigation furniture.
/// </para>
/// <para>
/// <b>No evidence is a no-op.</b> An article with nothing to say returns empty, and the entity
/// stays exactly as <see cref="EntityShaper"/> shaped it. A miner that could make a model worse
/// than the one it replaces would be a regression, not a feature.
/// </para>
/// </remarks>
public static class PropertyMiner
{
    /// <summary>
    /// The most properties one article may contribute. A class with thirty mined fields is not a
    /// better model than one with eight — it is an unreadable grid. The strongest evidence
    /// (the earliest, list-borne items) survives the cap.
    /// </summary>
    public const int MaxMinedFields = 8;

    /// <summary>The most words a phrase may hold and still be a property name. "date of issue"
    /// is a property; "a detailed explanation of every term that may apply" is a sentence.</summary>
    private const int MaxPhraseWords = 4;

    /// <summary>
    /// The verbs an encyclopedia uses when it is about to state what something is made of. A list
    /// or enumeration NOT introduced by one of these is not evidence of anything.
    /// </summary>
    private static readonly Regex ContainmentLeadIn = new(
        @"\b(contains?|contain|includes?|include|comprises?|comprise|consists? of|records?|shows?|lists?|carries|specif(?:y|ies)|states?)\b[^.]*:\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The same verbs, mid-sentence, followed by the enumeration itself.</summary>
    private static readonly Regex ProseEnumeration = new(
        @"\b(?:contains?|includes?|comprises?|consists? of|records?|shows?|lists?|carries)\b\s+(?<items>[^.]+)\.",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Leading articles and determiners: "the date of sale" is the DateOfSale property.</summary>
    private static readonly Regex LeadingDeterminer = new(
        @"^\s*(?:a|an|the|its|their|each|every|any)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Anything parenthetical or after a dash is a gloss, not part of the name.</summary>
    private static readonly Regex Gloss = new(@"\s*[\(–—\-—:,;].*$", RegexOptions.Compiled);

    /// <summary>
    /// The properties <paramref name="articleBody"/> states that <paramref name="entityName"/> has.
    /// Empty when the article says nothing usable — which is the common case, and is fine.
    /// Never throws.
    /// </summary>
    public static IReadOnlyList<FieldShape> Mine(string articleBody, string entityName)
    {
        if (string.IsNullOrWhiteSpace(articleBody))
            return Array.Empty<FieldShape>();

        try
        {
            var phrases = new List<string>();
            phrases.AddRange(FromIntroducedLists(articleBody));
            phrases.AddRange(FromProseEnumerations(articleBody));

            var fields = new List<FieldShape>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var phrase in phrases)
            {
                if (fields.Count >= MaxMinedFields)
                    break;

                if (ToFieldShape(phrase, entityName) is not { } shape)
                    continue;

                if (!taken.Add(shape.Name))
                    continue;

                fields.Add(shape);
            }

            return fields;
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        {
            // The no-op contract. A surprise here leaves the entity as EntityShaper shaped it.
            return Array.Empty<FieldShape>();
        }
    }

    /// <summary>
    /// Every item of every bulleted list whose preceding non-list line ends in a containment
    /// phrase. The list markers this reads are the ones WikitextStripper preserved on purpose.
    /// </summary>
    private static IEnumerable<string> FromIntroducedLists(string body)
    {
        var lines = body.Split('\n');
        var introduced = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("* ", StringComparison.Ordinal))
            {
                if (introduced)
                    yield return line[2..];
                continue;
            }

            // A non-list line either introduces the list that follows it, or ends the one above.
            introduced = ContainmentLeadIn.IsMatch(line);
        }
    }

    /// <summary>
    /// Items enumerated in prose after a containment verb: "A receipt includes the date of sale,
    /// the amount paid and the payment method."
    /// </summary>
    private static IEnumerable<string> FromProseEnumerations(string body)
    {
        foreach (Match match in ProseEnumeration.Matches(body))
        {
            var items = match.Groups["items"].Value;

            foreach (var item in Regex.Split(items, @",|\band\b|\bor\b"))
            {
                if (!string.IsNullOrWhiteSpace(item))
                    yield return item;
            }
        }
    }

    /// <summary>
    /// One phrase as a field, or null when it cannot be one: too long, a stop word, not a valid
    /// identifier, named "Id" (which DomainModelMapper generates), or named after its own class
    /// (CS0542 — <c>class Invoice { string Invoice; }</c> does not compile).
    /// </summary>
    private static FieldShape? ToFieldShape(string phrase, string entityName)
    {
        var cleaned = Gloss.Replace(LeadingDeterminer.Replace(phrase.Trim(), string.Empty), string.Empty).Trim();

        if (cleaned.Length == 0)
            return null;

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > MaxPhraseWords)
            return null;

        var name = NameNormalizer.ToFieldName(cleaned);

        if (!NameNormalizer.IsValidIdentifier(name))
            return null;

        if (StopWords.Contains(name))
            return null;

        if (string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(name, entityName, StringComparison.OrdinalIgnoreCase))
            return null;

        return new FieldShape(name, TypeInference.Infer(name));
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests --filter PropertyMinerTests`
Expected: PASS — 11 passed.

If `Infers_a_type_for_each_mined_property` fails, read `DomainDiscovery/Enrichment/TypeInference.cs` and confirm what `Infer` returns for a name ending in `Amount` and one starting with `Date`. Fix the **test's expectation** to match the existing inference rules — do not change `TypeInference`; other code depends on it.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/PropertyMiner.cs DomainDiscovery.Tests/Enrichment/PropertyMinerTests.cs
git commit -m "Mine a class's properties from the article it was discovered from"
```

---

## Task 9: Wire the miner and the template into EntityShaper

**Files:**
- Modify: `DomainDiscovery/Enrichment/EntityShaper.cs`
- Test: `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs` (add tests)

**Interfaces:**
- Consumes: `RoleTemplate.For` (Task 7), `PropertyMiner.Mine` (Task 8), `IWikipediaClient.FetchExtract` (existing).
- Produces: no new signature. `EntityShaper.Shape(CandidateDomainModel)` keeps its shape and gains a third pass.

- [ ] **Step 1: Write the failing tests**

Add to `DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs`:

```csharp
[Fact]
public void Adds_the_role_template_fields_to_a_curated_entity()
{
    var client = new FakeWikipediaClient();
    var model = new CandidateDomainModel();
    model.Entities.Add(new CandidateEntity { Name = "Invoice", Provenance = Provenance.Wiki });

    new EntityShaper(client).Shape(model);

    var invoice = model.Entities.Single(e => e.Name == "Invoice");

    // Curated (Invoice is a Document in DomainFieldLexicon) ...
    Assert.Contains(invoice.Fields, f => f.Name == "Number");
    // ... plus the Document role template, which curation alone never gave it.
    Assert.Contains(invoice.Fields, f => f.Name == "Status");
}

[Fact]
public void Mines_properties_from_the_entitys_own_article()
{
    var client = new FakeWikipediaClient();
    client.Extracts["Invoice"] = """
        An invoice is a commercial document.
        An invoice typically contains:
        * shipping terms
        * delivery date
        """;

    var model = new CandidateDomainModel();
    model.Entities.Add(new CandidateEntity { Name = "Invoice", Provenance = Provenance.Wiki });

    new EntityShaper(client).Shape(model);

    var names = model.Entities.Single(e => e.Name == "Invoice").Fields.Select(f => f.Name).ToList();

    Assert.Contains("ShippingTerms", names);
    Assert.Contains("DeliveryDate", names);
}

[Fact]
public void An_entity_whose_article_says_nothing_keeps_the_shape_it_already_had()
{
    var client = new FakeWikipediaClient();
    client.Extracts["Invoice"] = "An invoice is a commercial document. Nothing more.";

    var model = new CandidateDomainModel();
    model.Entities.Add(new CandidateEntity { Name = "Invoice", Provenance = Provenance.Wiki });

    new EntityShaper(client).Shape(model);

    // The no-op contract: still curated + templated, just not mined.
    Assert.Contains(model.Entities.Single(e => e.Name == "Invoice").Fields, f => f.Name == "Number");
}

[Fact]
public void A_rejected_entity_is_never_mined()
{
    var client = new FakeWikipediaClient();
    client.Leads["System"] = "A system is a group of interacting elements.";

    var model = new CandidateDomainModel();
    model.Entities.Add(new CandidateEntity { Name = "System", Provenance = Provenance.Wiki });

    new EntityShaper(client).Shape(model);

    Assert.Contains("System", model.Rejected);
    Assert.Empty(model.Entities);
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test DomainDiscovery.Tests --filter EntityShaperTests`
Expected: FAIL — `Adds_the_role_template_fields_to_a_curated_entity` and `Mines_properties_from_the_entitys_own_article` fail; `Status`/`ShippingTerms` are absent.

- [ ] **Step 3: Implement**

In `DomainDiscovery/Enrichment/EntityShaper.cs`, replace the body of `Shape` (lines 55-114) with:

```csharp
    public void Shape(CandidateDomainModel model)
    {
        var entities = model.Entities.ToList();

        // Pass 1: the curated lexicon. This must run for every entity before any lead is
        // fetched, since an entity it fully covers needs no lead at all.
        foreach (var entity in entities)
        {
            var curatedFields = DomainFieldLexicon.Fields(entity.Name);

            // Role precedence mirrors field precedence exactly: the curated lexicon wins over
            // the archetype (assigned below, in pass 2) over EntityRole.None (the default).
            if (curatedFields.Count > 0)
                entity.Role = DomainFieldLexicon.Role(entity.Name);

            foreach (var shape in curatedFields)
                AddField(entity, shape.Name, shape.Type);
        }

        // Prefetch every remaining entity's lead in ONE call instead of one per entity. Against
        // the dump this is a loop over local reads; it stays batched because the HTTP client is
        // still the fallback, and one at a time is what drove it to 429.
        var titlesNeedingLeads = entities
            .Where(e => e.Fields.Count == 0)
            .Select(e => e.SourceTitle ?? e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var leads = _client.FetchLeads(titlesNeedingLeads);

        // Pass 2: resolve the three-way verdict for anything still fieldless.
        foreach (var entity in entities)
        {
            if (entity.Fields.Count > 0)
                continue;

            var title = entity.SourceTitle ?? entity.Name;
            var lead = leads.TryGetValue(title, out var fetchedLead) ? fetchedLead : string.Empty;
            var result = ArchetypeLexicon.Resolve(HypernymExtractor.Extract(lead));

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

        // Pass 3: widen every surviving class. The role template fills in the baseline its kind
        // implies, and PropertyMiner reads the class's own article for what makes it specific.
        //
        // This pass runs LAST, over model.Entities rather than the local list, so a class rejected
        // or demoted to a suggestion above is never mined: a class we decided is not a class must
        // not acquire properties on its way out.
        Widen(model);
    }

    /// <summary>
    /// Adds the role baseline and the mined properties to every surviving class.
    /// </summary>
    /// <remarks>
    /// Reading each class's own article is the thing the MediaWiki API's rate limit forbade — it is
    /// one fetch per class, and a real domain has twenty. Against the dump store it is a local
    /// seek, which is what makes this pass affordable at all.
    /// <para>
    /// Both sources are additive and neither can overwrite a field that is already there: precedence
    /// runs curated, then archetype, then template, then mined, and AddField silently declines a
    /// name already taken.
    /// </para>
    /// </remarks>
    private void Widen(CandidateDomainModel model)
    {
        foreach (var entity in model.Entities)
        {
            foreach (var shape in RoleTemplate.For(entity.Role))
                AddField(entity, shape.Name, shape.Type);

            var article = _client.FetchExtract(entity.SourceTitle ?? entity.Name);
            if (string.IsNullOrWhiteSpace(article))
                continue;

            foreach (var shape in PropertyMiner.Mine(article, entity.Name))
                AddField(entity, shape.Name, shape.Type);
        }
    }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DomainDiscovery.Tests`
Expected: PASS — the whole suite. If an existing `EntityShaperTests` case now fails because a class has *more* fields than it used to, that is this feature working; update the assertion to check for the field it cares about rather than an exact count.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/EntityShaper.cs DomainDiscovery.Tests/Enrichment/EntityShaperTests.cs
git commit -m "Widen every surviving class from its role and its own article"
```

---

## Task 10: Attributes and usings in the generated code

**Files:**
- Modify: `CodeGenerationEngine/Models/PropertyDefinition.cs`
- Modify: `CodeGenerationEngine/Models/ClassDefinition.cs`
- Modify: `CodeGenerationEngine/Core/RoslynClassGenerator.cs:19-43,75-133`
- Test: `CodeGenerator.Tests/RoslynClassGeneratorTests.cs` (create if absent)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `PropertyDefinition.Attributes` (`List<string>`, e.g. `["JsonIgnore"]`); `ClassDefinition.Usings` (`List<string>`, e.g. `["System.Text.Json.Serialization"]`); `RoslynClassGenerator.Generate` emits both.

> **Note:** `CodeGenerator.Tests` uses **MSTest** (`[TestClass]`/`[TestMethod]`/`Assert.IsTrue`), unlike `DomainDiscovery.Tests`. Match whichever framework the file you are adding to already uses.

- [ ] **Step 1: Write the failing tests**

Add to `CodeGenerator.Tests/RoslynClassGeneratorTests.cs` (create the file, matching the existing MSTest style of that project):

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests;

[TestClass]
public class RoslynClassGeneratorTests
{
    [TestMethod]
    public void Emits_the_usings_the_class_declares()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Usings.Add("System.Text.Json.Serialization");

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "using System.Text.Json.Serialization;");
    }

    [TestMethod]
    public void Emits_an_attribute_a_property_declares()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition
        {
            Name = "Customer",
            Type = "Customer",
            Attributes = { "JsonIgnore" }
        });

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "[JsonIgnore]");
        StringAssert.Contains(source, "public Customer Customer");
    }

    [TestMethod]
    public void Emits_a_generic_collection_property()
    {
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition
        {
            Name = "Orders",
            Type = "List<Order>"
        });

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        StringAssert.Contains(source, "public List<Order> Orders");
    }

    [TestMethod]
    public void A_class_with_no_usings_emits_none()
    {
        var definition = new ClassDefinition { Name = "Order" };

        var source = RoslynClassGenerator.Generate(definition, "App.Models");

        Assert.IsFalse(source.Contains("using "));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test CodeGenerator.Tests --filter RoslynClassGeneratorTests`
Expected: FAIL — `Usings` and `Attributes` do not exist (CS0117 / CS1061).

- [ ] **Step 3: Implement**

In `CodeGenerationEngine/Models/PropertyDefinition.cs`, add to the class:

```csharp
    /// <summary>
    /// Attribute names to emit on this property, without brackets (e.g. <c>JsonIgnore</c>). The
    /// owning <see cref="ClassDefinition"/> must declare the matching using, or the generated file
    /// will not compile.
    /// </summary>
    public List<string> Attributes { get; set; } = new List<string>();
```

In `CodeGenerationEngine/Models/ClassDefinition.cs`, add to the class:

```csharp
    /// <summary>
    /// Namespaces to emit as using directives. Needed because the generator emits none by
    /// default, so an attribute like <c>[JsonIgnore]</c> would otherwise not resolve.
    /// </summary>
    public List<string> Usings { get; set; } = [];
```

In `CodeGenerationEngine/Core/RoslynClassGenerator.cs`, replace `Generate` (lines 19-43):

```csharp
    public static string Generate(ClassDefinition model, string namespaceName = "GeneratedCode")
    {
        var members = new List<MemberDeclarationSyntax>();

        // Properties
        members.AddRange(model.Properties.Select(CreateProperty));

        // Constructor
        members.Add(CreateConstructor(model));

        // Methods
        members.AddRange(model.Methods.Select(m => CreateMethod(model, m)));

        var classDeclaration = ClassDeclaration(model.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddMembers([.. members]);

        var namespaceDeclaration = NamespaceDeclaration(
                ParseName(namespaceName)).AddMembers(classDeclaration);

        var compilationUnit = CompilationUnit()
                .AddUsings([.. model.Usings.Select(u => UsingDirective(ParseName(u)))])
                .AddMembers(namespaceDeclaration)
                .NormalizeWhitespace();

        return compilationUnit.ToFullString();
    }
```

In the same file, in `CreateProperty`, replace the `if (attributes.Any())` block (lines 126-131) with:

```csharp
        // Attributes named directly on the property, alongside the rule-derived ones above.
        attributes.AddRange(property.Attributes.Select(a => Attribute(IdentifierName(a))));

        if (attributes.Any())
        {
            propertyDeclaration = propertyDeclaration.AddAttributeLists(
                    AttributeList(SeparatedList(attributes)));
        }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test CodeGenerator.Tests --filter RoslynClassGeneratorTests`
Expected: PASS — 4 passed.

- [ ] **Step 5: Run the full suite, to be sure no existing generated output changed**

Run: `dotnet test`
Expected: PASS — everything.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/ CodeGenerator.Tests/RoslynClassGeneratorTests.cs
git commit -m "Let a generated class declare its usings and a property its attributes"
```

---

## Task 11: Navigation properties and audit fields in DomainModelMapper

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs:37-65`
- Test: `CodeGenerator.Tests/DomainModelMapperTests.cs` (add tests; create the file if absent, MSTest style)

**Interfaces:**
- Consumes: `PropertyDefinition.Attributes`, `ClassDefinition.Usings` (Task 10); `CandidateEntity.Relationships` (existing, populated by `RelationshipMiner`).
- Produces: no new signature. `DomainModelMapper.ToClassDefinitions(CandidateDomainModel)` keeps its shape.

- [ ] **Step 1: Write the failing tests**

Add to `CodeGenerator.Tests/DomainModelMapperTests.cs`:

```csharp
using AppScaffolding;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests;

[TestClass]
public class DomainModelMapperNavigationTests
{
    private static CandidateDomainModel OrderReferencesCustomer()
    {
        var model = new CandidateDomainModel();

        var customer = new CandidateEntity { Name = "Customer" };
        customer.Fields.Add(new CandidateField { Name = "FullName", Type = "string" });

        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "CustomerId", Type = "int" });
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });

        model.Entities.Add(customer);
        model.Entities.Add(order);
        return model;
    }

    [TestMethod]
    public void Gives_the_referencing_class_a_navigation_property()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        var nav = order.Properties.Single(p => p.Name == "Customer");

        Assert.AreEqual("Customer", nav.Type);
        CollectionAssert.Contains(nav.Attributes, "JsonIgnore");
    }

    [TestMethod]
    public void Gives_the_referenced_class_the_inverse_collection()
    {
        var customer = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Customer");

        var inverse = customer.Properties.Single(p => p.Name == "Orders");

        Assert.AreEqual("List<Order>", inverse.Type);
        CollectionAssert.Contains(inverse.Attributes, "JsonIgnore");
    }

    // JsonStore persists with System.Text.Json, and Order.Customer -> Customer.Orders -> Order is
    // a reference cycle that throws on save. [JsonIgnore] is what keeps persistence FK-only, and
    // it needs this using or the generated file does not compile.
    [TestMethod]
    public void Declares_the_using_that_JsonIgnore_needs()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        CollectionAssert.Contains(order.Usings, "System.Text.Json.Serialization");
    }

    [TestMethod]
    public void Keeps_the_foreign_key_alongside_the_navigation_property()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        Assert.IsTrue(order.Properties.Any(p => p.Name == "CustomerId" && p.Type == "int"));
    }

    [TestMethod]
    public void Every_class_gets_audit_fields()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        Assert.IsTrue(order.Properties.Any(p => p.Name == "CreatedOn" && p.Type == "DateTime"));
        Assert.IsTrue(order.Properties.Any(p => p.Name == "ModifiedOn" && p.Type == "DateTime"));
    }

    [TestMethod]
    public void A_relationship_to_a_class_that_did_not_survive_is_ignored()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Relationships.Add(new CandidateRelationship { Target = "Ghost", Role = "references" });
        model.Entities.Add(order);

        var definition = DomainModelMapper.ToClassDefinitions(model).Single();

        // A nav property to a class that is not generated would not compile.
        Assert.IsFalse(definition.Properties.Any(p => p.Name == "Ghost"));
    }

    [TestMethod]
    public void A_class_with_no_relationships_declares_no_usings()
    {
        var model = new CandidateDomainModel();
        var plain = new CandidateEntity { Name = "Note" };
        plain.Fields.Add(new CandidateField { Name = "Text", Type = "string" });
        model.Entities.Add(plain);

        Assert.AreEqual(0, DomainModelMapper.ToClassDefinitions(model).Single().Usings.Count);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test CodeGenerator.Tests --filter DomainModelMapperNavigationTests`
Expected: FAIL — no `Customer` navigation property is produced.

- [ ] **Step 3: Implement**

In `AppScaffolding/DomainModelMapper.cs`, replace `ToClassDefinitions` (lines 37-65) with:

```csharp
    /// <summary>The namespace [JsonIgnore] lives in. Declared only by a class that uses it.</summary>
    private const string JsonSerializationNamespace = "System.Text.Json.Serialization";

    /// <summary>
    /// Kept off the JSON file, not off the class. A navigation property is resolved from the
    /// foreign key at load time (see RelationshipResolverTemplate), and serializing it as well
    /// would make Order.Customer -> Customer.Orders -> Order a reference cycle, which
    /// System.Text.Json throws on rather than writes.
    /// </summary>
    private const string JsonIgnoreAttribute = "JsonIgnore";

    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(CandidateDomainModel model)
    {
        var classes = new List<ClassDefinition>();

        // Class names double as file names, and Windows file names are case-insensitive.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Entity name -> generated class name. A relationship names its target by ENTITY name, but
        // a nav property must be typed by the CLASS name, and ToUniqueClassName can rename one
        // ("Type" -> "TypeItem"). Without this map a renamed target would emit an uncompilable
        // reference to a type that does not exist.
        var classNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            var className = ToUniqueClassName(entity.Name, usedNames);
            classNames[entity.Name] = className;

            var definition = new ClassDefinition { Name = className };

            bool hasId = entity.Fields.Any(
                f => string.Equals(f.Name, "Id", StringComparison.OrdinalIgnoreCase));

            // Guarantee an int Id key for CRUD. If the entity already declares an
            // Id field, keep it in place; otherwise prepend one. A field-less
            // entity therefore ends up with a single int Id.
            if (!hasId)
                definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

            foreach (var field in entity.Fields)
                definition.Properties.Add(
                    new PropertyDefinition { Name = field.Name, Type = field.Type });

            classes.Add(definition);
        }

        AddNavigationProperties(model, classes, classNames);
        AddAuditFields(classes);

        return classes;
    }

    /// <summary>
    /// Turns each mined relationship into a pair of navigation properties: the forward reference on
    /// the class that carries the foreign key, and the inverse collection on the class it points at.
    /// </summary>
    /// <remarks>
    /// RelationshipMiner has populated <see cref="CandidateEntity.Relationships"/> since it was
    /// written, and nothing ever read it — only the <c>&lt;Target&gt;Id</c> field it also emits
    /// reached the generated class. This is where the rest of that work finally lands.
    /// <para>
    /// A relationship whose target did not survive shaping is skipped: a property typed as a class
    /// that is never generated does not compile.
    /// </para>
    /// </remarks>
    private static void AddNavigationProperties(
        CandidateDomainModel model,
        List<ClassDefinition> classes,
        Dictionary<string, string> classNames)
    {
        var byName = classes.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            if (!classNames.TryGetValue(entity.Name, out var ownerName)
                || !byName.TryGetValue(ownerName, out var owner))
                continue;

            foreach (var relationship in entity.Relationships)
            {
                if (!classNames.TryGetValue(relationship.Target, out var targetName)
                    || !byName.TryGetValue(targetName, out var target))
                    continue;

                AddNavigation(owner, targetName, targetName);
                AddNavigation(target, Pluralize(ownerName), $"List<{ownerName}>");
            }
        }
    }

    /// <summary>
    /// Adds one [JsonIgnore] navigation property, unless its name is already taken or would
    /// collide with the class's own name (CS0542).
    /// </summary>
    private static void AddNavigation(ClassDefinition definition, string name, string type)
    {
        if (string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase))
            return;

        if (definition.Properties.Any(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        definition.Properties.Add(new PropertyDefinition
        {
            Name = name,
            Type = type,
            Attributes = { JsonIgnoreAttribute }
        });

        if (!definition.Usings.Contains(JsonSerializationNamespace))
            definition.Usings.Add(JsonSerializationNamespace);
    }

    /// <summary>
    /// What a running app needs and the domain never said: when the record was created and last
    /// touched. Added here rather than in discovery, so the boundary between "what Wikipedia says"
    /// and "what a CRUD app needs" stays where it is.
    /// </summary>
    private static void AddAuditFields(List<ClassDefinition> classes)
    {
        foreach (var definition in classes)
        {
            foreach (var name in new[] { "CreatedOn", "ModifiedOn" })
            {
                if (string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (definition.Properties.Any(
                        p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                definition.Properties.Add(new PropertyDefinition { Name = name, Type = "DateTime" });
            }
        }
    }

    /// <summary>
    /// English plural, well enough for a collection property name. Not a linguistics engine: the
    /// worst case is an inverse collection called "Companys", which compiles and reads oddly, and
    /// the user can rename it in the review loop.
    /// </summary>
    private static string Pluralize(string name)
    {
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            return name + "es";

        if (name.Length > 1
            && name.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && !"aeiou".Contains(char.ToLowerInvariant(name[^2])))
            return name[..^1] + "ies";

        return name + "s";
    }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test CodeGenerator.Tests --filter DomainModelMapperNavigationTests`
Expected: PASS — 7 passed.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/DomainModelMapperTests.cs
git commit -m "Spend the relationships the miner has always produced"
```

---

## Task 12: Make navigation properties work in the generated app

Without this task the previous one **breaks the generated app**: `AutoGenerateColumns="True"` renders a `Customer` column of type names, and the nav props are never populated.

**Files:**
- Create: `AppScaffolding/RelationshipResolverTemplate.cs`
- Modify: `AppScaffolding/MainWindowTemplate.cs:67-71`
- Modify: `AppScaffolding/MainViewModelTemplate.cs`
- Modify: `AppScaffolding/AppScaffolder.cs`
- Test: `CodeGenerator.Tests/RelationshipResolverTemplateTests.cs`

**Interfaces:**
- Consumes: the `[JsonIgnore]` nav props from Task 11.
- Produces: `RelationshipResolverTemplate.Source(string appName) → string` — the source of `Data/RelationshipResolver.cs`, exposing `RelationshipResolver.Resolve(IEnumerable<IEntityViewModel> tabs)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/RelationshipResolverTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests;

[TestClass]
public class RelationshipResolverTemplateTests
{
    [TestMethod]
    public void Emits_a_resolver_in_the_apps_data_namespace()
    {
        var source = RelationshipResolverTemplate.Source("MyApp");

        StringAssert.Contains(source, "namespace MyApp.Data;");
        StringAssert.Contains(source, "public static class RelationshipResolver");
        StringAssert.Contains(source, "public static void Resolve(");
    }

    [TestMethod]
    public void The_resolver_is_reflection_driven_so_it_needs_no_per_entity_code()
    {
        var source = RelationshipResolverTemplate.Source("MyApp");

        StringAssert.Contains(source, "GetProperties()");
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test CodeGenerator.Tests --filter RelationshipResolverTemplateTests`
Expected: FAIL — `RelationshipResolverTemplate` does not exist (CS0246).

- [ ] **Step 3: Implement the template**

Create `AppScaffolding/RelationshipResolverTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// Emits the generated app's FK -> navigation-property resolver.
/// </summary>
/// <remarks>
/// The nav properties DomainModelMapper emits are [JsonIgnore], so they are never persisted — only
/// the foreign keys are, which is what keeps Order.Customer -> Customer.Orders -> Order from being
/// a cycle System.Text.Json throws on. That leaves the nav properties null at load, and this is
/// what fills them in.
/// <para>
/// Reflection-driven on purpose: one file, no per-entity code, and it works for any model the
/// discovery pipeline produces.
/// </para>
/// </remarks>
public static class RelationshipResolverTemplate
{
    /// <summary>Source of <c>Data/RelationshipResolver.cs</c> for <paramref name="appName"/>.</summary>
    public static string Source(string appName) => $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{appName}}.ViewModels;

namespace {{appName}}.Data;

/// <summary>
/// Fills in the navigation properties from the foreign keys, once every store has loaded.
/// </summary>
/// <remarks>
/// The JSON files hold only foreign keys (the nav properties are [JsonIgnore], or saving would
/// hit a reference cycle). So after load, Order.CustomerId is set and Order.Customer is null.
/// This walks every loaded record and joins the two back up.
/// </remarks>
public static class RelationshipResolver
{
    public static void Resolve(IEnumerable<IEntityViewModel> tabs)
    {
        var records = tabs.SelectMany(tab => tab.Items.Cast<object>()).ToList();

        // Type -> Id -> record. Every lookup below is a hit in this map.
        var byTypeAndId = new Dictionary<Type, Dictionary<int, object>>();

        foreach (var record in records)
        {
            var id = IdOf(record);
            if (id is null)
                continue;

            if (!byTypeAndId.TryGetValue(record.GetType(), out var byId))
                byTypeAndId[record.GetType()] = byId = new Dictionary<int, object>();

            byId[id.Value] = record;
        }

        foreach (var record in records)
        {
            ResolveReferences(record, byTypeAndId);
            ResolveCollections(record, byTypeAndId);
        }
    }

    /// <summary>Sets Order.Customer from Order.CustomerId.</summary>
    private static void ResolveReferences(
        object record, Dictionary<Type, Dictionary<int, object>> byTypeAndId)
    {
        foreach (var property in record.GetType().GetProperties())
        {
            if (!byTypeAndId.TryGetValue(property.PropertyType, out var byId))
                continue;

            var key = record.GetType().GetProperty(property.Name + "Id");
            if (key is null || key.PropertyType != typeof(int))
                continue;

            if (key.GetValue(record) is int id && byId.TryGetValue(id, out var target))
                property.SetValue(record, target);
        }
    }

    /// <summary>Sets Customer.Orders to every Order whose CustomerId is this customer's Id.</summary>
    private static void ResolveCollections(
        object owner, Dictionary<Type, Dictionary<int, object>> byTypeAndId)
    {
        var ownerId = IdOf(owner);
        if (ownerId is null)
            return;

        foreach (var property in owner.GetType().GetProperties())
        {
            if (!property.PropertyType.IsGenericType
                || property.PropertyType.GetGenericTypeDefinition() != typeof(List<>))
                continue;

            var itemType = property.PropertyType.GetGenericArguments()[0];
            if (!byTypeAndId.TryGetValue(itemType, out var candidates))
                continue;

            var foreignKey = itemType.GetProperty(owner.GetType().Name + "Id");
            if (foreignKey is null || foreignKey.PropertyType != typeof(int))
                continue;

            var list = (IList)Activator.CreateInstance(property.PropertyType)!;

            foreach (var candidate in candidates.Values)
            {
                if (foreignKey.GetValue(candidate) is int id && id == ownerId.Value)
                    list.Add(candidate);
            }

            property.SetValue(owner, list);
        }
    }

    private static int? IdOf(object record)
        => record.GetType().GetProperty("Id",
               BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
               ?.GetValue(record) as int?;
}
""";
}
```

- [ ] **Step 4: Hide navigation properties from the grid**

In `AppScaffolding/MainWindowTemplate.cs`, replace `OnAutoGeneratingColumn` (lines 67-71) with:

```csharp
    // Column presentation is a view concern, so the Id lock lives here rather
    // than in a view model. This is the only code-behind in the generated app.
    private void OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (string.Equals(e.PropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            e.Column.IsReadOnly = true;

        // A navigation property has no sensible column: a DataGrid would render Customer as its
        // type name and List<Payment> as "System.Collections.Generic.List`1[Payment]". The
        // foreign key next to it is the editable, meaningful one.
        if (!IsDisplayable(e.PropertyType))
            e.Cancel = true;
    }

    private static bool IsDisplayable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(Guid);
    }
```

Confirm the file's generated `using` block includes `System` (for `Type`). If it does not, add it.

- [ ] **Step 5: Call the resolver once every store has loaded**

In `AppScaffolding/MainViewModelTemplate.cs`, replace `Build` with:

```csharp
    public static string Build(string appName, IReadOnlyList<string> entityNames)
    {
        var registrations = string.Join("\n",
            entityNames.Select(name =>
                $"            new EntityViewModel<{name}>(\"{name}\"),"));

        return $$"""
using System.Collections.ObjectModel;
using {{appName}}.Data;
using {{appName}}.Models;

namespace {{appName}}.ViewModels;

/// <summary>One tab per discovered entity.</summary>
public sealed class MainViewModel
{
    public MainViewModel()
    {
        Entities = new ObservableCollection<IEntityViewModel>
        {
{{registrations}}
        };

        // Each EntityViewModel loaded its own store in its constructor, so the foreign keys are
        // in but the navigation properties are still null -- they are [JsonIgnore] and never
        // persisted. This is the earliest point at which every store is loaded, and so the
        // earliest point at which they can be joined back up.
        RelationshipResolver.Resolve(Entities);
    }

    public ObservableCollection<IEntityViewModel> Entities { get; }
}
""";
    }
```

- [ ] **Step 6: Emit the new file from the scaffolder**

Read `AppScaffolding/AppScaffolder.cs` and find where `JsonStoreTemplate.Source(...)` is written to `Data/JsonStore.cs`. Immediately after it, write the resolver to the same folder:

```csharp
        File.WriteAllText(
            Path.Combine(dataDirectory, "RelationshipResolver.cs"),
            RelationshipResolverTemplate.Source(appName));
```

Use whatever local variable names that method already has for the data directory and the app name — do not introduce new ones.

- [ ] **Step 7: Run the tests and confirm they pass**

Run: `dotnet test`
Expected: PASS — the whole suite.

- [ ] **Step 8: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/RelationshipResolverTemplateTests.cs
git commit -m "Make the generated app's navigation properties real"
```

---

## Task 13: End-to-end verification

Tests passing is not the same as the feature working. This task drives the real thing.

**Files:** none — this is verification.

- [ ] **Step 1: Confirm the import finished and the store is real**

```bash
ls -la D:/SmartCodeGenerator-Repository/Dump/enwiki.db
```

Expected: a file of roughly 10-15 GB. If Task 6's import has not finished, wait for it.

- [ ] **Step 2: Rebuild the CLI**

`dotnet test` leaves the CLI's `bin` stale, so a fix that works can look dead. Rebuild before driving it:

```bash
cd D:/SmartCodeGenerator-Repository
dotnet build CodeGenerator/CodeGenerator.csproj -c Debug
```

Expected: build succeeded.

- [ ] **Step 3: Drive `build app:` against the dump**

The CLI reads piped stdin, so it can be driven non-interactively:

```bash
cd D:/SmartCodeGenerator-Repository
printf 'build app: point of sale system\ny\nexit\n' | dotnet run --project CodeGenerator --no-build
```

Expected, and check each one:
- Discovery completes in **seconds**, not the tens of seconds the throttled API took.
- The discovered classes each show **more than the three or four fields** they used to.
- At least one class shows a mined, domain-specific property that no template could have produced.
- Relationships are still reported.

- [ ] **Step 4: Build the generated app**

```bash
cd D:/SmartCodeGenerator-Repository
dotnet build <path printed by the previous step>
```

Expected: **build succeeded**. A failure here almost certainly means a generated class references a type that was not generated, or `[JsonIgnore]` is missing its using — both are Task 10/11 bugs.

- [ ] **Step 5: Run the generated app and save a record**

Launch it, add a row on two related tabs (e.g. Customer and Order), set the Order's `CustomerId` to the Customer's `Id`, and press Save on both.

Expected:
- The grid shows the **foreign key column** and **no** `Customer` or `Payments` column.
- Save succeeds — **no `JsonException` about a possible object cycle**. That exception is the exact failure `[JsonIgnore]` exists to prevent, so seeing it means Task 11 regressed.
- Restart the app: the records reload, and the nav props are resolved (confirm by breakpoint or by adding a temporary debug column if needed).

- [ ] **Step 6: Report what actually happened**

Report the real import numbers (articles, redirects, elapsed, store size), the real discovery time, and the real field counts per class — before and after. If any of it disappointed, say so plainly rather than reporting the plan back.

---

## Self-Review Notes

**Spec coverage:** DumpImporter → Tasks 4, 5. Store → Task 2. DumpWikipediaClient → Task 3. Wikitext stripping and list markers → Task 1. Wiring/CreateDefault → Task 6. Gitignore → already committed with the spec (`04b82ed`). RoleTemplate → Task 7. PropertyMiner → Task 8. Shaper integration → Task 9. CodeGenerationEngine attributes/usings → Task 10. Audit fields and nav props → Task 11. Grid guard, JsonIgnore cycle, RelationshipResolver → Task 12. Testing section → Tasks 1-12 plus Task 13.

**Known deviations from the spec, decided while planning:**
- The spec's store schema said `pages(title, lead, body, links)`. The implementation packs `body` and `links` into one Deflate blob (`payload`), because two blobs cost two reads and the text compresses about 4:1.
- The spec did not mention resume state; `import_state` was added, because a 2-hour import that cannot resume is not one you can afford to interrupt.
