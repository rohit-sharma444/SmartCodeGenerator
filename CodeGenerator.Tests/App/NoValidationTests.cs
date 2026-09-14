using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Skipping the Validation folder is not just skipping files: Repository and the
/// desktop view models both call ValidatorFactory.Create&lt;T&gt;(), so the emitted code must
/// change with them or the generated app compiles nowhere.</summary>
[TestClass]
public class NoValidationTests
{
    [TestMethod]
    public void The_repository_still_references_the_validator_by_default()
        => StringAssert.Contains(RepositoryTemplate.Base("Acme"), "ValidatorFactory.Create<T>()");

    [TestMethod]
    public void A_repository_without_validation_names_no_validator()
    {
        var source = RepositoryTemplate.Base("Acme", validation: false);

        Assert.IsFalse(source.Contains("ValidatorFactory", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IValidator", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Acme.Validation", StringComparison.Ordinal));
    }

    /// <summary>The point of the whole task: it has to COMPILE without the Validation folder.</summary>
    [TestMethod]
    public void A_repository_without_validation_compiles_without_the_validation_seams()
    {
        // SaveResult now names ValidationError from the Validation namespace even when the
        // repository it returns to never populates Failures - the type has to resolve, so the
        // files that declare it join the compile list. Neither carries a validator: Permissions
        // and ValidationContext survive novalidation on their own (spec Decision 4), which is
        // exactly why SaveResult can still reference them here. JsonStore now asks Permissions
        // before every write (Task 7's coarse store guard), so Permissions.cs joins the list too
        // -- it was already emitted unconditionally by AppScaffolder, this is just the first test
        // that needs it present to compile.
        var source = EmittedSource.Flatten(
            ValidationContextTemplate.Source("Acme"),
            ValidatorTemplate.EntityLookupInterface("Acme"),
            PermissionsTemplate.Source("Acme", Array.Empty<string>(), login: false),
            StoreSeamTemplate.Source("Acme"),
            SaveResultTemplate.Source("Acme"),
            StoreFactoryTemplate.Source("Acme", PersistenceKind.Json),
            JsonStoreTemplate.Source("Acme.Data"),
            RepositoryTemplate.Base("Acme", validation: false));

        EmittedSource.AssertCompiles(source);
    }

    /// <summary>Cutting the validation out must not cut the surface out with it: every caller
    /// still reads Success and Errors off the same SaveResult.</summary>
    [TestMethod]
    public void Create_and_update_still_return_a_save_result()
    {
        var source = RepositoryTemplate.Base("Acme", validation: false);

        StringAssert.Contains(source, "public SaveResult<T> Create(T item)");
        StringAssert.Contains(source, "public SaveResult<T> Update(T item)");

        // The result still carries an Errors collection, so every caller of Create/Update —
        // the console menu, the Web API's ValidationProblem — compiles unchanged. The message
        // itself now prefers the store's own refusal reason (Task 7's StoreWrite) and falls back
        // to this sentence only when the store gave none.
        StringAssert.Contains(source, "\"Could not persist \"");
    }

    [TestMethod]
    public void The_desktop_view_model_still_validates_by_default()
        => StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Acme"), "ValidatorFactory.Create<T>()");

    /// <summary>The second hard dependency: this Save path writes through the store rather than
    /// Repository, so it runs the validator itself.</summary>
    /// <remarks>
    /// It still names the Validation NAMESPACE, and must: its delete button asks the permission
    /// matrix, which survives novalidation on its own (spec Decision 4) — the same reason
    /// JsonStore names it. What has to go is the validator.
    /// </remarks>
    [TestMethod]
    public void A_desktop_view_model_without_validation_names_no_validator()
    {
        var source = ViewModelTemplates.EntityViewModel("Acme", validation: false);

        Assert.IsFalse(source.Contains("ValidatorFactory", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IValidator", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ValidationContext", StringComparison.Ordinal));

        // ...and still saves.
        StringAssert.Contains(source, "_store.SaveAll(Records.ToList())");
    }

    /// <summary>
    /// With validation on, both files have to come out byte-identical to what this generator
    /// produced before the flag existed — and the way that breaks is invisible in a diff: a
    /// fragment lifted out of a raw-string template and rebuilt with "\n" loses one CR per line on
    /// a CRLF checkout. Line endings that stay uniform within the file is the checkout-independent
    /// way to say it, since a raw literal carries whatever its own source file does.
    /// </summary>
    [TestMethod]
    public void The_validation_branch_leaves_the_line_endings_alone()
    {
        string[] sources =
        [
            RepositoryTemplate.Base("Acme"),
            RepositoryTemplate.Base("Acme", validation: false),
            ViewModelTemplates.EntityViewModel("Acme"),
            ViewModelTemplates.EntityViewModel("Acme", validation: false),
        ];

        foreach (var source in sources)
        {
            var newlines = source.Count(c => c == '\n');
            var carriageReturns = source.Count(c => c == '\r');

            Assert.IsTrue(
                carriageReturns == 0 || carriageReturns == newlines,
                $"mixed line endings: {newlines} newline(s), {carriageReturns} carriage return(s)");
        }
    }

    /// <summary>A whole generated app with validation off must build. This is the check that
    /// catches a call site nobody remembered — and, since Security sits outside the flag, the
    /// check that the Validation folder survives novalidation holding only what permissions need.
    /// </summary>
    [TestMethod]
    public void A_generated_app_without_validation_keeps_only_the_permission_matrix()
    {
        var root = Path.Combine(Path.GetTempPath(), "nov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with { Customization = AppCustomization.Default with { Validation = false } };

            var result = new AppScaffolder().Scaffold(request);

            Assert.IsTrue(result.Success, result.Message);

            // The Validation folder is not gone: Security sits outside the novalidation flag (spec
            // Decision 4), so turning validation off must not turn permissions off. It holds
            // exactly Permissions.cs, ValidationContext.cs (which Permissions names
            // ValidationOperation from) and IEntityLookup.cs (which ValidationContext<T> names) —
            // and nothing that only the validation pipeline needs.
            var validationFolder = Path.Combine(result.ProjectPath, "Validation");
            Assert.IsTrue(Directory.Exists(validationFolder));
            Assert.IsTrue(File.Exists(Path.Combine(validationFolder, "Permissions.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validationFolder, "ValidationContext.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validationFolder, "IEntityLookup.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "ValidatorFactory.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "OrderValidator.Generated.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "OrderValidator.Custom.cs")));

            Assert.IsFalse(
                File.ReadAllText(Path.Combine(result.ProjectPath, "Data", "Repository.cs"))
                    .Contains("Validator", StringComparison.Ordinal));

            // The companion test project's per-model validator tests would not compile either.
            Assert.IsNotNull(result.TestProjectPath);
            Assert.IsFalse(File.Exists(Path.Combine(
                Path.GetDirectoryName(result.TestProjectPath)!, "OrderValidatorTests.cs")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>The whole reason Security sits outside the flag: turning validation off must not
    /// turn permissions off. Asserted against the emitted file list, because this is the one
    /// guarantee a user cannot see until something is refused in a running app.</summary>
    [TestMethod]
    public void NoValidation_StillEmitsThePermissionMatrix()
    {
        var root = Path.Combine(Path.GetTempPath(), "nov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with { Customization = AppCustomization.Default with { Validation = false } };

            var result = new AppScaffolder().Scaffold(request);
            Assert.IsTrue(result.Success, result.Message);

            var files = Directory.GetFiles(result.ProjectPath, "*.cs", SearchOption.AllDirectories);

            Assert.IsTrue(files.Any(f => f.EndsWith("Permissions.cs", StringComparison.Ordinal)),
                "novalidation dropped the permission matrix");
            Assert.IsTrue(files.Any(f => f.EndsWith("ValidationContext.cs", StringComparison.Ordinal)),
                "Permissions names ValidationOperation, so the context must survive the flag too");

            Assert.IsFalse(files.Any(f => f.EndsWith("ValidationPipeline.cs", StringComparison.Ordinal)),
                "novalidation must still drop the pipeline");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>Validation stays on unless it is turned off: the default has to leave the
    /// Validation folder exactly where it has always been.</summary>
    [TestMethod]
    public void A_generated_app_keeps_its_validation_folder_by_default()
    {
        var root = Path.Combine(Path.GetTempPath(), "nov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(TestModels.OneEntity(), "Shop", root, new ConsoleTarget()));

            var validation = Path.Combine(result.ProjectPath, "Validation");

            Assert.IsTrue(File.Exists(Path.Combine(validation, "ValidatorFactory.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "OrderValidator.Generated.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "OrderValidator.Custom.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(
                Path.GetDirectoryName(result.TestProjectPath)!, "OrderValidatorTests.cs")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
