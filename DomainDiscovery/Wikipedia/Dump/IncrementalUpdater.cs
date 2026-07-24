using System.Net;
using System.Net.Http.Headers;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>What applying one day's increment did.</summary>
public sealed record IncrementStats(long Articles, long Redirects, long Skipped);

/// <summary>
/// Brings the store forward one day at a time, from Wikimedia's daily "adds-changes" dumps.
/// </summary>
/// <remarks>
/// This is the expensive path: each day is ~780 MB, a store is routinely a fortnight behind, and the
/// file carries every namespace and the full revision history of each changed page while this store
/// keeps only the latest revision of article space. Most of what it downloads, it throws away. The app
/// runs it at startup (see <see cref="StartupDumpUpdate"/>) having said what it will cost, and Ctrl-C
/// escapes it; <see cref="ArticleRefresher"/> remains the cheap path, refreshing the thirty articles a
/// discovery actually reads.
/// <para>
/// <b>A day at a time, committed as it goes.</b> The store's dump date is advanced only after a day
/// has been fully applied, so a run killed on day nine of twelve keeps its first eight days and
/// resumes at the ninth. Nothing here is atomic across days, and it does not need to be: an
/// increment re-applied over content it already contains overwrites a page with itself.
/// </para>
/// </remarks>
public sealed class IncrementalUpdater
{
    /// <summary>Pages per transaction — large enough that commit cost disappears.</summary>
    private const int BatchSize = 2000;

    private readonly WikipediaStore _store;
    private readonly HttpClient _http;
    private readonly Action<string> _say;
    private readonly IStoreUpdateProgress _progress;

    public IncrementalUpdater(
        WikipediaStore store, HttpClient? http = null, Action<string>? say = null,
        IStoreUpdateProgress? progress = null)
    {
        _store = store;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _say = say ?? Console.WriteLine;
        _progress = progress ?? new ConsoleBarProgress();

        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "SmartCodeGenerator-DomainDiscovery/1.0");
    }

    private static string UrlFor(int day)
        => $"https://dumps.wikimedia.org/other/incr/enwiki/{day}/enwiki-{day}-pages-meta-hist-incr.xml.bz2";

    /// <summary>
    /// Downloads <paramref name="day"/> to <paramref name="destination"/>, showing what it is doing.
    /// Resumes a part-downloaded file rather than starting it again — 780 MB is too much to throw
    /// away over a dropped connection.
    /// </summary>
    public void Download(int day, string destination, CancellationToken cancellation = default)
    {
        var already = File.Exists(destination) ? new FileInfo(destination).Length : 0;
        var response = Get(day, already);

        try
        {
            // The server will not serve a range starting where our file ENDS, which is precisely what
            // a day downloaded in full and not yet applied asks it for. The refusal carries the file's
            // real length, and that is the only thing telling the two causes apart: a file we already
            // have whole (nothing to do), or a local file longer than the server's and so not its file
            // at all (start again). Verified against the live server: 416, not a 206 with no body.
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                var length = response.Content.Headers.ContentRange?.Length;

                if (length == already)
                {
                    _say($"  download {day}: already on disk in full.");
                    return;
                }

                response.Dispose();
                already = 0;
                response = Get(day, 0);
            }

            // A server that ignores the Range header sends the whole file back with 200, and appending
            // THAT to what we have would corrupt it. Start again instead.
            var resuming = response.StatusCode == HttpStatusCode.PartialContent;
            if (already > 0 && !resuming)
                already = 0;

            response.EnsureSuccessStatusCode();

            var total = (response.Content.Headers.ContentLength ?? 0) + already;

            _progress.BeginPhase(UpdatePhase.Download, day, total, already);

            using var source = response.Content.ReadAsStream();
            using var file = new FileStream(
                destination, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write);

            var buffer = new byte[81920];
            var written = already;

            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Interrupted. What is on disk stays on disk — the next run resumes from exactly
                // here, and 780 MB is far too much to throw away because the user changed their mind.
                cancellation.ThrowIfCancellationRequested();

                file.Write(buffer, 0, read);
                written += read;
                _progress.Report(written);
            }

            // Only on a clean finish. A cancelled phase must not snap the bar/percentage to 100%.
            _progress.EndPhase();
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>Asks for one day's file from <paramref name="from"/> bytes in.</summary>
    private HttpResponseMessage Get(int day, long from)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UrlFor(day));

        if (from > 0)
            request.Headers.Range = new RangeHeaderValue(from, null);

        return _http.Send(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>
    /// Applies one downloaded increment to the store: article-space pages are re-stripped and
    /// written over whatever the store held, redirects likewise.
    /// </summary>
    /// <remarks>
    /// The progress bar is driven by bytes of the COMPRESSED file consumed, which is the only total
    /// known in advance — the page count is not, and the decompressed size is not either.
    /// <para>
    /// <b>Cancellation is checked here, between pages, and never inside the stream.</b> BZip2InputStream
    /// catches ANY exception thrown by the stream it is reading and treats it as end-of-input, so a
    /// token checked down there would not stop the apply — it would silently END it, and the caller
    /// would be told a partial day was a whole one.
    /// </para>
    /// </remarks>
    /// <param name="path">The downloaded increment file to apply.</param>
    /// <param name="day">
    /// The day this file is for, so progress can report it. Defaults to 0 (unknown) for callers —
    /// such as tests — that apply a file directly without going through <see cref="UpdateDay"/>,
    /// which always passes the real day.
    /// </param>
    /// <param name="cancellation">Checked between pages; see the remarks above.</param>
    public IncrementStats Apply(string path, int day = 0, CancellationToken cancellation = default)
    {
        var total = new FileInfo(path).Length;

        _progress.BeginPhase(UpdatePhase.Apply, day, total, initial: 0);
        using var file = File.OpenRead(path);
        using var counted = new CountingStream(file, _progress.Report);

        long articles = 0, redirects = 0, skipped = 0;

        var pending = new List<ArticleRow>(BatchSize);
        var pendingRedirects = new List<(string From, string To)>(BatchSize);

        foreach (var page in DumpPageReader.ReadBz2Stream(counted))
        {
            cancellation.ThrowIfCancellationRequested();

            // Every namespace is in this file — talk pages, user pages, templates. The store holds
            // article space, so the rest is read and dropped.
            if (page.Namespace != 0)
            {
                skipped++;
                continue;
            }

            if (page.RedirectTo is { Length: > 0 } target)
            {
                pendingRedirects.Add((page.Title, target));
                redirects++;
            }
            else
            {
                var stripped = WikitextStripper.Parse(page.Wikitext);

                // Strips to nothing: a page that is pure template, or one whose text this file does
                // not carry. Overwriting a good article with an empty one is worse than being stale.
                if (stripped.Body.Length == 0)
                {
                    skipped++;
                    continue;
                }

                pending.Add(new ArticleRow(
                    page.Title,
                    WikitextStripper.LeadOf(stripped.Body),
                    stripped.Body,
                    stripped.Links));
                articles++;
            }

            if (pending.Count >= BatchSize)
                Flush(pending, pendingRedirects);
        }

        Flush(pending, pendingRedirects);

        _progress.EndPhase();

        return new IncrementStats(articles, redirects, skipped);
    }

    /// <summary>
    /// Applies every increment published since the store's dump, oldest first, and moves the store's
    /// date forward as each one lands.
    /// </summary>
    public void Update(
        IReadOnlyList<int> days, string workingDirectory, CancellationToken cancellation = default)
    {
        foreach (var day in days)
            UpdateDay(day, workingDirectory, cancellation);
    }

    /// <summary>
    /// Brings the store forward by exactly one day: downloads it, applies it, and only then records
    /// it. Interrupting this costs that day and nothing more — the next run does it again from the
    /// bytes already on disk.
    /// </summary>
    public void UpdateDay(int day, string workingDirectory, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(workingDirectory);

        var file = Path.Combine(workingDirectory, $"enwiki-{day}-incr.xml.bz2");

        _say($"{day}:");
        Download(day, file, cancellation);

        var stats = Apply(file, day, cancellation);

        // Only now. A day half-applied must be applied again, not skipped.
        _store.SetDumpDate(day);

        _say($"  applied {stats.Articles:N0} articles, {stats.Redirects:N0} redirects " +
             $"({stats.Skipped:N0} pages skipped). Store now at {day}.");

        // 780 MB apiece; keeping twelve of them around would cost 9 GB for nothing.
        try { File.Delete(file); }
        catch (IOException) { /* it is a temp file; leaving it behind is not a failure */ }
    }

    private void Flush(List<ArticleRow> articles, List<(string From, string To)> redirects)
    {
        if (articles.Count > 0)
        {
            _store.ReplaceArticles(articles);
            articles.Clear();
        }

        if (redirects.Count > 0)
        {
            _store.ReplaceRedirects(redirects);
            redirects.Clear();
        }
    }
}

/// <summary>
/// Passes a stream through untouched while reporting how much of it has been read — which is how the
/// apply bar knows where it is, since the number of pages in the file is not known until it ends.
/// </summary>
internal sealed class CountingStream(Stream inner, Action<long> report) : Stream
{
    private long _read;

    /// <remarks>
    /// <b>The callback is not allowed to fail.</b> <c>BZip2InputStream</c> catches ANY exception the
    /// underlying stream throws and treats it as end-of-input — so a bug in the progress bar does not
    /// crash the import, it SILENTLY TRUNCATES it, and the run reports success having applied a
    /// fraction of the file. That is exactly what happened: an overflow in the bar's first redraw
    /// ended the stream after zero pages. Reporting on the work must never be able to damage it.
    /// </remarks>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        _read += read;

        try { report(_read); }
        catch { /* a progress bar is not worth an import */ }

        return read;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();

        base.Dispose(disposing);
    }
}
