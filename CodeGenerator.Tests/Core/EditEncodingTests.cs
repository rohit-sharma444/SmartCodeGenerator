using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// An edit of the user's files keeps every byte it did not mean to change: files the edit never
/// touched are not rewritten, a file that is not UTF-8 is refused rather than decoded wrong, and a
/// UTF-16 file stays UTF-16 with its line ends.
/// </summary>
[TestClass]
public class EditEncodingTests
{
    private static readonly Encoding Windows1252 = CodePagesEncoding();

    private string _root = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "ee-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void A_refused_relationship_leaves_a_windows_1252_bystander_byte_for_byte()
    {
        // Customer is static, so the List<Invoice> written on it is CS0708 and Verify refuses.
        WriteBytes("Invoice.cs", Utf8Bom("namespace Shop;\r\n\r\npublic class Invoice\r\n{\r\n    public int Id { get; set; }\r\n}\r\n"));
        WriteBytes("Customer.cs", Utf8Bom("namespace Shop;\r\n\r\npublic static class Customer\r\n{\r\n}\r\n"));
        WriteBytes("Util.cs", Windows1252.GetBytes("// résumé\r\nnamespace Shop;\r\n\r\npublic class Util\r\n{\r\n}\r\n"));

        var before = Snapshot();

        var result = RelationshipWriter.Apply(
            _root, new RelationshipProposal("Invoice", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsFalse(result.Ok, result.Message);
        AssertSameBytes(before, Snapshot());
    }

    [TestMethod]
    public void A_relationship_on_a_windows_1252_owner_is_refused_and_nothing_changes()
    {
        WriteBytes("Invoice.cs", Windows1252.GetBytes("// café\r\nnamespace Shop;\r\n\r\npublic class Invoice\r\n{\r\n    public int Id { get; set; }\r\n}\r\n"));
        WriteBytes("Customer.cs", Utf8Bom("namespace Shop;\r\n\r\npublic class Customer\r\n{\r\n    public int Id { get; set; }\r\n}\r\n"));

        var before = Snapshot();

        var result = RelationshipWriter.Apply(
            _root, new RelationshipProposal("Invoice", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsFalse(result.Ok, result.Message);
        Assert.Contains("Invoice.cs is not UTF-8", result.Message);
        AssertSameBytes(before, Snapshot());
    }

    [TestMethod]
    public void A_refused_many_to_many_leaves_a_windows_1252_bystander_byte_for_byte()
    {
        WriteBytes("Order.cs", Utf8Bom("namespace Shop;\r\n\r\npublic class Order\r\n{\r\n    public int Id { get; set; }\r\n}\r\n"));
        WriteBytes("Product.cs", Utf8Bom("namespace Shop;\r\n\r\npublic static class Product\r\n{\r\n}\r\n"));
        WriteBytes("Util.cs", Windows1252.GetBytes("// résumé\r\nnamespace Shop;\r\n\r\npublic class Util\r\n{\r\n}\r\n"));

        var before = Snapshot();

        var result = RelationshipWriter.Apply(
            _root, new RelationshipProposal("Order", "Product", RelationshipKind.ManyToMany, "because"));

        Assert.IsFalse(result.Ok, result.Message);
        AssertSameBytes(before, Snapshot());
    }

    [TestMethod]
    public void Remove_property_on_a_windows_1252_file_is_refused_and_the_bytes_stay()
    {
        var bytes = Windows1252.GetBytes(
            "namespace Shop;\r\n\r\npublic class Note\r\n{\r\n    public int Old { get; set; }\r\n    public string Text { get; set; } = \"Café\";\r\n}\r\n");
        WriteBytes("Note.cs", bytes);

        var ok = ClassPropertyService.RemoveProperty(_root, "Note", "Old", out var message);

        Assert.IsFalse(ok, message);
        Assert.Contains("Note.cs is not UTF-8; save it as UTF-8 and try again. Nothing was changed.", message);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(_root, "Note.cs")));
    }

    [TestMethod]
    public void A_member_edit_on_a_utf16_file_keeps_utf16_with_its_bom_and_crlf()
    {
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var source = "namespace Shop;\r\n\r\npublic class Note\r\n{\r\n    public int Old { get; set; }\r\n    public int Keep { get; set; }\r\n}\r\n";
        WriteBytes("Note.cs", [.. utf16.GetPreamble(), .. utf16.GetBytes(source)]);

        var ok = ClassPropertyService.RemoveProperty(_root, "Note", "Old", out var message);
        Assert.IsTrue(ok, message);

        var after = File.ReadAllBytes(Path.Combine(_root, "Note.cs"));

        Assert.IsTrue(after.Length > 2 && after[0] == 0xFF && after[1] == 0xFE, "the UTF-16 LE byte-order mark is kept");
        var text = utf16.GetString(after, 2, after.Length - 2);
        Assert.DoesNotContain("Old", text);
        Assert.Contains("public int Keep { get; set; }\r\n", text);
        Assert.AreEqual(text.Count(c => c == '\n'), CountCrlf(text), "every line end is still CRLF");
    }

    [TestMethod]
    public void Add_property_on_a_utf16_file_keeps_utf16_with_its_bom_and_crlf()
    {
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var source = "namespace Shop;\r\n\r\npublic class Note\r\n{\r\n    public int Keep { get; set; }\r\n}\r\n";
        WriteBytes("Note.cs", [.. utf16.GetPreamble(), .. utf16.GetBytes(source)]);

        var ok = ClassPropertyService.AddProperty(_root, "Note", "Count", "int", out var message);
        Assert.IsTrue(ok, message);

        var after = File.ReadAllBytes(Path.Combine(_root, "Note.cs"));

        Assert.IsTrue(after.Length > 2 && after[0] == 0xFF && after[1] == 0xFE, "the UTF-16 LE byte-order mark is kept");
        var text = utf16.GetString(after, 2, after.Length - 2);
        Assert.Contains("public int Count { get; set; }", text);
        Assert.AreEqual(text.Count(c => c == '\n'), CountCrlf(text), "every line end is still CRLF");
    }

    private static int CountCrlf(string text)
    {
        var count = 0;
        for (var i = text.IndexOf("\r\n", StringComparison.Ordinal); i >= 0; i = text.IndexOf("\r\n", i + 2, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static byte[] Utf8Bom(string text) => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)];

    private void WriteBytes(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(_root, name), bytes);

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(_root, f), File.ReadAllBytes);

    private static void AssertSameBytes(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToList(), after.Keys.ToList(), "the same files are there");
        foreach (var (name, bytes) in before)
            CollectionAssert.AreEqual(bytes, after[name], $"{name} is byte for byte as it was");
    }

    private static Encoding CodePagesEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
