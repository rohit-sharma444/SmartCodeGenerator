using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WebApiTargetTests
{
    [TestMethod]
    public void Metadata_IsWebApi()
    {
        var t = new WebApiTarget();
        Assert.AreEqual("webapi", t.Key);
        Assert.AreEqual(".Api", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.WebApi, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_EmitsProgramAtProjectRoot()
    {
        var files = new WebApiTarget().FrontEndFiles("Shop", new[] { "Book" });
        Assert.IsTrue(files.Any(f => f.RelativeFolder == "" && f.Name == "Program.cs"));
    }

    [TestMethod]
    public void Program_MapsCrudEndpointsPerEntity()
    {
        var src = WebApiProgramTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "MapEntity<Book>(app, \"book\", new BookRepository());");
        StringAssert.Contains(src, "MapEntity<Member>(app, \"member\", new MemberRepository());");
        StringAssert.Contains(src, "app.MapGet(");
        StringAssert.Contains(src, "app.MapPost(");
        StringAssert.Contains(src, "app.MapPut(");
        StringAssert.Contains(src, "app.MapDelete(");
        StringAssert.Contains(src, "app.Run();");
    }
}
