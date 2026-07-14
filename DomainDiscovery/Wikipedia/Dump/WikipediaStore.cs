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

    /// <summary>
    /// The tables, with NO index on title or source — those are built by
    /// <see cref="CreateSearchIndexes"/> once the import has finished.
    /// </summary>
    /// <remarks>
    /// <b>The missing index is the design, not an omission.</b> The first version made title the
    /// PRIMARY KEY, which is a unique index maintained on every insert. Titles arrive in effectively
    /// random order, so each insert landed at a random point in a B-tree that soon outgrew the page
    /// cache: measured over a two-hour run, throughput fell from ~790 pages/s to ~180 as the store
    /// passed 4 GB, and the full import was heading for 15-25 hours.
    /// <para>
    /// So the import writes into a plain rowid table — appends, which stay cheap however large it
    /// gets — and the index is built once at the end, where SQLite can sort in bulk instead of
    /// paying a random write per row.
    /// </para>
    /// <para>
    /// NOCASE lives on the COLUMN, so it is inherited by the index built later and by every
    /// <c>WHERE title = ...</c>, which keeps lookups case-insensitive exactly as before. The index
    /// is deliberately NOT unique: Wikipedia titles are case-sensitive after the first character
    /// ("AIDS" and "Aids" are different articles), so a unique NOCASE index would fail to build.
    /// </para>
    /// </remarks>
    public void CreateSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS pages (
                title   TEXT NOT NULL COLLATE NOCASE,
                lead    TEXT NOT NULL,
                payload BLOB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS redirects (
                source  TEXT NOT NULL COLLATE NOCASE,
                target  TEXT NOT NULL COLLATE NOCASE
            );

            CREATE TABLE IF NOT EXISTS import_state (
                key     TEXT PRIMARY KEY,
                value   INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Builds the title and source indexes. Run once, after the import — every read is a scan
    /// until it has been.
    /// </summary>
    /// <remarks>
    /// <b>The PRAGMAs are what make this finish.</b> Building an index means sorting every title,
    /// and on the default 2 MB page cache SQLite spills that sort to a temp file and merges it in
    /// multiple passes — random I/O. Measured on the real 18 GB store: CPU sat at 2% while the disk
    /// managed ~1.2 MB/s, and after 48 minutes it had read perhaps a fifth of the table.
    /// <para>
    /// The sort itself is small — 7.2M titles is a few hundred MB — so it belongs in memory. A
    /// 1 GB cache and an in-memory temp store keep it there, and the build becomes a sequential
    /// scan plus an in-RAM sort rather than a disk-thrashing merge.
    /// </para>
    /// </remarks>
    public void CreateSearchIndexes()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        // Negative cache_size is KiB rather than pages: -1000000 is ~1 GB.
        //
        // LENGTH(payload) is in the index ON PURPOSE, and it is not decoration. PrefixSearch ranks
        // by article size, and without the size in the index SQLite has to fetch the ROW of every
        // title matching the prefix just to measure it — thousands of random reads into an 18 GB
        // table. Measured on the full store: PrefixSearch("Point") took 25 SECONDS, and the topic
        // recognizer issues up to six of those per discovery. With the size carried in the index the
        // query is answered from the index alone and never touches the table.
        command.CommandText = """
            PRAGMA cache_size = -1000000;
            PRAGMA temp_store = MEMORY;

            CREATE INDEX IF NOT EXISTS ix_pages_title ON pages (title, LENGTH(payload));
            CREATE INDEX IF NOT EXISTS ix_redirects_source ON redirects (source);
            """;
        command.CommandTimeout = 0;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Drops the indexes so a resumed import writes at full speed again. Building them is cheap
    /// relative to maintaining them across millions of random inserts.
    /// </summary>
    public void DropSearchIndexes()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            DROP INDEX IF EXISTS ix_pages_title;
            DROP INDEX IF EXISTS ix_redirects_source;
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Whether the store is indexed, and so whether reads are seeks rather than scans.</summary>
    public bool HasSearchIndexes()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_pages_title'";

        return command.ExecuteScalar() is long count && count > 0;
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

    /// <summary>
    /// Writes a batch in one transaction — a plain append into an unindexed table, which is what
    /// keeps the cost flat however large the store grows (see <see cref="CreateSchema"/>).
    /// </summary>
    /// <remarks>
    /// There is no unique constraint to conflict with any more, so this no longer overwrites a title
    /// it has already seen — it would insert it twice. Nothing does: the dump names each page once,
    /// and <c>ImportRunner</c> skips anything at or below the last committed page id when it
    /// resumes, so no page is ever offered twice in the first place.
    /// </remarks>
    public void WriteArticles(IEnumerable<ArticleRow> rows)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.CommandText =
            "INSERT INTO pages (title, lead, payload) VALUES ($title, $lead, $payload)";

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

    /// <summary>
    /// Remembers, per article, the revision timestamp the store currently holds — but only for the
    /// articles something has refreshed, which is a handful, not seven million.
    /// </summary>
    /// <remarks>
    /// Without this the store's only notion of "how old am I" is the dump date, which never moves.
    /// Every run would then find the same articles edited-since-the-dump and fetch them AGAIN, having
    /// already fetched them — the same downloads, every time, forever. Recording what was ingested
    /// turns a repeated fetch into a comparison.
    /// </remarks>
    public void EnsureFreshnessTable()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS article_freshness (
                title     TEXT PRIMARY KEY COLLATE NOCASE,
                last_edit TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>The revision timestamps the store holds, for whichever of <paramref name="titles"/>
    /// it has ever refreshed. ISO-8601, so ordinal comparison is chronological.</summary>
    public IReadOnlyDictionary<string, string> GetIngestedRevisions(IReadOnlyList<string> titles)
    {
        var ingested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (titles.Count == 0)
            return ingested;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_edit FROM article_freshness WHERE title = $title";
        var parameter = command.Parameters.Add("$title", SqliteType.Text);

        foreach (var title in titles)
        {
            parameter.Value = title;
            if (command.ExecuteScalar() is string lastEdit)
                ingested[title] = lastEdit;
        }

        return ingested;
    }

    public void SetIngestedRevision(string title, string lastEdit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO article_freshness (title, last_edit) VALUES ($title, $edit)";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$edit", lastEdit);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Replaces one article in place, or inserts it if the store has never seen it.
    /// </summary>
    /// <remarks>
    /// The delete is what makes this a REPLACE. <see cref="WriteArticles"/> deliberately cannot do
    /// it: the table carries no unique constraint on title (see <see cref="CreateSchema"/>), so an
    /// insert alone would leave the old revision sitting beside the new one, and
    /// <see cref="ReadExact"/> takes the first row it finds — which would be the stale one.
    /// <para>
    /// The delete is a seek rather than a scan only because the title index exists by now. This is
    /// for the handful of articles a discovery run actually reads, not for bulk import, where
    /// maintaining that index across millions of random writes is exactly what the schema avoids.
    /// </para>
    /// </remarks>
    public void ReplaceArticle(ArticleRow row)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM pages WHERE title = $title";
            delete.Parameters.AddWithValue("$title", row.Title);
            delete.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO pages (title, lead, payload) VALUES ($title, $lead, $payload)";
            insert.Parameters.AddWithValue("$title", row.Title);
            insert.Parameters.AddWithValue("$lead", row.Lead);
            insert.Parameters.AddWithValue("$payload", Pack(row.Body, row.Links));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Replaces a batch of articles in ONE transaction. What <see cref="ReplaceArticle"/> does, at
    /// the rate an incremental update needs: a day of edits is hundreds of thousands of pages, and a
    /// transaction apiece would spend the whole import in fsync.
    /// </summary>
    public void ReplaceArticles(IEnumerable<ArticleRow> rows)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM pages WHERE title = $title";
        var deleteTitle = delete.Parameters.Add("$title", SqliteType.Text);

        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO pages (title, lead, payload) VALUES ($title, $lead, $payload)";
        var title = insert.Parameters.Add("$title", SqliteType.Text);
        var lead = insert.Parameters.Add("$lead", SqliteType.Text);
        var payload = insert.Parameters.Add("$payload", SqliteType.Blob);

        foreach (var row in rows)
        {
            deleteTitle.Value = row.Title;
            delete.ExecuteNonQuery();

            title.Value = row.Title;
            lead.Value = row.Lead;
            payload.Value = Pack(row.Body, row.Links);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Replaces a batch of redirects. A page that BECAME a redirect since the dump must also stop
    /// being an article, or the store would answer with both.
    /// </summary>
    public void ReplaceRedirects(IEnumerable<(string From, string To)> redirects)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using var deletePage = connection.CreateCommand();
        deletePage.CommandText = "DELETE FROM pages WHERE title = $source";
        var pageTitle = deletePage.Parameters.Add("$source", SqliteType.Text);

        using var deleteRedirect = connection.CreateCommand();
        deleteRedirect.CommandText = "DELETE FROM redirects WHERE source = $source";
        var oldSource = deleteRedirect.Parameters.Add("$source", SqliteType.Text);

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO redirects (source, target) VALUES ($source, $target)";
        var source = insert.Parameters.Add("$source", SqliteType.Text);
        var target = insert.Parameters.Add("$target", SqliteType.Text);

        foreach (var (from, to) in redirects)
        {
            pageTitle.Value = from;
            deletePage.ExecuteNonQuery();

            oldSource.Value = from;
            deleteRedirect.ExecuteNonQuery();

            source.Value = from;
            target.Value = to;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void WriteRedirects(IEnumerable<(string From, string To)> redirects)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.CommandText =
            "INSERT INTO redirects (source, target) VALUES ($source, $target)";

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

        // Rank by ARTICLE SIZE, not title length. The shortest-title rule seemed reasonable and is
        // wrong on the real encyclopedia: measured against the full store it answers "Point" with
        // "Pointer", "Pointcut", "Point Air" — and never surfaces "Point of sale", which the API's
        // opensearch returned at rank 2. That noise is fatal downstream, because the topic
        // recognizer votes on exactly these hits.
        //
        // opensearch ranks by an importance signal we do not have locally. Article size is the
        // stand-in: a concept the world writes 47,000 characters about outranks a hamlet with a
        // stub. Ordering this way reproduces opensearch's top hits closely on the terms tested.
        // ORDER BY must name LENGTH(payload) exactly as ix_pages_title indexes it, or SQLite cannot
        // serve the ordering from the index and falls back to reading every matching row. No
        // secondary sort key for the same reason: it would defeat the index-only scan.
        command.CommandText = """
            SELECT title FROM pages
            WHERE title LIKE $prefix ESCAPE '\'
            ORDER BY LENGTH(payload) DESC
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

    /// <summary>
    /// The date of the dump this store was built from, as yyyyMMdd, or null when the store predates
    /// this being recorded and cannot say.
    /// </summary>
    /// <remarks>
    /// It is the DUMP's date, not the import's: an import that takes eleven hours is no fresher at
    /// the end than the file it read. And it should be the date the dump run STARTED, not the date
    /// the file was published — a full dump is generated over several days, so its content is older
    /// than its timestamp. Erring early is free: a daily increment re-applied over content it
    /// already contains overwrites a page with itself, while one that is skipped loses an edit for
    /// good.
    /// </remarks>
    public int? GetDumpDate()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM import_state WHERE key = 'dump_date'";
        return command.ExecuteScalar() is long value ? (int)value : null;
    }

    public void SetDumpDate(int yyyyMMdd)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO import_state (key, value) VALUES ('dump_date', $date)";
        command.Parameters.AddWithValue("$date", yyyyMMdd);
        command.ExecuteNonQuery();
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

    /// <remarks>
    /// LIMIT 1 because the title index is not unique: "AIDS" and "Aids" are different articles and
    /// both are stored, but a NOCASE lookup matches either. The first row wins, deterministically.
    /// </remarks>
    private static ArticleRow? ReadExact(SqliteConnection connection, string title)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT title, lead, payload FROM pages WHERE title = $title LIMIT 1";
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
        command.CommandText = "SELECT target FROM redirects WHERE source = $source LIMIT 1";
        command.Parameters.AddWithValue("$source", source);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Separates the body from the link list inside the blob. U+0001 cannot occur in
    /// wikitext-derived prose, so it can never appear inside a body and split it early. A space or
    /// a newline would NOT do: both are all over the body, and either would truncate an article at
    /// its first word.
    /// </summary>
    private const char Separator = '\u0001';

    /// <summary>Body and links in one Deflate blob: two reads become one, and the text compresses
    /// about four to one, which is the difference between a 40 GB store and a 12 GB one.</summary>
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
