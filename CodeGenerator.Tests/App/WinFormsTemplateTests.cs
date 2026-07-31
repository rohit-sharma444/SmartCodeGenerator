using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsTemplateTests
{
    [TestMethod]
    public void MainForm_LivesInTheAppsViewsNamespace()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Views;");
        StringAssert.Contains(source, "public sealed class MainForm : Form");
        StringAssert.Contains(source, "using Payroll.ViewModels;");
    }

    [TestMethod]
    public void MainForm_NamesNoEntity()
    {
        // The same property the WPF view has: one tab per IEntityViewModel, so the view is
        // identical whatever the discovered model turned out to be.
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "foreach (var entity in _model.Entities)");
        StringAssert.Contains(source, "new TabPage(entity.Title)");
    }

    [TestMethod]
    public void MainForm_RepublishesTheCollectionWhenItChanges()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        // DataGridView ignores INotifyCollectionChanged. Without this, Add puts a record in the
        // view model and nothing appears in the grid.
        StringAssert.Contains(source, "INotifyCollectionChanged");
        StringAssert.Contains(source, "binding.ResetBindings(false)");
    }

    [TestMethod]
    public void MainForm_WiresEveryCommandAndTheStatus()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "entity.AddCommand.Execute(null)");
        StringAssert.Contains(source, "entity.DeleteCommand.Execute(null)");
        StringAssert.Contains(source, "entity.SaveCommand.Execute(null)");
        StringAssert.Contains(source, "status.Text = entity.Status");
    }

    [TestMethod]
    public void MainForm_EnablesDeleteFromTheSelection()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        // There is no CommandManager here, so the button's Enabled state is the view's job.
        StringAssert.Contains(source, "entity.Selected = grid.CurrentRow?.DataBoundItem");
        StringAssert.Contains(source, "delete.Enabled = entity.DeleteCommand.CanExecute(null)");
    }

    [TestMethod]
    public void MainForm_KeepsIdReadOnlyAndDropsNavigationColumns()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "column.ReadOnly = true;");
        StringAssert.Contains(source, "grid.Columns.RemoveAt(i);");
        StringAssert.Contains(source, "DataGridViewComboBoxColumn");
    }

    [TestMethod]
    public void MainForm_RunsColumnRulesOnEveryDataBindingComplete()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        // DataBindingComplete fires more than once during ordinary startup, and WinForms'
        // AutoGenerateColumns machinery re-adds a removed navigation column on the later fire.
        // A one-shot latch here means the removed column silently comes back. The rules must run
        // on every fire.
        StringAssert.Contains(source, "grid.DataBindingComplete += (_, _) => ApplyColumnRules(grid, model);");
        Assert.DoesNotContain(
            "columnsAdjusted", source,
            "the one-shot latch must not reappear: it lets WinForms re-add a removed navigation column unchecked");
    }

    [TestMethod]
    public void MainForm_ColumnRulesAreIdempotent()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        // Because the rules now run on every DataBindingComplete, a combo column already built on
        // an earlier pass must be left alone rather than torn down and rebuilt each time.
        StringAssert.Contains(source, "if (column is DataGridViewComboBoxColumn)");
    }

    [TestMethod]
    public void Program_RunsTheMainFormOnAnStaThread()
    {
        var source = WinFormsProgramTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll;");
        StringAssert.Contains(source, "[STAThread]");
        StringAssert.Contains(source, "Application.Run(new MainForm())");
        StringAssert.Contains(source, "using Payroll.Views;");

        // ApplicationConfiguration.Initialize() is source-generated and only exists when the
        // csproj opts in. The explicit pair always compiles.
        StringAssert.Contains(source, "Application.EnableVisualStyles()");
    }
}
