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
        var (proposal, error) = FindTheOneAcceptedStatusProposal(model, className);
        if (error is not null)
            return error;

        var stated = valuesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var discovered = proposal!.Rule.AllowedValues;

        if (stated.Length != discovered.Count || !discovered.All(v => stated.Contains(v)))
            return $"Order must name every discovered value exactly once: {string.Join(", ", discovered)}.";

        proposal.Order = stated;
        return $"Order set for {className}.Status: {string.Join(" -> ", stated)}.";
    }

    public static string FinalStatus(CandidateDomainModel model, string className, string valuesText)
    {
        var (proposal, error) = FindTheOneAcceptedStatusProposal(model, className);
        if (error is not null)
            return error;

        var stated = valuesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var discovered = proposal!.Rule.AllowedValues;
        var unknown = stated.Where(v => !discovered.Contains(v)).ToList();

        if (unknown.Count > 0)
            return $"'{string.Join("', '", unknown)}' was not discovered in {className}.Status.";

        var finals = new HashSet<string>(proposal.FinalValues);
        foreach (var value in stated)
            finals.Add(value);
        proposal.FinalValues = finals;

        var message = $"{string.Join(", ", stated)} marked as final for {className}.Status.";
        if (proposal.Order is null)
        {
            message += $" No order stated yet -- run 'order status <values> in {className}'"
                + " or nothing will be enforced.";
        }

        return message;
    }

    private static (RuleProposal? Proposal, string? Error) FindTheOneAcceptedStatusProposal(
        CandidateDomainModel model, string className)
    {
        var entity = model.Entities.FirstOrDefault(e =>
            string.Equals(e.Name, className, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
            return (null, $"There is no class named '{className}'.");

        var accepted = entity.Fields
            .SelectMany(f => f.Proposals.Where(p => p.Kind == RuleProposalKind.Status && p.Accepted))
            .ToList();

        if (accepted.Count == 0)
            return (null, $"{className} has no confirmed status field yet -- accept its Status proposal first.");

        if (accepted.Count > 1)
            return (null, $"{className} has more than one confirmed status field; not supported yet.");

        return (accepted[0], null);
    }
}
