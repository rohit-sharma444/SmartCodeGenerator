using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaturalLanguageEngine.Core;

/// <summary>
/// Captures structured execution metrics for each pipeline stage.
/// Useful for performance analysis and debugging.
/// </summary>
public sealed class PipelineLogger
{
    private readonly List<PipelineExecutionLog> _logs = new();
    private readonly object _lockObj = new();

    /// <summary>
    /// Logs the execution of a pipeline stage.
    /// </summary>
    public void LogStageExecution(
        string stageName,
        int order,
        TimeSpan elapsed,
        int entityCount,
        int relationshipCount,
        bool success,
        string? errorMessage = null)
    {
        if (stageName == null)
            throw new ArgumentNullException(nameof(stageName));

        var log = new PipelineExecutionLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            StageName = stageName,
            Order = order,
            ElapsedMilliseconds = (long)elapsed.TotalMilliseconds,
            EntityCount = entityCount,
            RelationshipCount = relationshipCount,
            Success = success,
            ErrorMessage = errorMessage
        };

        lock (_lockObj)
        {
            _logs.Add(log);
        }
    }

    /// <summary>
    /// Gets all captured logs in chronological order.
    /// </summary>
    public IReadOnlyList<PipelineExecutionLog> GetLogs()
    {
        lock (_lockObj)
        {
            return _logs.AsReadOnly();
        }
    }

    /// <summary>
    /// Gets all captured logs (alias for GetLogs for backward compatibility).
    /// </summary>
    public IReadOnlyList<PipelineExecutionLog> GetAllLogs()
    {
        return GetLogs();
    }

    /// <summary>
    /// Gets overall statistics for all executions.
    /// </summary>
    public PipelineExecutionStats GetStats()
    {
        lock (_lockObj)
        {
            if (_logs.Count == 0)
                return new PipelineExecutionStats();

            var totalMs = _logs.Sum(l => l.ElapsedMilliseconds);
            var successCount = _logs.Count(l => l.Success);
            var failureCount = _logs.Count(l => !l.Success);
            var totalEntities = _logs.Sum(l => l.EntityCount);
            var totalRelationships = _logs.Sum(l => l.RelationshipCount);

            var slowestStage = _logs
                .OrderByDescending(l => l.ElapsedMilliseconds)
                .FirstOrDefault();

            return new PipelineExecutionStats
            {
                TotalExecutions = _logs.Count,
                TotalElapsedMilliseconds = totalMs,
                AverageElapsedMilliseconds = totalMs / (double)_logs.Count,
                StageCount = _logs.GroupBy(l => l.StageName).Count(),
                SuccessfulStages = successCount,
                FailedStages = failureCount,
                TotalEntitiesFound = totalEntities,
                TotalRelationshipsFound = totalRelationships,
                SlowestStageName = slowestStage?.StageName ?? string.Empty,
                SlowestStageMs = slowestStage?.ElapsedMilliseconds ?? 0
            };
        }
    }

    /// <summary>
    /// Exports logs as JSON for analysis and debugging.
    /// </summary>
    public string ExportAsJson()
    {
        lock (_lockObj)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(_logs, options);
        }
    }

    /// <summary>
    /// Clears all captured logs.
    /// </summary>
    public void Clear()
    {
        lock (_lockObj)
        {
            _logs.Clear();
        }
    }
}

/// <summary>
/// Represents a single stage execution event.
/// </summary>
public sealed class PipelineExecutionLog
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("stageName")]
    public string StageName { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("elapsedMilliseconds")]
    public long ElapsedMilliseconds { get; set; }

    [JsonPropertyName("entityCount")]
    public int EntityCount { get; set; }

    [JsonPropertyName("relationshipCount")]
    public int RelationshipCount { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Statistics from all pipeline executions.
/// </summary>
public sealed class PipelineExecutionStats
{
    /// <summary>
    /// Total number of stage executions logged.
    /// </summary>
    public int TotalExecutions { get; set; }

    /// <summary>
    /// Combined elapsed time across all executions in milliseconds.
    /// </summary>
    public long TotalElapsedMilliseconds { get; set; }

    /// <summary>
    /// Average elapsed time per execution in milliseconds.
    /// </summary>
    public double AverageElapsedMilliseconds { get; set; }

    /// <summary>
    /// Number of distinct stages executed.
    /// </summary>
    public int StageCount { get; set; }

    /// <summary>
    /// Number of successful stage executions.
    /// </summary>
    public int SuccessfulStages { get; set; }

    /// <summary>
    /// Number of failed stage executions.
    /// </summary>
    public int FailedStages { get; set; }

    /// <summary>
    /// Total entities found across all executions.
    /// </summary>
    public int TotalEntitiesFound { get; set; }

    /// <summary>
    /// Total relationships found across all executions.
    /// </summary>
    public int TotalRelationshipsFound { get; set; }

    /// <summary>
    /// Name of the slowest stage by average execution time.
    /// </summary>
    public string SlowestStageName { get; set; } = string.Empty;

    /// <summary>
    /// Execution time of the slowest stage in milliseconds.
    /// </summary>
    public long SlowestStageMs { get; set; }
}