using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Helper;

/// <summary>
/// The streaming overload. It exists so a three-minute `dotnet build` can report progress
/// instead of appearing at the end all at once; <c>Run</c> reads to the end and cannot.
/// </summary>
[TestClass]
public class ProcessRunnerStreamingTests
{
    [TestMethod]
    public void Hands_each_line_over_as_it_arrives()
    {
        var lines = new List<string>();

        var result = ProcessRunner.RunStreaming(
            "cmd.exe", "/c echo one& echo two& echo three", lines.Add);

        Assert.AreEqual(0, result.ExitCode);
        Assert.HasCount(3, lines);
        Assert.AreEqual("one", lines[0].Trim());
        Assert.AreEqual("three", lines[2].Trim());
    }

    // Callers that already have the whole output must keep working, so the streamed lines are
    // also accumulated into the result exactly as Run would have.
    [TestMethod]
    public void Still_returns_the_whole_output_in_the_result()
    {
        var result = ProcessRunner.RunStreaming("cmd.exe", "/c echo hello", _ => { });

        StringAssert.Contains(result.StandardOutput, "hello");
    }

    [TestMethod]
    public void Reports_a_non_zero_exit_code()
    {
        var result = ProcessRunner.RunStreaming("cmd.exe", "/c exit 3", _ => { });

        Assert.AreEqual(3, result.ExitCode);
    }

    [TestMethod]
    public void Writes_standard_input_and_closes_it()
    {
        var lines = new List<string>();

        var result = ProcessRunner.RunStreaming("findstr.exe", "\"^\"", lines.Add, standardInput: "alpha\r\nbeta\r\n");

        Assert.AreEqual(0, result.ExitCode);
        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, lines.Select(l => l.Trim()).ToArray());
    }

    [TestMethod]
    public void Sets_the_childs_environment()
    {
        var lines = new List<string>();

        ProcessRunner.RunStreaming("cmd.exe", "/c echo %SCG_SCRIPT_TEST%", lines.Add,
            environment: new Dictionary<string, string> { ["SCG_SCRIPT_TEST"] = "from the parent" });

        Assert.AreEqual("from the parent", lines.Single().Trim());
    }
}
