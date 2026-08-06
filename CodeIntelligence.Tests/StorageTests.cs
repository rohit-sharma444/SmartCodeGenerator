using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Storage;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-store-" + Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_dir, "ai-index.db");

    public StorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static (CorpusDocument, float[]) Row(string text, IntentType intent, float a, float b)
        => (new CorpusDocument(text, intent, CorpusSourceKind.IntentRule, "test", text.GetHashCode().ToString()),
            [a, b]);

    [Fact]
    public void Empty_store_requires_reindex()
    {
        var store = new SqliteVectorStore(DbPath);
        var status = store.GetStatus("model-a");

        Assert.Equal(0, status.DocumentCount);
        Assert.True(status.ReindexRequired);
    }

    [Fact]
    public void ReplaceAll_then_LoadAll_round_trips_vectors_and_intents()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        var all = store.LoadAll("model-a");

        Assert.Single(all);
        Assert.Equal("list classes", all[0].Text);
        Assert.Equal(IntentType.ListClasses, all[0].Intent);
        Assert.Equal([1f, 0f], all[0].Vector);
    }

    [Fact]
    public void Status_reports_reindex_required_when_the_model_changed()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        Assert.False(store.GetStatus("model-a").ReindexRequired);
        Assert.True(store.GetStatus("model-b").ReindexRequired);
    }

    [Fact]
    public void LoadAll_returns_nothing_for_a_different_model()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        Assert.Empty(store.LoadAll("model-b"));
    }

    [Fact]
    public void ReplaceAll_leaves_the_previous_index_intact_when_the_build_throws()
    {
        var store = new SqliteVectorStore(DbPath);
        store.ReplaceAll("model-a", 2, [Row("list classes", IntentType.ListClasses, 1f, 0f)]);

        // A row whose vector length disagrees with the declared dimension is
        // rejected mid-build — the temp-then-swap must roll back, not truncate.
        Assert.ThrowsAny<Exception>(() =>
            store.ReplaceAll("model-a", 2,
            [
                Row("create class", IntentType.CreateClass, 0f, 1f),
                (new CorpusDocument("bad", IntentType.Unknown, CorpusSourceKind.IntentRule, "t", "h"),
                 new[] { 1f, 2f, 3f })
            ]));

        var survivors = store.LoadAll("model-a");
        Assert.Single(survivors);
        Assert.Equal("list classes", survivors[0].Text);
    }
}
