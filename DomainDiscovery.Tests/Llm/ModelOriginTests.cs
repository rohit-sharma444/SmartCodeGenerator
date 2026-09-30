using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

public class ModelOriginTests
{
    private static LlmDomainModel Raw() => new()
    {
        Classes =
        {
            new LlmClass { Name = "Member", Role = "Party",
                Properties = { new LlmProperty { Name = "Name", Type = "string" } } },
            new LlmClass { Name = "Loan", Role = "Transaction",
                Properties = { new LlmProperty { Name = "MemberId", Type = "int" },
                               new LlmProperty { Name = "DueDate", Type = "date" } } },
            new LlmClass { Name = "2nd", Role = "Item" }
        }
    };

    [Fact]
    public void WithNoOriginTheModelIsTheLanguageModelAsBefore()
    {
        var model = LlmModelNormalizer.Normalize(Raw());

        Assert.All(model.Entities, e => Assert.Equal(Provenance.Model, e.Provenance));
        Assert.Contains(model.Decisions, d => d.Name == "Member"
            && d.Stage == DecisionStage.ModelProposal && d.Reason == DecisionReasons.ProposedByTheModel);
    }

    [Fact]
    public void ABuiltInOriginTagsEveryEntityFieldAndDecision()
    {
        var model = LlmModelNormalizer.Normalize(Raw(), ModelOrigin.BuiltIn("Library"));

        Assert.All(model.Entities, e =>
        {
            Assert.Equal(Provenance.Data, e.Provenance);
            Assert.All(e.Fields, f => Assert.Equal(Provenance.Data, f.Provenance));
        });
        Assert.All(model.Decisions, d => Assert.Equal(DecisionStage.BuiltInDomain, d.Stage));
        Assert.Contains(model.Decisions, d => d.Name == "Member"
            && d.Reason == "from the built-in Library domain");
        // The link still forms: origin changes labels, not rules.
        Assert.Contains(model.Entities.Single(e => e.Name == "Loan").Relationships,
            r => r.Target == "Member");
    }
}
