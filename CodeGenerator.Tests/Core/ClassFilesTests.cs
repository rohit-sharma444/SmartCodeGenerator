using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>Which .cs files count as the workspace's classes. Build output and the recycle
/// bin are skipped, or a stale copy under obj wins the race against the source.</summary>
[TestClass]
public class ClassFilesTests
{
    [TestMethod]
    public void Source_files_are_listed()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { }");

        var files = ClassFiles.Under(root);

        Assert.HasCount(1, files);
        Assert.AreEqual("Customer.cs", Path.GetFileName(files[0]));
    }

    [TestMethod]
    public void Build_output_and_deleted_classes_are_skipped()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { }");
        Write(Path.Combine(root, "obj"), "Customer.cs", "public class Customer { }");
        Write(Path.Combine(root, "bin"), "Customer.cs", "public class Customer { }");
        Write(Path.Combine(root, "DeletedClasses"), "Order.cs", "public class Order { }");

        var files = ClassFiles.Under(root);

        Assert.HasCount(1, files);
        Assert.AreEqual("Customer.cs", Path.GetFileName(files[0]));
    }

    [TestMethod]
    public void A_missing_root_lists_nothing()
        => Assert.IsEmpty(ClassFiles.Under(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid())));

    [TestMethod]
    public void FileOf_matches_the_file_named_after_the_class()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { }");
        Write(root, "Order.cs", "public class Order { }");

        Assert.AreEqual("Order.cs", Path.GetFileName(ClassFiles.FileOf(root, "Order")!));
    }

    [TestMethod]
    public void FileOf_returns_null_when_there_is_no_such_class()
        => Assert.IsNull(ClassFiles.FileOf(NewRoot(), "Nothing"));

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), code);
    }
}
