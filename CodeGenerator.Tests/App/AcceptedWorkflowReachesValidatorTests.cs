using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The full path an accepted, ordered Status proposal travels: DomainModelMapper carries it
/// once accepted-and-ordered, ValidatorTemplate emits it, and the compiled validator actually
/// refuses a record that breaks it.
/// </summary>
[TestClass]
public class AcceptedWorkflowReachesValidatorTests
{
    [TestMethod]
    public void An_accepted_ordered_status_proposal_refuses_a_backward_move()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved", "Shipped"] },
            Evidence = "Status holds only Draft, Approved, Shipped",
            Accepted = true,
            Order = ["Draft", "Approved", "Shipped"]
        });
        order.Fields.Add(status);
        order.Fields.Add(new CandidateField { Name = "Id", Type = "int" });
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Order");

        Assert.HasCount(1, definition.WorkflowRules);

        var source = ValidatorTemplate.Generated(
            "Acme", definition, [], referencesRequired: false, workflowRules: definition.WorkflowRules);

        StringAssert.Contains(source, "private static void Workflow(");
        StringAssert.Contains(source, "        Workflow,");
        StringAssert.Contains(source, "stored.Status");
    }
}
