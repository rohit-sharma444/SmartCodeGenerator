using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>`nologin` is the switch the whole regression proof hangs on: with it, a named layout
/// must still produce exactly what the generator produced before this feature.</summary>
[TestClass]
public class LoginFlagTests
{
    [TestMethod]
    public void Login_is_on_by_default()
        => Assert.IsTrue(AppCustomization.Default.Login);

    [TestMethod]
    public void The_nologin_flag_turns_it_off()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop nologin", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.IsFalse(customization.Login);
    }

    /// <summary>Options are stripped right to left, so a flag must sit happily beside the value
    /// keys without swallowing one of their values.</summary>
    [TestMethod]
    public void The_flag_reads_beside_the_other_options()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop company Codis layout sidebar nologin noseed", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.IsFalse(customization.Login);
        Assert.IsFalse(customization.SampleSeedData);
        Assert.AreEqual("Codis", customization.Company);
        Assert.AreEqual(AppLayout.Sidebar, customization.Layout);
    }

    /// <summary>The word must not be eaten out of ordinary prose. "nologin" is not a word anyone
    /// types by accident, but "a shop with no login" is a sentence someone might.</summary>
    [TestMethod]
    public void Prose_about_logins_is_left_alone()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop with no login", AppCustomization.Default);

        Assert.AreEqual("a shop with no login", remaining);
        Assert.IsTrue(customization.Login);
    }

    /// <summary>Every surface `noseed` reaches, `nologin` must reach too — a flag the help does
    /// not mention is a flag nobody finds.</summary>
    [TestMethod]
    public void The_inline_options_help_line_lists_the_flag()
        => StringAssert.Contains(BuildAppCommand.InlineOptionsHelp, "nologin");

    [TestMethod]
    public void The_summary_reports_a_build_with_no_login()
    {
        var lines = CustomizationSummary.Lines(
            AppCustomization.Default with { Login = false }, target: null);

        Assert.IsTrue(
            lines.Any(l => l.Contains("no login", StringComparison.Ordinal)),
            "The summary is where a user checks that the flag took effect.");
    }

    /// <summary>Only differences from the default are listed, and login is on by default.</summary>
    [TestMethod]
    public void The_summary_stays_silent_when_login_is_on()
    {
        var lines = CustomizationSummary.Lines(AppCustomization.Default, target: null);

        Assert.IsFalse(lines.Any(l => l.Contains("login", StringComparison.OrdinalIgnoreCase)));
    }
}
