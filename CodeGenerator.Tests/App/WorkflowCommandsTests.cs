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

    private static RuleProposal AddValueList(CandidateDomainModel model, string field, params string[] values)
    {
        var candidate = new CandidateField { Name = field, Type = "string" };
        var proposal = new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = field, Name = "allowedvalues", AllowedValues = [.. values] },
            Evidence = $"{field} holds only {string.Join(", ", values)}",
            Accepted = true
        };
        candidate.Proposals.Add(proposal);
        model.Entities.Single().Fields.Add(candidate);
        return proposal;
    }

    /// <summary>
    /// Every value list is a Status-kind proposal, so after `accept all` a Ticket with a Priority
    /// list as well as a Status list refused every `order status`. The values given name the
    /// field: only Status holds Draft, Approved and Shipped.
    /// </summary>
    [TestMethod]
    public void The_values_given_pick_the_field_when_a_class_has_two_value_lists()
    {
        var model = ModelWithAnAcceptedStatusProposal();
        var priority = AddValueList(model, "Priority", "Low", "High");

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved,Shipped");

        var status = model.Entities.Single().Fields.Single(f => f.Name == "Status").Proposals.Single();
        CollectionAssert.AreEqual(new[] { "Draft", "Approved", "Shipped" }, status.Order?.ToArray());
        Assert.IsNull(priority.Order);
        StringAssert.Contains(result, "Order.Status");
    }

    [TestMethod]
    public void The_values_given_can_pick_a_field_not_called_status()
    {
        var model = ModelWithAnAcceptedStatusProposal();
        var priority = AddValueList(model, "Priority", "Low", "High");

        var result = WorkflowCommands.OrderStatus(model, "Order", "Low,High");

        CollectionAssert.AreEqual(new[] { "Low", "High" }, priority.Order?.ToArray());
        StringAssert.Contains(result, "Order.Priority");
    }

    /// <summary>Values two lists both hold, neither called Status or State: a real ambiguity, so
    /// it is refused, not guessed — and the refusal names both and says how to pick.</summary>
    [TestMethod]
    public void Values_that_fit_two_fields_are_refused_with_the_fields_named()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Ticket" });
        var priority = AddValueList(model, "Priority", "Low", "High");
        var impact = AddValueList(model, "Impact", "Low", "High");

        var result = WorkflowCommands.OrderStatus(model, "Ticket", "Low,High");

        StringAssert.Contains(result, "Priority");
        StringAssert.Contains(result, "Impact");
        StringAssert.Contains(result, "unaccept");
        Assert.IsNull(priority.Order);
        Assert.IsNull(impact.Order);
    }
}
