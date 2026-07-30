using CodeGenerator.App;
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppDataSourceTests
{
    [TestMethod]
    public void IsBuildApp_StillAcceptsTheColonForm()
    {
        Assert.IsTrue(BuildAppCommand.IsBuildApp("build app: a library"));
        Assert.IsTrue(BuildAppCommand.IsBuildApp("  BUILD APP: a library"));
    }

    /// <summary>The requirement is "run build app" with no description at all.</summary>
    [TestMethod]
    public void IsBuildApp_AcceptsTheBareForm()
    {
        Assert.IsTrue(BuildAppCommand.IsBuildApp("build app"));
        Assert.IsTrue(BuildAppCommand.IsBuildApp("Build App"));
        Assert.IsTrue(BuildAppCommand.IsBuildApp(@"build app from csv D:\orders.csv"));
    }

    /// <summary>A prefix check on "build app" would hijack these. Widening the trigger must not
    /// start swallowing other commands.</summary>
    [TestMethod]
    public void IsBuildApp_DoesNotHijackOtherInput()
    {
        Assert.IsFalse(BuildAppCommand.IsBuildApp("build apple tracker"));
        Assert.IsFalse(BuildAppCommand.IsBuildApp("build application"));
        Assert.IsFalse(BuildAppCommand.IsBuildApp("rebuild app"));
        Assert.IsFalse(BuildAppCommand.IsBuildApp("build"));
    }

    [TestMethod]
    public void Tail_IsWhatFollowsTheCommand_WithOrWithoutTheColon()
    {
        Assert.AreEqual("a library", BuildAppCommand.Tail("build app: a library"));
        Assert.AreEqual("a library", BuildAppCommand.Tail("build app a library"));
        Assert.AreEqual(string.Empty, BuildAppCommand.Tail("build app"));
        Assert.AreEqual(string.Empty, BuildAppCommand.Tail("build app:"));
    }

    /// <summary>A description names the app when there is one...</summary>
    [TestMethod]
    public void AppNameFor_PrefersTheDescription()
        => Assert.AreEqual("OrderTracking", BuildAppCommand.AppNameFor(
            "order tracking", new DataSourceSpec(DataSourceKind.Csv, @"D:\orders.csv")));

    /// <summary>...and with no description the source's base name goes through the SAME naming
    /// rule, rather than a second one invented for this path.</summary>
    [TestMethod]
    public void AppNameFor_FallsBackToTheSourcesBaseName()
        => Assert.AreEqual("Orders", BuildAppCommand.AppNameFor(
            string.Empty, new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv")));

    [TestMethod]
    public void AppNameFor_WithNeither_UsesTheExistingFallback()
        => Assert.AreEqual("GeneratedApp", BuildAppCommand.AppNameFor(string.Empty, null));

    /// <summary>Findings from every table are numbered in ONE list, so `n 2` means the same thing
    /// whichever table the finding came from.</summary>
    [TestMethod]
    public void CombineReports_NumbersFindingsAcrossEveryTable()
    {
        var orders = Table("Orders", ["Ref", "Item1", "Item2"],
            ["ORD-1", "Nut", "Bolt"], ["ORD-2", "Washer", null]);
        var members = Table("Members", ["Name", "Phone_1", "Phone_2"],
            ["Ada", "555-1", "555-2"], ["Grace", "555-3", null]);

        var report = BuildAppCommand.CombineReports([orders, members]);

        Assert.HasCount(2, report.Findings);
        Assert.AreEqual("Orders", report.Findings[0].TableName);
        Assert.AreEqual("Members", report.Findings[1].TableName);
    }

    /// <summary>The same note from several tables is said once, not once per table.</summary>
    [TestMethod]
    public void CombineReports_DoesNotRepeatTheSameNote()
    {
        var a = Table("Orders", ["Ref", "CustomerName", "CustomerCity"], ["1", "Ada", "London"]);
        var b = Table("Members", ["Ref", "CustomerName", "CustomerCity"], ["1", "Ada", "London"]);

        var report = BuildAppCommand.CombineReports([a, b]);

        Assert.AreEqual(1, report.Notes.Count(n => n == NormalizationAnalyzer.TooFewRowsNote));
    }

    private static TabularTable Table(string name, string[] header, params string?[][] rows)
    {
        var table = new TabularTable { Name = name, SourceLabel = name + ".csv" };
        foreach (var column in header)
            table.Columns.Add(new TabularColumn { Name = column });

        foreach (var row in rows)
            table.Rows.Add(row);

        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        return table;
    }
}
