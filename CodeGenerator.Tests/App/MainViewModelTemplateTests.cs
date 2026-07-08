using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class MainViewModelTemplateTests
{
    [TestMethod]
    public void Build_ConstructsOneViewModelPerEntity()
    {
        var source = MainViewModelTemplate.Build("Payroll", new[] { "Employee", "Department" });

        StringAssert.Contains(source, "namespace Payroll.ViewModels");
        StringAssert.Contains(source, "using Payroll.Models;");
        StringAssert.Contains(source, "new EntityViewModel<Employee>(\"Employee\"),");
        StringAssert.Contains(source, "new EntityViewModel<Department>(\"Department\"),");
        StringAssert.Contains(source, "ObservableCollection<IEntityViewModel> Entities");
    }

    [TestMethod]
    public void Build_NamesEachEntityExactlyOnceAsATypeArgument()
    {
        var source = MainViewModelTemplate.Build("Payroll", new[] { "Employee" });

        var occurrences = source.Split("EntityViewModel<Employee>").Length - 1;
        Assert.AreEqual(1, occurrences);
    }

    [TestMethod]
    public void Build_WithNoEntities_StillCompilesToAnEmptyCollection()
    {
        var source = MainViewModelTemplate.Build("Payroll", Array.Empty<string>());

        StringAssert.Contains(source, "ObservableCollection<IEntityViewModel>");
        Assert.IsFalse(source.Contains("new EntityViewModel<"));
    }
}
