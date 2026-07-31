using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsTargetTests
{
    [TestMethod]
    public void Metadata_DescribesAWindowsFormsApp()
    {
        var target = new WinFormsTarget();

        Assert.AreEqual("winforms", target.Key);
        Assert.AreEqual(".WinForms", target.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.WinForms, target.ProjectType);
        StringAssert.Contains(target.Description, "Windows Forms");
    }

    [TestMethod]
    public void FrontEndFiles_EmitsTheSharedViewModelsPlusTheFormAndEntryPoint()
    {
        var files = new WinFormsTarget().FrontEndFiles("Payroll", new[] { "Employee" });
        var names = files.Select(f => f.RelativeFolder + "/" + f.Name).ToList();

        CollectionAssert.Contains(names, "Data/RelationshipResolver.cs");
        CollectionAssert.Contains(names, "ViewModels/EntityViewModel.cs");
        CollectionAssert.Contains(names, "ViewModels/MainViewModel.cs");
        CollectionAssert.Contains(names, "Views/MainForm.cs");
        CollectionAssert.Contains(names, "/Program.cs");

        // No XAML: this is the whole difference from WpfTarget's file list.
        Assert.IsFalse(names.Any(n => n.EndsWith(".xaml", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void FrontEndFiles_EmitsTheRelayCommandThatDoesNotNeedWpf()
    {
        var files = new WinFormsTarget().FrontEndFiles("Payroll", new[] { "Employee" });
        var relay = files.Single(f => f.Name == "RelayCommand.cs").Source;

        // CommandManager is in PresentationCore; a WinForms project has no reference to it.
        Assert.IsFalse(relay.Contains("CommandManager", StringComparison.Ordinal));
    }
}
