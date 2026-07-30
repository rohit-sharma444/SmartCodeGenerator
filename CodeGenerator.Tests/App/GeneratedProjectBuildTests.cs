using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AppScaffolding;
using AppScaffolding.Targets;
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema.Readers;
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
    [DataRow("wpf")]
    public void GeneratedProject_Builds(string targetKey)
    {
        // wpf is TargetScaffolders.Default -- what `build app:` ships unless told otherwise -- and
        // EntityViewModel<T> is the one place that resolves a validator outside Repository, under a
        // `where T : class, new()` constraint. A missing constraint is CS0452, which no substring
        // assertion can see, so the default target has to be compiled like the rest.
        if (targetKey == "wpf" && !OperatingSystem.IsWindows())
            Assert.Inconclusive("The WPF target needs the Windows desktop targeting pack.");

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
            // is a plain column with no FK constraint. The generated companion test fills an Order
            // via TestSupport and inserts it with CustomerId = 0 (the emitted lookup check skips a
            // zero FK), and SeedData can assign FKs across seeded parents, without EF Core or
            // SQLite rejecting either.
            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed (sqlite, with relationship):\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void A_generated_console_app_with_validators_builds_and_its_tests_pass()
    {
        // The unit suite flattens the emitted Validation/ files into one compilation unit, which
        // strips their `using <App>.Models;` / `using <App>.Data;`. Only a real build of a real
        // generated app proves those namespaces resolve. Customer has the required Name field and
        // Order carries a foreign key, so the rule branch and the lookup branch are both emitted.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields =
            {
                new CandidateField { Name = "Name", Type = "string" },
                new CandidateField { Name = "Email", Type = "string" },
                new CandidateField { Name = "Phone", Type = "string" },
            }
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

            var validation = Path.Combine(result.ProjectPath, "Validation");
            Assert.IsTrue(File.Exists(Path.Combine(validation, "CustomerValidator.Generated.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "CustomerValidator.Custom.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Data", "SaveResult.cs")));

            var (buildExit, buildOutput) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, buildExit, $"dotnet build failed:\n{buildOutput}");

            // The generated suite now includes the per-model validator tests, so a green run also
            // proves the emitted rules behave: a filled record passes, a blank one names its
            // required field, and a null one is reported rather than throwing.
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");
            var (testExit, testOutput) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, testExit, $"dotnet test failed:\n{testOutput}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The whole data path in one test: a CSV is read, normalized, turned into a model, scaffolded,
    /// and the result is COMPILED. Every stage in between can produce something that parses and
    /// still will not build -- a column named after its own class is CS0542, a seed value containing
    /// a quote breaks the JSON, a foreign key to a class that was never generated is CS0246 -- and
    /// none of that is visible to a substring assertion.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromACsv_BuildsAndCarriesTheData()
    {
        // Short root: the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and
        // MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var csv = Path.Combine(workspace, "orders.csv");

        // Deliberately awkward: an Id that must be dropped, a column named after its own class,
        // a repeating group, a partial dependency, a value carrying a quote and a brace.
        File.WriteAllText(csv, string.Join("\n",
            "Id,Order,CustomerName,CustomerCity,Qty,Item1,Item2",
            "1,\"a \"\"quoted\"\" {value}\",Ada,London,2,Nut,Bolt",
            "2,plain,Grace,York,3,Washer,",
            "3,another,Ada,London,4,Nut,",
            "4,more,Grace,York,5,Bolt,",
            "5,last,Ada,London,6,Washer,Nut",
            ""));

        try
        {
            var tables = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, csv));

            var findings = NormalizationAnalyzer.Analyze(tables[0]).Findings;
            Assert.IsGreaterThanOrEqualTo(2, findings.Count,
                "expected at least the repeating group and the partial dependency, got: "
                + string.Join("; ", findings.Select(f => f.Headline)));

            var normalized = NormalizationPlan.Apply(tables, findings);
            var model = DataModelBuilder.Build(normalized);
            var imported = TabularImport.ToImportedData(normalized);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Shop", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // The generated project compiles. This is the assertion the others exist to enable.
            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            // ...and the data really is in there, as valid JSON with the awkward value intact.
            var seedFolder = Path.Combine(result.ProjectPath, "Data", SeedFileWriter.SeedFolderName);
            var orderSeed = Path.Combine(seedFolder, "Order.json");

            Assert.IsTrue(File.Exists(orderSeed), $"no seed file at {orderSeed}");

            var rows = JsonDocument.Parse(File.ReadAllText(orderSeed))
                .RootElement.EnumerateArray().ToList();

            Assert.HasCount(5, rows);
            Assert.AreEqual(1, rows[0].GetProperty("Id").GetInt32());

            // The column named after its own class was renamed, not dropped, and its hostile value
            // survived as one string.
            Assert.AreEqual("a \"quoted\" {value}", rows[0].GetProperty("OrderName").GetString());

            // The read Id column is gone -- Id is generated, and rows[0].Id above is the generated one.
            Assert.AreEqual(5, rows.Count(r => r.TryGetProperty("Id", out _)));

            // The extracted entities were scaffolded and seeded too.
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "Customer.json")));
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "OrderItem.json")));

            var customers = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(seedFolder, "Customer.json")))
                .RootElement.EnumerateArray().ToList();

            // Two distinct customers out of five order rows -- the point of the split.
            Assert.HasCount(2, customers);

            // The seed files were registered as copied-to-output content, so the app finds them
            // beside its binary at run time. Without this the project still BUILDS and the app
            // still starts -- just with no data -- so asserting the copy is the only way the
            // csproj registration is covered at all.
            var copied = Directory.GetFiles(
                Path.Combine(result.ProjectPath, "bin"), "Order.json", SearchOption.AllDirectories);

            Assert.IsNotEmpty(copied);
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The `k` (keep as-is) path must compile too. A flat table with a repeating group left in place
    /// produces Item1/Item2 as ordinary columns, and nothing about that may break the build.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromACsv_WithoutNormalizing_AlsoBuilds()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var csv = Path.Combine(workspace, "orders.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Ref,Item1,Item2,Placed,Paid,Total",
            "ORD-1,Nut,Bolt,2026-01-15,yes,9.99",
            "ORD-2,Washer,,2026-02-01,no,4.50",
            ""));

        try
        {
            var tables = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, csv));

            // No findings applied: this is exactly what `k` produces.
            var model = DataModelBuilder.Build(tables);
            var imported = TabularImport.ToImportedData(tables);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Flat", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                    result.ProjectPath, "Data", SeedFileWriter.SeedFolderName, "Order.json")))
                .RootElement.EnumerateArray().ToList();

            // Each of the five types round-tripped into JSON as itself.
            Assert.AreEqual("Nut", rows[0].GetProperty("Item1").GetString());
            Assert.AreEqual(9.99m, rows[0].GetProperty("Total").GetDecimal());
            Assert.IsTrue(rows[0].GetProperty("Paid").GetBoolean());
            Assert.AreEqual(new DateTime(2026, 1, 15), rows[0].GetProperty("Placed").GetDateTime());
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
