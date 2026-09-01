using AppScaffolding;
using CodeGenerator.App;
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using DomainDiscovery;
using DomainDiscovery.Model;

namespace CodeGenerator.Web.Workflow;

/// <summary>What reading the user's file produced, before any normalization is applied.</summary>
internal sealed record ReadResult(
    IReadOnlyList<TabularTable> Tables,
    NormalizationReport Report,
    string FileLabel,
    IReadOnlyList<string> Lines);

/// <summary>
/// Node 5's work: discover a domain from a description, or read a file and analyse it.
/// </summary>
/// <remarks>
/// Split out of the component for the reason <c>ReviewActions</c> is split out of the review loop:
/// everything decidable is lifted out of the thing that cannot be driven from a test. The
/// component owns only the progress panel and the checkboxes.
/// <para>
/// Both paths call exactly the services the console path calls. There is no second pipeline here.
/// </para>
/// </remarks>
internal static class DiscoveryStep
{
    /// <summary>
    /// Reads the source. False with a message rather than an exception: a mistyped path is the
    /// likeliest thing to go wrong at this node, and the panel offers a way back to node 3.
    /// </summary>
    public static bool TryRead(DataSourceSpec source, out ReadResult result, out string error)
    {
        result = null!;
        error = string.Empty;

        if (!DataSourceReaders.TryCreate(source.Kind, out var reader))
        {
            error = DataSourceReaders.NotSupportedMessage(source.Kind);
            return false;
        }

        IReadOnlyList<TabularTable> tables;

        try
        {
            tables = reader.Read(source);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
                                       or UnauthorizedAccessException)
        {
            // The reader's message already says what was wrong in the user's own terms.
            //
            // IOException rather than only FileNotFoundException, which was the whole of it before:
            // a spreadsheet open in Excel is the ordinary case and throws the base type, so it
            // escaped this handler and took the circuit with it — while the summary above promised
            // "false with a message rather than an exception" the entire time.
            error = ex.Message;
            return false;
        }

        // The file, not tables[0].Label: a workbook labels each table with its own sheet, and a
        // check covering every sheet must not claim to be about the first one.
        var fileLabel = DataSourceLabel.For(source);

        var report = BuildAppCommand.CombineReports(tables);

        var lines = new List<string>
        {
            $"Read {tables.Count} table(s) from {fileLabel}: "
            + string.Join(", ", tables.Select(t => $"{t.Name} ({t.Rows.Count} rows)")),
        };

        lines.AddRange(
            NormalizationPrompt.Lines(report, fileLabel, tables.Sum(t => t.Rows.Count)));

        result = new ReadResult(tables, report, fileLabel, lines);

        return true;
    }

    /// <summary>
    /// Applies the findings the user ticked and builds the model.
    /// </summary>
    /// <remarks>
    /// <paramref name="chosen"/> goes to <see cref="DataModelBuilder.Build(IReadOnlyList{TabularTable}, IReadOnlyList{NormalizationFinding})"/>
    /// as well as to <see cref="NormalizationPlan.Apply"/>, and that is not redundant: every
    /// applied split gets a <c>DecisionRecord</c>, which is what lets `why OrderItem` answer for
    /// a class the user never named.
    /// </remarks>
    public static (CandidateDomainModel Model, ImportedData Imported) BuildFromData(
        ReadResult read, IReadOnlyList<NormalizationFinding> chosen)
    {
        var tables = chosen.Count > 0
            ? NormalizationPlan.Apply(read.Tables, chosen)
            : read.Tables;

        return (DataModelBuilder.Build(tables, chosen), TabularImport.ToImportedData(tables));
    }

    /// <summary>
    /// The description-driven path: NLE seeds, Wikipedia topic, enrichment. Slow on a first fetch
    /// for an unseen topic, which is why the component runs it on a background task.
    /// </summary>
    public static (CandidateDomainModel Model, bool TopicRecognized) Discover(
        string description, string workspaceRoot)
    {
        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");

        var result = DomainDiscoveryPipeline.CreateDefault(cacheDir).Discover(description);

        return (result.Model, result.Topic.IsRecognized);
    }
}
