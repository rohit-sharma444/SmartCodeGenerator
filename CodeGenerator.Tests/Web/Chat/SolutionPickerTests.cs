using Bunit;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Choosing what the next command acts on, and being told what that is.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SolutionPickerTests
{
    [TestMethod]
    public void The_strip_names_the_root_and_what_is_selected()
    {
        using var context = new BunitContext();
        var session = NewSession(out var root);

        var strip = context.Render<ContextStrip>(p => p.Add(c => c.Session, session));

        StringAssert.Contains(strip.Markup, root);
        StringAssert.Contains(strip.Markup, "projects");
    }

    [TestMethod]
    public void Typing_a_path_and_loading_it_re_roots_the_session()
    {
        using var context = new BunitContext();
        var session = NewSession(out _);
        var other = NewTree();

        var picker = context.Render<SolutionPicker>(p => p.Add(c => c.Session, session));

        picker.Find(".picker-path").Change(other);
        picker.Find(".picker-load").Click();

        Assert.AreEqual(other, session.Root);
    }

    [TestMethod]
    public void A_path_that_is_not_there_is_reported_and_the_root_is_kept()
    {
        using var context = new BunitContext();
        var session = NewSession(out var root);

        var picker = context.Render<SolutionPicker>(p => p.Add(c => c.Session, session));

        picker.Find(".picker-path").Change(Path.Combine(root, "nowhere"));
        picker.Find(".picker-load").Click();

        Assert.AreEqual(root, session.Root);
        Assert.IsNotNull(picker.Find(".picker-problem"));
    }

    [TestMethod]
    public void The_listing_offers_solution_files_and_folders_to_click()
    {
        using var context = new BunitContext();
        var session = NewSession(out var root);

        Directory.CreateDirectory(Path.Combine(root, "Api"));
        File.WriteAllText(Path.Combine(root, "App.slnx"), "<Solution />");

        var picker = context.Render<SolutionPicker>(p => p.Add(c => c.Session, session));

        Assert.IsNotEmpty(picker.FindAll(".picker-solution"));
    }

    [TestMethod]
    public void The_ai_question_is_drawn_and_both_answers_are_wired()
    {
        using var context = new BunitContext();
        var session = NewSession(out _);

        var wait = Task.Run(() => session.Ask(SessionQuestion.YesNo("Did you mean `list classes`?")));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (session.Question is null && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        var page = context.Render<ContextStrip>(p => p.Add(c => c.Session, session));

        StringAssert.Contains(page.Find(".question").TextContent, "list classes");

        // One button per option, drawn by the loop rather than hard-coded, so a four-answer
        // question draws four. Clicking the primary one is answering with index 0.
        Assert.HasCount(2, page.FindAll(".question-option"));

        page.Find(".question-option.is-primary").Click();

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, wait.Result);
    }

    private static ChatSession NewSession(out string root)
    {
        root = NewTree();

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }

    private static string NewTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pk-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
