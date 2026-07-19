namespace AppScaffolding;

/// <summary>Emits a BackgroundService that, on start, reports each entity repository's record
/// count. A worker is not naturally CRUD, so this is an honest harness proving the headless
/// model + data core runs.</summary>
public static class WorkerTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
    {
        var logs = string.Join("\n",
            entityNames.Select(name =>
                $"        _logger.LogInformation(\"{name}: {{Count}} record(s)\", new {name}Repository().GetAll().Count);"));

        return $$"""
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using {{projectName}}.Data;

namespace {{projectName}};

/// <summary>Exercises the generated repositories once at startup.</summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) => _logger = logger;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Exercising the generated repositories:");
{{logs}}
        return Task.CompletedTask;
    }
}
""";
    }
}
