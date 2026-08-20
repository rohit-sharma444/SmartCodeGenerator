using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;
using Microsoft.AspNetCore.Components;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Every box a panel asks an answer into is the same width as every other.
/// </summary>
/// <remarks>
/// There was no width rule for a control anywhere in the stylesheet, so each one was whatever its
/// browser default happened to be: a textarea about twenty characters wide, a drop-down as wide as
/// its longest option, a path box shorter than most paths. The edges lined up nowhere.
/// <para>
/// Asserted as a class on the markup rather than as a rendered width, because a test renderer has
/// no layout: what a test can hold is that every panel-level control is marked as one of the boxes
/// that line up, which is the thing a new panel would forget. The look itself was checked in a
/// browser.
/// </para>
/// </remarks>
[TestClass]
public class AnswerAlignmentTests
{
    [TestMethod]
    public void The_describe_panel_marks_its_box()
    {
        using var context = new BunitContext();

        AssertAligned(Describe(context, Session()));
    }

    [TestMethod]
    public void The_data_panel_marks_every_box_including_the_drop()
    {
        using var context = new BunitContext();

        var rendered = Data(context, Session());

        rendered.Find("input[type=radio][value=file]").Change(true);

        AssertAligned(rendered);
    }

    [TestMethod]
    public void The_look_panel_marks_every_box()
    {
        using var context = new BunitContext();

        var rendered = Look(context, Session());

        // The options live behind the "customize" checkbox, which has to be on for them to exist.
        foreach (var box in rendered.FindAll("input[type=checkbox]"))
            box.Change(true);

        AssertAligned(rendered);
    }

    [TestMethod]
    public void The_generate_panel_marks_its_name_box()
    {
        using var context = new BunitContext();

        AssertAligned(Generate(context, OnGenerate()));
    }

    /// <summary>
    /// The review panel's own box lines up; the ones inside the entity cards deliberately do not,
    /// because a card is as wide as a card and a name box in it is as wide as the name.
    /// </summary>
    [TestMethod]
    public void The_review_panel_marks_its_own_box_and_leaves_the_cards_alone()
    {
        using var context = new BunitContext();

        var rendered = Review(context, OnReview());

        Assert.IsTrue(rendered.Find("input.new-class").ClassList.Contains("answer"));
        Assert.IsFalse(rendered.Find(".entity input.entity-name").ClassList.Contains("answer"));
    }

    /// <summary>
    /// Every text-ish control outside an entity card carries the class. A new panel that asks for
    /// something and forgets it is what this is for.
    /// </summary>
    private static void AssertAligned<T>(IRenderedComponent<T> rendered) where T : class, IComponent
    {
        var boxes = rendered
            .FindAll("textarea, select, input:not([type=radio]):not([type=checkbox])")
            .Where(e => e.Closest(".entity") is null && e.Closest(".add-field-row") is null)
            .ToList();

        Assert.IsNotEmpty(boxes, "the panel asks for nothing at all?");

        foreach (var box in boxes)
            Assert.IsTrue(
                box.ClassList.Contains("answer"),
                $"<{box.TagName.ToLowerInvariant()} class=\"{box.ClassName}\"> is not aligned with "
                + "the other boxes");
    }

    // bUnit only offers the typed parameter builder, and each panel's parameters are its own type,
    // so these cannot collapse into one generic helper.
    private static IRenderedComponent<DescribeStage> Describe(
        BunitContext context, BuildSession session)
        => context.Render<DescribeStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

    private static IRenderedComponent<DataStage> Data(
        BunitContext context, BuildSession session)
        => context.Render<DataStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

    private static IRenderedComponent<LookStage> Look(
        BunitContext context, BuildSession session)
        => context.Render<LookStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

    private static IRenderedComponent<GenerateStage> Generate(
        BunitContext context, BuildSession session)
        => context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

    private static IRenderedComponent<ReviewStage> Review(
        BunitContext context, BuildSession session)
        => context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

    private static BuildSession Session()
        => new("a shop", Path.GetTempPath(), AppCustomization.Default);

    private static BuildSession OnReview()
    {
        var session = Session();

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });
        session.SetModel(model);
        session.Advance();

        return session;
    }

    private static BuildSession OnGenerate()
    {
        var session = OnReview();
        session.Advance();

        return session;
    }
}
