using DataIngestion.Schema;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// What a file turned out to hold, for the panel that has just been handed it.
/// </summary>
/// <remarks>
/// The data stage used to accept a path in silence: it checked that the file existed, that the
/// format was known and that a reader for it shipped, and said nothing about what was inside. The
/// first sign of whether the tool had read the file the user meant was a model two stages later,
/// by which point a wrong pick has cost a discovery run. The counts are the answer to "did it take
/// the right file", and the reader has them already.
/// </remarks>
internal static class DataPeek
{
    public static string Describe(ReadResult read)
        => $"{read.FileLabel}: " + string.Join("; ", read.Tables.Select(Describe));

    private static string Describe(TabularTable table)
        => $"{table.Name} — {Count(table.Rows.Count, "row")}, {Count(table.Columns.Count, "column")}";

    /// <summary>Plural only when it is plural. "1 rows" in the first thing the panel says about a
    /// user's own data reads as carelessness about the data itself.</summary>
    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
