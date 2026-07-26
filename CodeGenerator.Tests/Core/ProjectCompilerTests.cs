using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class ProjectCompilerTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "cg_compile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string code) =>
        File.WriteAllText(Path.Combine(_workspace, name), code);

    [TestMethod]
    public void Reports_success_for_a_clean_workspace()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { public int Id { get; set; } } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsTrue(result.Success, result.Report());
        Assert.AreEqual(1, result.FileCount);
        Assert.IsEmpty(result.Errors);
    }

    // One compilation over all files, so a type declared in another file resolves.
    // Compiler.Compile(string) is one tree per call and would report CS0246 here.
    [TestMethod]
    public void Resolves_a_type_declared_in_another_file()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { public Address Home { get; set; } = new(); } }");
        Write("Address.cs", "namespace Shop { public class Address { public string City { get; set; } = \"\"; } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsTrue(result.Success, result.Report());
        Assert.AreEqual(2, result.FileCount);
    }

    [TestMethod]
    public void Reports_a_real_diagnostic_with_file_and_line()
    {
        Write("Broken.cs", "namespace Shop\n{\n    public class Broken { public int Id { get; set } }\n}");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsFalse(result.Success);
        Assert.IsNotEmpty(result.Errors);
        StringAssert.Contains(result.Errors[0], "Broken.cs");
        StringAssert.Contains(result.Errors[0], "(3,");
    }

    [TestMethod]
    public void Caps_the_reported_diagnostics_but_reports_the_total()
    {
        for (var i = 0; i < 30; i++)
            Write($"Broken{i}.cs", $"public class Broken{i} {{ public int Id {{ get; set }} }}");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsFalse(result.Success);
        Assert.HasCount(ProjectCompiler.MaxReportedErrors, result.Errors);
        Assert.IsGreaterThan(ProjectCompiler.MaxReportedErrors, result.TotalErrors,
            $"Expected more than {ProjectCompiler.MaxReportedErrors} errors, got {result.TotalErrors}.");
        StringAssert.Contains(result.Report(), result.TotalErrors.ToString());
    }

    [TestMethod]
    public void An_empty_workspace_succeeds_and_says_it_found_nothing()
    {
        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.FileCount);
        StringAssert.Contains(result.Report(), "No C# files");
    }

    [TestMethod]
    public void A_missing_root_fails_rather_than_claiming_success()
    {
        var result = ProjectCompiler.CompileWorkspace(Path.Combine(_workspace, "nope"));

        Assert.IsFalse(result.Success);
    }

    // bin/ and obj/ hold generated duplicates of the same types; compiling them
    // produces CS0101 noise that has nothing to do with the user's code.
    [TestMethod]
    public void Skips_bin_and_obj()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { } }");
        var obj = Path.Combine(_workspace, "obj");
        Directory.CreateDirectory(obj);
        File.WriteAllText(Path.Combine(obj, "Customer.cs"), "namespace Shop { public class Customer { } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.AreEqual(1, result.FileCount);
        Assert.IsTrue(result.Success, result.Report());
    }

    [TestMethod]
    public void Validate_project_is_recognized()
    {
        Assert.AreEqual(
            CodeGenerationEngine.Models.IntentType.ValidateProject,
            NaturalLanguageBridge.CommandFront.Parse("validate project", "").Intent);
    }

    // Every console/webapi/worker app `build app:` generates has a Program.cs using
    // top-level statements (see AppScaffolding/ConsoleProgramTemplate.cs). Compiling
    // top-level statements as a library is CS8805, which is an error but not the
    // user's defect -- it is an artifact of how ProjectCompiler chooses to compile.
    [TestMethod]
    public void A_top_level_statement_Program_does_not_report_a_spurious_error()
    {
        Write("Program.cs", "System.Console.WriteLine(\"hello\");");
        Write("Customer.cs", "namespace Shop { public class Customer { public int Id { get; set; } } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsTrue(result.Success, result.Report());
        Assert.AreEqual(2, result.FileCount);
    }

    // `delete class` moves the file to DeletedClasses/<Name>_<timestamp>.cs without
    // renaming the type inside it. Re-creating a class of that name must not collide
    // with its own deliberately-deleted predecessor as a CS0101 duplicate.
    [TestMethod]
    public void Skips_DeletedClasses()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { } }");
        var deleted = Path.Combine(_workspace, "DeletedClasses");
        Directory.CreateDirectory(deleted);
        File.WriteAllText(Path.Combine(deleted, "Customer_20260101120000.cs"),
            "namespace Shop { public class Customer { } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.AreEqual(1, result.FileCount);
        Assert.IsTrue(result.Success, result.Report());
    }
}
