using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsMasterDetailLayoutTests
{
    private static string Source(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WinFormsLayouts.Resolve(AppLayout.MasterDetail).ViewFiles(
                new LayoutContext("Payroll.WinForms", "Payroll.WinForms", settings,
                    ThemePalette.Resolve(settings.Theme, settings.Accent)))
            .Single(f => f.Name == "MainForm.cs").Source;
    }

    [TestMethod]
    public void KeepsTheTabsAndAddsAPropertyGridUnderTheRecords()
    {
        var source = Source();

        StringAssert.Contains(source, "new TabControl");
        StringAssert.Contains(source, "new PropertyGrid");
        StringAssert.Contains(source, "detail.SelectedObject = grid.CurrentRow?.DataBoundItem");
    }

    [TestMethod]
    public void GivesTheDetailRowItsOwnRowInThePaneLayout()
    {
        // A PropertyGrid built but never placed would satisfy the assertions above and show
        // nothing. Four rows, and the button and status rows shifted down to make room.
        var source = Source();

        StringAssert.Contains(source, "RowCount = 4,");
        StringAssert.Contains(source, "layout.Controls.Add(detail, 0, 1);");
        StringAssert.Contains(source, "layout.Controls.Add(buttons, 0, 2);");
        StringAssert.Contains(source, "layout.Controls.Add(status, 0, 3);");
    }

    [TestMethod]
    public void Compiles()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source()),
            ("ViewModels/MainViewModel.cs", ViewModelStubs.WinFormsHost),
            ("Auth/Session.cs", ViewModelStubs.WinFormsSession));
    }

    [TestMethod]
    public void CompilesWhenThemedAndIconed()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source(AppCustomization.Default with
            {
                Theme = AppTheme.Dark,
                IconPath = @"D:\brand\logo.ico",
            })),
            ("ViewModels/MainViewModel.cs", ViewModelStubs.WinFormsHost),
            ("Auth/Session.cs", ViewModelStubs.WinFormsSession));
    }

    [TestMethod]
    public void EscapesTheTitleForASourceLiteralNotForMarkup()
    {
        Assert.DoesNotContain("&amp;", Source(AppCustomization.Default with
        {
            Company = "Smith & Sons",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        }));
    }

    /// <summary>
    /// The PropertyGrid edits every public setter, so CreatedOn and ModifiedOn — which the store
    /// stamps on save, overwriting whatever was typed — were editable, as was Id. The grid now
    /// shows the record through a descriptor that marks those three read-only. Compiled and run
    /// here: it is plain System.ComponentModel, so its behaviour can be checked off Windows.
    /// </summary>
    [TestMethod]
    public void The_property_grid_shows_the_stamped_fields_read_only()
    {
        var source = "using System;\nusing System.ComponentModel;\nusing System.Linq;\nnamespace Acme;\n"
            + "public class Book { public int Id { get; set; } public string Title { get; set; } = \"\";"
            + " public DateTime CreatedOn { get; set; } public DateTime ModifiedOn { get; set; } }\n"
            + "public static class Host {\n"
            + "    public static ICustomTypeDescriptor Wrap(object item) => new StampsReadOnly(item);\n"
            + WinFormsPaneCode.StampsReadOnlyClass
            + "\n}\n";

        var result = CodeGenerationEngine.Core.Compiler.Compile(source);
        var errors = result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.IsEmpty(errors, string.Join("\n", errors));

        var assembly = System.Reflection.Assembly.Load(result.AssemblyBytes!);
        var book = Activator.CreateInstance(assembly.GetType("Acme.Book")!)!;
        var wrapped = (System.ComponentModel.ICustomTypeDescriptor)assembly.GetType("Acme.Host")!
            .GetMethod("Wrap")!.Invoke(null, [book])!;

        var properties = wrapped.GetProperties(null);
        Assert.IsTrue(properties["CreatedOn"]!.IsReadOnly, "CreatedOn is editable");
        Assert.IsTrue(properties["ModifiedOn"]!.IsReadOnly, "ModifiedOn is editable");
        Assert.IsTrue(properties["Id"]!.IsReadOnly, "Id is editable");
        Assert.IsFalse(properties["Title"]!.IsReadOnly, "Title became read-only");

        // An edit through the grid still lands on the record itself.
        properties["Title"]!.SetValue(wrapped.GetPropertyOwner(properties["Title"]), "Emma");
        Assert.AreEqual("Emma", book.GetType().GetProperty("Title")!.GetValue(book));
    }

    [TestMethod]
    public void The_grid_columns_keep_the_stamped_fields_read_only()
    {
        var source = Source();

        StringAssert.Contains(source, "detail.SelectedObject = grid.CurrentRow?.DataBoundItem is { } row ? new StampsReadOnly(row) : null");
        StringAssert.Contains(source, "if (IsStamped(column.DataPropertyName))");
    }

    [TestMethod]
    public void LeaksNoGeneratorImplementationDetails()
    {
        var source = Source();

        Assert.DoesNotContain("WinFormsPaneCode", source);
        Assert.DoesNotContain("WinFormsMainFormTemplate", source);
    }
}
