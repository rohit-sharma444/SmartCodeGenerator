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
            EntryTemplate = AppScaffolding.Screens.EntryTemplate.Tabbed,
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
        Assert.AreEqual(asked.EntryTemplate, parsed.Customization.EntryTemplate);
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

    /// <summary>Left out before, so a pasted line titled the window with the default pattern.</summary>
    [TestMethod]
    public void The_window_title_comes_back_as_itself()
    {
        var session = Session("a shop");
        session.SetCustomization(AppCustomization.Default with
        {
            Company = "Codis Ltd",
            // Not the company pattern: naming a company inline switches to that one by itself, so
            // it would come back even from a line that carried no title at all.
            TitlePattern = "{app} ({company})",
        });

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.AreEqual("{app} ({company})", parsed.Customization.TitlePattern);
        Assert.AreEqual("a shop", parsed.Request.Description);
    }

    /// <summary>
    /// A company switches a typed line onto the company title, so a title put back to the app
    /// alone has to be said - and said where the parser applies it after the company.
    /// </summary>
    [TestMethod]
    public void A_title_put_back_to_the_app_alone_survives_a_company()
    {
        var session = Session("a shop");
        session.SetCustomization(AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.AppPlaceholder,
        });

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.AreEqual(AppCustomization.AppPlaceholder, parsed.Customization.TitlePattern);
        Assert.AreEqual("Codis Ltd", parsed.Customization.Company);
    }

    /// <summary>
    /// The whole customization, with the values that break a line: spaces in the icon path and
    /// the transaction list, and a quote inside the company. Compared as one record, so an option
    /// the echo forgets, or one it writes that parses differently, fails here.
    /// </summary>
    [TestMethod]
    public void Awkward_values_parse_back_to_the_same_customization()
    {
        var session = Session("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();

        var asked = AppCustomization.Default with
        {
            Company = "Codis \"Best\" Ltd",
            NamespacePrefix = "CodisLtd",
            Theme = AppTheme.Light,
            Accent = "#123456",
            IconPath = @"C:\My Icons\app logo.ico",
            TitlePattern = "{app} for \"{company}\"",
            Layout = AppLayout.Sidebar,
            EntryTemplate = AppScaffolding.Screens.EntryTemplate.Split,
            Transactions = "Order, Sales Invoice",
            Masters = "Region, Price List",
            Validation = false,
            Logging = true,
            SampleSeedData = false,
            Login = false,
        };

        session.SetCustomization(asked);

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.AreEqual(asked, parsed.Customization);
        Assert.AreEqual("a shop", parsed.Request.Description);
        Assert.IsNull(parsed.Request.Source);
    }

    /// <summary>`normalize` typed on the first line was dropped from the replay, so the pasted
    /// line built the model from the raw tables.</summary>
    [TestMethod]
    public void Normalize_comes_back_when_the_findings_were_applied()
    {
        var session = new BuildSession(
            "orders", @"C:\ws", AppCustomization.Default,
            new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv"))
        {
            NormalizeAll = true,
        };

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        Assert.IsTrue(parsed.Request.Normalize);
        Assert.AreEqual(@"D:\data\orders.csv", parsed.Request.Source?.Location);
    }

    [TestMethod]
    public void Normalize_is_not_said_when_nothing_was_applied()
    {
        var session = new BuildSession(
            "orders", @"C:\ws", AppCustomization.Default,
            new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv"));

        Assert.IsFalse(Reparse(BuildCommandEcho.For(session)[0]).Request.Normalize);
    }

    /// <summary>The persistence chosen at Generate is an `approve` argument on the console, and
    /// the replay approved without it - a SQLite app came back as JSON.</summary>
    [TestMethod]
    public void The_persistence_generated_with_is_on_the_approve_line()
    {
        var session = Session("a shop");
        session.SetTarget(new ConsoleTarget());
        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, @"C:\ws\Shop", [], "ok"), string.Empty, true,
            Persistence: PersistenceKind.Sqlite));

        var approve = BuildCommandEcho.For(session)[1];

        Assert.AreEqual($"approve {new ConsoleTarget().Key} sqlite", approve);
        Assert.AreEqual(
            PersistenceKind.Sqlite,
            BuildAppCommand.ResolvePersistence(approve.Split(' ')));
    }

    /// <summary>Left out before, so a pasted line read EVERY table of the database - a different
    /// model from the one this run reviewed.</summary>
    [TestMethod]
    public void The_chosen_sql_tables_come_back_as_themselves()
    {
        var session = new BuildSession(
            "trading", @"C:\ws", AppCustomization.Default,
            new DataSourceSpec(
                DataSourceKind.Sql, "Server=.;Database=TRADING;Trusted_Connection=True",
                "TRADING", ["CorporateActions", "Order Details"]));

        var parsed = Reparse(BuildCommandEcho.For(session)[0]);

        CollectionAssert.AreEqual(
            new[] { "CorporateActions", "Order Details" }, parsed.Request.Source!.SqlTables!.ToArray());
        Assert.AreEqual(
            "Server=.;Database=TRADING;Trusted_Connection=True", parsed.Request.Source.Location);
    }

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

    /// <summary>
    /// The echo prints the location, and for SQL the location IS the connection string. This line
    /// is shown on the review page AND printed to the console when the command finishes, so
    /// without redaction an ordinary SQL-auth password lands in the terminal scrollback and in
    /// anything the user pastes the line into.
    /// </summary>
    [TestMethod]
    public void A_sql_password_is_not_echoed()
    {
        var session = new BuildSession(
            "a shop",
            Path.GetTempPath(),
            AppCustomization.Default,
            new DataSourceSpec(
                DataSourceKind.Sql,
                "Server=.;Database=Northwind;User ID=sa;Password=hunter2",
                SqlDatabase: "Northwind"));

        var echo = string.Join(Environment.NewLine, BuildCommandEcho.For(session));

        StringAssert.Contains(echo, "Password=***");
        StringAssert.Contains(echo, "Database=Northwind");
        Assert.DoesNotContain("hunter2", echo, "the password reached the echo");
    }
}
