using System.Globalization;
using DataIngestion.Schema;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Normalization;

/// <summary>
/// The 2NF/3NF half of the analyser. Separate file from the 1NF half because these two
/// detections reason about relationships BETWEEN columns over all rows, where those two reason
/// about one column or one set of names.
/// </summary>
public static partial class NormalizationAnalyzer
{
    /// <summary>
    /// Fewest rows a dependency claim can be made from. Below this, any two columns agree by
    /// coincidence and the "finding" is an artefact of the sample size.
    /// </summary>
    public const int MinRowsForDependencies = 5;

    /// <summary>
    /// A determinant may not have more distinct values than this share of the rows. A near-unique
    /// column determines every other column trivially — that is what being a key means — and
    /// extracting one entity per row is not normalization.
    /// </summary>
    public const double MaxDeterminantShare = 0.5;

    /// <summary>Tolerance for a derived-column match, in absolute terms. Money is rounded at the
    /// source, so an exact equality test would reject real derivations.</summary>
    public const decimal DerivedTolerance = 0.01m;

    /// <summary>Said out loud when the table is too short to test dependencies on. Silence here
    /// would read as "no problems found", which is a different claim.</summary>
    public const string TooFewRowsNote =
        "Too few rows to test dependencies — a partial-dependency check needs at least "
        + "5 rows to tell a real pattern from a coincidence.";

    /// <summary>
    /// Columns functionally determined by another, non-key column: every value of the determinant
    /// maps to exactly one value of each dependent, across every row.
    /// </summary>
    /// <remarks>
    /// A single dependent needs a second signal — a shared name prefix — because on its own,
    /// "column A determines column B" happens constantly in real data without meaning a missing
    /// entity. Two or more dependents is signal enough on its own.
    /// </remarks>
    private static IEnumerable<NormalizationFinding> FunctionalDependencies(
        TabularTable table, List<string> notes)
    {
        if (table.Rows.Count < MinRowsForDependencies)
        {
            notes.Add(TooFewRowsNote);
            yield break;
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var determinant in table.Columns)
        {
            if (claimed.Contains(determinant.Name) || !IsCandidateDeterminant(table, determinant))
                continue;

            var dependents = table.Columns
                .Where(c => !string.Equals(c.Name, determinant.Name, StringComparison.OrdinalIgnoreCase))
                .Where(c => !claimed.Contains(c.Name))
                .Where(c => Determines(table, determinant.Name, c.Name))
                .Select(c => c.Name)
                .ToList();

            if (dependents.Count == 0)
                continue;

            if (dependents.Count == 1 && SharedPrefix(determinant.Name, dependents[0]) is null)
                continue;

            var stem = SharedPrefix(determinant.Name, dependents[0]) ?? determinant.Name;
            var entity = NameNormalizer.ToUserTypedEntityName(stem);

            var columns = new List<string> { determinant.Name };
            columns.AddRange(dependents);

            foreach (var column in columns)
                claimed.Add(column);

            yield return new NormalizationFinding
            {
                Kind = NormalizationFindingKind.FunctionalDependency,
                TableName = table.Name,
                SourceLabel = table.Label,
                Columns = columns,
                ProposedEntity = entity,
                Headline = $"Partial dependency: {string.Join(", ", columns)}",
                Proposal = "split into " + entity
                    + $" ({NameNormalizer.ToUserTypedEntityName(table.Name)} references it)",
                Evidence = DependencyEvidence(table, determinant.Name, dependents),
            };
        }
    }

    /// <summary>
    /// Whether <paramref name="column"/> could be a determinant: at least two distinct values (one
    /// determines nothing interesting) and not so many that it is effectively a key.
    /// </summary>
    private static bool IsCandidateDeterminant(TabularTable table, TabularColumn column)
    {
        var values = table.ColumnValues(column.Name)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (values.Count < table.Rows.Count)
            return false;   // any gap makes the mapping untestable for those rows

        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return distinct >= 2 && distinct <= table.Rows.Count * MaxDeterminantShare;
    }

    /// <summary>Whether every value of <paramref name="determinant"/> maps to exactly one value of
    /// <paramref name="dependent"/>. A single counter-example is enough to say no.</summary>
    private static bool Determines(TabularTable table, string determinant, string dependent)
    {
        var mapping = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var key = table.Value(row, determinant);
            if (key is null)
                return false;

            var value = table.Value(row, dependent);

            if (mapping.TryGetValue(key, out var seen))
            {
                if (!string.Equals(seen, value, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                mapping[key] = value;
            }
        }

        // A dependent that never varies at all carries no information; calling it a dependency
        // would propose an entity with one row.
        return mapping.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    /// <summary>
    /// The leading camel/Pascal segment two column names share ("CustomerName" and "CustomerCity"
    /// share "Customer"), or null when they share none.
    /// </summary>
    private static string? SharedPrefix(string first, string second)
    {
        var a = Segments(first);
        var b = Segments(second);

        if (a.Count == 0 || b.Count == 0
            || !string.Equals(a[0], b[0], StringComparison.OrdinalIgnoreCase))
            return null;

        // Both names must carry something beyond the shared part, or they are the same name.
        return a.Count > 1 && b.Count > 1 ? a[0] : null;
    }

    /// <summary>Camel/Pascal segments of a name. A run of capitals stays whole.</summary>
    private static List<string> Segments(string name)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }

            if (char.IsUpper(c) && current.Length > 0 && !char.IsUpper(current[^1]))
                Flush();

            current.Append(c);
        }

        Flush();
        return segments;

        void Flush()
        {
            if (current.Length > 0)
                segments.Add(current.ToString());
            current.Clear();
        }
    }

    private static List<string> DependencyEvidence(
        TabularTable table, string determinant, IReadOnlyList<string> dependents)
    {
        var lines = new List<string>
        {
            $"  {determinant} -> {string.Join(", ", dependents)}"
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 0; row < table.Rows.Count && seen.Count < EvidenceRows; row++)
        {
            var key = table.Value(row, determinant);
            if (key is null || !seen.Add(key))
                continue;

            var values = dependents.Select(d => table.Value(row, d) ?? "(none)");
            lines.Add($"  {key} always has {string.Join(", ", values)}");
        }

        return lines;
    }

    /// <summary>
    /// A numeric column equal to the product or the sum of two others on every row. Storing it
    /// duplicates what the other two already say, and the two can then disagree with it.
    /// </summary>
    private static IEnumerable<NormalizationFinding> DerivedColumns(TabularTable table)
    {
        var numeric = table.Columns
            .Where(c => c.Type is "int" or "decimal")
            .Select(c => c.Name)
            .ToList();

        if (numeric.Count < 3 || table.Rows.Count < MinRowsForDependencies)
            yield break;

        foreach (var target in numeric)
        {
            var operands = numeric.Where(
                n => !string.Equals(n, target, StringComparison.OrdinalIgnoreCase)).ToList();

            for (var i = 0; i < operands.Count; i++)
            {
                for (var j = i + 1; j < operands.Count; j++)
                {
                    var (left, right) = (operands[i], operands[j]);

                    var operatorName =
                        Matches(table, target, left, right, (a, b) => a * b) ? "x"
                        : Matches(table, target, left, right, (a, b) => a + b) ? "+"
                        : null;

                    if (operatorName is null)
                        continue;

                    yield return new NormalizationFinding
                    {
                        Kind = NormalizationFindingKind.DerivedColumn,
                        TableName = table.Name,
                        SourceLabel = table.Label,
                        Columns = [target],
                        Headline = $"Derived column: \"{target}\" is always {left} {operatorName} {right}",
                        Proposal = $"drop {target} and compute it",
                        Evidence = RowEvidence(table, [target, left, right]),
                    };

                    yield break;   // one explanation per column is enough
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="target"/> equals <paramref name="combine"/> of the two operands on
    /// every row, within <see cref="DerivedTolerance"/>. An all-zero set never matches: zero is
    /// both the sum and the product of zeros, and reporting that is worse than reporting nothing.
    /// </summary>
    private static bool Matches(
        TabularTable table,
        string target,
        string left,
        string right,
        Func<decimal, decimal, decimal> combine)
    {
        var sawNonZero = false;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (!TryNumber(table.Value(row, target), out var expected)
                || !TryNumber(table.Value(row, left), out var a)
                || !TryNumber(table.Value(row, right), out var b))
                return false;

            if (Math.Abs(expected - combine(a, b)) > DerivedTolerance)
                return false;

            if (expected != 0m)
                sawNonZero = true;
        }

        return sawNonZero;
    }

    private static bool TryNumber(string? text, out decimal value)
        => decimal.TryParse(
            text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
