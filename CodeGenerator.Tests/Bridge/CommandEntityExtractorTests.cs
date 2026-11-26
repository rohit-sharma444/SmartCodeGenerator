using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
[DoNotParallelize]
public class CommandEntityExtractorTests
{
    [TestMethod]
    public void Extracts_class_name_for_create_class()
    {
        var cmd = CommandEntityExtractor.Extract("create a class called Customer", IntentType.CreateClass, "");
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Extracts_rename_source_and_target_positionally()
    {
        var cmd = CommandEntityExtractor.Extract("rename class Foo to Bar", IntentType.RenameClass, "");
        Assert.AreEqual("Foo", cmd.ClassName);
        Assert.AreEqual("Bar", cmd.NewClassName);
    }

    [TestMethod]
    public void Carries_the_raw_sentence_as_provenance()
    {
        var cmd = CommandEntityExtractor.Extract("create a class called Customer", IntentType.CreateClass, "");
        Assert.AreEqual("create a class called Customer", cmd.OriginalSentence);
    }

    private static ParsedCommand CreateClass(string sentence) =>
        CommandEntityExtractor.Extract(sentence, IntentType.CreateClass, "");

    [TestMethod]
    public void And_add_method_to_same_class_is_a_method()
    {
        var cmd = CreateClass("create class Order with properties Total decimal and add method Ship to Order");
        CollectionAssert.AreEqual(new[] { "Total" }, cmd.Properties.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Ship" }, cmd.Methods.Select(m => m.Name).ToArray());
        Assert.IsEmpty(cmd.IgnoredProperties);
        Assert.IsEmpty(cmd.IgnoredMethods);
    }

    [TestMethod]
    public void And_add_method_without_class()
    {
        var cmd = CreateClass("create class Order with properties Name string and add method Ship");
        CollectionAssert.AreEqual(new[] { "Ship" }, cmd.Methods.Select(m => m.Name).ToArray());
        Assert.IsEmpty(cmd.IgnoredProperties);
        Assert.IsEmpty(cmd.IgnoredMethods);
    }

    [TestMethod]
    public void And_create_method_with_returns()
    {
        var cmd = CreateClass("create class Order with properties Total decimal and create method Total2 returns int to Order");
        Assert.HasCount(1, cmd.Methods);
        Assert.AreEqual("Total2", cmd.Methods[0].Name);
        Assert.AreEqual("int", cmd.Methods[0].ReturnType);
        Assert.IsEmpty(cmd.IgnoredProperties);
        Assert.IsEmpty(cmd.IgnoredMethods);
    }

    [TestMethod]
    public void And_add_method_to_other_class_is_refused()
    {
        var cmd = CreateClass("create class Order with properties Total decimal and add method Ship to Customer");
        Assert.IsEmpty(cmd.Methods);
        Assert.HasCount(1, cmd.IgnoredMethods);
        StringAssert.Contains(cmd.IgnoredMethods[0].Error, "Customer");
        StringAssert.Contains(cmd.IgnoredMethods[0].Error, "add method Ship to Customer");
    }

    [TestMethod]
    public void Unreadable_method_entry_is_reported()
    {
        var cmd = CreateClass("create class Order with methods Ship quickly now");
        Assert.IsEmpty(cmd.Methods);
        Assert.HasCount(1, cmd.IgnoredMethods);
        Assert.AreEqual("Ship quickly now", cmd.IgnoredMethods[0].Entry);
    }
    [TestMethod]
    [DataRow("Data byte[]", "Data", "byte[]")]
    [DataRow("Count short", "Count", "short")]
    [DataRow("Letter char", "Letter", "char")]
    [DataRow("Size uint?", "Size", "uint?")]
    [DataRow("Ids List<long>", "Ids", "List<long>")]
    public void Byte_array_property_is_read(string entry, string name, string type)
    {
        var cmd = CreateClass($"create class Note with properties Title string, {entry}");
        var property = cmd.Properties.SingleOrDefault(p => p.Name == name);
        Assert.IsNotNull(property, entry);
        Assert.AreEqual(type, property.Type, entry);
        Assert.IsEmpty(cmd.IgnoredProperties, entry);
    }

    [TestMethod]
    public void Project_type_entry_is_a_property()
    {
        var cmd = CreateClass("create class Customer with properties Name string, Kind Buyer, Home Shape");
        CollectionAssert.AreEqual(new[] { "Name", "Kind", "Home" }, cmd.Properties.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "string", "Buyer", "Shape" }, cmd.Properties.Select(p => p.Type).ToArray());
        Assert.IsEmpty(cmd.IgnoredProperties);
    }

    [TestMethod]
    public void Project_type_entry_keeps_wrappers_and_modifiers()
    {
        var cmd = CreateClass("create class Customer with properties Name string, Homes List<Shape>, Spare Shape?, Old Shape[], Main Shape required");
        CollectionAssert.AreEqual(new[] { "string", "List<Shape>", "Shape?", "Shape[]", "Shape" },
            cmd.Properties.Select(p => p.Type).ToArray());
        Assert.AreEqual(PropertyModifiers.Required, cmd.Properties[4].Modifiers);
        Assert.IsEmpty(cmd.IgnoredProperties);
    }

    [TestMethod]
    public void Dotnet_and_generic_types_are_properties()
    {
        var cmd = CreateClass("create class Note with properties Created DateTimeOffset, Map Dictionary<string, int>, Link System.Uri, Maybe Shape?[], Title string");
        CollectionAssert.AreEqual(new[] { "Created", "Map", "Link", "Maybe", "Title" }, cmd.Properties.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "DateTimeOffset", "Dictionary<string, int>", "System.Uri", "Shape?[]", "string" },
            cmd.Properties.Select(p => p.Type).ToArray());
        Assert.IsEmpty(cmd.IgnoredProperties);
    }

    [TestMethod]
    public void Type_first_entries_still_go_to_the_fallback()
    {
        var cmd = CreateClass("create class Note with properties int Id, Name");
        CollectionAssert.AreEqual(new[] { "Id", "Name" }, cmd.Properties.Select(p => p.Name).ToArray());
        Assert.AreEqual("int", cmd.Properties[0].Type);
    }

    [TestMethod]
    public void Untyped_list_still_infers()
    {
        var cmd = CreateClass("create class Customer with properties Name, Email");
        CollectionAssert.AreEqual(new[] { "Name", "Email" }, cmd.Properties.Select(p => p.Name).ToArray());
        Assert.AreEqual("string", cmd.Properties[0].Type);
        Assert.IsEmpty(cmd.IgnoredProperties);
    }
}
