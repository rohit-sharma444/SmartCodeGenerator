using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The theme drop-down on the workflow page, and that the choice outlives the run.
/// </summary>
/// <remarks>
/// Remembered in the workspace rather than in the browser: the host listens on a new port every
/// run, and browser storage is per origin, so a choice kept there would be forgotten each time.
/// </remarks>
[TestClass]
public class WorkflowThemeTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "theme-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void The_page_opens_light_when_nothing_has_been_chosen()
    {
        using var context = PageFor(_root);

        var page = context.Render<BuildWorkflow>();

        Assert.AreEqual("light", ThemeOf(page));
        Assert.AreEqual("light", page.Find("select.theme-choice").GetAttribute("value"));
    }

    [TestMethod]
    public void The_drop_down_offers_every_theme()
    {
        using var context = PageFor(_root);

        var page = context.Render<BuildWorkflow>();

        CollectionAssert.AreEqual(
            WorkflowThemes.All.Select(t => t.Key).ToArray(),
            page.FindAll("select.theme-choice option").Select(o => o.GetAttribute("value")).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Light", "Dark", "High contrast", "Warm" },
            page.FindAll("select.theme-choice option").Select(o => o.TextContent.Trim()).ToArray());
    }

    [TestMethod]
    public void Choosing_a_theme_repaints_the_page()
    {
        using var context = PageFor(_root);

        var page = context.Render<BuildWorkflow>();

        foreach (var theme in WorkflowThemes.All)
        {
            page.Find("select.theme-choice").Change(theme.Key);

            Assert.AreEqual(theme.Key, ThemeOf(page));
        }
    }

    [TestMethod]
    public void The_next_run_opens_the_way_it_was_left()
    {
        using (var first = PageFor(_root))
            first.Render<BuildWorkflow>().Find("select.theme-choice").Change("contrast");

        using var second = PageFor(_root);

        Assert.AreEqual("contrast", ThemeOf(second.Render<BuildWorkflow>()));
    }

    [TestMethod]
    public void Going_back_to_light_is_remembered_too()
    {
        using (var first = PageFor(_root))
        {
            var page = first.Render<BuildWorkflow>();
            page.Find("select.theme-choice").Change("dark");
            page.Find("select.theme-choice").Change("light");
        }

        using var second = PageFor(_root);

        Assert.AreEqual("light", ThemeOf(second.Render<BuildWorkflow>()));
    }

    [TestMethod]
    public void A_broken_file_opens_light_rather_than_failing()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(WorkflowThemeStore.PathFor(_root), "{ not json");

        Assert.AreEqual("light", WorkflowThemeStore.Load(_root));
    }

    /// <summary>A theme this build does not know - removed, or typed into the file by hand -
    /// must not reach the page as a data-theme no stylesheet rule matches.</summary>
    [TestMethod]
    public void An_unknown_theme_opens_light()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(WorkflowThemeStore.PathFor(_root), """{ "Theme": "neon" }""");

        Assert.AreEqual("light", WorkflowThemeStore.Load(_root));
    }

    /// <summary>The finished view has no stage diagram, but the page is still in the colours
    /// that were chosen.</summary>
    [TestMethod]
    public void A_finished_run_keeps_the_chosen_theme()
    {
        WorkflowThemeStore.Save(_root, "warm");

        using var context = PageFor(_root, out var session);
        session.Cancel();

        Assert.AreEqual("warm", ThemeOf(context.Render<BuildWorkflow>()));
    }

    /// <summary>Every theme the drop-down offers has colours of its own in the stylesheet — one
    /// added to the list and not to the CSS would repaint nothing.</summary>
    [TestMethod]
    public void Every_theme_but_light_has_its_own_colours()
    {
        var css = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "CodeGenerator", "wwwroot", "workflow.css"));

        foreach (var theme in WorkflowThemes.All.Where(t => t.Key != WorkflowThemes.Default))
            StringAssert.Contains(css, $""":root:has(.workflow[data-theme="{theme.Key}"])""");
    }

    private static string? ThemeOf(IRenderedComponent<BuildWorkflow> page)
        => page.Find("main.workflow").GetAttribute("data-theme");

    private static BunitContext PageFor(string root) => PageFor(root, out _);

    private static BunitContext PageFor(string root, out BuildSession session)
    {
        var context = new BunitContext();

        // The page focuses the open panel, which is an interop call the strict default refuses.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();

        session = new BuildSession("a shop", root, AppCustomization.Default);
        context.Services.AddSingleton(session);

        return context;
    }
}
