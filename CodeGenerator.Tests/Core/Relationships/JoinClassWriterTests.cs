using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>A many-to-many is a third class with two foreign keys. It is the only shape that
/// creates a file, so it is also the only one whose rollback has to delete one.</summary>
[TestClass]
public class JoinClassWriterTests
{
    [TestMethod]
    public void The_default_name_is_the_two_class_names_joined()
        => Assert.AreEqual("OrderProduct", JoinClassWriter.DefaultName("Order", "Product"));

    [TestMethod]
    public void The_join_class_carries_both_foreign_keys_and_both_references()
    {
        var root = NewRoot();
        WritePair(root);

        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsTrue(result.Ok, result.Message);

        var join = File.ReadAllText(Path.Combine(root, "OrderProduct.cs"));
        Assert.Contains("int OrderId", join);
        Assert.Contains("int ProductId", join);
        Assert.Contains("Order Order", join);
        Assert.Contains("Product Product", join);
    }

    [TestMethod]
    public void The_join_class_lands_in_the_namespace_of_the_classes_it_joins()
    {
        // RoslynClassGenerator defaults to `GeneratedCode`. A join class there cannot see Order
        // or Product in `Shop`, so the verification compile would fail on every many-to-many and
        // the feature would never work once - the failure would look like a compiler complaint
        // about the user's own classes rather than a namespace the tool chose for them.
        var root = NewRoot();
        WritePair(root);

        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsTrue(result.Ok, result.Message);
        Assert.Contains("namespace Shop", File.ReadAllText(Path.Combine(root, "OrderProduct.cs")));
    }

    [TestMethod]
    public void Both_parents_get_a_collection_of_the_join_class()
    {
        var root = NewRoot();
        WritePair(root);

        JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.Contains("List<OrderProduct> OrderProducts", File.ReadAllText(Path.Combine(root, "Order.cs")));
        Assert.Contains("List<OrderProduct> OrderProducts", File.ReadAllText(Path.Combine(root, "Product.cs")));
    }

    [TestMethod]
    public void A_name_already_taken_is_refused_before_anything_is_written()
    {
        var root = NewRoot();
        WritePair(root);
        Write(root, "OrderProduct.cs", "namespace Shop;\n\npublic class OrderProduct\n{\n    public int Id { get; set; }\n}\n");

        var before = Snapshot(root);

        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsFalse(result.Ok);
        Assert.Contains("OrderProduct", result.Message);
        CollectionAssert.AreEquivalent(before, Snapshot(root));
    }

    [TestMethod]
    public void A_class_that_is_not_in_the_workspace_is_refused_before_anything_is_written()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");

        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsFalse(result.Ok);
        Assert.Contains("Product", result.Message);
        Assert.IsFalse(File.Exists(Path.Combine(root, "OrderProduct.cs")));
    }

    [TestMethod]
    public void A_failed_join_deletes_the_file_it_created()
    {
        // Product is a static class, so the `List<OrderProduct> OrderProducts` written onto it is
        // CS0708. The join file is created FIRST, so this is the case that proves the rollback
        // deletes it rather than the one where nothing was ever written - a join class left on
        // disk after a refusal is a class the user did not ask for and was not told about.
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "Product.cs", "namespace Shop;\n\npublic static class Product\n{\n}\n");

        var before = Snapshot(root);

        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsFalse(result.Ok, "a join class that does not compile must not be reported as written");
        Assert.IsFalse(File.Exists(Path.Combine(root, "OrderProduct.cs")),
            "a join class that did not work out must not be left on disk");
        CollectionAssert.AreEquivalent(before, Snapshot(root));
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WritePair(string root)
    {
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "Product.cs", "namespace Shop;\n\npublic class Product\n{\n    public int Id { get; set; }\n}\n");
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);
}
