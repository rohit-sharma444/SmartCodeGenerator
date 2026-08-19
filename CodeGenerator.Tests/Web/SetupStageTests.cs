using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DataIngestion.Schema.Readers;

namespace CodeGenerator.Tests.Web;

/// <summary>The four setup panels. Bindings and blocked-Next only — the rules are the session's
/// and the validators'.</summary>
[TestClass]
public class SetupStageTests
{
    private static BuildSession New(string description = "")
        => new(description, Path.GetTempPath(), AppCustomization.Default);

    [TestMethod]
    public void The_describe_panel_opens_with_the_inline_description_in_it()
    {
        using var context = new BunitContext();
        var session = New("a library system");

        var rendered = context.Render<DescribeStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        Assert.AreEqual("a library system", rendered.Find("textarea").GetAttribute("value"));
    }

    // Blank is a real answer: `build app from csv orders.csv` with no description is supported.
    [TestMethod]
    public void The_describe_panel_allows_a_blank_description()
    {
        using var context = new BunitContext();
        var session = New();

        var rendered = context.Render<DescribeStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsFalse(rendered.Find("button.next").HasAttribute("disabled"));
    }

    [TestMethod]
    public void The_app_type_panel_offers_every_menu_row()
    {
        using var context = new BunitContext();

        var rendered = context.Render<AppTypeStage>(p => p
            .Add(c => c.Session, New("a shop"))
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

    [TestMethod]
    public void The_data_panel_refuses_a_path_that_is_not_there()
    {
        using var context = new BunitContext();
        var session = New("a shop");

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input[type=radio][value=file]").Change(true);
        rendered.Find("input.path").Change(@"C:\nope\missing.csv");
        rendered.Find("button.next").Click();

        Assert.IsNull(session.Source);
        StringAssert.Contains(rendered.Markup, "missing.csv");
    }

    // The console wizard silently keeps the old value on a bad accent, because trapping someone
    // in a terminal loop over a colour is worse. A form has no such excuse.
    [TestMethod]
    public void The_look_panel_blocks_next_on_an_unusable_accent()
    {
        using var context = new BunitContext();

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);
        rendered.Find("input.accent").Change("not-a-colour");

        Assert.IsTrue(rendered.Find("button.next").HasAttribute("disabled"));
    }

    [TestMethod]
    public void The_look_panel_blocks_next_on_an_unusable_namespace_prefix()
    {
        using var context = new BunitContext();

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);
        rendered.Find("input.prefix").Change("9nope-");

        Assert.IsTrue(rendered.Find("button.next").HasAttribute("disabled"));
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
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);

        Assert.IsEmpty(rendered.FindAll("select.layout"));
    }
}
