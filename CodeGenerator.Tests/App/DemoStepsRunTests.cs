using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerator.App;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.App;

/// <summary>Every demo step, run through the real recogniser and handlers, in order.</summary>
/// <remarks>
/// The catalog check in <see cref="DemoScriptTests"/> passed while step five failed in front of
/// every user: "add business validation Amount must be greater than 0" is a data rule, and the
/// business parser could not read it. Only running the steps shows that. Serial: the project
/// registry and the validation rules are static.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class DemoStepsRunTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "dm" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(_root);
        ProjectManager.CurrentProject = null;
        SessionContext.Clear();
        ValidationRegistry.Clear();
        LearnedIntentStore.UseInMemory();
    }

    [TestCleanup]
    public void Cleanup()
    {
        ProjectManager.CurrentProject = null;
        ValidationRegistry.Clear();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void Every_step_succeeds()
    {
        var pipeline = new NaturalLanguagePipeline();
        ProjectMetaData? project = null;

        foreach (var step in DemoScript.Steps)
        {
            var original = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);
            PipelineOutcome outcome;
            try
            {
                outcome = pipeline.Handle(step.Command, ref project, [], _root, p => ProjectManager.CurrentProject = p);
            }
            finally
            {
                Console.SetOut(original);
            }

            Assert.IsTrue(outcome.Handled, $"'{step.Command}' failed:{Environment.NewLine}{writer}");
        }

        // The help promises two validation rules.
        Assert.HasCount(2, ValidationRegistry.Get("Invoice"));
    }
}
