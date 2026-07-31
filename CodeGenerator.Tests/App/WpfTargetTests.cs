using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfTargetTests
{
    [TestMethod]
    public void Metadata_MatchesTheExistingWpfDefaults()
    {
        var target = new WpfTarget();

        Assert.AreEqual("wpf", target.Key);
        Assert.AreEqual(string.Empty, target.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.Wpf, target.ProjectType);
        StringAssert.Contains(target.Description, "WPF");
    }

    [TestMethod]
    public void FrontEndFiles_EmitsTheRelationshipResolverViewModelsAndViews()
    {
        var files = new WpfTarget().FrontEndFiles("Payroll", new[] { "Employee", "Department" });
        var names = files.Select(f => f.RelativeFolder + "/" + f.Name).ToList();

        CollectionAssert.Contains(names, "Data/RelationshipResolver.cs");
        CollectionAssert.Contains(names, "ViewModels/ObservableObject.cs");
        CollectionAssert.Contains(names, "ViewModels/RelayCommand.cs");
        CollectionAssert.Contains(names, "ViewModels/IEntityViewModel.cs");
        CollectionAssert.Contains(names, "ViewModels/EntityViewModel.cs");
        CollectionAssert.Contains(names, "ViewModels/MainViewModel.cs");
        CollectionAssert.Contains(names, "Views/MainWindow.xaml");
        CollectionAssert.Contains(names, "Views/MainWindow.xaml.cs");

        var mainVm = files.Single(f => f.Name == "MainViewModel.cs").Source;
        StringAssert.Contains(mainVm, "new EntityViewModel<Employee>(\"Employee\"),");
        StringAssert.Contains(mainVm, "new EntityViewModel<Department>(\"Department\"),");

        // This is the silent-failure direction: if WpfTarget ever passed the portable
        // RelayCommand instead, the generated app would still compile (ICommand is available
        // either way) and every other assertion here would stay green, but the shipping WPF
        // app's Delete button would never re-enable when the grid selection changes.
        var relayCommand = files.Single(f => f.Name == "RelayCommand.cs").Source;
        StringAssert.Contains(relayCommand, "CommandManager.RequerySuggested");
    }
}
