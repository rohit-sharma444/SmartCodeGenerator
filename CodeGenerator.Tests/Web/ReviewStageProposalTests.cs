using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerationEngine.Models;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// A rule the scan found holding in every row of a field's data, offered on the review card and
/// taken only if someone ticks it. <see cref="ModelEditor"/> and <see cref="RuleProposal"/> are
/// tested where they live; what matters here is that the checkbox reaches the model and that
/// deciding one is saved the way every other edit on this page is.
/// </summary>
[TestClass]
public class ReviewStageProposalTests
{
    [TestMethod]
    public void A_proposal_is_offered_unticked_with_its_evidence()
    {
        using var context = new BunitContext();

        var page = Render(context, SessionWithProposal(), out _);

        var box = page.Find("input.proposal");

        Assert.IsFalse(box.HasAttribute("checked"));
        StringAssert.Contains(page.Markup, "holds in all 12,431 rows");
    }

    [TestMethod]
    public void Ticking_a_proposal_accepts_it()
    {
        using var context = new BunitContext();
        var session = SessionWithProposal();
        var page = Render(context, session, out var proposal);

        page.Find("input.proposal").Change(true);

        Assert.IsNotNull(proposal);
        Assert.IsTrue(proposal.Accepted);
    }

    [TestMethod]
    public void A_field_with_no_proposal_shows_no_checkbox()
    {
        using var context = new BunitContext();

        var page = Render(context, SessionWithNoProposals(), out _);

        Assert.IsEmpty(page.FindAll("input.proposal"));
    }

    [TestMethod]
    public void Ticking_a_proposal_saves_the_session()
    {
        // A closed tab loses unsaved work, and a decision is work. Report() is where every other
        // edit on this page passes through for the same reason.
        using var context = new BunitContext();
        var session = SessionWithProposal();
        var saves = CountSaves(session, out var getSaves);
        var page = Render(context, session, out _);

        page.Find("input.proposal").Change(true);

        Assert.IsGreaterThan(saves, getSaves());
    }

    /// <summary>
    /// Installs the Autosave hook once and hands back a live reader, so a caller can capture a
    /// baseline before the change under test and compare it against the count after — rather
    /// than the counter being reset on every read.
    /// </summary>
    private static int CountSaves(BuildSession session, out Func<int> current)
    {
        var saves = 0;
        session.Autosave = _ => saves++;
        current = () => saves;
        return saves;
    }

    private static IRenderedComponent<ReviewStage> Render(
        BunitContext context, BuildSession session, out RuleProposal? proposal)
    {
        proposal = session.Model!.Entities
            .SelectMany(e => e.Fields)
            .SelectMany(f => f.Proposals)
            .FirstOrDefault();

        return context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));
    }

    private static BuildSession SessionWithProposal()
    {
        var model = AModel();
        var field = model.Entities.Single().Fields.Single();

        field.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Unique,
            Rule = new PropertyRule { PropertyName = field.Name, IsUnique = true },
            Evidence = "holds in all 12,431 rows"
        });

        return SessionWith(model);
    }

    private static BuildSession SessionWithNoProposals() => SessionWith(AModel());

    private static BuildSession SessionWith(CandidateDomainModel model)
    {
        var session = new BuildSession("a library", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);
        session.Advance();

        return session;
    }

    private static CandidateDomainModel AModel()
    {
        var model = new CandidateDomainModel();

        var book = new CandidateEntity { Name = "Book", Provenance = Provenance.Wiki };
        book.Fields.Add(new CandidateField
        {
            Name = "Title", Type = "string", Provenance = Provenance.Wiki
        });

        model.Entities.Add(book);
        model.Suggestions.Add(new CandidateEntity { Name = "Shelf", Provenance = Provenance.Wiki });

        return model;
    }
}
