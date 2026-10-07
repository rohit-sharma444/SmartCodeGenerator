using CodeGenerator.App;
using CodeIntelligence;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The `ai ...` meta-commands. AiCommand.Handle is pure string-in/string-out given a
/// layer, so it is testable despite living in the CLI project — CodeGenerator carries
/// InternalsVisibleTo for this assembly.
///
/// The plan specified no tests for this task. That is the same gap that let the
/// CreatedUtc defect ship in Task 7, so these were added rather than skipped.
/// </summary>
[TestClass]
public sealed class AiCommandTests
{
    private string _dir = string.Empty;

    private string ConfigPath => Path.Combine(_dir, "ai.json");
    private string IndexPath => Path.Combine(_dir, "ai-index.db");

    [TestInitialize]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ai-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private AiLayer Layer() => AiLayer.Create(ConfigPath, IndexPath);

    [TestMethod]
    public void Recognizes_the_bare_verb_and_its_subcommands()
    {
        Assert.IsTrue(AiCommand.IsAiCommand("ai"));
        Assert.IsTrue(AiCommand.IsAiCommand("  ai  "));
        Assert.IsTrue(AiCommand.IsAiCommand("AI STATUS"));
        Assert.IsTrue(AiCommand.IsAiCommand("ai why"));
    }

    [TestMethod]
    public void Does_not_swallow_ordinary_commands_that_merely_start_with_ai()
    {
        // "aim", "airline" and the like must reach the normal pipeline. The space in
        // the "ai " prefix is what prevents this, so it is worth pinning.
        Assert.IsFalse(AiCommand.IsAiCommand("aim for the Customer class"));
        Assert.IsFalse(AiCommand.IsAiCommand("airline"));
        Assert.IsFalse(AiCommand.IsAiCommand("list classes"));
        Assert.IsFalse(AiCommand.IsAiCommand(string.Empty));
    }

    [TestMethod]
    public void A_bare_ai_reports_status()
    {
        var output = AiCommand.Handle("ai", Layer());

        StringAssert.Contains(output, "AI:");
        StringAssert.Contains(output, "Index:");
    }

    [TestMethod]
    public void Enable_then_disable_persists_to_the_layers_own_config_file()
    {
        var layer = Layer();

        AiCommand.Handle("ai enable", layer);
        Assert.IsTrue(AiConfig.Load(ConfigPath).Enabled);

        AiCommand.Handle("ai disable", layer);
        Assert.IsFalse(AiConfig.Load(ConfigPath).Enabled);
    }

    // "AI disabled", and then ai status said "AI: enabled": disable only saved the config, and the
    // session went on using the resolver it had loaded.
    [TestMethod]
    public void Disable_takes_effect_in_the_session_at_once()
    {
        var enabled = AiConfig.Load(ConfigPath);
        enabled.Enabled = true;
        enabled.Save(ConfigPath);
        var layer = Layer();
        if (layer.Resolver is null)
            Assert.Inconclusive($"The AI model is not available here: {layer.Unavailable}");

        AiCommand.Handle("ai disable", layer);

        Assert.IsNull(layer.Resolver);
        StringAssert.Contains(AiCommand.Handle("ai status", layer), "AI: off (disabled");
    }

    [TestMethod]
    public void Enable_does_not_write_to_the_default_config_path()
    {
        // AiLayer.Config.Save() with no argument targets AiConfig.DefaultPath, which is
        // the CLI's own ai.json in the binary folder. A layer built on an explicit path
        // must write back to THAT path; otherwise `ai enable` in any non-default
        // context silently edits the wrong file.
        var defaultPath = AiConfig.DefaultPath;
        var before = File.Exists(defaultPath) ? File.ReadAllText(defaultPath) : null;

        AiCommand.Handle("ai enable", Layer());

        var after = File.Exists(defaultPath) ? File.ReadAllText(defaultPath) : null;
        Assert.AreEqual(before, after, "'ai enable' wrote to the default ai.json");
        Assert.IsTrue(AiConfig.Load(ConfigPath).Enabled);
    }

    [TestMethod]
    public void Mode_without_an_argument_explains_the_usage()
        => StringAssert.Contains(AiCommand.Handle("ai mode", Layer()), "Usage: ai mode");

    [TestMethod]
    public void Mode_rejects_an_unknown_value()
        => StringAssert.Contains(AiCommand.Handle("ai mode sideways", Layer()), "Unknown mode");

    [TestMethod]
    public void Phase_B_modes_are_declined_rather_than_silently_accepted()
    {
        var layer = Layer();

        StringAssert.Contains(AiCommand.Handle("ai mode generative", layer), "phase B");
        StringAssert.Contains(AiCommand.Handle("ai mode hybrid", layer), "phase B");

        // The rejection must not have changed the stored mode.
        Assert.AreEqual("Retrieval", AiConfig.Load(ConfigPath).ResolverMode);
    }

    [TestMethod]
    public void Why_reports_plainly_when_nothing_has_been_recorded()
        => StringAssert.Contains(AiCommand.Handle("ai why", Layer()), "No AI decisions recorded yet.");

    [TestMethod]
    public void An_unknown_subcommand_lists_the_real_ones()
    {
        var output = AiCommand.Handle("ai frobnicate", Layer());

        StringAssert.Contains(output, "Unknown ai command 'frobnicate'");
        StringAssert.Contains(output, "status");
        StringAssert.Contains(output, "why");
    }
}
