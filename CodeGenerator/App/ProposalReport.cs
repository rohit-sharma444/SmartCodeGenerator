using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.App;

/// <summary>
/// The review loop's view of the rules the scan proposed.
/// </summary>
/// <remarks>
/// A summary line rather than a block under every field: PrintModel's whole per-entity printout
/// was commented out because it was reprinted after every action and scrolled the loop away, and
/// a proposals block would arrive the same way. One line says there is something to look at; the
/// `rules` verb shows it.
/// <para>
/// Pure and tested, for the reason ReviewActions is: the loop reads the console and cannot be
/// driven from a test, so everything decidable is lifted out of it.
/// </para>
/// <para>
/// Numbering is by a stable walk -- entities in order, fields in order, proposals in order -- so
/// `accept 2` means the same thing as long as the model has not been edited between listing it
/// and accepting from it. Nothing is cached: every call re-walks the model, so an edit that adds
/// or removes a field is reflected the next time <see cref="Lines"/> or <see cref="Accept"/> runs.
/// </para>
/// </remarks>
internal static class ProposalReport
{
    /// <summary>The one line under the model, or null when the data proposed nothing.</summary>
    public static string? Summary(CandidateDomainModel model)
    {
        var count = All(model).Count;

        if (count == 0)
            return null;

        var accepted = All(model).Count(p => p.Proposal.Accepted);

        return $"  {count} rule(s) proposed from your data, {accepted} accepted — "
             + "type `rules` to see them.";
    }

    /// <summary>Every proposal, numbered from 1, with its evidence and whether it is taken.
    /// </summary>
    public static IReadOnlyList<string> Lines(CandidateDomainModel model)
    {
        var lines = new List<string>();
        var all = All(model);

        if (all.Count == 0)
            return ["  Nothing was proposed from your data."];

        lines.Add("  Rules your data supports. None is applied until you accept it.");

        for (var i = 0; i < all.Count; i++)
        {
            var (entity, field, proposal) = all[i];

            lines.Add(
                $"  {i + 1}. [{(proposal.Accepted ? "x" : " ")}] {entity}.{field} — "
                + $"{proposal.Evidence}");
        }

        lines.Add("  accept <n>   accept all");

        return lines;
    }

    /// <summary>Accepts one proposal by number, or every one, and reports what happened.
    /// </summary>
    public static string Accept(CandidateDomainModel model, string argument)
    {
        var all = All(model);

        if (string.Equals(argument, "all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entry in all)
                entry.Proposal.Accepted = true;

            return $"Accepted {all.Count} rule(s).";
        }

        if (!int.TryParse(argument, out var number) || number < 1 || number > all.Count)
            return $"There is no proposal {argument}. Type `rules` to see them.";

        var picked = all[number - 1];
        picked.Proposal.Accepted = true;

        return $"Accepted: {picked.Entity}.{picked.Field}.";
    }

    private static List<(string Entity, string Field, RuleProposal Proposal)> All(
        CandidateDomainModel model)
    {
        var all = new List<(string, string, RuleProposal)>();

        foreach (var entity in model.Entities)
            foreach (var field in entity.Fields)
                foreach (var proposal in field.Proposals)
                    all.Add((entity.Name, field.Name, proposal));

        return all;
    }
}
