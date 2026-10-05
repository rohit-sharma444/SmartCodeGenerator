using System.Text;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.App;

/// <summary>
/// <c>build app from csv books.csv</c> reads books.csv from the workspace, wherever the console
/// was started from.
/// </summary>
/// <remarks>
/// Piped with PROJECTPATH set and started from any other folder, it said "No file at books.csv":
/// the reader opened the bare name against the process's current directory. The manual's Scripts
/// chapter relies on this line working. Drives the real CodeGenerator.exe from the test bin, with
/// the working directory deliberately NOT the workspace; ~15 s.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class BuildAppRelativeSourceEndToEndTests
{
    private string _root = string.Empty;
    private string _elsewhere = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // Short: generated apps nest deep, and a long temp path breaks MAX_PATH.
        var id = Guid.NewGuid().ToString("N")[..6];
        _root = Path.Combine(Path.GetTempPath(), "rs" + id);
        _elsewhere = Path.Combine(Path.GetTempPath(), "rc" + id);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_elsewhere);
        File.WriteAllText(Path.Combine(_root, "books.csv"), "Title,Pages\nDune,412\nEmma,320\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var folder in new[] { _root, _elsewhere })
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void A_relative_csv_is_read_from_the_workspace_not_the_current_directory()
    {
        var output = new StringBuilder();

        var result = ProcessRunner.RunStreaming(
            Path.Combine(AppContext.BaseDirectory, "CodeGenerator.exe"),
            string.Empty,
            line => { lock (output) output.AppendLine(line); },
            workingDirectory: _elsewhere,
            timeout: TimeSpan.FromMinutes(3),
            standardInput: "build app from csv books.csv\napprove console\nexit\n",
            environment: new Dictionary<string, string>
            {
                ["PROJECTPATH"] = _root,
                ["CG_NO_WEB"] = "1",
            },
            outputEncoding: new UTF8Encoding(false));

        var text = output.ToString();

        Assert.IsFalse(result.TimedOut, text);
        Assert.DoesNotContain("No file at", text);
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "Books.Console")), text);
    }
}
