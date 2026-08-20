using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// What the data stage says about the file it has just been given.
/// </summary>
/// <remarks>
/// It said nothing. The panel checked that the path existed, that the format was known and that a
/// reader for it shipped — and then accepted it in silence, so the first sign of whether the tool
/// had read the file the user meant was a model two stages later. The counts are the answer to
/// "did it take the right file", and the reader already works them out.
/// </remarks>
[TestClass]
public class DataPeekTests
{
    [TestMethod]
    public void It_counts_the_rows_and_the_columns_of_every_table()
    {
        var read = ReadOf(("orders", 2, 3), ("lines", 5, 2));

        var described = DataPeek.Describe(read);

        StringAssert.Contains(described, "orders");
        StringAssert.Contains(described, "2 rows");
        StringAssert.Contains(described, "3 columns");
        StringAssert.Contains(described, "lines");
        StringAssert.Contains(described, "5 rows");
    }

    [TestMethod]
    public void One_row_is_not_called_one_rows()
    {
        StringAssert.Contains(DataPeek.Describe(ReadOf(("orders", 1, 1))), "1 row,");
        StringAssert.Contains(DataPeek.Describe(ReadOf(("orders", 1, 1))), "1 column");
    }

    /// <summary>
    /// The whole point, end to end: a real file in the box, and the panel says what is in it before
    /// the user commits to it.
    /// </summary>
    [TestMethod]
    public void The_panel_says_what_the_file_holds_as_soon_as_it_is_named()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cg-peek-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path, ["Name,Age,Town", "Ada,36,London", "Grace,45,New York"]);

        try
        {
            using var context = new BunitContext();

            var rendered = context.Render<DataStage>(p => p
                .Add(c => c.Session, Session())
                .Add(c => c.Navigation, new StageNavigation())
                .Add(c => c.OnAdvance, () => { }));

            rendered.Find("input[type=radio][value=file]").Change(true);
            rendered.Find("input.path").Change(path);

            var peek = rendered.Find(".peek").TextContent;

            StringAssert.Contains(peek, "2 rows");
            StringAssert.Contains(peek, "3 columns");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A half-typed path is not a mistake yet. Reporting it as one, on every change, would make the
    /// panel shout at someone who is still typing — the click is where refusals belong.
    /// </summary>
    [TestMethod]
    public void A_path_that_is_not_there_yet_says_nothing_at_all()
    {
        using var context = new BunitContext();

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, Session())
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input[type=radio][value=file]").Change(true);
        rendered.Find("input.path").Change(@"C:\nope\half-typed");

        Assert.IsEmpty(rendered.FindAll(".peek"));
        Assert.IsEmpty(rendered.FindAll(".failure"));
    }

    private static BuildSession Session()
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();

        return session;
    }

    private static ReadResult ReadOf(params (string Name, int Rows, int Columns)[] tables)
    {
        var built = new List<DataIngestion.Schema.TabularTable>();

        foreach (var (name, rows, columns) in tables)
        {
            var table = new DataIngestion.Schema.TabularTable { Name = name };

            for (var c = 0; c < columns; c++)
                table.Columns.Add(new DataIngestion.Schema.TabularColumn { Name = $"c{c}" });

            for (var r = 0; r < rows; r++)
                table.Rows.Add(new string?[columns]);

            built.Add(table);
        }

        return new ReadResult(built, null!, "orders.csv", []);
    }
}
