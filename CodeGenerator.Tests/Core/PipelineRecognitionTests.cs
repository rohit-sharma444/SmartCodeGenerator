using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// A command's <em>recognition</em> (was it understood?) is distinct from its
/// <em>success</em> (did the action complete?). The CLI must only offer the
/// "Command not recognized / did you mean" help for genuinely unrecognized input,
/// never for a recognized command whose handler failed for a real reason (e.g.
/// "No validation rules found"). <see cref="PipelineOutcome.Recognized"/> carries
/// that signal; these guard it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PipelineRecognitionTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        LearnedIntentStore.UseInMemory();
        ProjectManager.CurrentProject = null;
        _workspace = Path.Combine(Path.GetTempPath(), "cg_recog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        LearnedIntentStore.UseInMemory();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private PipelineOutcome Handle(string input)
    {
        ProjectMetaData? project = null;
        return new NaturalLanguagePipeline().Handle(input, ref project, [], _workspace, _ => { });
    }

    [TestMethod]
    public void Recognized_command_that_fails_is_still_marked_recognized()
    {
        // GenerateValidator is a known intent; it fails only because no rules exist.
        var outcome = Handle("generate validator for Ghost");

        Assert.IsFalse(outcome.Handled, "The command should report failure (no rules).");
        Assert.IsTrue(outcome.Recognized, "A recognized-but-failed command must stay recognized.");
    }

    [TestMethod]
    public void Unintelligible_input_is_marked_not_recognized()
    {
        var outcome = Handle("blarg flooble wingding");

        Assert.IsFalse(outcome.Handled);
        Assert.IsFalse(outcome.Recognized);
    }
}
