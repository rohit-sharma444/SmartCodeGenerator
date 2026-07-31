using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
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

    /// <summary>
    /// The Excel path end to end: a workbook is read, modelled, scaffolded, and the result is
    /// COMPILED. What makes this worth its runtime over the CSV case is the two things only a
    /// workbook can get wrong — a date that is really a serial number, and a second sheet that has
    /// to become a second class — and neither is visible to a substring assertion.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromAnExcelWorkbook_BuildsAndCarriesTheData()
    {
        // Short root: the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and
        // MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var xlsx = Path.Combine(workspace, "shop.xlsx");
        WriteWorkbook(xlsx);

        try
        {
            var tables = new ExcelReader().Read(new DataSourceSpec(DataSourceKind.Excel, xlsx));

            // Two visible sheets, two entities. The hidden third sheet is not one.
            Assert.HasCount(2, tables);
            CollectionAssert.AreEquivalent(
                new[] { "Orders", "Customers" }, tables.Select(t => t.Name).ToArray());

            // The date column really is a date, not a five-digit serial.
            var orders = tables.Single(t => t.Name == "Orders");
            Assert.AreEqual("DateTime", orders.Columns.Single(c => c.Name == "Placed").Type);

            // ...and the padded reference kept its zeros, so it stayed a string.
            Assert.AreEqual("string", orders.Columns.Single(c => c.Name == "Code").Type);
            Assert.AreEqual("00042", orders.Value(0, "Code"));

            // The unstyled foreign key did NOT inherit the padding: only an int pairs with the
            // entity the other sheet produced, so a stray zero here would drop the relationship
            // and the generated navigation property with it.
            Assert.AreEqual("int", orders.Columns.Single(c => c.Name == "CustomerId").Type);

            var model = DataModelBuilder.Build(tables);

            var order = model.Entities.Single(e => e.Name == "Order");
            Assert.IsTrue(
                order.Relationships.Any(r => r.Target == "Customer"),
                "expected Order -> Customer, got: "
                + string.Join(", ", order.Relationships.Select(r => r.Target)));
            var imported = TabularImport.ToImportedData(tables);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Shop", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // The generated project compiles. This is the assertion the rest exist to enable.
            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            var seedFolder = Path.Combine(
                result.ProjectPath, "Data", SeedFileWriter.SeedFolderName);

            var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(seedFolder, "Order.json")))
                .RootElement.EnumerateArray().ToList();

            Assert.HasCount(2, rows);
            Assert.AreEqual(new DateTime(2026, 3, 15), rows[0].GetProperty("Placed").GetDateTime());
            Assert.AreEqual("00042", rows[0].GetProperty("Code").GetString());
            Assert.AreEqual(9.99m, rows[0].GetProperty("Total").GetDecimal());

            // The second sheet became its own seeded class.
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "Customer.json")));
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A minimal .xlsx written by hand. A committed binary workbook would be unreviewable bytes in
    /// the repo, and this way the date serial and the padded format the test asserts on are visible
    /// right next to the assertions.
    /// </summary>
    private static void WriteWorkbook(string path)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // Style 0 is General, as Excel always writes it: a cell with no s attribute uses index 0,
        // so putting a real format there would silently apply it to every unstyled number in the
        // file -- which is what CustomerId is, and it has to stay an int for the relationship.
        // Style 1: the 00000 padded code. Style 2: a date. 46096 is 15 March 2026.
        var styles = $"<styleSheet xmlns=\"{ns}\">"
            + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"00000\"/></numFmts>"
            + "<cellXfs count=\"3\"><xf numFmtId=\"0\" xfId=\"0\"/>"
            + "<xf numFmtId=\"164\" xfId=\"0\"/>"
            + "<xf numFmtId=\"14\" xfId=\"0\"/></cellXfs></styleSheet>";

        var orders = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">"
            + Inline("A1", "Code") + Inline("B1", "Placed")
            + Inline("C1", "Total") + Inline("D1", "CustomerId")
            + "</row>"
            + "<row r=\"2\"><c r=\"A2\" s=\"1\"><v>42</v></c><c r=\"B2\" s=\"2\"><v>46096</v></c>"
            + "<c r=\"C2\"><v>9.99</v></c><c r=\"D2\"><v>1</v></c></row>"
            + "<row r=\"3\"><c r=\"A3\" s=\"1\"><v>7</v></c><c r=\"B3\" s=\"2\"><v>46097</v></c>"
            + "<c r=\"C3\"><v>4.50</v></c><c r=\"D3\"><v>2</v></c></row>"
            + "</sheetData></worksheet>";

        var customers = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">" + Inline("A1", "Name") + Inline("B1", "City") + "</row>"
            + "<row r=\"2\">" + Inline("A2", "Ada") + Inline("B2", "London") + "</row>"
            + "<row r=\"3\">" + Inline("A3", "Grace") + Inline("B3", "York") + "</row>"
            + "</sheetData></worksheet>";

        var scratch = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">" + Inline("A1", "Note") + "</row>"
            + "<row r=\"2\">" + Inline("A2", "working") + "</row>"
            + "</sheetData></worksheet>";

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        Add(zip, "xl/worksheets/sheet1.xml", orders);
        Add(zip, "xl/worksheets/sheet2.xml", customers);
        Add(zip, "xl/worksheets/sheet3.xml", scratch);
        Add(zip, "xl/styles.xml", styles);

        Add(zip, "xl/workbook.xml",
            $"<workbook xmlns=\"{ns}\" xmlns:r=\"{rns}\"><sheets>"
            + "<sheet name=\"Orders\" sheetId=\"1\" r:id=\"rId1\"/>"
            + "<sheet name=\"Customers\" sheetId=\"2\" r:id=\"rId2\"/>"
            + "<sheet name=\"Scratch\" sheetId=\"3\" state=\"hidden\" r:id=\"rId3\"/>"
            + "</sheets></workbook>");

        Add(zip, "xl/_rels/workbook.xml.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/"
            + "relationships\">"
            + $"<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId3\" Target=\"worksheets/sheet3.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId4\" Target=\"styles.xml\" Type=\"{rns}/styles\"/>"
            + "</Relationships>");

        static string Inline(string reference, string text)
            => $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{text}</t></is></c>";

        static void Add(ZipArchive zip, string name, string content)
        {
            using var entry = zip.CreateEntry(name).Open();
            using var writer = new StreamWriter(entry, new UTF8Encoding(false));
            writer.Write("<?xml version=\"1.0\"?>" + content);
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
