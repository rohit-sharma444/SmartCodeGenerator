using System.Text.RegularExpressions;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>How far behind the encyclopedia the local store has fallen.</summary>
/// <param name="StoreDate">The dump the store was built from, as yyyyMMdd, or null if it cannot say.</param>
/// <param name="PendingDays">The daily increments published since, oldest first.</param>
/// <param name="Answered">
/// Whether Wikimedia actually answered. This is NOT a technicality, and merging it into
/// <see cref="IsCurrent"/> was a real bug: the first run of this check said "up to date" about a
/// store twelve days behind, because the request had been refused and no pending days came back. A
/// failed check knows nothing, and must not be allowed to sound like a clean bill of health.
/// </param>
public sealed record DumpStaleness(int? StoreDate, IReadOnlyList<int> PendingDays, bool Answered)
{
    public static readonly DumpStaleness Unknown = new(null, Array.Empty<int>(), false);

    /// <summary>Wikimedia answered, and it had nothing newer than the store.</summary>
    public bool IsCurrent => Answered && PendingDays.Count == 0;

    /// <summary>There is something to apply, and we know it.</summary>
    public bool IsStale => Answered && PendingDays.Count > 0;

    /// <summary>What the pending increments cost to download, at the ~800 MB a day they run to.</summary>
    public double ApproximateGigabytes => PendingDays.Count * ApproximateDailyGigabytes;

    /// <summary>
    /// Measured 2026-07-14 against the real feed: the daily files ran 753 MB and 812 MB. They are
    /// this large because <c>pages-meta-hist-incr</c> carries EVERY namespace and the FULL revision
    /// history of each changed page, while this store keeps only the latest revision of article
    /// space — so almost all of what you would download, you would then throw away.
    /// </summary>
    public const double ApproximateDailyGigabytes = 0.78;
}

/// <summary>
/// Asks Wikimedia which daily increments have been published since the store's dump was cut.
/// </summary>
/// <remarks>
/// <b>This checks; it does not download.</b> Applying what it finds is <see cref="IncrementalUpdater"/>'s
/// job, and costing it is <see cref="DumpStaleness.ApproximateGigabytes"/>'s — a store is routinely a
/// fortnight behind and owes ~10 GB at ~780 MB a day, which is why the app shows that figure before it
/// starts spending it, and why Ctrl-C escapes it.
/// <para>
/// The read is deliberately feeble: one request, a short timeout, and every failure swallowed. Being
/// unable to reach Wikimedia is the NORMAL case for this app — the whole point of the dump is that
/// discovery runs offline — so a failed check must be silent and must never delay startup. It reports
/// that it failed rather than reporting nothing to do: see <see cref="DumpStaleness.Answered"/>.
/// </para>
/// </remarks>
public sealed class DumpFreshness
{
    /// <summary>The index of daily "adds-changes" dumps, newest last.</summary>
    private const string IncrementIndexUrl = "https://dumps.wikimedia.org/other/incr/enwiki/";

    /// <summary>
    /// Startup is not the place to wait on a network. The index answers in about a second, so four
    /// leaves room for a slow link while keeping an unreachable one cheap.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Wikimedia REFUSES a request that does not identify itself — measured: 403 without this
    /// header, 200 with it. The first version of this check omitted it, was refused, caught the
    /// failure, and cheerfully reported a twelve-day-old store as up to date.
    /// </summary>
    private const string UserAgent = "SmartCodeGenerator-DomainDiscovery/1.0";

    private static readonly Regex DatedDirectory = new(@">(\d{8})/<", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public DumpFreshness(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.Timeout = Timeout;

        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
    }

    /// <summary>
    /// The increments published after <paramref name="store"/>'s dump. Never throws, and answers
    /// <see cref="DumpStaleness.Unknown"/> whenever it cannot find out — offline, timed out, or a
    /// store too old to know its own date.
    /// </summary>
    public DumpStaleness Check(WikipediaStore store)
    {
        int? storeDate;
        try
        {
            storeDate = store.GetDumpDate();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            return DumpStaleness.Unknown;
        }

        if (storeDate is not { } cutoff)
            return DumpStaleness.Unknown;

        if (PublishedDays() is not { } published)
            return new DumpStaleness(cutoff, Array.Empty<int>(), Answered: false);

        var pending = published.Where(day => day > cutoff).Order().ToList();
        return new DumpStaleness(cutoff, pending, Answered: true);
    }

    /// <summary>
    /// The days Wikimedia has published, or NULL when it did not answer. The distinction is the
    /// point: an empty list means "nothing newer exists", and null means "we do not know" — and
    /// conflating them is what made this check lie the first time it ran.
    /// </summary>
    private IReadOnlyList<int>? PublishedDays()
    {
        string index;
        try
        {
            index = _http.GetStringAsync(IncrementIndexUrl).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or InvalidOperationException or UriFormatException)
        {
            // Offline is the normal case for this app. Say nothing, and claim nothing.
            return null;
        }

        var days = DatedDirectory.Matches(index)
            .Select(m => int.TryParse(m.Groups[1].Value, out var day) ? day : 0)
            .Where(day => day > 0)
            .Distinct()
            .ToList();

        // A body we cannot read a single date out of is not an index — it is an error page dressed
        // as HTTP 200, and it tells us nothing about what has been published.
        return days.Count > 0 ? days : null;
    }
}
