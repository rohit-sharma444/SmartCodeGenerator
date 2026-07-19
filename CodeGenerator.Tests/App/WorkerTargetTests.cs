using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WorkerTargetTests
{
    [TestMethod]
    public void Metadata_IsWorker()
    {
        var t = new WorkerTarget();
        Assert.AreEqual("worker", t.Key);
        Assert.AreEqual(".Worker", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.WorkerService, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_EmitsWorkerAndProgram()
    {
        var files = new WorkerTarget().FrontEndFiles("Shop", new[] { "Book" });
        var names = files.Select(f => f.Name).ToList();
        CollectionAssert.Contains(names, "Worker.cs");
        CollectionAssert.Contains(names, "Program.cs");
    }

    [TestMethod]
    public void Worker_LogsEachEntityRepositoryCount()
    {
        var src = WorkerTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "namespace Shop;");
        StringAssert.Contains(src, ": BackgroundService");
        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "new BookRepository().GetAll().Count");
        StringAssert.Contains(src, "new MemberRepository().GetAll().Count");
    }

    [TestMethod]
    public void Program_RegistersTheWorker()
    {
        var src = WorkerProgramTemplate.Source("Shop");

        StringAssert.Contains(src, "using Shop;");
        StringAssert.Contains(src, "AddHostedService<Worker>()");
        StringAssert.Contains(src, ".Run();");
    }
}
