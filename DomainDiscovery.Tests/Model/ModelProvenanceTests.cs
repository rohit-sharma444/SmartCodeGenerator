using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class ModelProvenanceTests
{
    [Fact]
    public void ProvenanceHasAMemberForAnLlmProposal()
        => Assert.Equal("Model", Provenance.Model.ToString());

    [Fact]
    public void DecisionStageHasAMemberForAnLlmProposal()
        => Assert.Equal("ModelProposal", DecisionStage.ModelProposal.ToString());

    [Fact]
    public void ProvenanceModelRoundTripsThroughItsName()
    {
        Assert.True(Enum.TryParse<Provenance>(Provenance.Model.ToString(), out var parsed));
        Assert.Equal(Provenance.Model, parsed);
    }
}
