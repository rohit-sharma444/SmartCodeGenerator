using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using DomainDiscovery.SeedExtraction;

namespace DataIngestion.Normalization;

/// <summary>
/// Rewrites the read tables to carry out the findings the user chose. The analyser finds and
/// explains; this is the only thing that changes anything.
/// </summary>
/// <remarks>
/// Every child is built from the ORIGINAL table, and the replacement parent is assembled
/// afterwards from the columns that survived. No finding ever sees a table another finding has
/// already edited, so the result does not depend on the order the user listed them in — which is
/// the bug a straightforward apply-each-in-turn loop would have.
/// </remarks>
public static class NormalizationPlan
{
    /// <summary>
    /// Applies <paramref name="chosen"/> to <paramref name="tables"/>, returning the new table
    /// set: each affected table rewritten, each extracted entity added, and every unaffected
    /// table passed through as it was.
    /// </summary>
    public static IReadOnlyList<TabularTable> Apply(
        IReadOnlyList<TabularTable> tables, IReadOnlyList<NormalizationFinding> chosen)
    {
        var result = new List<TabularTable>();

        // Names already in play, so an extracted entity never collides with a table this run
        // already has — DataModelBuilder would drop the loser as a duplicate and the user's split
        // would disappear without a word.
        var takenNames = new HashSet<string>(
            tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var findings = chosen
                .Where(f => string.Equals(f.TableName, table.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (findings.Count == 0)
            {
                result.Add(table);
                continue;
            }

            var parentEntity = NameNormalizer.ToUserTypedEntityName(table.Name);
            var foreignKey = parentEntity + JsonTableReader.ParentIdSuffix;

            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extracted = new List<TabularTable>();

            // Determinant column -> (new FK column name, one FK value per original row).
            var addedKeys = new List<(string Column, string?[] Values)>();

            foreach (var finding in findings)
            {
                switch (finding.Kind)
                {
                    case NormalizationFindingKind.RepeatingGroup:
                        extracted.Add(RepeatingGroupChild(table, finding, foreignKey, takenNames));
                        foreach (var column in finding.Columns)
                            removed.Add(column);
                        break;

                    case NormalizationFindingKind.NonAtomicValues:
                        extracted.Add(NonAtomicChild(table, finding, foreignKey, takenNames));
                        removed.Add(finding.Columns[0]);
                        break;

                    case NormalizationFindingKind.FunctionalDependency:
                        var (entity, keys) = DependencyParent(table, finding, takenNames);
                        extracted.Add(entity);
                        addedKeys.Add((entity.Name + JsonTableReader.ParentIdSuffix, keys));
                        foreach (var column in finding.Columns)
                            removed.Add(column);
                        break;

                    case NormalizationFindingKind.DerivedColumn:
                        removed.Add(finding.Columns[0]);
                        break;
                }
            }

            result.Add(WithoutColumns(table, removed, addedKeys));
            result.AddRange(extracted);
        }

        return result;
    }

    /// <summary>
    /// One child row per non-empty cell across the group's columns, tagged with its parent's
    /// 1-based row number — 1-based because seeded Ids are assigned 1..N in row order.
    /// </summary>
    private static TabularTable RepeatingGroupChild(
        TabularTable table,
        NormalizationFinding finding,
        string foreignKey,
        HashSet<string> takenNames)
    {
        // The value column is named for what the group holds, not for one of its numbered
        // columns: Item1/Item2/Item3 all hold an Item.
        var valueColumn = ValueColumnName(finding.Columns[0]);

        var child = NewChild(finding.ProposedEntity!, table, foreignKey, valueColumn, takenNames);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            foreach (var column in finding.Columns)
            {
                var value = table.Value(row, column);
                if (!string.IsNullOrWhiteSpace(value))
                    child.Rows.Add([(row + 1).ToString(), value]);
            }
        }

        TypeChild(child);
        return child;
    }

    /// <summary>One child row per split part of each cell.</summary>
    private static TabularTable NonAtomicChild(
        TabularTable table,
        NormalizationFinding finding,
        string foreignKey,
        HashSet<string> takenNames)
    {
        var child = NewChild(
            finding.ProposedEntity!, table, foreignKey,
            JsonTableReader.ScalarValueColumn, takenNames);

        var separator = finding.Separator ?? ',';

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var cell = table.Value(row, finding.Columns[0]);
            if (string.IsNullOrWhiteSpace(cell))
                continue;

            foreach (var part in cell.Split(separator))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    child.Rows.Add([(row + 1).ToString(), trimmed]);
            }
        }

        TypeChild(child);
        return child;
    }

    /// <summary>
    /// The extracted entity for a functional dependency: one row per DISTINCT determinant value,
    /// in first-appearance order, plus the foreign key each original row should carry.
    /// </summary>
    private static (TabularTable Entity, string?[] Keys) DependencyParent(
        TabularTable table, NormalizationFinding finding, HashSet<string> takenNames)
    {
        var entity = new TabularTable
        {
            Name = UniqueName(finding.ProposedEntity!, takenNames),
            SourceLabel = table.Label,
        };

        foreach (var column in finding.Columns)
        {
            var source = table.Columns.First(
                c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

            entity.Columns.Add(new TabularColumn
            {
                Name = column,
                Type = source.Type,
                Facts = source.Facts,
            });
        }

        var determinant = finding.Columns[0];
        // Ordinal, as the analyser compared them: "ada" and "Ada" are two rows, not one row with
        // the second spelling thrown away.
        var rowNumberByValue = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new string?[table.Rows.Count];

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var key = table.Value(row, determinant);
            if (key is null)
                continue;

            if (!rowNumberByValue.TryGetValue(key, out var number))
            {
                entity.Rows.Add(finding.Columns.Select(c => table.Value(row, c)).ToArray());
                number = entity.Rows.Count;   // 1-based, matching the seeded Id
                rowNumberByValue[key] = number;
            }

            keys[row] = number.ToString();
        }

        return (entity, keys);
    }

    /// <summary>A child table with its foreign key and one value column, and nothing else.</summary>
    private static TabularTable NewChild(
        string proposedName,
        TabularTable table,
        string foreignKey,
        string valueColumn,
        HashSet<string> takenNames)
    {
        var child = new TabularTable
        {
            Name = UniqueName(proposedName, takenNames),
            SourceLabel = table.Label,
        };

        child.Columns.Add(new TabularColumn { Name = foreignKey, Type = "int", HoldsRowNumbers = true });
        child.Columns.Add(new TabularColumn { Name = valueColumn });

        return child;
    }

    /// <summary>The value column's name: the group's stem, with its trailing digits gone.</summary>
    private static string ValueColumnName(string firstColumn)
    {
        var end = firstColumn.Length;
        while (end > 0 && char.IsDigit(firstColumn[end - 1]))
            end--;

        var stem = firstColumn[..end].TrimEnd('_', '-', ' ');
        return NameNormalizer.ToFieldName(stem.Length == 0 ? firstColumn : stem);
    }

    /// <summary>The child's value column is typed from the values it actually received, so a
    /// repeating group of ints becomes an int column rather than defaulting to string.</summary>
    private static void TypeChild(TabularTable child)
    {
        var valueColumn = child.Columns[1];
        valueColumn.Type = ColumnTypeInference.Infer(
            child.ColumnValues(valueColumn.Name), valueColumn.Name);
    }

    /// <summary><paramref name="proposed"/>, or the first numbered variant that is free. Claimed
    /// as a side effect, so two findings in one run cannot pick the same name.</summary>
    private static string UniqueName(string proposed, HashSet<string> takenNames)
    {
        if (takenNames.Add(proposed))
            return proposed;

        for (var n = 2; ; n++)
        {
            var candidate = proposed + n;
            if (takenNames.Add(candidate))
                return candidate;
        }
    }

    /// <summary>The parent as it survives: the columns no finding removed, then the foreign keys
    /// the extracted entities need.</summary>
    private static TabularTable WithoutColumns(
        TabularTable table,
        HashSet<string> removed,
        List<(string Column, string?[] Values)> addedKeys)
    {
        var kept = table.Columns.Where(c => !removed.Contains(c.Name)).ToList();

        // Still the table the source described: how much of it was read, the keys it declared on
        // columns that survive, and what it declared about each column.
        var rewritten = new TabularTable
        {
            Name = table.Name,
            SourceLabel = table.SourceLabel,
            IsPartial = table.IsPartial,
            SourceRowCount = table.SourceRowCount,
        };

        rewritten.ForeignKeys.AddRange(table.ForeignKeys.Where(k => !removed.Contains(k.Column)));
        rewritten.Notes.AddRange(table.Notes);

        foreach (var column in kept)
            rewritten.Columns.Add(new TabularColumn
            {
                Name = column.Name,
                Type = column.Type,
                Facts = column.Facts,
                HoldsRowNumbers = column.HoldsRowNumbers,
                IsKey = column.IsKey
            });

        foreach (var (column, _) in addedKeys)
            rewritten.Columns.Add(new TabularColumn { Name = column, Type = "int", HoldsRowNumbers = true });

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var values = kept.Select(c => table.Value(row, c.Name)).ToList();
            values.AddRange(addedKeys.Select(k => k.Values[row]));
            rewritten.Rows.Add(values.ToArray());
        }

        return rewritten;
    }
}
