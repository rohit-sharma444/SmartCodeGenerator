using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// What to say about a workspace that is not there.
/// </summary>
/// <remarks>
/// A <c>PROJECTPATH</c> pointing at a directory that does not exist used to surface as
/// <c>Warning: Failed to initialize existing projects: Could not find a part of the path …</c> —
/// the tool describing its own internals failing, when the actual situation is simple and the
/// user's to fix.
/// </remarks>
[TestClass]
public class WorkspaceNoticeTests
{
    private static readonly string Missing =
        Path.Combine(Path.GetTempPath(), "scg-absent-" + Guid.NewGuid().ToString("N"));

    private static string Notice(string root, bool projectPathSet)
        => string.Join("\n", WorkspaceNotice.Lines(root, projectPathSet));

    [TestMethod]
    public void AnExistingWorkspaceSaysNothing()
    {
        Assert.IsEmpty(WorkspaceNotice.Lines(Path.GetTempPath(), projectPathSet: false));
        Assert.IsEmpty(WorkspaceNotice.Lines(Path.GetTempPath(), projectPathSet: true));
    }

    [TestMethod]
    public void AMissingWorkspaceIsNamedPlainly()
    {
        var notice = Notice(Missing, projectPathSet: true);

        Assert.Contains(Missing, notice);

        // Not the internal phrasing. "Failed to initialize existing projects" describes the tool's
        // own method, which is no help to whoever set the variable.
        Assert.DoesNotContain("Failed to initialize", notice);
        Assert.DoesNotContain("Could not find a part of the path", notice);
    }

    [TestMethod]
    public void AMissingWorkspaceFromProjectPathBlamesProjectPath()
    {
        // The actionable fact: it is that variable, and unsetting it is one of the two fixes.
        var notice = Notice(Missing, projectPathSet: true);

        Assert.Contains("PROJECTPATH", notice);
    }

    [TestMethod]
    public void AMissingWorkspaceWithoutProjectPathDoesNotBlameIt()
    {
        // Without the variable set, the workspace is the current directory — mentioning
        // PROJECTPATH would send the user looking for something they never set.
        var notice = Notice(Missing, projectPathSet: false);

        Assert.DoesNotContain("PROJECTPATH", notice);
        Assert.Contains(Missing, notice);
    }

    [TestMethod]
    public void TheNoticeSaysWhatWillHappenNext()
    {
        // Silence about consequences is what makes a warning ignorable. It has to say that the
        // directory gets created on first write, or the user cannot judge whether to act.
        Assert.Contains("created", Notice(Missing, projectPathSet: true).ToLowerInvariant());
    }
}
