using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Keeps the user manual's command-reference appendix in lockstep with
/// <see cref="CommandDictionary"/>, the same way <see cref="CommandReferenceDocTests"/> keeps
/// <c>docs/commands.md</c>.
/// </summary>
/// <remarks>
/// The appendix was maintained by hand and drifted: it offered four window layouts after a fifth
/// became the default, and the optional-layer row never gained <c>nologin</c>. Nothing failed,
/// because nothing was comparing them — a manual is exactly the artefact that can be wrong for
/// months without anyone noticing. Regenerate with:
/// <code>UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests</code>
/// and then rebuild the .docx (<c>tools/build-user-manual.ps1</c>), because the HTML is the source
/// and the document is a build artefact that happens to be committed.
/// </remarks>
[TestClass]
public class ManualAppendixDocTests
{
    private const string Begin = "<!-- APPENDIX:BEGIN";
    private const string End = "<!-- APPENDIX:END -->";

    [TestMethod]
    public void ManualAppendix_MatchesTheCatalog()
    {
        var sourcePath = Path.Combine(FindRepoRoot(), "docs", "user-manual-source.html");

        Assert.IsTrue(File.Exists(sourcePath), $"Missing {sourcePath}");

        var document = File.ReadAllText(sourcePath);
        var (from, to) = Region(document, sourcePath);
        var expected = CommandDictionary.GetHtmlReference();

        if (Environment.GetEnvironmentVariable("UPDATE_MANUAL_APPENDIX") == "1")
        {
            // The document is CRLF in a fresh checkout and the generator emits LF; writing LF into
            // the middle of it would leave one region with different line endings from the rest.
            var replacement = expected.Replace("\n", "\r\n").TrimEnd('\r', '\n');

            File.WriteAllText(sourcePath, document[..from] + "\r\n" + replacement + "\r\n" + document[to..]);
            return;
        }

        Assert.AreEqual(
            Normalize(expected),
            Normalize(document[from..to]),
            "The manual's command-reference appendix is stale. Regenerate with: "
            + "UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests, then rebuild the .docx.");
    }

    /// <summary>
    /// Every catalog entry reaches the appendix, and its angle brackets survive as entities.
    /// </summary>
    /// <remarks>
    /// The escaping is the half that fails silently: Word's HTML converter reads
    /// <c>&lt;class name&gt;</c> as a tag and drops it, so an unescaped row loses its arguments in
    /// the .docx while the HTML still looks right in a browser.
    /// </remarks>
    [TestMethod]
    public void Every_command_reaches_the_appendix_with_its_arguments_intact()
    {
        var html = CommandDictionary.GetHtmlReference();
        var commands = CommandDictionary.Commands;

        Assert.IsNotEmpty(commands);

        foreach (var command in commands)
        {
            // All five characters CommandDictionary.Html escapes, not just the brackets. It
            // mirrored three of them until a catalog command carried a double quote - the SQL
            // form, build app: ... from sql "<connection>" - at which point this test failed on
            // a row that was perfectly correct.
            var expected = command.Command
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&#x27;", StringComparison.Ordinal);

            StringAssert.Contains(html, expected, $"{command.Command} is missing from the appendix");
        }

        // No raw bracket anywhere except the markup's own tags: the rows are the only place a
        // command's arguments appear, and an escaped one cannot produce this.
        foreach (var line in html.Split('\n').Where(l => l.StartsWith("<tr><td>", StringComparison.Ordinal)))
        {
            var withoutTags = line
                .Replace("<tr><td>", string.Empty, StringComparison.Ordinal)
                .Replace("</td><td>", string.Empty, StringComparison.Ordinal)
                .Replace("</td></tr>", string.Empty, StringComparison.Ordinal)
                .Replace("<code>", string.Empty, StringComparison.Ordinal)
                .Replace("</code>", string.Empty, StringComparison.Ordinal);

            Assert.DoesNotContain("<", withoutTags, StringComparison.Ordinal);
            Assert.DoesNotContain(">", withoutTags, StringComparison.Ordinal);
        }
    }

    /// <summary>The region between the markers, or a failure naming the file that lost them.</summary>
    private static (int From, int To) Region(string document, string path)
    {
        var begin = document.IndexOf(Begin, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, begin, $"{path} has no {Begin} marker");

        var afterBegin = document.IndexOf("-->", begin, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, afterBegin, $"{path} has an unterminated {Begin} marker");

        var end = document.IndexOf(End, afterBegin, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"{path} has no {End} marker");

        return (afterBegin + "-->".Length, end);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim('\n');

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;

        Assert.IsNotNull(dir, "Could not locate the repo root (no Directory.Build.props above the test binary).");
        return dir!.FullName;
    }
}
