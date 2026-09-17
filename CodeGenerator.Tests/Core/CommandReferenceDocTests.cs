using CodeGenerationEngine.Core;
using CodeGenerator.Tests.Helper;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Keeps <c>docs/commands.md</c> in lockstep with <see cref="CommandDictionary"/>.
/// The catalog is the single source of truth; the committed doc is generated from
/// it. If the catalog changes, regenerate the doc:
/// <code>UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests</code>
/// </summary>
[TestClass]
public class CommandReferenceDocTests
{
    [TestMethod]
    public void CommandsDoc_MatchesTheCatalog()
    {
        var docPath = Path.Combine(FindRepoRoot(), "docs", "commands.md");
        var expected = CommandDictionary.GetMarkdownReference();

        if (Environment.GetEnvironmentVariable("UPDATE_COMMAND_DOCS") == "1")
        {
            File.WriteAllText(docPath, expected);
            return;
        }

        Assert.IsTrue(File.Exists(docPath),
            $"Missing {docPath}. Regenerate with: UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests");

        Assert.AreEqual(
            Normalize(expected),
            Normalize(File.ReadAllText(docPath)),
            "docs/commands.md is stale. Regenerate with: UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests");
    }

    // Compare on content, not line endings: the repo checks out CRLF on Windows
    // but the generator emits LF.
    private static string Normalize(string s) => EmittedLines.Normalized(s).TrimEnd('\n');

    /// <summary>
    /// Walks up from the test binary to the repository root, identified by the
    /// single <c>Directory.Build.props</c> that lives there.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;

        Assert.IsNotNull(dir, "Could not locate the repo root (no Directory.Build.props above the test binary).");
        return dir!.FullName;
    }
}
