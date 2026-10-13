using Bunit;
using CodeGenerator.Web.Components;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The progress list the long stages share. It exists to be assertable: a panel in the middle of
/// scaffolding or compiling cannot be rendered from a test, because the work would have to really
/// run — so what it draws is lifted out to here.
/// </summary>
[TestClass]
public class ProgressListTests
{
    [TestMethod]
    public void Draws_nothing_at_all_before_anything_starts()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ProgressList>(p => p.Add(c => c.Current, (string?)null));

        Assert.IsEmpty(rendered.FindAll(".progress"));
    }

    [TestMethod]
    public void A_failed_step_is_drawn_as_failed_never_with_the_done_tick()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ProgressList>(p => p
            .Add(c => c.Done, ["Wrote 14 files", "The compiler reported problems"])
            .Add(c => c.Failed, new HashSet<string> { "The compiler reported problems" }));

        Assert.AreEqual("Wrote 14 files", rendered.Find("li.step-done").TextContent.Trim());
        Assert.HasCount(1, rendered.FindAll("li.step-done"));
        Assert.AreEqual("The compiler reported problems", rendered.Find("li.step-failed").TextContent.Trim());
    }

    [TestMethod]
    public void Shows_the_step_that_is_running_with_its_elapsed_time()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ProgressList>(p => p
            .Add(c => c.Current, "Compiling it")
            .Add(c => c.Seconds, 12));

        var current = rendered.Find(".step-current");

        StringAssert.Contains(current.TextContent, "Compiling it");
        StringAssert.Contains(current.TextContent, "12");
    }

    [TestMethod]
    public void Keeps_the_steps_that_are_already_finished()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ProgressList>(p => p
            .Add(c => c.Done, ["Wrote 14 files"])
            .Add(c => c.Current, "Compiling it"));

        Assert.HasCount(1, rendered.FindAll(".step-done"));
        StringAssert.Contains(rendered.Find(".step-done").TextContent, "Wrote 14 files");
    }

    // The list is the only part of the page that changes with nobody touching it, so a screen
    // reader has to be told about it rather than left to notice.
    [TestMethod]
    public void Announces_the_running_step()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ProgressList>(p => p.Add(c => c.Current, "Writing it"));

        Assert.AreEqual("polite", rendered.Find(".step-current").GetAttribute("aria-live"));
    }

    [TestMethod]
    public void Still_lists_the_finished_steps_once_nothing_is_running()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ProgressList>(p => p
            .Add(c => c.Done, ["Wrote 14 files", "Compiled"])
            .Add(c => c.Current, (string?)null));

        Assert.HasCount(2, rendered.FindAll(".step-done"));
        Assert.IsEmpty(rendered.FindAll(".step-current"));
    }

    // Razor passes a STRING parameter the literal text of its attribute unless it starts with @,
    // so `Current="_step"` compiles happily and draws the words "_step" on the page. Nothing in
    // the compiler catches it and no rendered test of this component can, because the mistake is
    // at the call site — so the call sites are checked here.
    [TestMethod]
    public void Every_progress_list_passes_its_label_as_an_expression()
    {
        var components = Path.Combine(RepoRoot(), "CodeGenerator", "Web", "Components");

        var offenders = Directory
            .EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, number) => (file, number: number + 1, line))
                .Where(x => x.line.Contains("<ProgressList")
                            && x.line.Contains("Current=\"")
                            && !x.line.Contains("Current=\"@")))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.number}")
            .ToList();

        Assert.IsEmpty(offenders, $"a literal label reaches the page: {string.Join(", ", offenders)}");
    }

    /// <summary>The repo root, identified by the single Directory.Build.props that lives there.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;

        Assert.IsNotNull(dir, "could not locate the repo root above the test binary");

        return dir!.FullName;
    }
}
