using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Storage;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class DecisionLogTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-decisions-" + Guid.NewGuid().ToString("N"));

    public DecisionLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private DecisionLog NewLog() => new(Path.Combine(_dir, "ai-index.db"));

    private static AiResolution Resolution(double confidence = 0.8) =>
        new(IntentType.ListClasses, confidence,
            [
                new RetrievedExample(11, "list classes", IntentType.ListClasses, 0.9),
                new RetrievedExample(22, "show classes", IntentType.ListClasses, 0.7)
            ],
            "because");

    [Fact]
    public void An_empty_log_has_no_last_decision()
    {
        var log = NewLog();

        Assert.Null(log.Last());
        Assert.Equal(0, log.Count());
    }

    [Fact]
    public void Append_then_Last_round_trips_the_decision()
    {
        var log = NewLog();
        log.Append("show me the classes", Resolution(0.83), accepted: true);

        var last = log.Last();

        Assert.NotNull(last);
        Assert.Equal("show me the classes", last!.Input);
        Assert.Equal(nameof(IntentType.ListClasses), last.Intent);
        Assert.Equal(0.83, last.Confidence, 3);
        Assert.Equal("11,22", last.EvidenceIds);
        Assert.True(last.Accepted);
    }

    [Fact]
    public void CreatedUtc_comes_back_as_Utc_not_Local()
    {
        // Regression guard. created_utc is written with "o" and a bare DateTime.Parse
        // hands it back as DateTimeKind.Local, shifting the value by the machine's
        // offset on a field the record calls CreatedUtc. This is the identical defect
        // that shipped in SqliteVectorStore.BuiltUtc and was fixed at 1ce691f; the
        // plan's Task 7 sample reintroduced it here.
        var log = NewLog();
        var before = DateTime.UtcNow.AddSeconds(-5);

        log.Append("show me the classes", Resolution(), accepted: null);

        var last = log.Last();

        Assert.NotNull(last);
        Assert.Equal(DateTimeKind.Utc, last!.CreatedUtc.Kind);
        Assert.InRange(last.CreatedUtc, before, DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void An_unanswered_decision_stores_a_null_accepted()
    {
        var log = NewLog();
        log.Append("show me the classes", Resolution(), accepted: null);

        Assert.Null(log.Last()!.Accepted);
    }

    [Fact]
    public void A_rejected_decision_is_distinguishable_from_an_unanswered_one()
    {
        var log = NewLog();
        log.Append("show me the classes", Resolution(), accepted: false);

        var last = log.Last();

        Assert.NotNull(last!.Accepted);
        Assert.False(last.Accepted);
    }

    [Fact]
    public void Last_returns_the_most_recent_of_several_appends()
    {
        var log = NewLog();
        log.Append("first", Resolution(), accepted: true);
        log.Append("second", Resolution(), accepted: true);
        log.Append("third", Resolution(), accepted: false);

        Assert.Equal("third", log.Last()!.Input);
        Assert.Equal(3, log.Count());
    }

    [Fact]
    public void An_unwritable_database_loses_the_row_rather_than_failing_the_command()
    {
        // The audit trail is best-effort: a logging failure must never turn a working
        // command into an error. A directory that does not exist stands in for any
        // I/O failure.
        var log = new DecisionLog(Path.Combine(_dir, "no", "such", "folder", "ai.db"));

        log.Append("show me the classes", Resolution(), accepted: true);

        Assert.Null(log.Last());
        Assert.Equal(0, log.Count());
    }
}
