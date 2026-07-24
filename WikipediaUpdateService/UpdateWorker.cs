using DomainDiscovery.Wikipedia.Dump;
using Microsoft.Extensions.Options;

namespace WikipediaUpdateService;

/// <summary>
/// The service's only moving part: it builds a <see cref="StoreUpdateCycle"/> over the real store
/// and runs it until the host stops. All the decisions live in the cycle, which is unit-tested;
/// this class is wiring.
/// </summary>
public sealed class UpdateWorker : BackgroundService
{
    private readonly ServiceOptions _options;
    private readonly IUpdateLog _log;

    /// <summary>
    /// Whether the last tick found the store, so <see cref="CheckStore"/> logs a missing store once
    /// per transition rather than once per interval. Null before the first tick, so the first check
    /// always logs — covering the former startup-only check this replaced.
    /// </summary>
    private bool? _storePresentLastTick;

    public UpdateWorker(IOptions<ServiceOptions> options, IUpdateLog log)
    {
        _options = options.Value;
        _options.ApplyDefaults();
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var storePath = _options.StorePath!;
        var workingDirectory = _options.WorkingDirectory!;

        _log.Info($"Started. Store: {storePath}. Interval: {_options.IntervalHours}h.");

        var status = new StoreUpdateStatusFile(_options.StatusFilePath!);
        var version = typeof(UpdateWorker).Assembly.GetName().Version?.ToString();

        var cycle = new StoreUpdateCycle(
            check: () => CheckStore(storePath),
            apply: progress =>
            {
                // Built once per cycle (not per day, per StartupDumpUpdate.ApplyDay's per-day call)
                // so a multi-day catch-up reuses one WikipediaStore and one IncrementalUpdater —
                // and so the HttpClient IncrementalUpdater owns (30-minute timeout) is not
                // allocated and leaked once per pending day. Matches BackgroundStoreUpdate.Start,
                // which builds it once outside its day loop for the same reason.
                var updater = new IncrementalUpdater(
                    new WikipediaStore(storePath), say: _ => { }, progress: progress);

                return (day, token) => updater.UpdateDay(day, workingDirectory, token);
            },
            log: _log,
            status: status,
            serviceVersion: version);

        await cycle.RunLoop(
            TimeSpan.FromHours(_options.IntervalHours),
            Task.Delay,
            stoppingToken);

        _log.Info("Stopped.");
    }

    /// <summary>
    /// Re-checked at the top of every tick, not only once at startup — the store may appear or
    /// vanish while the service runs. When it is absent, this returns <see cref="DumpStaleness.Unknown"/>
    /// WITHOUT constructing a <see cref="WikipediaStore"/> against the missing path: that
    /// constructor's <c>ReadWriteCreate</c> mode would open (and so create) an empty, schema-less
    /// SQLite file there — at exactly the path <c>WikipediaDumpImporter</c> is meant to build the
    /// real store at, confusing a later import or diagnosis on a fresh install.
    /// </summary>
    private DumpStaleness CheckStore(string storePath)
    {
        var exists = File.Exists(storePath);

        if (exists != _storePresentLastTick)
        {
            _storePresentLastTick = exists;

            // A missing store is a job for WikipediaDumpImporter, not for this service. Say so once
            // per transition — not once per tick — and keep ticking.
            if (!exists)
                _log.Error($"No store at {storePath}. Run WikipediaDumpImporter to build it.");
            else
                _log.Info($"Store found at {storePath}. Resuming checks.");
        }

        return exists ? new DumpFreshness().Check(new WikipediaStore(storePath)) : DumpStaleness.Unknown;
    }
}
