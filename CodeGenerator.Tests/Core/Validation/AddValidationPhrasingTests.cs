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

    /// <summary>
    /// The rule text already names the property, so the user omits the "on
    /// &lt;property&gt;" clause: "add validation &lt;rule&gt; in &lt;class&gt;".
    /// Without a form for this, the class name was never extracted and the
    /// command failed with "No class specified for the validation rule."
    /// </summary>
    [TestMethod]
    public void Property_embedded_rule_without_on_clause_registers_a_rule() =>
        AssertRegistered(
            "add validation Number should not be blank in Bill",
            "Bill", "Number is required");

    [TestMethod]
    public void Inclusive_bound_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be at least 18 on Age in Employee",
            "Employee", "Age must be at least 18");

    [TestMethod]
    public void Character_bound_phrasing_registers_a_length_rule() =>
        AssertRegistered(
            "add validation must be at most 50 characters on Name in Employee",
            "Employee", "Name must be at most 50 characters");

    [TestMethod]
    public void Sign_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be positive on Salary in Employee",
            "Employee", "Salary must be positive");

    // The expected description is the pattern the rule carries, so it has to be the pattern that
    // actually shipped — not the one this plan first drafted. Read the value out of
    // NamedFormatRuleParser.Patterns["url"] rather than retyping it here from memory.
    [TestMethod]
    public void Named_format_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be a valid URL on Website in Employee",
            "Employee", @"Website must match ^(?:https?://)?[^\s.]+(?:\.[^\s.]+)+$");

    [TestMethod]
    public void Relative_date_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be in the past on DateOfBirth in Employee",
            "Employee", "DateOfBirth must be in the past");

    [TestMethod]
    public void Text_shape_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be uppercase on Code in Employee",
            "Employee", "Code must be uppercase");

    // ValidDateRuleParser's pattern requires the literal article "a" ("must be a valid
    // date"), same as RelativeDateRuleParser requires the literal "the" ("in the past").
    // This is the second of the two parsers Finding 1 asked to be proven, not just assumed.
    [TestMethod]
    public void Valid_date_phrasing_registers_a_rule() =>
        AssertRegistered(
            "add validation must be a valid date on Born in Employee",
            "Employee", "Born must be a valid date");
}
