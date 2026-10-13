using System.Linq;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.App;

/// <summary>
/// `order status`/`final status`, pure and tested for the same reason ProposalReport is: the
/// review loop reads the console and cannot be driven from a test, so everything decidable is
/// lifted out of it.
/// </summary>
internal static class WorkflowCommands
{
    public static string OrderStatus(CandidateDomainModel model, string className, string valuesText)
    {
        var stated = valuesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var (field, proposal, error) = FindTheStatusProposal(model, className, stated);
        if (error is not null)
            return error;

        var discovered = proposal!.Rule.AllowedValues;

        if (stated.Length != discovered.Count || !discovered.All(v => stated.Contains(v)))
            return $"Order must name every discovered value exactly once: {string.Join(", ", discovered)}.";

        proposal.Order = stated;
        return $"Order set for {className}.{field}: {string.Join(" -> ", stated)}.";
    }

    public static string FinalStatus(CandidateDomainModel model, string className, string valuesText)
    {
        var stated = valuesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var (field, proposal, error) = FindTheStatusProposal(model, className, stated);
        if (error is not null)
            return error;

        var discovered = proposal!.Rule.AllowedValues;
        var unknown = stated.Where(v => !discovered.Contains(v)).ToList();

        if (unknown.Count > 0)
            return $"'{string.Join("', '", unknown)}' was not discovered in {className}.{field}.";

        var finals = new HashSet<string>(proposal.FinalValues);
        foreach (var value in stated)
            finals.Add(value);
        proposal.FinalValues = finals;

        var message = $"{string.Join(", ", stated)} marked as final for {className}.{field}.";
        if (proposal.Order is null)
        {
            message += $" No order stated yet -- run 'order status <values> in {className}'"
                + " or nothing will be enforced.";
        }

        return message;
    }

    /// <summary>
    /// The accepted value list <paramref name="stated"/> is about.
    /// </summary>
    /// <remarks>
    /// Every value list is a Status-kind proposal, so a class can have several once
    /// <c>accept all</c> has run — a Ticket's Priority as well as its Status. The command names
    /// values, not a field, so the values pick it: the one list that holds them all. Failing
    /// that, a field called Status or State. Only a real tie is refused, and the refusal names
    /// the fields and how to pick.
    /// </remarks>
    private static (string? Field, RuleProposal? Proposal, string? Error) FindTheStatusProposal(
        CandidateDomainModel model, string className, IReadOnlyList<string> stated)
    {
        var entity = model.Entities.FirstOrDefault(e =>
            string.Equals(e.Name, className, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
            return (null, null, $"There is no class named '{className}'.");

        var accepted = entity.Fields
            .SelectMany(f => f.Proposals
                .Where(p => p.Kind == RuleProposalKind.Status && p.Accepted)
                .Select(p => (Field: f.Name, Proposal: p)))
            .ToList();

        if (accepted.Count == 0)
            return (null, null, $"{className} has no confirmed status field yet -- accept its Status proposal first.");

        if (accepted.Count == 1)
            return (accepted[0].Field, accepted[0].Proposal, null);

        var holding = accepted
            .Where(a => stated.Count > 0 && stated.All(v => a.Proposal.Rule.AllowedValues.Contains(v)))
            .ToList();

        if (holding.Count == 1)
            return (holding[0].Field, holding[0].Proposal, null);

        var pool = holding.Count > 1 ? holding : accepted;
        var named = pool
            .Where(a => a.Field.Equals("Status", StringComparison.OrdinalIgnoreCase)
                        || a.Field.Equals("State", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (named.Count == 1)
            return (named[0].Field, named[0].Proposal, null);

        var lists = string.Join("; ", pool.Select(
            a => $"{a.Field} holds {string.Join(", ", a.Proposal.Rule.AllowedValues)}"));

        return (null, null,
            $"{className} has more than one confirmed value list, and those values do not pick one: {lists}. "
            + "Give the values of the field you mean, or take back the list you do not want ordered "
            + "with 'unaccept <n>'.");
    }
}
