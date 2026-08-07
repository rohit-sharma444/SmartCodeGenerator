using CodeIntelligence;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class AiLayerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-layer-" + Guid.NewGuid().ToString("N"));

    public AiLayerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string ConfigPath => Path.Combine(_dir, "ai.json");
    private string IndexPath => Path.Combine(_dir, "ai-index.db");

    private AiLayer Layer(bool enabled)
    {
        var config = AiConfig.Load(ConfigPath);
        config.Enabled = enabled;
        config.Save(ConfigPath);

        return AiLayer.Create(ConfigPath, IndexPath);
    }

    [Fact]
    public void A_disabled_layer_offers_no_resolver_and_says_why()
    {
        var layer = Layer(enabled: false);

        Assert.Null(layer.Resolver);
        Assert.Contains("disabled", layer.Unavailable);
        Assert.Contains("off (", layer.StatusText());
    }

    [Fact]
    public void An_enabled_layer_exposes_a_resolver()
    {
        var layer = Layer(enabled: true);

        Assert.Null(layer.Unavailable);
        Assert.NotNull(layer.Resolver);
    }

    [Fact]
    public void Reindex_populates_the_index()
    {
        var layer = Layer(enabled: true);

        var message = layer.Reindex();

        Assert.Contains("Indexed", message);
        Assert.Contains("all-MiniLM-L6-v2-int8", message);
        Assert.NotNull(layer.Resolver);
    }

    [Fact]
    public void A_successful_reinitialization_clears_the_previous_reason()
    {
        // Start DISABLED, which is what puts a reason in Unavailable. Starting enabled
        // would leave it null from the outset and this test could not fail.
        var layer = Layer(enabled: false);
        Assert.Contains("disabled", layer.Unavailable);

        // The embedder and store are built before the Enabled check, so a disabled
        // layer can still reindex — this path is reachable, not hypothetical.
        layer.Config.Enabled = true;
        layer.Reindex();

        Assert.NotNull(layer.Resolver);

        // Initialize() must reset Unavailable at the top. Without that reset the layer
        // reports a working resolver and "disabled - run 'ai enable'" at the same time.
        Assert.Null(layer.Unavailable);
    }

    [Fact]
    public void Status_reports_reindex_required_before_a_reindex_and_not_after()
    {
        var layer = Layer(enabled: true);

        Assert.Contains("REINDEX REQUIRED", layer.StatusText());

        layer.Reindex();

        Assert.DoesNotContain("REINDEX REQUIRED", layer.StatusText());
    }

    [Fact]
    public void Status_surfaces_both_gates_so_they_can_be_calibrated_separately()
    {
        var status = Layer(enabled: true).StatusText();

        Assert.Contains("floor:", status);
        Assert.Contains("minTopScore:", status);
    }

    [Fact]
    public void Reindex_on_a_layer_that_never_initialized_reports_rather_than_throws()
    {
        // Index path inside a file, not a folder, so the store cannot be created.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "not a directory");

        var config = AiConfig.Load(ConfigPath);
        config.Enabled = true;
        config.Save(ConfigPath);

        var layer = AiLayer.Create(ConfigPath, Path.Combine(blocker, "ai-index.db"));

        Assert.Null(layer.Resolver);
        Assert.Contains("AI unavailable", layer.Reindex());
    }
}
