using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// An accepted Status proposal, once ordered, reaches ClassDefinition.WorkflowRules with no
/// scan and no generated-app run involved.
/// </summary>
[TestClass]
public class WorkflowRulesReachAppTests
{
    [TestMethod]
    public void An_ordered_accepted_status_proposal_becomes_a_workflow_rule()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Accepted = true,
            Order = ["Draft", "Approved"],
            FinalValues = new HashSet<string> { "Approved" }
        });
        order.Fields.Add(status);
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Order");

        Assert.HasCount(1, definition.WorkflowRules);
        Assert.AreEqual(ValidationCategory.Workflow, definition.WorkflowRules[0].Category);
        CollectionAssert.AreEqual(new[] { "Status" }, definition.WorkflowRules[0].Properties.ToArray());
    }

    [TestMethod]
    public void An_accepted_status_proposal_with_no_order_yet_becomes_no_workflow_rule()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Accepted = true
            // Order left null: accepted but never ordered.
        });
        order.Fields.Add(status);
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsEmpty(classes.Single(c => c.Name == "Order").WorkflowRules);
    }

    [TestMethod]
    public void An_unaccepted_status_proposal_becomes_no_workflow_rule_even_if_ordered()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Accepted = false,
            Order = ["Draft", "Approved"]
        });
        order.Fields.Add(status);
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsEmpty(classes.Single(c => c.Name == "Order").WorkflowRules);
    }
}
