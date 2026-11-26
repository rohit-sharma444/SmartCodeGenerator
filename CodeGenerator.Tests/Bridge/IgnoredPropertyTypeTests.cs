using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// An entry beside a built-in typed property that is not one itself.
/// </summary>
/// <remarks>
/// The primary regex matches built-in types and returns the moment one of them does, so anything
/// else in the list never reaches the fallback parser. It was dropped in silence, then reported.
/// A name and a type of the project (<c>Customer Customer</c>) is now read as a property, as add
/// property takes it - the handler checks the project declares the type - so what is reported is
/// an entry that is not a name and a type.
/// </remarks>
[TestClass]
public class IgnoredPropertyTypeTests
{
    [TestMethod]
    public void A_non_primitive_entry_beside_a_primitive_one_is_not_reported()
        => Assert.IsEmpty(CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Order with properties CustomerId int, Customer Customer"));

    [TestMethod]
    public void An_untyped_entry_beside_a_typed_one_is_reported()
    {
        var ignored = CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Order with properties CustomerId int, Notes");

        CollectionAssert.AreEqual(new[] { "Notes" }, ignored.ToArray());
    }

    [TestMethod]
    public void An_all_primitive_list_reports_nothing()
        => Assert.IsEmpty(CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Order with properties Reference string, Amount decimal"));

    [TestMethod]
    public void A_command_with_no_property_list_reports_nothing()
        => Assert.IsEmpty(CommandEntityExtractor.IgnoredPropertyEntries("list classes"));

    [TestMethod]
    public void A_list_with_no_types_at_all_reports_nothing()
    {
        // Nothing matched the primitive regex, so the fallback parser ran and read every entry.
        // Reporting these as ignored would name properties that were in fact created.
        Assert.IsEmpty(CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Order with properties reference, amount"));
    }

    [TestMethod]
    public void The_dropped_entry_travels_on_the_parsed_command()
    {
        // The half that matters: the extractor is in NaturalLanguageBridge and the handler that
        // speaks is in CodeGenerationEngine, which the bridge references and not the other way
        // round. So the list has to arrive on the command, or nothing can report it.
        var command = new ParsedCommand();

        CommandEntityExtractor.ExtractProperties(
            "create class Order with properties CustomerId int, Customer Customer, Notes", command);

        CollectionAssert.AreEqual(new[] { "CustomerId", "Customer" }, command.Properties.Select(p => p.Name).ToArray());
        Assert.AreEqual("Customer", command.Properties[1].Type);

        CollectionAssert.AreEqual(new[] { "Notes" }, command.IgnoredProperties);
    }

    [TestMethod]
    public void An_entry_containing_the_word_and_is_not_split_down_the_middle()
    {
        // The separator is `\band\b`, not `and`: a property called Brand would otherwise be cut
        // into "Br" and "d" and read as two things nobody typed.
        var command = new ParsedCommand();

        CommandEntityExtractor.ExtractProperties(
            "create class Product with properties Price decimal, Brand Brand", command);

        CollectionAssert.AreEqual(new[] { "Price", "Brand" }, command.Properties.Select(p => p.Name).ToArray());
        Assert.AreEqual("Brand", command.Properties[1].Type);
        Assert.IsEmpty(command.IgnoredProperties);
    }
}
