using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>delete class</c> refuses a class that other files of its project need.
/// </summary>
/// <remarks>
/// "delete class Person" with a Customer that inherits Person answered "Class 'Person' deleted.",
/// and the project then failed with CS0246. Nothing said that Customer needed it.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class DeleteUsedClassTests
{
    private string _workspace = string.Empty;
    private string Shop => Path.Combine(_workspace, "Shop");

    [TestInitialize]
    public void Setup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "cg_duc_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Shop);
        File.WriteAllText(Path.Combine(Shop, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("Person.cs", "namespace Shop;\n\npublic class Person\n{\n    public string Name { get; set; } = string.Empty;\n}\n");
        Write("Tag.cs", "namespace Shop;\n\npublic class Tag\n{\n    public string Text { get; set; } = string.Empty;\n}\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(Shop, name), text);

    [TestMethod]
    public void A_base_class_is_not_deleted_and_its_users_are_named()
    {
        Write("Customer.cs", "namespace Shop;\n\npublic class Customer : Person\n{\n}\n");
        Write("Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public Person? Buyer { get; set; }\n}\n");

        var (deleted, message) = ClassDeletionService.DeleteClass(_workspace, "Person");

        Assert.IsFalse(deleted, message);
        StringAssert.Contains(message, "Customer.cs");
        StringAssert.Contains(message, "Order.cs");
        Assert.IsTrue(File.Exists(Path.Combine(Shop, "Person.cs")), message);
    }

    [TestMethod]
    public void A_class_nothing_uses_is_deleted()
    {
        Write("Customer.cs", "namespace Shop;\n\npublic class Customer : Person\n{\n}\n");

        var (deleted, message) = ClassDeletionService.DeleteClass(_workspace, "Tag");

        Assert.IsTrue(deleted, message);
        Assert.IsFalse(File.Exists(Path.Combine(Shop, "Tag.cs")), message);
    }

    // An error the project already had is not the delete's doing, and does not block it.
    [TestMethod]
    public void An_error_already_in_the_project_does_not_block_the_delete()
    {
        Write("Broken.cs", "namespace Shop;\n\npublic class Broken : Missing\n{\n}\n");

        var (deleted, message) = ClassDeletionService.DeleteClass(_workspace, "Tag");

        Assert.IsTrue(deleted, message);
    }
}
