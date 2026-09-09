using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The pipeline as a whole scaffold sees it: a real <see cref="AppScaffolder"/> run over a
/// two-entity model, asserting on the files that land on disk rather than on one template's
/// return value.
/// </summary>
/// <remarks>
/// Both tests here are regression PINS, not red-then-green tests. Neither behaviour is new —
/// they exist so that a later edit which changes either one has to change a test that says why
/// it was that way.
/// </remarks>
[TestClass]
public class PipelineEndToEndTests
{
    [TestMethod]
    public void AThreeEntityAppEmitsNoEmptyStages()
    {
        // The mitigation for "seven stages is more machinery than a three-entity CRUD app
        // needs", measured on a real scaffold rather than on one template's output.
        var (workspace, folder) = Scaffold();

        try
        {
            var validator = File.ReadAllText(
                Path.Combine(folder, "Validation", "CustomerValidator.Generated.cs"));

            Assert.DoesNotContain("private static void Workflow(", validator);
            Assert.DoesNotContain("private static void Transaction(", validator);
            StringAssert.Contains(validator, "private static void Security(");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [TestMethod]
    public void AConsoleAppHasNoRole_AndStillGenerates()
    {
        // The limit this phase cannot lift, pinned so nobody later "fixes" CurrentRole into
        // something that names a Session the console target never emits.
        var (workspace, folder) = Scaffold();

        try
        {
            var repository = File.ReadAllText(Path.Combine(folder, "Data", "Repository.cs"));

            StringAssert.Contains(repository, "private static string? CurrentRole() => null;");
            Assert.DoesNotContain("Session.Current", repository);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>The temp workspace as well as the project inside it, so a run leaves nothing
    /// behind in <c>%TEMP%</c>.</summary>
    private static (string Workspace, string Project) Scaffold()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Surname", Type = "string" } }
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Order",
            Fields = { new CandidateField { Name = "CustomerId", Type = "int" } }
        });

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var result = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(model, "Shop", workspace, target));

        Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

        return (workspace, result.ProjectPath);
    }
}
