using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DataIngestion.Schema.Readers.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The SQL branch of the data panel: connect, see what is there, tick, then take the step.
/// </summary>
/// <remarks>
/// Nothing is read until the step is taken, which is the entire reason the catalog is a separate
/// phase from the reader.
/// </remarks>
[TestClass]
public class DataStageSqlTests
{
    private const string Connection = "Server=.;Database=Northwind;Trusted_Connection=True";

    [TestMethod]
    public void Connecting_lists_the_tables_with_their_counts()
    {
        using var context = new BunitContext();
        var rendered = RenderSql(context, out _, out _);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        StringAssert.Contains(rendered.Markup, "Customers");
        StringAssert.Contains(rendered.Markup, "91");
        Assert.HasCount(2, rendered.FindAll("input.table-pick"));
    }

    [TestMethod]
    public void Listing_the_tables_reads_no_rows()
    {
        using var context = new BunitContext();
        var rendered = RenderSql(context, out var source, out _);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        Assert.IsEmpty(source.RowsFetched);
    }

    [TestMethod]
    public async Task Ticking_a_table_records_it_on_the_source()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        var rendered = RenderSql(context, out _, out var session, navigation);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();
        rendered.FindAll("input.table-pick")[0].Change(true);

        Assert.IsTrue(await navigation.ApplyAsync());

        Assert.IsNotNull(session.Source);
        Assert.AreEqual(DataSourceKind.Sql, session.Source!.Kind);
        CollectionAssert.Contains(session.Source.SqlTables!.ToList(), "Customers");
    }

    /// <summary>The same meaning `tables A,B,C` has when it is left off a piped line: one rule,
    /// not two.</summary>
    [TestMethod]
    public async Task Ticking_nothing_reads_every_table()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        var rendered = RenderSql(context, out _, out var session, navigation);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        Assert.IsTrue(await navigation.ApplyAsync());

        Assert.IsNull(session.Source!.SqlTables);
    }

    /// <summary>The database names the generated app when the run carried no description.</summary>
    [TestMethod]
    public async Task The_database_is_taken_off_the_connection_string()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        var rendered = RenderSql(context, out _, out var session, navigation);

        rendered.Find("input.connection").Change(Connection);

        Assert.IsTrue(await navigation.ApplyAsync());

        Assert.AreEqual("Northwind", session.Source!.SqlDatabase);
    }

    /// <summary>A failure says why and leaves the stage usable, rather than ending the run.</summary>
    [TestMethod]
    public void A_connection_that_fails_says_so_and_stays_clickable()
    {
        using var context = new BunitContext();

        var rendered = Render(
            context,
            out _,
            new StageNavigation(),
            new ThrowingSqlSchemaSource("Login failed for user 'sa'."));

        rendered.Find("select.answer").Change("sql");
        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        StringAssert.Contains(rendered.Find(".failure").TextContent, "Login failed");
        Assert.IsNotEmpty(rendered.FindAll("button.connect"));
    }

    [TestMethod]
    public async Task An_empty_connection_is_refused_with_a_reason()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        var rendered = RenderSql(context, out _, out _, navigation);

        Assert.IsFalse(await navigation.ApplyAsync());

        // The page re-renders after taking a step (BuildWorkflow calls ApplyAsync from its own
        // handler), which is what puts the refusal on screen. Driving the navigation directly
        // skips that, so the render is asked for here.
        rendered.Render();

        StringAssert.Contains(rendered.Find(".failure").TextContent, "connection string");
    }

    [TestMethod]
    public void A_table_whose_row_count_is_unknown_is_still_offered()
    {
        using var context = new BunitContext();
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, null));

        var rendered = Render(context, out _, new StageNavigation(), source);

        rendered.Find("select.answer").Change("sql");
        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        Assert.HasCount(1, rendered.FindAll("input.table-pick"));
        StringAssert.Contains(rendered.Find(".rows").TextContent, "—");
    }

    /// <summary>The file controls have no meaning for a database, and a path box beside a
    /// connection string is an invitation to type the wrong thing.</summary>
    [TestMethod]
    public void The_file_controls_are_not_shown_for_sql()
    {
        using var context = new BunitContext();
        var rendered = RenderSql(context, out _, out _);

        Assert.IsEmpty(rendered.FindAll("input.path"));
        Assert.IsEmpty(rendered.FindAll(".drop"));
        Assert.IsNotEmpty(rendered.FindAll("input.connection"));
    }

    private static IRenderedComponent<DataStage> RenderSql(
        BunitContext context,
        out FakeSqlSchemaSource source,
        out BuildSession session,
        StageNavigation? navigation = null)
    {
        source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Customers", 11, 91));
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, 830));

        var rendered = Render(context, out session, navigation ?? new StageNavigation(), source);

        rendered.Find("select.answer").Change("sql");

        return rendered;
    }

    private static IRenderedComponent<DataStage> Render(
        BunitContext context,
        out BuildSession session,
        StageNavigation navigation,
        ISqlSchemaSource schemaSource)
    {
        session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);

        var captured = session;

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { })
            .Add(c => c.SchemaSource, schemaSource));

        rendered.Find("input[type=radio][value=file]").Change(true);

        return rendered;
    }
}
