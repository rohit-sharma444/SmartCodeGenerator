using CodeIntelligence;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-config-" + Guid.NewGuid().ToString("N"));

    public ConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Path_ => Path.Combine(_dir, "ai.json");

    [Fact]
    public void Defaults_are_disabled_retrieval_mode()
    {
        var config = AiConfig.Load(Path_);

        Assert.False(config.Enabled);
        Assert.Equal("Retrieval", config.ResolverMode);
        Assert.Equal(0.55, config.ConfidenceFloor, 3);

        // Literals, not AiConfig.Default* — asserting a constant against itself proves
        // nothing. These are the values calibrated by the golden-set sweep on
        // 2026-08-07; changing a default should require deliberately editing this line.
        Assert.Equal(2, config.TopK);
        Assert.Equal(0.55, config.MinTopScore, 3);

        Assert.True(config.ConfidenceFloor > 0.5,
            "an inclusive floor at or below 0.5 would accept two-way ambiguous phrases");

        // topK 1 makes every vote unanimous, which silently disables ConfidenceFloor
        // and the decline-on-ambiguity behaviour the corpus dedup exists for.
        Assert.True(config.TopK >= 2,
            "topK below 2 makes the confidence floor inert - see AiConfig.DefaultTopK");
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var config = AiConfig.Load(Path_);
        config.Enabled = true;
        config.ConfidenceFloor = 0.72;
        config.MinTopScore = 0.41;
        config.Save(Path_);

        var reloaded = AiConfig.Load(Path_);

        Assert.True(reloaded.Enabled);
        Assert.Equal(0.72, reloaded.ConfidenceFloor, 3);

        // MinTopScore is the newer of the two gates and the one Task 10 calibrates
        // separately. Without this it could silently fail to persist and every machine
        // would quietly run the default.
        Assert.Equal(0.41, reloaded.MinTopScore, 3);
    }

    [Fact]
    public void A_corrupt_config_falls_back_to_defaults_instead_of_throwing()
    {
        File.WriteAllText(Path_, "{ this is not json");

        var config = AiConfig.Load(Path_);

        Assert.False(config.Enabled);
        Assert.Equal(0.55, config.ConfidenceFloor, 3);
    }
}
