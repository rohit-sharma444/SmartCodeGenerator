using System.Collections;
using System.Reflection;
using System.Text.Json;
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using CodeGenerator.Tests.Helper;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ViewModelTemplatesTests
{
    [TestMethod]
    public void AllTemplates_LiveInTheAppsViewModelsNamespace()
    {
        foreach (var source in new[]
        {
            ViewModelTemplates.ObservableObject("Payroll"),
            ViewModelTemplates.RelayCommand("Payroll"),
            ViewModelTemplates.EntityViewModelInterface("Payroll"),
            ViewModelTemplates.EntityViewModel("Payroll"),
        })
        {
            StringAssert.Contains(source, "namespace Payroll.ViewModels");
        }
    }

    [TestMethod]
    public void Interface_DeclaresEverythingTheXamlBindsTo()
    {
        var source = ViewModelTemplates.EntityViewModelInterface("Payroll");

        StringAssert.Contains(source, "interface IEntityViewModel");
        StringAssert.Contains(source, "string Title { get; }");
        StringAssert.Contains(source, "IEnumerable Items { get; }");
        StringAssert.Contains(source, "object? Selected { get; set; }");
        StringAssert.Contains(source, "ICommand AddCommand { get; }");
        StringAssert.Contains(source, "ICommand DeleteCommand { get; }");
        StringAssert.Contains(source, "ICommand SaveCommand { get; }");
        StringAssert.Contains(source, "string Status { get; }");
    }

    [TestMethod]
    public void EntityViewModel_ExposesRecordsAsTypedCollection()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        // DataGrid reads its columns from IEnumerable<T>'s type argument, so the
        // backing collection must stay generic or empty tabs lose their columns.
        StringAssert.Contains(source, "public ObservableCollection<T> Records { get; }");
        StringAssert.Contains(source, "public IEnumerable Items => Records;");
    }

    [TestMethod]
    public void EntityViewModel_UsesTheStoreForIdsAndPersistence()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(source, "using Payroll.Data;");
        StringAssert.Contains(source, "_store.NewItem(Records)");
        StringAssert.Contains(source, "_store.SaveAll(Records.ToList())");
    }

    [TestMethod]
    public void EntityViewModel_DisablesDeleteWithoutASelection()
    {
        var source = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(source, "new RelayCommand(Delete, () => _selected is T)");
    }

    [TestMethod]
    public void RelayCommand_RequeriesOnUiActivity()
    {
        var source = ViewModelTemplates.RelayCommand("Payroll");

        // Without CommandManager the Delete button never re-enables on selection change.
        StringAssert.Contains(source, "CommandManager.RequerySuggested");
    }

    [TestMethod]
    public void EntityViewModel_ValidatesBeforeSaving()
    {
        var src = ViewModelTemplates.EntityViewModel("Payroll");

        // This path writes through _store, not Repository, so repository-level enforcement never
        // reaches it: it has to resolve and run the generated validator itself.
        StringAssert.Contains(src, "using Payroll.Validation;");
        StringAssert.Contains(src, "ValidatorFactory.Create<T>()");
        StringAssert.Contains(src, "Cannot save:");
    }

    /// <summary>
    /// The Stored argument: what is on disk right now, not `default`. A stage that compares
    /// against the pre-edit value (a status transition, an immutable field) has nothing to
    /// compare against otherwise -- issue #135, the same shape of bug validation-phase-5's task 8
    /// found and fixed for RecordEditorTemplate/BlazorScreenTemplates (commit 8a923cf), but never
    /// fixed here: this screen binds Records directly, so by Save time data binding has already
    /// mutated the very instance in place, and there is no captured-at-load snapshot to thread
    /// through the way the other two editors have -- Stored has to be re-read from disk instead.
    /// </summary>
    [TestMethod]
    public void EntityViewModel_ReadsStoredFromDisk_NotDefault()
    {
        var src = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(src, "var onDisk = _store.GetAll();");
        StringAssert.Contains(src, "var stored = onDisk.FirstOrDefault(existing => GetId(existing) == GetId(record));");
        StringAssert.Contains(
            src,
            "record!, ValidationOperation.Update, stored, new StoreEntityLookup(),");
        Assert.DoesNotContain(
            "record!, ValidationOperation.Update, default, new StoreEntityLookup(),", src);
    }

    [TestMethod]
    public void EntityViewModel_constrains_T_to_a_reference_type()
    {
        // ValidatorFactory.Create<T>() is declared `where T : class, new()`; without the class
        // constraint here the generated WPF app does not compile (CS0452).
        StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Payroll"),
            "EntityViewModel<T> : ObservableObject, IEntityViewModel where T : class, new()");
    }

    [TestMethod]
    public void PortableRelayCommand_DoesNotDependOnWpf()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // CommandManager lives in PresentationCore. A WinForms project has no reference to it,
        // so its presence here is a compile error in the generated app, not a style question.
        Assert.IsFalse(
            source.Contains("CommandManager", StringComparison.Ordinal),
            "the portable RelayCommand must not reference CommandManager");
    }

    [TestMethod]
    public void PortableRelayCommand_IsTheSameClassUnderTheSameNamespace()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // EntityViewModel is shared between both desktop targets and says `new RelayCommand(...)`,
        // so the type it binds to must have the same name and namespace in either variant.
        StringAssert.Contains(source, "namespace Payroll.ViewModels;");
        StringAssert.Contains(source, "public sealed class RelayCommand : ICommand");
        StringAssert.Contains(source, "public bool CanExecute(object? parameter)");
        StringAssert.Contains(source, "public void Execute(object? parameter)");
    }

    [TestMethod]
    public void PortableRelayCommand_LetsTheCallerRaiseCanExecuteChanged()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // The generated WinForms view never calls this (it polls CanExecute directly instead),
        // but the member must still exist and be invocable: it is the seam hand-written code
        // uses to signal a change itself.
        StringAssert.Contains(source, "public void RaiseCanExecuteChanged()");
        StringAssert.Contains(source, "CanExecuteChanged?.Invoke(this, EventArgs.Empty)");
    }

    [TestMethod]
    public void The_plain_grid_refuses_a_delete_the_pipeline_refuses()
    {
        // Same call every other test in this file makes — the template's signature is not what
        // this test is about, so the argument list is copied, not invented.
        var source = ViewModelTemplates.EntityViewModel("Payroll");
        var normalized = EmittedLines.Normalized(source);

        // Compiled but never executed as a unit test before this. Its twin on the business-layout
        // grid is MasterScreenTests.A_plain_user_cannot_delete_a_record.
        StringAssert.Contains(normalized, "if (!outcome.IsValid)");
        StringAssert.Contains(normalized, "Status = outcome.Messages[0];");
        Assert.AreEqual(1, EmittedLines.Occurrences(normalized, "Records.Remove(item);"));
    }

    /// <summary>
    /// The grid passes what is ACTUALLY on disk to the validator, over emitted code that is
    /// compiled and RUN -- not the same bound instance the grid has already edited in place.
    /// </summary>
    /// <remarks>
    /// A Workflow rule is the sharpest possible probe for this: it reads nothing BUT
    /// <c>context.Stored</c>, so if this screen ever again built that context with <c>default</c>
    /// -- issue #135, the bug validation-phase-5's task 8 found and fixed for the two per-record
    /// editors but not for this per-list screen -- this is the one assertion that would have
    /// failed for it: the backward move would go through as "Saved" and the store would show the
    /// illegal value, instead of being refused with the store left untouched.
    /// <para>
    /// Unlike <see cref="FieldHighlightTests"/>'s twin test, this drives a REAL, compiled
    /// <c>JsonStore&lt;T&gt;</c> -- EntityViewModel has no store seam to substitute a fixture for
    /// (it <c>new()</c>s <c>JsonStore&lt;T&gt;</c> directly) -- so the on-disk record is seeded by
    /// writing the same JSON file <c>JsonStore&lt;T&gt;</c> itself would, and cleaned up in a
    /// <c>finally</c>. The entity name is GUID-suffixed so its file can never collide with
    /// another test's, following <see cref="EntityLookupCountingTests"/>'s established pattern
    /// for the same seam.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EntityViewModel_RefusesAnIllegalStatusMove_UsingWhatIsActuallyOnDisk()
    {
        var entityName = $"Entity{Guid.NewGuid():N}"[..16];

        var definition = new ClassDefinition { Name = entityName };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var workflowRule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());

        var model = $$"""
namespace Acme.Models;

public class {{entityName}}
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
}
""";

        var files = new (string Path, string Code)[]
        {
            ("Models/Entity.cs", model),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
            ("Screens/EntityRole.cs", ScreenTemplates.RoleEnum("Acme")),
            ("ViewModels/ObservableObject.cs", ViewModelTemplates.ObservableObject("Acme")),
            ("ViewModels/RelayCommand.cs", ViewModelTemplates.PortableRelayCommand("Acme")),
            ("ViewModels/IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface("Acme")),
            ("ViewModels/EntityViewModel.cs", ViewModelTemplates.EntityViewModel("Acme", validation: true)),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [entityName], login: false)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: false)),
            ("Validation/ValidatorFactory.cs", ValidatorTemplate.Factory("Acme", [entityName])),
            ($"Validation/{entityName}Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    "Acme", definition, [], referencesRequired: false, workflowRules: [workflowRule])),
            ($"Validation/{entityName}Validator.Custom.cs", ValidatorTemplate.CustomStub("Acme", entityName)),
        };

        var result = Compiler.CompileSources(files);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.IsEmpty(errors, "Compilation errors:\n" + string.Join("\n", errors));
        Assert.IsTrue(result.Success);

        var assembly = Assembly.Load(result.AssemblyBytes!);
        var entityType = assembly.GetType($"Acme.Models.{entityName}")!;
        var vmType = assembly.GetType("Acme.ViewModels.EntityViewModel`1")!.MakeGenericType(entityType);
        var roleType = assembly.GetType("Acme.Screens.EntityRole")!;

        var path = Path.Combine(AppContext.BaseDirectory, entityType.Name + ".json");
        try
        {
            // The record on disk, Approved -- the pre-edit value the Workflow stage has to see.
            var listType = typeof(List<>).MakeGenericType(entityType);
            var stored = Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Id")!.SetValue(stored, 1);
            entityType.GetProperty("Status")!.SetValue(stored, "Approved");
            var seeded = (IList)Activator.CreateInstance(listType)!;
            seeded.Add(stored);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(seeded, listType, new JsonSerializerOptions { WriteIndented = true }));

            var vm = Activator.CreateInstance(
                vmType, [entityName, Enum.Parse(roleType, "Master")])!;

            // Records is loaded from disk at construction -- mutate the SAME bound instance, the
            // way WPF/WinForms data binding would, rather than replacing it with a new one. There
            // is no separate Draft to edit: this is exactly why Stored cannot be captured once at
            // load the way the two per-record editors do it.
            var records = (IList)vmType.GetProperty("Records")!.GetValue(vm)!;
            entityType.GetProperty("Status")!.SetValue(records[0], "Draft");

            RecordEditorTests.Execute(vm, "SaveCommand");

            StringAssert.Contains(
                (string)vmType.GetProperty("Status")!.GetValue(vm)!,
                "cannot move backward",
                "the illegal move was not refused -- Stored reached the validator as something " +
                "other than what is on disk");

            var onDisk = (IList)JsonSerializer.Deserialize(File.ReadAllText(path), listType)!;
            Assert.AreEqual(
                "Approved",
                (string)entityType.GetProperty("Status")!.GetValue(onDisk[0])!,
                "the refused save was written to the store anyway");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
