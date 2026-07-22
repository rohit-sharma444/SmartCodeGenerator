using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core.Validation;

/// <summary>
/// Drives <c>add validation</c> from the raw sentence, the way the CLI does:
/// <see cref="CommandFront.Parse"/> composes the rule fragment, and only then does
/// the catalog get a chance to parse it. The older tests hand-built an already
/// composed <c>ValidationText</c>, which is why every data rule except
/// <c>required</c> could be unreachable end-to-end while its own unit tests passed.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AddValidationPhrasingTests
{
    [TestInitialize]
    public void Reset()
    {
        ValidationRegistry.Clear();
        LearnedIntentStore.UseInMemory();
    }

    private static bool Run(string sentence)
    {
        var command = CommandFront.Parse(sentence, "");
        ProjectMetaData? project = null;
        return CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });
    }

    private static void AssertRegistered(string sentence, string className, string expectedDescription)
    {
        Assert.IsTrue(Run(sentence), $"Command failed: {sentence}");

        var rules = ValidationRegistry.Get(className, ValidationCategory.Data);
        Assert.HasCount(1, rules);
        Assert.AreEqual(expectedDescription, rules[0].Describe());
    }

    [TestMethod]
    public void Bare_predicate_still_registers_a_required_rule() =>
        AssertRegistered(
            "add validation required on Name in Employee",
            "Employee", "Name is required");

    [TestMethod]
    public void Numeric_range_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be between 18 and 65 on Age in Employee",
            "Employee", "Age must be between 18 and 65");

    [TestMethod]
    public void Length_range_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation length must be between 2 and 50 on Name in Employee",
            "Employee", "Name length must be between 2 and 50");

    // The email phrasing is sugar over a regex rule, so it describes itself as one.
    [TestMethod]
    public void Valid_email_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be a valid email on Email in Customer",
            "Customer", @"Email must match ^[^@\s]+@[^@\s]+\.[^@\s]+$");

    [TestMethod]
    public void Greater_than_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be greater than 0 on Quantity in Order",
            "Order", "Quantity must be greater than 0");

    /// <summary>
    /// The property name repeated in the rule text must not be doubled up into
    /// "Age Age must be ..." — the shape the parsers would reject.
    /// </summary>
    [TestMethod]
    public void Rule_text_that_already_names_the_property_is_not_doubled() =>
        AssertRegistered(
            "add validation Age must be between 18 and 65 on Age in Employee",
            "Employee", "Age must be between 18 and 65");

    /// <summary>
    /// The phrasing the catalog documented before the rule-registry rewrite;
    /// it stopped parsing when the old if-chain was deleted.
    /// </summary>
    [TestMethod]
    public void Should_not_be_blank_phrasing_registers_a_required_rule() =>
        AssertRegistered(
            "add validation should not be blank on Name in Employee",
            "Employee", "Name is required");
}
