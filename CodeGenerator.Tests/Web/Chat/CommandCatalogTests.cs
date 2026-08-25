using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// What the composer's dropdown offers.
/// </summary>
/// <remarks>
/// Filtered from <c>CommandDictionary.Commands</c> — the same list <c>help</c> and
/// <c>docs/commands.md</c> are generated from, so the dropdown cannot drift from the real catalog.
/// </remarks>
[TestClass]
public class CommandCatalogTests
{
    [TestMethod]
    public void Nothing_typed_offers_nothing()
    {
        Assert.IsEmpty(CommandCatalog.Matches(""));
        Assert.IsEmpty(CommandCatalog.Matches("   "));
    }

    [TestMethod]
    public void Offers_commands_that_start_with_what_was_typed()
    {
        var matches = CommandCatalog.Matches("list");

        Assert.IsNotEmpty(matches);
        Assert.IsTrue(
            matches.All(m => m.Command.StartsWith("list", StringComparison.OrdinalIgnoreCase)
                             || m.Command.Contains("list", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void A_prefix_match_is_offered_before_a_contains_match()
    {
        // Typing "class" should reach `create class` before `list classes in <project>`: the
        // command that begins with what you typed is the one you were most likely reaching for.
        var matches = CommandCatalog.Matches("show class");

        Assert.IsNotEmpty(matches);
        StringAssert.StartsWith(matches[0].Command, "show class");
    }

    [TestMethod]
    public void Matching_is_case_insensitive()
    {
        Assert.IsNotEmpty(CommandCatalog.Matches("LIST CLASSES"));
    }

    [TestMethod]
    public void The_limit_is_respected()
    {
        Assert.IsLessThanOrEqualTo(3, CommandCatalog.Matches("a", limit: 3).Count);
    }

    [TestMethod]
    public void Build_app_is_never_offered()
    {
        // The chat declines it. Offering a command that cannot be run there is an invitation to
        // a dead end.
        var matches = CommandCatalog.Matches("build", limit: 50);

        Assert.IsFalse(
            matches.Any(m => m.Command.StartsWith("build app", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Every_offer_carries_its_description_and_example()
    {
        foreach (var match in CommandCatalog.Matches("list", limit: 50))
        {
            Assert.IsNotEmpty(match.Description, $"{match.Command} has no description");
            Assert.IsNotEmpty(match.Example, $"{match.Command} has no example");
        }
    }
}
