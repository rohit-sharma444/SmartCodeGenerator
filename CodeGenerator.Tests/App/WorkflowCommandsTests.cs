using System.Linq;
using CodeGenerator.App;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WorkflowCommandsTests
{
    private static CandidateDomainModel ModelWithAnAcceptedStatusProposal(string entityName = "Order")
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = entityName };
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
    public void Order_status_states_the_full_sequence()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved,Shipped");

        var proposal = model.Entities.Single().Fields.Single().Proposals.Single();
        CollectionAssert.AreEqual(new[] { "Draft", "Approved", "Shipped" }, proposal.Order?.ToArray());
        StringAssert.Contains(result, "Order set");
    }

    [TestMethod]
    public void Order_status_refuses_a_class_with_no_accepted_status_proposal()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Order" });

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved");

        StringAssert.Contains(result, "no confirmed status");
        Assert.IsEmpty(model.Entities.Single().Fields);
    }

    [TestMethod]
    public void Order_status_refuses_a_partial_value_list()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved");

        StringAssert.Contains(result, "must name every discovered value");
        Assert.IsNull(model.Entities.Single().Fields.Single().Proposals.Single().Order);
    }

    [TestMethod]
    public void Order_status_refuses_an_unknown_class()
    {
        var model = new CandidateDomainModel();

        var result = WorkflowCommands.OrderStatus(model, "Nonexistent", "Draft,Approved");

        StringAssert.Contains(result, "no class named");
    }

    [TestMethod]
    public void Final_status_marks_one_or_more_values()
    {
        var model = ModelWithAnAcceptedStatusProposal();
        WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved,Shipped");

        var result = WorkflowCommands.FinalStatus(model, "Order", "Shipped");

        var proposal = model.Entities.Single().Fields.Single().Proposals.Single();
        Assert.Contains("Shipped", proposal.FinalValues);
        StringAssert.Contains(result, "marked as final");
    }

    [TestMethod]
    public void Final_status_does_not_warn_when_an_order_was_already_stated()
    {
        var model = ModelWithAnAcceptedStatusProposal();
        WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved,Shipped");

        var result = WorkflowCommands.FinalStatus(model, "Order", "Shipped");

        Assert.DoesNotContain("No order stated", result);
    }

    [TestMethod]
    public void Final_status_warns_when_no_order_has_been_stated_yet()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.FinalStatus(model, "Order", "Shipped");

        var proposal = model.Entities.Single().Fields.Single().Proposals.Single();
        Assert.Contains("Shipped", proposal.FinalValues);
        Assert.IsNull(proposal.Order);
        StringAssert.Contains(result, "marked as final");
        StringAssert.Contains(result, "No order stated yet");
        StringAssert.Contains(result, "order status");
    }

    [TestMethod]
    public void Final_status_refuses_a_value_the_data_never_showed()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.FinalStatus(model, "Order", "NoSuchStatus");

        StringAssert.Contains(result, "was not discovered");
    }

    [TestMethod]
    public void A_class_with_two_accepted_status_proposals_is_refused_not_guessed()
    {
        var model = ModelWithAnAcceptedStatusProposal();
        var second = new CandidateField { Name = "ReviewState", Type = "string" };
        second.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Open", "Closed"] },
            Evidence = "ReviewState holds only Open, Closed",
            Accepted = true
        });
        model.Entities.Single().Fields.Add(second);

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved,Shipped");

        StringAssert.Contains(result, "more than one confirmed status");
    }
}
