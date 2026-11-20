using System.Text;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Bug hunt 2, E2: a refused <c>rename class</c> had already renamed the declaration in
/// Customer.cs when Invoice.cs (Windows-1252) threw, and the message said "Nothing was changed.".
/// A refusal now leaves every file as it was.
/// </summary>
[TestClass]
[DoNotParallelize] // ValidationRegistry is a process-wide static.
public sealed class RenameClassAllOrNothingTests
{
    private const string Csproj = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    private string _workspace = string.Empty;
    private string App => Path.Combine(_workspace, "App");

    [TestInitialize]
    public void Setup()
    {
        ProjectManager.CurrentProject = null;
        _workspace = Path.Combine(Path.GetTempPath(), "cg_e2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(App);
        File.WriteAllText(Path.Combine(App, "App.csproj"), Csproj);
        File.WriteAllText(Path.Combine(App, "Customer.cs"),
            "namespace App;\n\npublic class Customer\n{\n    public string Name { get; set; } = \"\";\n}\n");
        File.WriteAllText(Path.Combine(App, "Order.cs"),
            "namespace App;\n\npublic class Order\n{\n    public Customer Buyer { get; set; } = new Customer();\n}\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private Dictionary<string, byte[]> Bytes() =>
        Directory.GetFiles(App, "*.cs").ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);

    private void AssertUnchanged(Dictionary<string, byte[]> before, string message)
    {
        var after = Bytes();
        CollectionAssert.AreEquivalent(before.Keys.ToList(), after.Keys.ToList(), message);
        foreach (var (name, bytes) in before)
            CollectionAssert.AreEqual(bytes, after[name], $"{name} was changed. {message}");
        Assert.IsFalse(File.Exists(Path.Combine(App, "Client.cs")), message);
    }

    [TestMethod]
    public void A_file_that_is_not_utf8_refuses_the_rename_before_anything_is_written()
    {
        File.WriteAllBytes(Path.Combine(App, "Invoice.cs"), Encoding.Latin1.GetBytes(
            "// café\nnamespace App;\n\npublic class Invoice\n{\n    public Customer? Payer { get; set; }\n}\n"));
        var before = Bytes();

        var ok = ClassRenameService.RenameClass(_workspace, "Customer", "Client", out var message);

        Assert.IsFalse(ok, message);
        StringAssert.Contains(message, "Invoice.cs");
        AssertUnchanged(before, message);
    }

    [TestMethod]
    public void A_move_that_fails_puts_every_file_back()
    {
        var before = Bytes();

        // Open for reading and writing by others but not for delete: the rewrite of Customer.cs
        // goes through, the move to Client.cs does not.
        string message;
        bool ok;
        using (new FileStream(Path.Combine(App, "Customer.cs"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            ok = ClassRenameService.RenameClass(_workspace, "Customer", "Client", out message);

        Assert.IsFalse(ok, message);
        StringAssert.Contains(message, "nothing was changed");
        AssertUnchanged(before, message);
    }
}
