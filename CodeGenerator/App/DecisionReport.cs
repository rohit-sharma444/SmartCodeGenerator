using System.Text;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace CodeGenerator.App;

/// <summary>
/// Renders the discovery decision trail for the review loop. Pure: it takes a model and
/// returns lines, touching no console. That is what makes the review loop's newest verb
/// testable without driving the loop.
/// </summary>
internal static class DecisionReport
{
    /// <summary>
    /// How many names one summary group shows before it is cut short. A real domain drops
    /// dozens of nouns, and an uncapped list pushes the model itself off the screen on every
    /// redraw — which is the thing the review loop exists to show.
    /// </summary>
    internal const int MaxNamesPerGroup = 8;

    /// <summary>
    /// The form two spellings of the same thing agree on. A record may be written under the
    /// article's spelling ("Cash register") while the user only ever saw the class name
    /// ("CashRegister"), so neither can be matched literally.
    /// </summary>
    internal static string Canonical(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    /// <summary>
    /// The grouped one-liners that sit under the model print: what was rejected, suggested or
    /// skipped, grouped by reason. Kept candidates are omitted — they are the model, printed
    /// immediately above.
    /// </summary>
    internal static IReadOnlyList<string> Summary(CandidateDomainModel model)
    {
        // The log is append-only — it is history, and history is what `why` reports. A summary
        // is not history: it describes the model as it stands right now, under the model print.
        // So it has to answer two questions the raw log cannot.
        //
        // First, what does the model already contain? `add` promotes a suggestion (or overrides
        // a rejection) without writing a decision, so an unfiltered summary goes on telling the
        // user to add a name that is already a class — advice that now fails as a duplicate.
        // Matched canonically, because `add Fairtrade` answers a rejection filed as "FairTrade".
        var current = model.Entities.Select(e => Canonical(e.Name)).ToHashSet(StringComparer.Ordinal);

        // Second, which of a name's decisions still stands? A candidate demoted by the lead and
        // then found in the seed collects a Suggested record and a Skipped one; reporting both
        // files one name under two contradictory headings in the same redraw. Only the last
        // decision is still true.
        var standing = model.Decisions
            .GroupBy(d => Canonical(d.Name), StringComparer.Ordinal)
            .Where(g => !current.Contains(g.Key))
            .Select(g => g.Last())
            .ToList();

        var lines = new List<string>();

        Append(DecisionOutcome.Rejected, "Not classes: ");
        Append(DecisionOutcome.Suggested, "Also found (add <name> to include): ");
        Append(DecisionOutcome.Skipped, "Ignored: ");

        return lines;

        void Append(DecisionOutcome outcome, string prefix)
        {
            var groups = standing
                .Where(d => d.Outcome == outcome)
                .GroupBy(d => d.Reason, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                var names = group.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var shown = string.Join(", ", names.Take(MaxNamesPerGroup));
                var overflow = names.Count > MaxNamesPerGroup
                    ? $"  (+{names.Count - MaxNamesPerGroup} more — try `why`)"
                    : string.Empty;

                lines.Add($"{prefix}{shown} — {group.Key}{overflow}");
            }
        }
    }

    /// <summary>The whole trail for one name, in the order the stages ran.</summary>
    internal static IReadOnlyList<string> ForName(CandidateDomainModel model, string name)
    {
        var wanted = Canonical(name);
        var records = model.Decisions.Where(d => Canonical(d.Name) == wanted).ToList();

        if (records.Count == 0)
        {
            var entity = model.Entities.FirstOrDefault(e => Canonical(e.Name) == wanted);
            return entity is null
                ? new[] { $"No decision recorded for '{name}'." }
                : new[] { $"{entity.Name} was added in the review loop, not discovered." };
        }

        var lines = new List<string> { records[0].Name };

        foreach (var record in records)
        {
            lines.Add($"  {Label(record.Stage),-16} {record.Outcome.ToString().ToLowerInvariant(),-10} {record.Reason}");
            if (!string.IsNullOrWhiteSpace(record.Evidence))
                lines.Add($"  {string.Empty,-16} {string.Empty,-10} {record.Evidence}");
        }

        // The last word on a name is the one worth acting on — a name rejected at shaping is
        // gone whatever seed extraction thought of it.
        var final = records[^1];

        // `add` takes a class name, but a record may be filed under the article's spelling
        // ("Cash register"), so the suggested command has to be given in the form the loop
        // will actually accept. The suggestion list already holds the converted name when the
        // candidate reached it; otherwise the same normalizer the pipeline used converts it.
        var className = model.Suggestions
            .FirstOrDefault(e => Canonical(e.Name) == wanted)?.Name
            ?? NameNormalizer.ToCompoundEntityName(final.Name);

        if (final.Outcome == DecisionOutcome.Rejected)
            lines.Add($"  Add it anyway with: add {className}");
        else if (final.Outcome == DecisionOutcome.Suggested)
            lines.Add($"  Include it with: add {className}");

        return lines;
    }

    /// <summary>Every decision, grouped by outcome — the answer to a bare <c>why</c>.</summary>
    internal static IReadOnlyList<string> All(CandidateDomainModel model)
    {
        if (model.Decisions.Count == 0)
            return new[] { "No decisions recorded." };

        var lines = new List<string> { $"Decisions ({model.Decisions.Count}):" };

        foreach (var outcome in new[]
                 {
                     DecisionOutcome.Kept, DecisionOutcome.Suggested,
                     DecisionOutcome.Rejected, DecisionOutcome.Skipped
                 })
        {
            var names = model.Decisions
                .Where(d => d.Outcome == outcome)
                .Select(d => d.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count > 0)
                lines.Add($"  {outcome} ({names.Count}): {string.Join(", ", names)}");
        }

        lines.Add("Ask `why <name>` for the detail on any one.");
        return lines;
    }

    private static string Label(DecisionStage stage) => stage switch
    {
        DecisionStage.SeedExtraction => "seed extraction",
        DecisionStage.Enrichment => "enrichment",
        DecisionStage.Shaping => "shaping",
        _ => stage.ToString().ToLowerInvariant()
    };
}
