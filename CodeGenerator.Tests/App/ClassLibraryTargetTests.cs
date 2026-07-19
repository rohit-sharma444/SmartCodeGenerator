using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ClassLibraryTargetTests
{
    [TestMethod]
    public void Metadata_IsClassLibrary()
    {
        var t = new ClassLibraryTarget();
        Assert.AreEqual("classlib", t.Key);
        Assert.AreEqual(".Lib", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.ClassLibrary, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_IsEmpty_NoEntryPoint()
    {
        var files = new ClassLibraryTarget().FrontEndFiles("Shop", new[] { "Book" });
        Assert.HasCount(0, files);
    }

    [TestMethod]
    public void FixupProject_DeletesTheTemplateClass1()
    {
        var folder = Path.Combine(Path.GetTempPath(), "clib_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var class1 = Path.Combine(folder, "Class1.cs");
            File.WriteAllText(class1, "public class Class1 { }");

            new ClassLibraryTarget().FixupProject(folder);

            Assert.IsFalse(File.Exists(class1), "Class1.cs should have been deleted");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
