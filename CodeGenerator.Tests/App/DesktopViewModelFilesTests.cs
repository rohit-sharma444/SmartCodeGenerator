using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DesktopViewModelFilesTests
{
    [TestMethod]
    public void Build_EmitsTheSixSharedFiles()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee" }, ViewModelTemplates.RelayCommand("Payroll"));

        var names = files.Select(f => f.RelativeFolder + "/" + f.Name).ToList();

        CollectionAssert.AreEqual(
            new[]
            {
                "Data/RelationshipResolver.cs",
                "ViewModels/ObservableObject.cs",
                "ViewModels/RelayCommand.cs",
                "ViewModels/IEntityViewModel.cs",
                "ViewModels/EntityViewModel.cs",
                "ViewModels/MainViewModel.cs",
            },
            names);
    }

    [TestMethod]
    public void Build_EmitsTheRelayCommandItWasGiven()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee" }, ViewModelTemplates.PortableRelayCommand("Payroll"));

        var relay = files.Single(f => f.Name == "RelayCommand.cs").Source;

        // The whole point of the parameter: WinForms must not receive the WPF variant.
        StringAssert.Contains(relay, "public void RaiseCanExecuteChanged()");
        Assert.IsFalse(relay.Contains("CommandManager", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Build_ReturnsAListTheCallerCanAppendViewsTo()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee" }, ViewModelTemplates.RelayCommand("Payroll"));

        files.Add(new GeneratedFile("Views", "Anything.cs", "// x"));

        Assert.HasCount(7, files);
    }

    [TestMethod]
    public void Build_RegistersEveryEntityWithTheMainViewModel()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee", "Department" },
            ViewModelTemplates.RelayCommand("Payroll"));

        var mainVm = files.Single(f => f.Name == "MainViewModel.cs").Source;

        StringAssert.Contains(mainVm, "new EntityViewModel<Employee>(\"Employee\"),");
        StringAssert.Contains(mainVm, "new EntityViewModel<Department>(\"Department\"),");
    }
}
