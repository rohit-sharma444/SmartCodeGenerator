using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The `build app` line the page can hand back.
/// </summary>
/// <remarks>
/// Eight answered questions and no way to repeat them: a cancelled run, or a second project like
/// the first, meant clicking the whole interview again, and nothing the page produced could go into
/// a script.
/// <para>
/// The tests that matter here parse what it writes, with the real parsers, and compare the answers
/// back. A line that reads plausibly and parses differently is worse than no line — and the two
/// parsers have real rules about quoting and about options going last, which taste would get wrong.
/// </para>
/// </remarks>
[TestClass]
public class BuildCommandEchoTests
{
    [TestMethod]
    public void The_description_comes_back_out_of_the_line_it_wrote()
    {
        var session = Session("a library that lends books");

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.AreEqual("a library that lends books", parsed.Request.Description);
    }

    /// <summary>
    /// The location has to be quoted, and this is the case that proves it rather than merely
    /// looking like it does.
    /// </summary>
    /// <remarks>
    /// <c>InlineCustomization</c> scans the line right to left stripping option words, and its own
    /// remarks warn that an unquoted location is not atomic, so its last word can be taken for an
    /// option. A file whose name ends in the word — <c>aug logging</c> — is all it takes: unquoted, the line comes back with
    /// the path truncated and logging switched on that nobody asked for. The trailing quote is what
    /// hides it from that scan — dropping the quotes from the echo reddens this and nothing else.
    /// </remarks>
    [TestMethod]
    public void A_location_ending_in_an_option_word_survives_because_it_is_quoted()
    {
        var session = Session("order tracking");
        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"D:\my exports\aug logging"));
        session.Advance();
        session.SetCustomization(AppCustomization.Default with { Company = "Codis Ltd" });

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.AreEqual(@"D:\my exports\aug logging", parsed.Request.Source?.Location);
        Assert.AreEqual(DataSourceKind.Csv, parsed.Request.Source?.Kind);
        Assert.AreEqual("order tracking", parsed.Request.Description);
        Assert.AreEqual("Codis Ltd", parsed.Customization.Company);
        Assert.IsFalse(parsed.Customization.Logging, "the path's last word is not an option");
    }

    [TestMethod]
    public void A_path_with_spaces_comes_back_whole()
    {
        var session = Session("order tracking");
        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"D:\my data\orders for august.csv"));
        session.Advance();

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.AreEqual(@"D:\my data\orders for august.csv", parsed.Request.Source?.Location);
    }

    [TestMethod]
    public void Every_customization_the_page_can_set_comes_back_as_itself()
    {
        var session = Session("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();

        var asked = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            NamespacePrefix = "CodisLtd",
            Theme = AppTheme.Dark,
            Accent = "#ff8800",
            Layout = AppLayout.MasterDetail,
            Validation = false,
            Logging = true,
            SampleSeedData = false,
            Login = false,
            Transactions = "Order",
            Masters = "Region",
        };

        session.SetCustomization(asked);

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.AreEqual(asked.Company, parsed.Customization.Company);
        Assert.AreEqual(asked.NamespacePrefix, parsed.Customization.NamespacePrefix);
        Assert.AreEqual(asked.Theme, parsed.Customization.Theme);
        Assert.AreEqual(asked.Accent, parsed.Customization.Accent);
        Assert.AreEqual(asked.Layout, parsed.Customization.Layout);
        Assert.AreEqual(asked.Validation, parsed.Customization.Validation);
        Assert.AreEqual(asked.Logging, parsed.Customization.Logging);
        Assert.AreEqual(asked.SampleSeedData, parsed.Customization.SampleSeedData);
        Assert.AreEqual(asked.Login, parsed.Customization.Login);
        Assert.AreEqual(asked.Transactions, parsed.Customization.Transactions);
        Assert.AreEqual(asked.Masters, parsed.Customization.Masters);
    }

    /// <summary>
    /// Nobody types the defaults, and a line carrying all nine options would bury the two that were
    /// actually chosen.
    /// </summary>
    [TestMethod]
    public void An_untouched_customization_adds_nothing_to_the_line()
    {
        var session = Session("a shop");

        Assert.AreEqual("build app: a shop", BuildCommandEcho.For(session)[0]);
    }

    /// <summary>
    /// The `build app:` grammar has no application type on it — the console answers that at the
    /// review prompt — so the type is a second line rather than a word that would not parse.
    /// </summary>
    [TestMethod]
    public void The_application_type_is_the_approve_line_the_console_would_use()
    {
        var session = Session("a shop");
        session.SetTarget(new ConsoleTarget());

        var lines = BuildCommandEcho.For(session);

        Assert.HasCount(2, lines);
        Assert.AreEqual($"approve {new ConsoleTarget().Key}", lines[1]);
        Assert.IsTrue(TargetScaffolders.TryResolve(new ConsoleTarget().Key, out _));
    }

    [TestMethod]
    public void Nothing_is_approved_before_a_type_is_chosen()
        => Assert.HasCount(1, BuildCommandEcho.For(Session("a shop")));

    /// <summary>Puts the line back through the two parsers a typed line goes through, in the order
    /// the CLI runs them.</summary>
    private static (InlineBuildRequest Request, AppCustomization Customization) Reparse(string line)
    {
        var tail = line["build app:".Length..];

        var (remaining, customization) = InlineCustomization.Split(tail, AppCustomization.Default);

        return (InlineDataSource.Parse(remaining), customization);
    }

    private static BuildSession Session(string description)
        => new(description, @"C:\ws", AppCustomization.Default);
}
