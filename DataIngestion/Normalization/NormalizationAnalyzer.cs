using DataIngestion.Schema;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Normalization;

/// <summary>
/// Looks for the normalization problems a flat export routinely carries, and proposes the split
/// each one implies. Finds and explains only — <see cref="NormalizationPlan"/> is what changes
/// anything, and only for the findings the user picks.
/// </summary>
public static partial class NormalizationAnalyzer
{
    /// <summary>How many rows must show a separator before it is structure rather than
    /// punctuation.</summary>
    public const double NonAtomicShare = 0.6;

    /// <summary>How often a split part must appear across rows to count as a repeated value.
    /// Prose fragments appear once each; a real tag appears again.</summary>
    public const int MinRecurrence = 2;

    /// <summary>Separators that pack several values into one cell, in priority order.</summary>
    public static readonly char[] Separators = [',', ';', '|'];

    /// <summary>How many evidence rows to quote. Enough to see the pattern, not a data dump.</summary>
    public const int EvidenceRows = 3;

    public static NormalizationReport Analyze(TabularTable table)
    {
        var findings = new List<NormalizationFinding>();
        var notes = new List<string>();

        findings.AddRange(RepeatingGroups(table));
        findings.AddRange(NonAtomicColumns(table));
        findings.AddRange(FunctionalDependencies(table, notes));
        findings.AddRange(DerivedColumns(table));

        return new NormalizationReport(findings, notes);
    }

    /// <summary>
    /// Columns sharing a stem and differing only by a trailing number: Item1/Item2/Item3, or
    /// Phone_1/Phone_2. Two or more is a group; one is just a column whose name ends in a digit.
    /// </summary>
    private static IEnumerable<NormalizationFinding> RepeatingGroups(TabularTable table)
    {
        var groups = table.Columns
            .Select(c => (Column: c.Name, Stem: OrdinalStem(c.Name)))
            .Where(x => x.Stem is not null)
            .GroupBy(x => x.Stem!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2);

        // The parent's ENTITY name, not its table name: Orders.csv is about an Order, and both the
        // child's name and the proposal text read wrong otherwise ("OrdersItem", "many per Orders").
        var parent = NameNormalizer.ToUserTypedEntityName(table.Name);

        foreach (var group in groups)
        {
            var columns = group.Select(x => x.Column).ToList();

            // Prefixed with the parent, because "Item" alone collides across domains and says
            // nothing about what it belongs to.
            var entity = parent + NameNormalizer.ToUserTypedEntityName(group.Key);

            yield return new NormalizationFinding
            {
                Kind = NormalizationFindingKind.RepeatingGroup,
                TableName = table.Name,
                SourceLabel = table.Label,
                Columns = columns,
                ProposedEntity = entity,
                Headline = $"Repeating group: {string.Join(", ", columns)}",
                Proposal = $"split into {entity} (many per {parent})",
                Evidence = RowEvidence(table, columns),
            };
        }
    }

    /// <summary>
    /// The stem of a name ending in digits, with any separator before them removed:
    /// "Item1" -> "Item", "Phone_1" -> "Phone". Null when the name does not end in a digit, or
    /// when nothing is left once the digits are stripped.
    /// </summary>
    private static string? OrdinalStem(string columnName)
    {
        var end = columnName.Length;
        while (end > 0 && char.IsDigit(columnName[end - 1]))
            end--;

        if (end == columnName.Length)
            return null;

        var stem = columnName[..end].TrimEnd('_', '-', ' ');
        return stem.Length == 0 ? null : stem;
    }

    /// <summary>
    /// A string column packing several values into one cell. Two conditions, and the second is
    /// what keeps prose out: most rows must carry the separator, AND some split part must appear
    /// in more than one row. A Notes column full of commas splits into fragments that never recur.
    /// </summary>
    private static IEnumerable<NormalizationFinding> NonAtomicColumns(TabularTable table)
    {
        foreach (var column in table.Columns.Where(c => c.Type == "string"))
        {
            var values = table.ColumnValues(column.Name)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();

            if (values.Count == 0)
                continue;

            foreach (var separator in Separators)
            {
                var withSeparator = values.Count(v => v.Contains(separator));
                if (withSeparator < values.Count * NonAtomicShare)
                    continue;

                var parts = values
                    .SelectMany(v => v.Split(separator))
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();

                var recurring = parts
                    .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() >= MinRecurrence)
                    .Select(g => g.Key)
                    .ToList();

                if (recurring.Count == 0)
                    continue;   // prose: commas everywhere, no repeated value

                var entity = NameNormalizer.ToUserTypedEntityName(column.Name);

                yield return new NormalizationFinding
                {
                    Kind = NormalizationFindingKind.NonAtomicValues,
                    TableName = table.Name,
                    SourceLabel = table.Label,
                    Columns = [column.Name],
                    ProposedEntity = entity,
                    Separator = separator,
                    Headline =
                        $"Non-atomic: \"{column.Name}\" holds {SeparatorName(separator)}-separated values",
                    Proposal = $"split into {entity}",
                    Evidence =
                    [
                        $"{withSeparator} of {values.Count} rows contain '{separator}'.",
                        "Values that appear in more than one row: "
                            + string.Join(", ", recurring.Take(EvidenceRows * 2)),
                    ],
                };

                break;   // one separator per column; the first that matches is the one
            }
        }
    }

    private static string SeparatorName(char separator) => separator switch
    {
        ',' => "comma",
        ';' => "semicolon",
        '|' => "pipe",
        _ => separator.ToString(),
    };

    /// <summary>The first few rows' values for <paramref name="columns"/>, as printable lines.</summary>
    private static List<string> RowEvidence(TabularTable table, IReadOnlyList<string> columns)
    {
        var lines = new List<string> { "  " + string.Join(" | ", columns) };

        for (var row = 0; row < Math.Min(EvidenceRows, table.Rows.Count); row++)
        {
            lines.Add("  " + string.Join(" | ",
                columns.Select(c => table.Value(row, c) ?? "(none)")));
        }

        return lines;
    }
}
