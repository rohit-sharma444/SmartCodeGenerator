using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// A property whose type is not one of the primitives the extractor knows.
/// </summary>
/// <remarks>
/// The primary regex matches primitives and returns the moment one of them does, so anything else
/// in the list never reaches the fallback parser. It was dropped in silence. It is still dropped —
/// emitting a property typed as another class fails the isolated per-class compile — but the user
/// is told, and told where it is handled instead.
/// </remarks>
[TestClass]
public class IgnoredPropertyTypeTests
{
    [TestMethod]
    public void A_non_primitive_entry_beside_a_primitive_one_is_reported()
    {
        var ignored = CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Order with properties CustomerId int, Customer Customer");

        Assert.HasCount(1, ignored);
        Assert.Contains("Customer", ignored[0]);
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
            "create class Order with properties CustomerId int, Customer Customer", command);

        Assert.HasCount(1, command.Properties);
        Assert.AreEqual("CustomerId", command.Properties[0].Name);

        Assert.HasCount(1, command.IgnoredProperties);
        Assert.Contains("Customer", command.IgnoredProperties[0]);
    }

    [TestMethod]
    public void An_entry_containing_the_word_and_is_not_split_down_the_middle()
    {
        // The separator is `\band\b`, not `and`: a property called Brand would otherwise be cut
        // into "Br" and "d" and reported as two things nobody typed.
        var ignored = CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Product with properties Price decimal, Brand Brand");

        Assert.HasCount(1, ignored);
        Assert.AreEqual("Brand Brand", ignored[0]);
    }
}
