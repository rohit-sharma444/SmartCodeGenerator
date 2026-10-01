using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// add method and rename class built a message and threw it away, so both printed nothing at
/// all - not when they worked, and not when the class did not exist.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SilentCommandOutputTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_silent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, "Inn.cs"),
            "namespace Shop { public class Inn { public string Name { get; set; } } }");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private string RunAndCapture(string input)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            ProjectMetaData? project = null;
            CommandFront.Run(input, ref project, [], _workspace, _ => { });
        }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    [TestMethod]
    [DataRow("add method Ping to Inn", "Method 'Ping' added to 'Inn'.")]
    [DataRow("add method Ping to Nowhere", "Class 'Nowhere' not found.")]
    [DataRow("rename class Inn to Hotel", "Class 'Inn' renamed to 'Hotel'.")]
    [DataRow("rename class Nowhere to Hotel", "Class 'Nowhere' not found.")]
    public void The_command_says_what_happened(string input, string expected)
        => StringAssert.Contains(RunAndCapture(input), expected);
}
