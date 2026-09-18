using CodeGenerationEngine.Models;
using CodeGenerator.App;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Drives `order status`/`final status` the way <see cref="BuildAppAddCommandTests"/> drives
/// `add`: through the internal dispatch method the review loop's switch calls, with a raw
/// `parts` array shaped exactly the way `line.Split(' ', 3, ...)` produces it -- the loop itself
/// reads the console and cannot be driven from a test.
/// </summary>
[TestClass]
public class BuildAppWorkflowCommandTests
{
    private static string[] Parts(params string[] parts) => parts;

    private static CandidateDomainModel ModelWithAnAcceptedStatusProposal()
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved", "Shipped"] },
            Evidence = "Status holds only Draft, Approved, Shipped",
            Accepted = true
        });
        entity.Fields.Add(status);
        model.Entities.Add(entity);
        return model;
    }

    [TestMethod]
    public void OrderStatusLine_RecoversTheValueListAndClassName_FromTheThreeTokenSplit()
    {
        // `line.Split(' ', 3, ...)` hands the loop exactly three parts for
        // `order status Draft,Approved,Shipped in Order`:
        // ["order", "status", "Draft,Approved,Shipped in Order"]. OrderStatusLine has to
        // recover the value list and the class name from that merged third token itself.
        var model = ModelWithAnAcceptedStatusProposal();

        var line = BuildAppCommand.OrderStatusLine(
            model, Parts("order", "status", "Draft,Approved,Shipped in Order"));

        var proposal = model.Entities.Single().Fields.Single().Proposals.Single();
        CollectionAssert.AreEqual(new[] { "Draft", "Approved", "Shipped" }, proposal.Order?.ToArray());
        StringAssert.Contains(line, "Order set");
    }

    [TestMethod]
    public void FinalStatusLine_RecoversTheValueListAndClassName_FromTheThreeTokenSplit()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var line = BuildAppCommand.FinalStatusLine(
            model, Parts("final", "status", "Shipped in Order"));

        var proposal = model.Entities.Single().Fields.Single().Proposals.Single();
        Assert.Contains("Shipped", proposal.FinalValues);
        StringAssert.Contains(line, "marked as final");
    }

    [TestMethod]
    public void OrderStatusLine_ReportsUsage_WhenThereIsNoInSeparator()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var line = BuildAppCommand.OrderStatusLine(
            model, Parts("order", "status", "Draft,Approved,Shipped"));

        StringAssert.Contains(line, "Usage:");
    }
}
