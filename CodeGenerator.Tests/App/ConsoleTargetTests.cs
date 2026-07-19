using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ConsoleTargetTests
{
    [TestMethod]
    public void Metadata_IsConsole()
    {
        var t = new ConsoleTarget();
        Assert.AreEqual("console", t.Key);
        Assert.AreEqual(".Console", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.Console, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_EmitsProgramAndMenuAtProjectRoot()
    {
        var files = new ConsoleTarget().FrontEndFiles("Shop", new[] { "Book", "Member" });
        var names = files.Select(f => f.RelativeFolder + "|" + f.Name).ToList();

        CollectionAssert.Contains(names, "|Program.cs");
        CollectionAssert.Contains(names, "|EntityMenu.cs");
    }

    [TestMethod]
    public void Program_RegistersOneMenuPerEntity()
    {
        var src = ConsoleProgramTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "new EntityMenu<Book>(new BookRepository())");
        StringAssert.Contains(src, "new EntityMenu<Member>(new MemberRepository())");
    }

    [TestMethod]
    public void Menu_IsGenericAndSkipsGeneratedFields()
    {
        var src = ConsoleMenuTemplate.Source("Shop");

        StringAssert.Contains(src, "namespace Shop;");
        StringAssert.Contains(src, "public sealed class EntityMenu<T>");
        StringAssert.Contains(src, "Repository<T>");
        StringAssert.Contains(src, "\"CreatedOn\"");   // audit fields excluded from prompts
    }
}
