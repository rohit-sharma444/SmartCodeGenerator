using System.Diagnostics;
using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class GeneratedProjectBuildTests
{
    private static CandidateDomainModel SampleModel()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Book",
            Fields = { new CandidateField { Name = "Title", Type = "string" } }
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Member",
            Fields = { new CandidateField { Name = "JoinedOn", Type = "DateTime" } }
        });
        return model;
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("classlib")]
    [DataRow("worker")]
    public void GeneratedProject_Builds(string targetKey)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(SampleModel(), "Sample", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GeneratedConsoleProject_WithRelationship_Builds()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Name", Type = "string" } }
        });
        var order = new CandidateEntity
        {
            Name = "Order",
            Fields =
            {
                new CandidateField { Name = "Total", Type = "decimal" },
                new CandidateField { Name = "CustomerId", Type = "int" }
            }
        };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(model, "Shop", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("classlib")]
    [DataRow("worker")]
    public void GeneratedTestProject_BuildsAndPasses(string targetKey)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(SampleModel(), "Sample", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("worker")]
    public void GeneratedSqliteProject_BuildsSeedsAndRoundTrips(string targetKey)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder()
                .Scaffold(SampleModel(), "Sample", workspace, target, PersistenceKind.Sqlite);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            // The generated companion tests exercise Repository + SeedData, which now route through
            // EfStore -> SQLite. A green run proves the schema is created, seeded, and round-tripped.
            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed for {targetKey} (sqlite):\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GeneratedSqliteProject_WithRelationship_SeedsAndRoundTrips()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Name", Type = "string" } }
        });
        var order = new CandidateEntity
        {
            Name = "Order",
            Fields =
            {
                new CandidateField { Name = "Total", Type = "decimal" },
                new CandidateField { Name = "CustomerId", Type = "int" }
            }
        };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder()
                .Scaffold(model, "Shop", workspace, target, PersistenceKind.Sqlite);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            // Proves the feature's central design decision against real SQLite: AppDbContext maps
            // scalar columns only (navigation/collection properties are ignored), so Order.CustomerId
            // is a plain column with no FK constraint. The generated companion test's
            // repository.Create(new Order()) can insert with CustomerId = 0, and SeedData can assign
            // FKs across seeded parents, without EF Core or SQLite rejecting either.
            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed (sqlite, with relationship):\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    private static (int ExitCode, string Output) RunDotNet(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(300_000);
        return (process.ExitCode, stdout + stderr);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
