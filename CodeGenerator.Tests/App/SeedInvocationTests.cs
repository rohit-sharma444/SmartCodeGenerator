using System.Collections.Generic;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SeedInvocationTests
{
    private static readonly IReadOnlyList<string> Entities = new[] { "Customer", "Order" };

    [TestMethod]
    public void ConsoleProgram_SeedsBeforeTheMenuLoop()
    {
        var src = ConsoleProgramTemplate.Build("Shop", Entities);
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        // Seeds before building the repositories map.
        Assert.IsLessThan(src.IndexOf("var repositories"), src.IndexOf("SeedData.EnsureSeeded();"));
    }

    [TestMethod]
    public void WpfMainViewModel_SeedsBeforeTheViewModelsLoad()
    {
        var src = MainViewModelTemplate.Build("Shop", Entities);
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        // Must seed before the ObservableCollection is built (each EntityViewModel loads its store).
        Assert.IsLessThan(src.IndexOf("new ObservableCollection"), src.IndexOf("SeedData.EnsureSeeded();"));
    }

    [TestMethod]
    public void WebApiProgram_SeedsBeforeRun()
    {
        var src = WebApiProgramTemplate.Build("Shop", Entities);
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        Assert.IsLessThan(src.IndexOf("app.Run();"), src.IndexOf("SeedData.EnsureSeeded();"));
    }

    [TestMethod]
    public void WorkerProgram_SeedsBeforeRun_AndImportsDataNamespace()
    {
        var src = WorkerProgramTemplate.Source("Shop");
        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "SeedData.EnsureSeeded();");
        Assert.IsLessThan(src.IndexOf("builder.Build().Run();"), src.IndexOf("SeedData.EnsureSeeded();"));
    }
}
