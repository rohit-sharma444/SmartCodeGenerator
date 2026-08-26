using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>Writing a navigation property into a file that already exists: the attributes and
/// the usings they need, in both namespace styles.</summary>
[TestClass]
public class AttributedPropertyWriterTests
{
    [TestMethod]
    public void The_property_and_its_attributes_are_written()
    {
        var file = WriteFile("""
            namespace Shop;

            public class Order
            {
                public int Id { get; set; }
            }
            """);

        var ok = AttributedPropertyWriter.TryAdd(
            file, "Order", Navigation("Customer", "Customer"),
            [RelationshipConvention.JsonSerializationNamespace], out var error);

        Assert.IsTrue(ok, error);

        var code = File.ReadAllText(file);
        Assert.Contains("JsonIgnore", code);
        Assert.Contains("Browsable(false)", code);
        Assert.Contains("Customer Customer", code);
    }

    [TestMethod]
    public void The_usings_are_added_once_and_only_when_missing()
    {
        var file = WriteFile("""
            using System.Text.Json.Serialization;

            namespace Shop;

            public class Order
            {
                public int Id { get; set; }
            }
            """);

        AttributedPropertyWriter.TryAdd(
            file, "Order", Navigation("Customer", "Customer"),
            [RelationshipConvention.JsonSerializationNamespace,
             RelationshipConvention.ComponentModelNamespace],
            out var error);

        var code = File.ReadAllText(file);

        Assert.HasCount(1, Occurrences(code, "using System.Text.Json.Serialization;"), error);
        Assert.HasCount(1, Occurrences(code, "using System.ComponentModel;"));
    }

    [TestMethod]
    public void A_block_scoped_namespace_works_too()
    {
        var file = WriteFile("""
            namespace Shop
            {
                public class Order
                {
                    public int Id { get; set; }
                }
            }
            """);

        var ok = AttributedPropertyWriter.TryAdd(
            file, "Order", Navigation("Customer", "Customer"),
            [RelationshipConvention.ComponentModelNamespace], out var error);

        Assert.IsTrue(ok, error);
        Assert.Contains("using System.ComponentModel;", File.ReadAllText(file));
    }

    [TestMethod]
    public void A_class_that_is_not_there_is_reported_rather_than_written()
    {
        var file = WriteFile("public class Order { }");

        var ok = AttributedPropertyWriter.TryAdd(
            file, "Missing", Navigation("Customer", "Customer"), [], out var error);

        Assert.IsFalse(ok);
        Assert.Contains("Missing", error);
    }

    private static PropertyDefinition Navigation(string name, string type) =>
        new() { Name = name, Type = type, Attributes = { "JsonIgnore", "Browsable(false)" } };

    private static List<int> Occurrences(string text, string needle)
    {
        var found = new List<int>();
        var at = text.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            found.Add(at);
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return found;
    }

    private static string WriteFile(string code)
    {
        var directory = Path.Combine(Path.GetTempPath(), "apw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        var file = Path.Combine(directory, "Order.cs");
        File.WriteAllText(file, code);
        return file;
    }
}
