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

        if (!File.Exists(storePath))
        {
            // A missing store is a job for WikipediaDumpImporter, not for this service. Say so once
            // and keep ticking — the store may appear while the service is running.
            _log.Error($"No store at {storePath}. Run WikipediaDumpImporter to build it.");
        }

        var status = new StoreUpdateStatusFile(_options.StatusFilePath!);
        var version = typeof(UpdateWorker).Assembly.GetName().Version?.ToString();

        var cycle = new StoreUpdateCycle(
            check: () => new DumpFreshness().Check(new WikipediaStore(storePath)),
            apply: progress => (day, token) => new IncrementalUpdater(
                    new WikipediaStore(storePath), say: _ => { }, progress: progress)
                .UpdateDay(day, workingDirectory, token),
            log: _log,
            status: status,
            serviceVersion: version);

        await cycle.RunLoop(
            TimeSpan.FromHours(_options.IntervalHours),
            Task.Delay,
            stoppingToken);

        _log.Info("Stopped.");
    }
}
