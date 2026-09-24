using AppScaffolding;
using CodeGenerationEngine.Models;
using CodeGenerator.App;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Covers the review loop's view of the rules the scan proposed: the one-line summary, the
/// numbered listing with evidence, and accepting one or all of them. The loop itself reads the
/// console and cannot be driven from a test, so everything decidable is lifted out of it here —
/// the same reason <see cref="ReviewActionsTests"/> exists.
/// </summary>
[TestClass]
public class ProposalReportTests
{
    private static CandidateDomainModel ModelWithNoProposals()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "Id", Type = "int" });
        model.Entities.Add(order);
        return model;
    }

    /// <summary>
    /// An Order entity with two proposed rules: Status (unaccepted) and Code (unaccepted), the
    /// same shape task 10 would produce from a scan.
    /// </summary>
    private static CandidateDomainModel ModelWithTwoProposals()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "Id", Type = "int" });

        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { Name = "allowedvalues", AllowedValues = ["Draft", "Posted"] },
            Evidence = "holds in every row",
            Accepted = false
        });
        order.Fields.Add(status);

        var code = new CandidateField { Name = "Code", Type = "string" };
        code.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Unique,
            Rule = new PropertyRule { Name = "unique" },
            Evidence = "no value repeats in 12,431 rows",
            Accepted = false
        });
        order.Fields.Add(code);

        model.Entities.Add(order);
        return model;
    }

    /// <summary>Every proposal in the model, walked the same way <see cref="ProposalReport"/>
    /// does, for assertions that need to reach the underlying <see cref="RuleProposal"/>.
    /// </summary>
    private static IEnumerable<RuleProposal> EveryProposal(CandidateDomainModel model) =>
        model.Entities.SelectMany(e => e.Fields).SelectMany(f => f.Proposals);

    /// <summary>An Order entity with one field proposal and one entity-scoped proposal.</summary>
    private static CandidateDomainModel ModelWithAnEntityScopedProposal()
    {
        var model = new CandidateDomainModel();
        var booking = new CandidateEntity { Name = "Booking" };
        booking.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        booking.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        booking.Proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.CrossField,
            Properties = ["StartDate", "EndDate"],
            Rule = new CodeGenerationEngine.Core.Validation.Rules.ComparisonRule(
                "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate"),
            Evidence = "StartDate is always on or before EndDate, in all 40 rows"
        });

        model.Entities.Add(booking);
        return model;
    }

    [TestMethod]
    public void A_model_with_no_proposals_says_nothing()
    {
        Assert.IsNull(ProposalReport.Summary(ModelWithNoProposals()));
    }

    [TestMethod]
    public void The_summary_says_how_many_and_how_to_see_them()
    {
        var line = ProposalReport.Summary(ModelWithTwoProposals());

        StringAssert.Contains(line!, "2");
        StringAssert.Contains(line!, "rules");
    }

    [TestMethod]
    public void Each_proposal_is_numbered_with_its_evidence()
    {
        var lines = ProposalReport.Lines(ModelWithTwoProposals());

        // lines[0] is Lines()'s own header ("Rules your data supports. None is applied until
        // you accept it."), added before the numbered proposals -- so the first proposal is
        // lines[1].
        StringAssert.Contains(lines[1], "1.");
        StringAssert.Contains(lines[1], "Order.Status");
        StringAssert.Contains(lines[1], "holds");
        StringAssert.Contains(lines[1], "[ ]");        // unticked
    }

    [TestMethod]
    public void Accepting_one_ticks_only_that_one()
    {
        var model = ModelWithTwoProposals();

        ProposalReport.Accept(model, "1");

        var all = EveryProposal(model).ToList();
        Assert.IsTrue(all[0].Accepted);
        Assert.IsFalse(all[1].Accepted);
    }

    [TestMethod]
    public void Accepting_all_ticks_all()
    {
        var model = ModelWithTwoProposals();

        ProposalReport.Accept(model, "all");

        Assert.IsTrue(EveryProposal(model).All(p => p.Accepted));
    }

    /// <summary>The console had no way back from an accept, where the page has always let a box be
    /// unticked.</summary>
    [TestMethod]
    public void Unaccepting_one_takes_back_only_that_one()
    {
        var model = ModelWithTwoProposals();
        ProposalReport.Accept(model, "all");

        var message = ProposalReport.Unaccept(model, "2");

        var all = EveryProposal(model).ToList();
        Assert.IsTrue(all[0].Accepted);
        Assert.IsFalse(all[1].Accepted);
        StringAssert.Contains(message, "No longer accepted");
    }

    [TestMethod]
    public void Unaccepting_all_takes_them_all_back()
    {
        var model = ModelWithTwoProposals();
        ProposalReport.Accept(model, "all");

        ProposalReport.Unaccept(model, "all");

        Assert.IsFalse(EveryProposal(model).Any(p => p.Accepted));
    }

    [TestMethod]
    public void A_number_that_is_not_there_is_reported_not_thrown()
    {
        var model = ModelWithTwoProposals();

        var message = ProposalReport.Accept(model, "9");

        StringAssert.Contains(message, "9");
        Assert.IsFalse(EveryProposal(model).Any(p => p.Accepted));
    }

    [TestMethod]
    public void Approving_without_accepting_takes_no_proposal()
    {
        // The decision of this phase, pinned by a test so convenience cannot reverse it: a scripted
        // build, and anyone who just types approve, gets none of it.
        var model = ModelWithTwoProposals();

        var classes = DomainModelMapper.ToClassDefinitions(model, out _);

        Assert.IsFalse(
            classes.SelectMany(c => c.Properties).SelectMany(p => p.Rules)
                .Any(r => r.Name is "allowedvalues" or "unique" or "pattern"));
    }

    [TestMethod]
    public void An_entity_scoped_proposal_is_numbered_alongside_field_proposals()
    {
        var model = ModelWithAnEntityScopedProposal();

        var lines = ProposalReport.Lines(model);

        Assert.IsTrue(lines.Any(l => l.Contains("1.") && l.Contains("Booking")
            && l.Contains("StartDate is always on or before EndDate")));
    }

    [TestMethod]
    public void Accepting_an_entity_scoped_proposal_by_number_takes_it()
    {
        var model = ModelWithAnEntityScopedProposal();

        var result = ProposalReport.Accept(model, "1");

        Assert.IsTrue(model.Entities.Single().Proposals[0].Accepted);
        StringAssert.Contains(result, "Accepted");
    }

    [TestMethod]
    public void A_status_proposal_starts_with_no_order_and_no_finals()
    {
        var proposal = new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved"
        };

        Assert.IsNull(proposal.Order);
        Assert.IsEmpty(proposal.FinalValues);
    }

    [TestMethod]
    public void A_status_proposal_keeps_a_stated_order_and_finals()
    {
        var proposal = new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Order = ["Draft", "Approved"],
            FinalValues = new HashSet<string> { "Approved" }
        };

        CollectionAssert.AreEqual(new[] { "Draft", "Approved" }, proposal.Order?.ToArray());
        Assert.Contains("Approved", proposal.FinalValues);
    }
}
