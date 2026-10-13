using System.IO;
using System.Linq;
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;
using DomainDiscovery.Model;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Overwriting an app removes the files an earlier generate wrote for classes the model no longer
/// has, and nothing else.
/// </summary>
/// <remarks>
/// Found on the web Review page: a class renamed to a name that could not compile left
/// <c>1BadRepository.cs</c> and <c>1BadValidator.Generated.cs</c> behind, and since overwriting
/// never removed anything, no later overwrite could build again.
/// </remarks>
[TestClass]
public class StaleGeneratedFilesTests
{
    [TestMethod]
    public void Overwriting_removes_the_files_of_a_class_the_model_no_longer_has()
    {
        var workspace = NewWorkspace();

        var first = Scaffold(workspace, "Book", "Loan");
        var project = first.ProjectPath;
        var tests = first.TestProjectPath is null ? null : Path.GetDirectoryName(first.TestProjectPath)!;

        Assert.IsTrue(File.Exists(Path.Combine(project, "Data", "LoanRepository.cs")));

        var second = Scaffold(workspace, "Book", "Member");

        foreach (var stale in new[]
                 {
                     "Models/Loan.cs", "Data/LoanRepository.cs",
                     "Validation/LoanValidator.Generated.cs", "Validation/LoanValidator.Custom.cs",
                 })
            Assert.IsFalse(File.Exists(Path.Combine(project, stale)), stale + " was left behind");

        if (tests is not null)
            Assert.IsFalse(File.Exists(Path.Combine(tests, "LoanValidatorTests.cs")));

        Assert.IsTrue(File.Exists(Path.Combine(project, "Models", "Member.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(project, "Validation", "BookValidator.Custom.cs")));
        Assert.IsTrue(second.StaleFiles.Any(s => s.Contains("Data/LoanRepository.cs")),
            "the report should say what was removed");

        // What is left is a data layer that still compiles: nothing the model needs went with it.
        var sources = new[] { "Models", "Data", "Validation" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(project, folder), "*.cs"))
            .Select(path => (path, File.ReadAllText(path)))
            .ToList();

        // The project's implicit usings, which a bare compilation does not have.
        sources.Add(("GlobalUsings.cs",
            "global using System; global using System.Collections.Generic; global using System.IO; "
            + "global using System.Linq; global using System.Threading; global using System.Threading.Tasks;"));

        var errors = Compiler.CompileSources(sources).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [TestMethod]
    public void Overwriting_keeps_files_the_generator_did_not_write_or_the_user_changed()
    {
        var workspace = NewWorkspace();

        var project = Scaffold(workspace, "Book", "Loan").ProjectPath;

        var mine = Path.Combine(project, "Models", "Notes.cs");
        File.WriteAllText(mine, "namespace Mine; public class Notes { }");

        var edited = Path.Combine(project, "Validation", "LoanValidator.Custom.cs");
        File.AppendAllText(edited, "\n// my rule\n");

        var second = Scaffold(workspace, "Book");

        Assert.IsTrue(File.Exists(mine), "a file the generator never wrote was removed");
        Assert.IsTrue(File.Exists(edited), "a generated file the user changed was removed");
        Assert.IsFalse(File.Exists(Path.Combine(project, "Models", "Loan.cs")));
        Assert.IsTrue(second.StaleFiles.Any(s => s.StartsWith("Kept") && s.Contains("LoanValidator.Custom.cs")),
            "the report should say what was kept and why");
    }

    [TestMethod]
    public void An_app_with_no_record_loses_nothing()
    {
        var workspace = NewWorkspace();

        var project = Scaffold(workspace, "Book", "Loan").ProjectPath;
        File.Delete(GeneratedFilesManifest.PathFor(project));

        Scaffold(workspace, "Book");

        Assert.IsTrue(File.Exists(Path.Combine(project, "Models", "Loan.cs")));
    }

    /// <summary>
    /// The report does not call every removed file a class's: switching Logging off removes
    /// Logging/AppLog.cs, which no model class ever had.
    /// </summary>
    [TestMethod]
    public void The_report_does_not_say_a_removed_file_belonged_to_a_class()
    {
        var sentences = new SweepResult(["Logging/AppLog.cs"], ["Logging/Notes.cs"]).Sentences("Shop");

        Assert.HasCount(2, sentences);

        foreach (var sentence in sentences)
            Assert.DoesNotContain("classes", sentence);

        Assert.AreEqual(
            "Removed 1 file(s) from Shop that an earlier generate wrote and this one no longer needs: "
            + "Logging/AppLog.cs",
            sentences[0]);
        Assert.AreEqual(
            "Kept Logging/Notes.cs in Shop: an earlier generate wrote it and this one no longer needs it, "
            + "but it was changed since, so remove it by hand if it is not wanted",
            sentences[1]);
    }

    private static ScaffoldResult Scaffold(string workspace, params string[] classes)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var model = new CandidateDomainModel();
        foreach (var name in classes)
            model.Entities.Add(new CandidateEntity
            {
                Name = name,
                Fields = { new CandidateField { Name = "Title", Type = "string" } }
            });

        var result = new AppScaffolder().Scaffold(model, "Shop", workspace, target, PersistenceKind.Json);
        Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

        return result;
    }

    private static string NewWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "stale_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        return workspace;
    }
}
