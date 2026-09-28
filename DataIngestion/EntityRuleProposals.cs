using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DomainDiscovery.Model;

namespace DataIngestion;

/// <summary>
/// Turns one entity's complete rows into the Cross-Field and Calculation rules they support —
/// the entity-wide counterpart to <see cref="RuleProposals"/>, which scans one column at a time.
/// </summary>
/// <remarks>
/// Every threshold here is a counter-example test over the FULL table, never a sample, for the
/// same reason RuleProposals is: a rule inferred from a sample is a guess about rows not yet
/// seen, and the app it generates would reject data its own source contains.
/// </remarks>
public static class EntityRuleProposals
{
    /// <summary>Above this many numeric columns, the Calculation scan's triple search is
    /// skipped for the entity rather than left to grow cubically. Twelve columns is 1,320
    /// candidate triples; fifty would be well over a hundred thousand.</summary>
    private const int MaxNumericColumnsForCalculationScan = 12;

    public static IReadOnlyList<EntityRuleProposal> For(CandidateEntity entity, TabularTable table)
    {
        var proposals = new List<EntityRuleProposal>();

        var dateFields = entity.Fields.Where(f => f.Type == "DateTime").ToList();
        var numericFields = entity.Fields.Where(f => f.Type is "int" or "decimal").ToList();

        // The raw source column for each field, not the normalized field name: a source header
        // that was renamed off its class name (see DataModelBuilder.ResolveColumns) never appears
        // in the table verbatim, so looking cells up by field.Name would silently find nothing.
        var columnByField = entity.Fields.ToDictionary(
            f => f.Name,
            f => DataModelBuilder.ColumnFor(table, f.Name) ?? f.Name);

        AddCrossFieldProposals(proposals, table, dateFields, columnByField);
        AddCrossFieldProposals(proposals, table, numericFields, columnByField);

        if (numericFields.Count <= MaxNumericColumnsForCalculationScan)
            AddCalculationProposals(proposals, table, numericFields, columnByField);

        return proposals;
    }

    private static void AddCrossFieldProposals(
        List<EntityRuleProposal> proposals, TabularTable table, List<CandidateField> fields,
        IReadOnlyDictionary<string, string> columnByField)
    {
        foreach (var left in fields)
        foreach (var right in fields)
        {
            if (left.Name == right.Name)
                continue;

            if (!HoldsForEveryRow(
                    table, (columnByField[left.Name], left.Type),
                    (columnByField[right.Name], right.Type), out var sawStrictlyLess))
                continue;

            if (!sawStrictlyLess)
                continue;

            var rows = table.Rows.Count.ToString("N0");

            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.CrossField,
                Properties = [left.Name, right.Name],
                Rule = new ComparisonRule(
                    left.Name, right.Name, "> 0", $"{left.Name} must not be after {right.Name}"),
                Evidence = $"{left.Name} is always on or before {right.Name}, in all {rows} rows{table.PartialNote}"
            });
        }
    }

    /// <summary>Whether left &lt;= right in every row AS THE GENERATED APP LOADS IT, with at
    /// least one row where both are actually present.
    /// <paramref name="sawStrictlyLess"/> guards against two columns that are always equal,
    /// which would otherwise pass in both directions and propose two redundant rules.</summary>
    /// <remarks>
    /// A row with a blank is judged too, never skipped: the seed file writes a blank cell as
    /// nothing, the model's non-nullable property keeps its default, and the emitted rule then
    /// compares that default. An open booking's blank EndDate is 0001-01-01 to the app, which
    /// every StartDate is after -- so skipping that row offered a rule the row breaks.
    /// </remarks>
    private static bool HoldsForEveryRow(
        TabularTable table, (string Column, string Type) leftField, (string Column, string Type) rightField,
        out bool sawStrictlyLess)
    {
        sawStrictlyLess = false;
        var compared = 0;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var left = AsLoaded(table.Value(row, leftField.Column), leftField.Type, out var leftPresent);
            var right = AsLoaded(table.Value(row, rightField.Column), rightField.Type, out var rightPresent);

            var order = left.CompareTo(right);

            if (order > 0)
                return false;

            if (!leftPresent || !rightPresent)
                continue;

            compared++;

            if (order < 0)
                sawStrictlyLess = true;
        }

        return compared > 0;
    }

    private static void AddCalculationProposals(
        List<EntityRuleProposal> proposals, TabularTable table, List<CandidateField> numericFields,
        IReadOnlyDictionary<string, string> columnByField)
    {
        foreach (var target in numericFields)
        {
            var others = numericFields.Where(f => f.Name != target.Name).ToList();

            for (var i = 0; i < others.Count; i++)
            for (var j = i + 1; j < others.Count; j++)
            {
                TryProposeSumOrProduct(proposals, table, columnByField, target, others[i], others[j]);
                TryProposePercentage(proposals, table, columnByField, target, others[i], others[j]);
                TryProposePercentage(proposals, table, columnByField, target, others[j], others[i]);
            }
        }
    }

    private static void TryProposeSumOrProduct(
        List<EntityRuleProposal> proposals, TabularTable table, IReadOnlyDictionary<string, string> columnByField,
        CandidateField target, CandidateField left, CandidateField right)
    {
        if (ZeroCounterExamples(
                table, (columnByField[target.Name], target.Type), (columnByField[left.Name], left.Type),
                (columnByField[right.Name], right.Type), (l, r) => l + r, out var sumRows))
            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.Calculation,
                Properties = [target.Name, left.Name, right.Name],
                Rule = new ArithmeticRule(target.Name, left.Name, "plus", right.Name),
                Evidence = $"{target.Name} always equals {left.Name} plus {right.Name}, in all {sumRows:N0} rows{table.PartialNote}"
            });

        if (ZeroCounterExamples(
                table, (columnByField[target.Name], target.Type), (columnByField[left.Name], left.Type),
                (columnByField[right.Name], right.Type), (l, r) => l * r, out var productRows))
            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.Calculation,
                Properties = [target.Name, left.Name, right.Name],
                Rule = new ArithmeticRule(target.Name, left.Name, "times", right.Name),
                Evidence = $"{target.Name} always equals {left.Name} times {right.Name}, in all {productRows:N0} rows{table.PartialNote}"
            });
    }

    private static void TryProposePercentage(
        List<EntityRuleProposal> proposals, TabularTable table, IReadOnlyDictionary<string, string> columnByField,
        CandidateField target, CandidateField net, CandidateField rate)
    {
        if (!ZeroCounterExamples(
                table, (columnByField[target.Name], target.Type), (columnByField[net.Name], net.Type),
                (columnByField[rate.Name], rate.Type), (n, r) => n * (1 + r), out var rows))
            return;

        proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.Calculation,
            Properties = [target.Name, net.Name, rate.Name],
            Rule = new PercentageRule(target.Name, net.Name, rate.Name),
            Evidence = $"{target.Name} always equals {net.Name} times (1 plus {rate.Name}), in all {rows:N0} rows{table.PartialNote}"
        });
    }

    private static bool ZeroCounterExamples(
        TabularTable table, (string Column, string Type) targetField, (string Column, string Type) leftField,
        (string Column, string Type) rightField, System.Func<decimal, decimal, decimal> combine,
        out int rowsCompared)
    {
        rowsCompared = 0;

        // Every row, as loaded, for the reason HoldsForEveryRow gives: a blank operand is 0 to
        // the generated app, and 120 is not 100 plus 0. Only the rows with all three present
        // count as evidence.
        for (var row = 0; row < table.Rows.Count; row++)
        {
            var t = (decimal)AsLoaded(table.Value(row, targetField.Column), targetField.Type, out var tPresent);
            var l = (decimal)AsLoaded(table.Value(row, leftField.Column), leftField.Type, out var lPresent);
            var r = (decimal)AsLoaded(table.Value(row, rightField.Column), rightField.Type, out var rPresent);

            if (System.Math.Abs(t - combine(l, r)) > 0.0001m)
                return false;

            if (tPresent && lPresent && rPresent)
                rowsCompared++;
        }

        return rowsCompared > 0;
    }

    /// <summary>
    /// The value the generated app holds for <paramref name="cell"/> in a property of
    /// <paramref name="type"/>: a DateTime for a date, a decimal for an int or a decimal, and the
    /// type's default when the cell is blank or does not parse.
    /// </summary>
    /// <remarks>
    /// Parsed exactly as SeedFileWriter.Write parses it -- trimmed, the same number styles, the
    /// invariant culture -- because a value the seed file cannot write is left at the model's
    /// default, and that default is what every emitted rule then judges.
    /// <paramref name="present"/> is false for exactly those cells.
    /// </remarks>
    private static System.IComparable AsLoaded(string? cell, string type, out bool present)
    {
        var raw = cell?.Trim() ?? string.Empty;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        switch (type)
        {
            case "DateTime":
                present = System.DateTime.TryParse(
                    raw, invariant, System.Globalization.DateTimeStyles.None, out var date);
                return present ? date : default(System.DateTime);

            case "int":
                present = int.TryParse(raw, System.Globalization.NumberStyles.Integer, invariant, out var whole);
                return present ? (decimal)whole : 0m;

            default:
                present = decimal.TryParse(raw, System.Globalization.NumberStyles.Number, invariant, out var number);
                return present ? number : 0m;
        }
    }
}
