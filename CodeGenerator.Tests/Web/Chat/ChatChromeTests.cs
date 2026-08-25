using Bunit;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The chrome around the conversation: the header's Change button, the picker that hides behind
/// it, the empty state, and the verdict a turn is labelled with.
/// </summary>
/// <remarks>
/// What is here is what a renderer can actually decide. The frame itself — that the transcript
/// scrolls inside its own box while the composer stays put — is layout, and no bUnit assertion
/// can see it; that is checked by looking at the running page.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChatChromeTests
{
    [TestMethod]
    public void The_change_button_asks_the_page_to_open_the_picker()
    {
        using var context = new BunitContext();
        var session = NewSession();
        var asked = 0;

        var strip = context.Render<ContextStrip>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnChangeSolution, () => asked++));

        strip.Find(".strip-picker").Click();

        Assert.AreEqual(1, asked);
    }

    /// <summary>Open, the button offers the way back out. A control that only ever opens a panel
    /// leaves the reader looking for the close.</summary>
    [TestMethod]
    public void The_change_button_says_close_while_the_picker_is_open()
    {
        using var context = new BunitContext();
        var session = NewSession();

        var strip = context.Render<ContextStrip>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.PickerOpen, true));

        StringAssert.Contains(strip.Find(".strip-picker").TextContent, "Close");
    }

    [TestMethod]
    public void The_tree_button_asks_the_page_to_toggle_the_tree()
    {
        using var context = new BunitContext();
        var session = NewSession();
        var asked = 0;

        var strip = context.Render<ContextStrip>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnToggleTree, () => asked++));

        strip.Find(".strip-tree").Click();

        Assert.AreEqual(1, asked);
    }

    /// <summary>
    /// The two header buttons must stay separately addressable.
    /// </summary>
    /// <remarks>
    /// They share `strip-change` for their look. When Tree was added it shared nothing else, so
    /// every selector written for "the Change button" started matching Tree first and two tests
    /// began clicking a control with no handler on it. This fails if that ever collapses again.
    /// </remarks>
    [TestMethod]
    public void The_header_buttons_are_told_apart_by_more_than_their_order()
    {
        using var context = new BunitContext();
        var session = NewSession();

        var strip = context.Render<ContextStrip>(p => p.Add(c => c.Session, session));

        Assert.HasCount(1, strip.FindAll(".strip-picker"));
        Assert.HasCount(1, strip.FindAll(".strip-tree"));

        StringAssert.Contains(strip.Find(".strip-tree").TextContent, "Tree");
        StringAssert.Contains(strip.Find(".strip-picker").TextContent, "Change");
    }

    [TestMethod]
    public void Loading_a_path_that_works_asks_the_page_to_put_the_picker_away()
    {
        using var context = new BunitContext();
        var session = NewSession();
        var other = NewTree();
        var closed = 0;

        var picker = context.Render<SolutionPicker>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnLoaded, () => closed++));

        picker.Find(".picker-path").Change(other);
        picker.Find(".picker-load").Click();

        Assert.AreEqual(other, session.Root);
        Assert.AreEqual(1, closed);
    }

    /// <summary>A panel that shut on a path that did not load would take the problem message with
    /// it, and the box the mistake was typed into.</summary>
    [TestMethod]
    public void Loading_a_path_that_does_not_work_leaves_the_picker_open()
    {
        using var context = new BunitContext();
        var session = NewSession();
        var closed = 0;

        var picker = context.Render<SolutionPicker>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnLoaded, () => closed++));

        picker.Find(".picker-path").Change(Path.Combine(NewTree(), "no-such-folder"));
        picker.Find(".picker-load").Click();

        Assert.AreEqual(0, closed);
        Assert.IsNotNull(picker.Find(".picker-problem"));
    }

    [TestMethod]
    public void The_empty_page_offers_starters_that_fill_the_box_rather_than_run()
    {
        using var context = new BunitContext();
        string? picked = null;

        var welcome = context.Render<ChatWelcome>(p => p
            .Add(c => c.OnPick, (string s) => picked = s));

        var chips = welcome.FindAll(".welcome-chip");

        Assert.IsNotEmpty(chips);

        chips[0].Click();

        Assert.AreEqual(
            welcome.FindAll(".welcome-chip-command")[0].TextContent.Trim(), picked);
    }

    /// <summary>Every starter has to be a command the chat will actually accept. Offering one it
    /// declines is worse than offering nothing.</summary>
    [TestMethod]
    public void Every_starter_is_a_command_the_chat_can_run()
    {
        var starters = CommandCatalog.Starters();

        Assert.IsNotEmpty(starters);

        foreach (var starter in starters)
        {
            Assert.AreNotEqual(
                CommandKind.BuildApp, CommandRoute.Classify(starter.Command), starter.Command);

            Assert.AreNotEqual(
                CommandKind.Batch, CommandRoute.Classify(starter.Command), starter.Command);

            Assert.AreNotEqual(
                CommandKind.Demo, CommandRoute.Classify(starter.Command), starter.Command);

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(starter.Description),
                $"{starter.Command} has no description to show beside it.");
        }
    }

    /// <summary>The rail's colour is not readable to everyone, and Declined and Unrecognized draw
    /// the same amber. The word is what separates them.</summary>
    [TestMethod]
    public void A_turn_says_its_verdict_in_words()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("list classes", CommandKind.Pipeline, ChatVerdict.Succeeded, []),
                new("wibble", CommandKind.Pipeline, ChatVerdict.Unrecognized, []),
                new("build app", CommandKind.BuildApp, ChatVerdict.Declined, []),
                new("create class", CommandKind.Pipeline, ChatVerdict.Failed, []),
            }));

        var words = rendered.FindAll(".turn-verdict").Select(v => v.TextContent.Trim()).ToList();

        CollectionAssert.AreEqual(
            new List<string> { "Succeeded", "Not understood", "Declined", "Failed" }, words);
    }

    [TestMethod]
    public void A_stamped_turn_shows_the_time_it_ran()
    {
        using var context = new BunitContext();

        var at = new DateTimeOffset(2026, 8, 25, 14, 2, 0, TimeSpan.Zero);

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("status", CommandKind.Status, ChatVerdict.Succeeded, [], at)
            }));

        Assert.AreEqual("14:02", rendered.Find(".turn-time").TextContent.Trim());
    }

    /// <summary>An unstamped turn draws no time at all, rather than the first of January in year
    /// one dressed up as a clock.</summary>
    [TestMethod]
    public void An_unstamped_turn_shows_no_time()
    {
        using var context = new BunitContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("status", CommandKind.Status, ChatVerdict.Succeeded, [])
            }));

        Assert.IsEmpty(rendered.FindAll(".turn-time"));
    }

    /// <summary>
    /// Input far enough from every command that the suggester finds nothing.
    /// </summary>
    /// <remarks>
    /// Found by looking at the running page: "Did you mean" was standing on its own under a turn
    /// with no chips after it, because the label was drawn on the verdict while the chips were
    /// drawn on what the suggester returned. Asserted first, so the fixture is known to produce
    /// no guesses before the absence of the label proves anything.
    /// </remarks>
    [TestMethod]
    public void An_unrecognized_turn_with_nothing_to_suggest_asks_nothing()
    {
        const string Nonsense = "wibble the frobnicator";

        Assert.IsEmpty(CommandSuggester.Closest(Nonsense), "the fixture has to have no guesses");

        using var context = new BunitContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new(Nonsense, CommandKind.Pipeline, ChatVerdict.Unrecognized, [])
            }));

        Assert.IsEmpty(rendered.FindAll(".turn-chips-label"));
    }

    [TestMethod]
    public void An_unrecognized_turn_with_guesses_offers_them_under_a_label()
    {
        const string NearMiss = "lst clases";

        Assert.IsNotEmpty(CommandSuggester.Closest(NearMiss), "the fixture has to have guesses");

        using var context = new BunitContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new(NearMiss, CommandKind.Pipeline, ChatVerdict.Unrecognized, [])
            }));

        StringAssert.Contains(rendered.Find(".turn-chips-label").TextContent, "Did you mean");
        Assert.IsNotEmpty(rendered.FindAll(".turn-chip"));
    }

    private static ChatSession NewSession()
        => new(new Workspace(NewTree(), projectPathSet: false), AiLayer.Create(), []);

    private static string NewTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "chat-chrome-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        return root;
    }
}
