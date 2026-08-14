using CodeGenerationEngine.Core;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The walkthrough <c>demo</c> runs.
/// </summary>
/// <remarks>
/// <c>demo</c> used to print a canned class and run nothing while the catalog called it a workflow.
/// These tests exist mostly to stop the replacement rotting into the same thing: a script naming
/// commands that no longer exist would fail in front of a first-time user.
/// </remarks>
[TestClass]
public class DemoScriptTests
{
    [TestMethod]
    public void EveryStepNarratesAndActs()
    {
        Assert.IsNotEmpty(DemoScript.Steps);

        foreach (var step in DemoScript.Steps)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(step.Narration), step.Command);
            Assert.IsFalse(string.IsNullOrWhiteSpace(step.Command), step.Narration);
        }
    }

    [TestMethod]
    public void EveryStepRunsACommandTheCatalogKnows()
    {
        // The guard that matters. A demo is the one place a stale command is guaranteed to be seen
        // by someone who has no idea whether the tool or their typing is at fault.
        var prefixes = CommandDictionary.Commands
            .Select(c => c.Command)
            .Select(c => c.Split('<', '[')[0].Trim())
            .Where(p => p.Length > 0)
            .ToList();

        foreach (var step in DemoScript.Steps)
        {
            Assert.IsTrue(
                prefixes.Any(p => step.Command.StartsWith(p, StringComparison.OrdinalIgnoreCase)),
                $"'{step.Command}' matches no command in the catalog");
        }
    }

    [TestMethod]
    public void TheFirstStepCreatesTheProjectAndTheSecondSelectsIt()
    {
        // Order is load-bearing: every later step acts on the current project, so a demo that
        // created one without selecting it would fail from step three onward.
        Assert.Contains(DemoScript.ProjectName, DemoScript.Steps[0].Command);
        Assert.Contains("create", DemoScript.Steps[0].Command);

        Assert.Contains(DemoScript.ProjectName, DemoScript.Steps[1].Command);
        Assert.Contains("use project", DemoScript.Steps[1].Command);
    }

    [TestMethod]
    public void EveryStepAfterTheFirstTwoNamesTheClassTheDemoCreated()
    {
        // Catches a rename of the demo's own class leaving later steps pointing at nothing.
        foreach (var step in DemoScript.Steps.Skip(2))
            Assert.Contains("Invoice", step.Command);
    }

    [TestMethod]
    public void TheIntroNamesTheExactPathItWillWriteTo()
    {
        var intro = string.Join("\n", DemoScript.Intro(@"D:\Work"));

        // It writes to disk. Saying where, before asking, is the whole basis for the confirmation
        // being meaningful.
        Assert.Contains(Path.Combine(@"D:\Work", DemoScript.ProjectName), intro);
    }

    [TestMethod]
    public void TheIntroSaysNothingElseIsTouched()
    {
        var intro = string.Join("\n", DemoScript.Intro(@"D:\Work")).ToLowerInvariant();

        Assert.Contains("delete", intro);
    }

    [TestMethod]
    public void TheOutroPointsAtTheRealFeatureAndSaysHowToCleanUp()
    {
        var outro = string.Join("\n", DemoScript.Outro(@"D:\Work\CodeGenDemo"));

        // A demo's job is to hand the user off to the thing they actually came for.
        Assert.Contains("build app:", outro);
        Assert.Contains(@"D:\Work\CodeGenDemo", outro);
        Assert.Contains("delete", outro.ToLowerInvariant());
    }
}
