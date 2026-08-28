using CodeGenerationEngine.Core.Validation;
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The page takes a file path in its query string. Everything about that path is checked before
/// anything is written, because loopback is reachable by every process on this machine and the
/// token is the only other thing standing in front of a file write.
/// </summary>
[TestClass]
public class ValidationTargetTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void MakeRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "vt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string File_(string name)
    {
        var path = Path.Combine(_root, name);
        System.IO.File.WriteAllText(path, "namespace A { public class B { } }");
        return path;
    }

    [TestMethod]
    public void Resolves_a_class_file_under_the_workspace()
    {
        var file = File_("Employee.cs");

        Assert.IsTrue(ValidationTargetReader.TryResolve(
            "Employee", file, _root, out var target, out var problem), problem);

        Assert.AreEqual("Employee", target!.ClassName);
        Assert.AreEqual(Path.GetFullPath(file), target.File);
    }

    [TestMethod]
    public void Refuses_a_file_outside_the_workspace()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N")[..8] + ".cs");
        System.IO.File.WriteAllText(outside, "namespace A { public class B { } }");

        try
        {
            Assert.IsFalse(ValidationTargetReader.TryResolve(
                "B", outside, _root, out _, out var problem));
            StringAssert.Contains(problem, "outside");
        }
        finally
        {
            System.IO.File.Delete(outside);
        }
    }

    [TestMethod]
    public void Refuses_a_traversal_out_of_the_workspace()
    {
        var sneaky = Path.Combine(_root, "..", "elsewhere.cs");

        Assert.IsFalse(ValidationTargetReader.TryResolve("X", sneaky, _root, out _, out _));
    }

    [TestMethod]
    public void Refuses_a_file_that_is_not_csharp()
    {
        var path = Path.Combine(_root, "notes.txt");
        System.IO.File.WriteAllText(path, "hello");

        Assert.IsFalse(ValidationTargetReader.TryResolve("notes", path, _root, out _, out var problem));
        StringAssert.Contains(problem, ".cs");
    }

    [TestMethod]
    public void Refuses_a_file_that_is_not_there()
    {
        Assert.IsFalse(ValidationTargetReader.TryResolve(
            "Ghost", Path.Combine(_root, "Ghost.cs"), _root, out _, out _));
    }

    [TestMethod]
    [DataRow(null, "Employee.cs")]
    [DataRow("Employee", null)]
    [DataRow("", "")]
    public void Refuses_a_missing_half(string? className, string? file)
    {
        Assert.IsFalse(ValidationTargetReader.TryResolve(
            className, file is null ? null : Path.Combine(_root, file), _root, out _, out _));
    }

    [TestMethod]
    public void A_box_of_text_splits_into_one_fragment_per_line()
    {
        var fragments = ValidationRuleCatalog.Fragments(
            "Age must be at least 18\r\nName is required; Email must be a valid email\r\n");

        Assert.HasCount(3, fragments);
        Assert.AreEqual("Age must be at least 18", fragments[0]);
        Assert.AreEqual("Name is required", fragments[1]);
        Assert.AreEqual("Email must be a valid email", fragments[2]);
    }
}
