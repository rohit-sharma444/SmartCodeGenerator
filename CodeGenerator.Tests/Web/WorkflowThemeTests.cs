using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The Light/Dark switch on the workflow page, and that the choice outlives the run.
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

        Assert.AreEqual("light", page.Find("main.workflow").GetAttribute("data-theme"));
        Assert.AreEqual("false", page.Find(".theme-toggle").GetAttribute("aria-pressed"));
    }

    [TestMethod]
    public void The_switch_turns_the_page_dark()
    {
        using var context = PageFor(_root);

        var page = context.Render<BuildWorkflow>();
        page.Find(".theme-toggle").Click();

        Assert.AreEqual("dark", page.Find("main.workflow").GetAttribute("data-theme"));
        Assert.AreEqual("true", page.Find(".theme-toggle").GetAttribute("aria-pressed"));
    }

    [TestMethod]
    public void The_next_run_opens_the_way_it_was_left()
    {
        using (var first = PageFor(_root))
            first.Render<BuildWorkflow>().Find(".theme-toggle").Click();

        using var second = PageFor(_root);

        var page = second.Render<BuildWorkflow>();

        Assert.AreEqual("dark", page.Find("main.workflow").GetAttribute("data-theme"));
    }

    [TestMethod]
    public void Switching_back_to_light_is_remembered_too()
    {
        using (var first = PageFor(_root))
        {
            var page = first.Render<BuildWorkflow>();
            page.Find(".theme-toggle").Click();
            page.Find(".theme-toggle").Click();
        }

        using var second = PageFor(_root);

        Assert.AreEqual(
            "light", second.Render<BuildWorkflow>().Find("main.workflow").GetAttribute("data-theme"));
    }

    [TestMethod]
    public void A_broken_file_opens_light_rather_than_failing()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(WorkflowThemeStore.PathFor(_root), "{ not json");

        Assert.IsFalse(WorkflowThemeStore.IsDark(_root));
    }

    /// <summary>The finished view has no stage diagram, but the page is still in the colours
    /// that were chosen.</summary>
    [TestMethod]
    public void A_finished_run_keeps_the_chosen_theme()
    {
        WorkflowThemeStore.Save(_root, dark: true);

        using var context = PageFor(_root, out var session);
        session.Cancel();

        var page = context.Render<BuildWorkflow>();

        Assert.AreEqual("dark", page.Find("main.workflow").GetAttribute("data-theme"));
    }

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
