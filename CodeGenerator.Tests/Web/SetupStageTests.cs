using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DataIngestion.Schema.Readers;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The four setup panels. Bindings and refused steps only — the rules are the session's and the
/// validators'.
/// </summary>
/// <remarks>
/// None of these panels has a Next button any more: each offers its step to the diagram, which
/// draws it in the box being answered. So "can the user move on" is a question about the
/// <see cref="StageNavigation"/> the panel was handed, and taking the step is calling it.
/// </remarks>
[TestClass]
public class SetupStageTests
{
    [TestMethod]
    public void The_describe_panel_opens_with_the_inline_description_in_it()
    {
        using var context = new BunitContext();
        var session = New("a library system");

        var rendered = context.Render<DescribeStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        Assert.AreEqual("a library system", rendered.Find("textarea").GetAttribute("value"));
    }

    // Blank is a real answer: `build app from csv orders.csv` with no description is supported.
    [TestMethod]
    public void The_describe_panel_allows_a_blank_description()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();

        context.Render<DescribeStage>(p => p
            .Add(c => c.Session, New())
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsTrue(navigation.CanContinue);
    }

    [TestMethod]
    public async Task The_describe_panel_takes_the_description_when_the_step_is_taken()
    {
        using var context = new BunitContext();
        var session = New();
        var navigation = new StageNavigation();

        var rendered = context.Render<DescribeStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("textarea").Change("a shop that sells books");

        // What the page does with the step: apply, and advance only if the panel took it.
        Assert.IsTrue(await navigation.ApplyAsync());
        session.Advance();

        Assert.AreEqual("a shop that sells books", session.Description);
        Assert.AreEqual(BuildStage.AppType, session.Current);
    }

    [TestMethod]
    public void The_app_type_panel_offers_every_menu_row()
    {
        using var context = new BunitContext();

        var rendered = context.Render<AppTypeStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        Assert.HasCount(
            CodeGenerator.App.ApplicationTypeMenu.Labels.Count,
            rendered.FindAll("input[type=radio]"));
    }

    // A format with no reader in this build is refused before it is chosen, not after — the same
    // courtesy DataSourceInterview.FormatLabel pays.
    [TestMethod]
    public void The_data_panel_disables_a_format_this_build_cannot_read()
    {
        using var context = new BunitContext();

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input[type=radio][value=file]").Change(true);

        var unreadable = DataSourceReaders.Keys
            .Where(k => DataSourceReaders.TryParseKind(k, out var kind)
                        && !DataSourceReaders.TryCreate(kind, out _))
            .ToList();

        Assert.IsNotEmpty(unreadable);

        foreach (var key in unreadable)
            Assert.IsTrue(
                rendered.Find($"option[value={key}]").HasAttribute("disabled"),
                $"{key} has no reader and should not be selectable");
    }

    /// <summary>
    /// Dropping an oversized file used to end the run. OpenReadStream throws IOException past the
    /// cap, nothing caught it, and a throw out of a component handler takes the circuit — so the
    /// page froze and the console was told the tab had been closed. It is a spreadsheet that is too
    /// big, which is a sentence, not a crash.
    /// </summary>
    [TestMethod]
    public void The_data_panel_refuses_an_oversized_drop_with_a_sentence()
    {
        using var context = new BunitContext();
        var session = New("a shop");

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input[type=radio][value=file]").Change(true);

        rendered.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(
                new string('x', 11 * 1024 * 1024), "huge.csv"));

        var failure = rendered.Find(".failure").TextContent;

        StringAssert.Contains(failure, "10 MB");
        StringAssert.Contains(failure, "path box");

        // A refusal, not a crash: the workflow is still running and still on this stage.
        Assert.AreEqual(SessionPhase.Running, session.Phase);
    }

    // The step is offered even when the path is wrong: reporting the mistake after the click beats
    // a control that stays dark without saying why.
    [TestMethod]
    public async Task The_data_panel_refuses_a_path_that_is_not_there()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();                    // the data node is the open one
        var navigation = new StageNavigation();

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input[type=radio][value=file]").Change(true);
        rendered.Find("input.path").Change(@"C:\nope\missing.csv");

        // Refused, so the page does not advance and the panel says why.
        Assert.IsFalse(await navigation.ApplyAsync());

        Assert.IsNull(session.Source);
        Assert.AreEqual(BuildStage.Data, session.Current);
        StringAssert.Contains(rendered.Markup, "missing.csv");
    }

    // The console wizard silently keeps the old value on a bad accent, because trapping someone
    // in a terminal loop over a colour is worse. A form has no such excuse — the step is refused.
    [TestMethod]
    public void The_look_panel_refuses_the_step_on_an_unusable_accent()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);
        rendered.Find("input.accent").Change("not-a-colour");

        Assert.IsTrue(navigation.HasStep, "the step should be visible, just not usable");
        Assert.IsFalse(navigation.CanContinue);
    }

    [TestMethod]
    public void The_look_panel_refuses_the_step_on_an_unusable_namespace_prefix()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);
        rendered.Find("input.prefix").Change("9nope-");

        Assert.IsFalse(navigation.CanContinue);
    }

    // Leaving the customization alone is still answering the question, and the box above has no
    // other way to tell "defaults" from "never reached".
    [TestMethod]
    public async Task The_look_panel_answers_its_node_even_when_nothing_was_customized()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        var navigation = new StageNavigation();

        context.Render<LookStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsFalse(session.HoldsAnAnswer(BuildStage.Look));

        Assert.IsTrue(await navigation.ApplyAsync());

        Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Look));
        Assert.AreEqual("defaults", StageSummary.For(session, BuildStage.Look));
    }

    // Asking a console app for a window shape is nonsense, and the console wizard already skips
    // the question for such a target.
    [TestMethod]
    public void The_look_panel_hides_the_layout_field_for_a_target_with_no_window()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        session.SetTarget(new AppScaffolding.Targets.ConsoleTarget());

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);

        Assert.IsEmpty(rendered.FindAll("select.layout"));
    }

    // Every setup panel is a step in a walk, so every one of them has to hand the diagram
    // something to draw — a panel that offers nothing is a workflow with no way on.
    [TestMethod]
    public void Every_setup_panel_offers_a_step()
    {
        using var context = new BunitContext();

        var describe = new StageNavigation();
        context.Render<DescribeStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, describe)
            .Add(c => c.OnAdvance, () => { }));

        var appType = new StageNavigation();
        context.Render<AppTypeStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, appType)
            .Add(c => c.OnAdvance, () => { }));

        var data = new StageNavigation();
        context.Render<DataStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, data)
            .Add(c => c.OnAdvance, () => { }));

        var look = new StageNavigation();
        context.Render<LookStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.Navigation, look)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsTrue(describe.HasStep, "describe offers no step");
        Assert.IsTrue(appType.HasStep, "app type offers no step");
        Assert.IsTrue(data.HasStep, "data offers no step");
        Assert.IsTrue(look.HasStep, "look offers no step");
    }

    private static BuildSession New(string description = "")
        => new(description, Path.GetTempPath(), AppCustomization.Default);
}
