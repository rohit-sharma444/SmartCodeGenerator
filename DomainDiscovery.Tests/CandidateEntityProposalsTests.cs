using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests;

public class CandidateEntityProposalsTests
{
    [Fact]
    public void An_entity_starts_with_no_proposals()
    {
        var entity = new CandidateEntity { Name = "Booking" };

        Assert.Empty(entity.Proposals);
    }

    [Fact]
    public void A_proposal_carries_its_rule_unaccepted_until_taken()
    {
        var entity = new CandidateEntity { Name = "Booking" };

        entity.Proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.CrossField,
            Properties = ["StartDate", "EndDate"],
            Rule = new ComparisonRule("StartDate", "EndDate", "> 0", "StartDate must not be after EndDate"),
            Evidence = "StartDate is always on or before EndDate, in all 40 rows"
        });

        Assert.False(entity.Proposals[0].Accepted);
        Assert.Equal(2, entity.Proposals[0].Properties.Count);
    }
}
