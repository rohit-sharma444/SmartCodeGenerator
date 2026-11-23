using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Bug hunt 2, E5: <c>generate validator for Person</c> wrote over a hand-written
/// Person.Validation.cs and said "Validator generated".
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GenerateValidatorKeepsHandWrittenTests
{
    private string _workspace = string.Empty;
    private string Folder => Path.Combine(_workspace, "P");
    private string Validator => Path.Combine(Folder, "Person.Validation.cs");

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        ValidationRegistry.Clear();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_e5_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "Person.cs"),
            "namespace App\n{\n    public partial class Person\n    {\n        public int Age { get; set; }\n    }\n}\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ValidationRegistry.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private const string AddRule = "add validation must be between 18 and 65 on Age in Person";

    private string Run(string input)
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
    public void A_hand_written_validation_partial_is_refused_and_kept()
    {
        File.WriteAllText(Validator,
            "namespace App\n{\n    // Written by hand.\n    public partial class Person\n    {\n        public bool IsAdult() => Age >= 18;\n    }\n}\n");
        Run(AddRule);
        Assert.IsGreaterThan(0, ValidationRegistry.Get("Person").Count);
        var before = File.ReadAllBytes(Validator);

        var output = Run("generate validator for Person");

        CollectionAssert.AreEqual(before, File.ReadAllBytes(Validator), output);
        StringAssert.Contains(output, "Person.Validation.cs");
        Assert.DoesNotContain("Validator generated", output, output);
    }

    [TestMethod]
    public void A_validator_it_generated_is_generated_again()
    {
        Run(AddRule);
        Assert.IsGreaterThan(0, ValidationRegistry.Get("Person").Count);
        Run("generate validator for Person");
        Assert.IsTrue(File.Exists(Validator), "generate validator wrote nothing");
        File.AppendAllText(Validator, "// stale\n");

        var output = Run("generate validator for Person");

        StringAssert.Contains(output, "Validator generated");
        Assert.DoesNotContain("// stale", File.ReadAllText(Validator));
    }
}
