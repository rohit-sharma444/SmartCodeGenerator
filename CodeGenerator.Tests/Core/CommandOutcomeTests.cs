using NaturalLanguageBridge;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

// A command that changed nothing must report failure. The batch command relies on
// this: it rolls the workspace back on the first handler that returns false, so a
// handler reporting success after doing nothing silently defeats the rollback.
[TestClass]
[DoNotParallelize]
public sealed class CommandOutcomeTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        LearnedIntentStore.UseInMemory();
        ProjectManager.CurrentProject = null;

        _workspace = Path.Combine(
            Path.GetTempPath(), "cg_outcome_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        LearnedIntentStore.UseInMemory();

        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail an otherwise green test.
        }
    }

    private bool Process(string input)
    {
        ProjectMetaData? project = null;

        return CommandFront.Run(
            input, ref project, [], _workspace, _ => { });
    }

    [TestMethod]
    public void ProcessInput_IntentWithNoHandler_ReportsFailure()
    {
        // The bridge mapper resolves this to Unknown (CreateRepository is a dead,
        // rule-but-no-handler intent it never reaches), so nothing is generated and
        // the command reports failure. Either way, no handler runs.
        var handled = Process("create a repository for Customer");

        Assert.IsFalse(handled);
    }

    [TestMethod]
    public void ProcessInput_DuplicateProperties_ReportsFailure()
    {
        var handled = Process(
            "create a Customer class with properties Name string and Name string");

        Assert.IsFalse(handled);
        Assert.IsEmpty(Directory.GetFiles(_workspace, "Customer.cs", SearchOption.AllDirectories));
    }

    [TestMethod]
    public void ProcessInput_UnsupportedPropertyType_ReportsFailure()
    {
        var handled = Process("create a Customer class with properties Name notatype");

        Assert.IsFalse(handled);
        Assert.IsEmpty(Directory.GetFiles(_workspace, "Customer.cs", SearchOption.AllDirectories));
    }
}
