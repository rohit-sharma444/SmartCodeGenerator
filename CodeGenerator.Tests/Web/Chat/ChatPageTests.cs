using Bunit;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The page, as a page: that a turn is drawn with the verdict it earned, and that two tabs stay in
/// step.
/// </summary>
/// <remarks>
/// What the session decides is asserted in <c>ChatSessionTests</c> without a renderer. What is
/// here is only what needs one.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChatPageTests
{
    [TestMethod]
    public void A_turn_is_drawn_with_its_verdict_class()
    {
        using var context = NewContext();

        var turns = new List<ChatTurn>
        {
            new("list classes", CommandKind.Pipeline, ChatVerdict.Succeeded,
                [new TextBlock("Customer")]),
            new("wibble", CommandKind.Pipeline, ChatVerdict.Unrecognized,
                [new TextBlock("not understood")]),
            new("build app", CommandKind.BuildApp, ChatVerdict.Declined,
                [new TextBlock("Run it from the console.")]),
        };

        var rendered = context.Render<ChatTranscript>(p => p.Add(c => c.Turns, turns));

        Assert.IsNotNull(rendered.Find(".turn-succeeded"));
        Assert.IsNotNull(rendered.Find(".turn-unrecognized"));
        Assert.IsNotNull(rendered.Find(".turn-declined"));
    }

    [TestMethod]
    public void The_input_is_shown_as_it_was_typed()
    {
        using var context = NewContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("Show Class Order", CommandKind.Pipeline, ChatVerdict.Succeeded, [])
            }));

        StringAssert.Contains(rendered.Find(".turn-said").TextContent, "Show Class Order");
    }

    [TestMethod]
    public void A_code_block_is_drawn_as_code_with_a_copy_button()
    {
        using var context = NewContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("show class Order", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new CodeBlock("Order", "public class Order { }")])
            }));

        Assert.IsNotNull(rendered.Find("pre.block-code"));
        Assert.IsNotNull(rendered.Find(".block-copy"));
        StringAssert.Contains(rendered.Markup, "public");
    }

    [TestMethod]
    public void A_table_block_is_drawn_as_a_table()
    {
        using var context = NewContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("list properties of Order", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new TableBlock(
                        "Properties in Order", ["Type", "Name"], [["int", "Id"]])])
            }));

        Assert.HasCount(2, rendered.FindAll("th"));
        Assert.HasCount(2, rendered.FindAll("td"));
    }

    [TestMethod]
    public void Long_output_is_collapsed_until_asked_for()
    {
        using var context = NewContext();
        var many = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"line {i}"));

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("list classes", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new TextBlock(many)])
            }));

        Assert.DoesNotContain("line 199", rendered.Markup);

        rendered.Find(".block-more").Click();

        StringAssert.Contains(rendered.Markup, "line 199");
    }

    [TestMethod]
    public void Short_output_has_no_show_all_button()
    {
        using var context = NewContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("status", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new TextBlock("one line")])
            }));

        Assert.IsEmpty(rendered.FindAll(".block-more"));
    }

    [TestMethod]
    public void A_second_circuit_repaints_when_the_first_one_sends()
    {
        // Two tabs on one conversation. The session raises Changed; both pages must redraw, or the
        // second tab shows a transcript that stopped growing.
        using var first = NewContext(out var session);
        using var second = NewContext(session);

        var one = first.Render<ChatPage>();
        var two = second.Render<ChatPage>();

        session.Send("help").Wait();

        one.WaitForAssertion(() => Assert.IsNotNull(one.Find(".turn")));
        two.WaitForAssertion(() => Assert.IsNotNull(two.Find(".turn")));
    }

    [TestMethod]
    public void An_unrecognised_turn_offers_what_it_might_have_meant()
    {
        using var context = NewContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("lst classes", CommandKind.Pipeline, ChatVerdict.Unrecognized,
                    [new TextBlock("Command not recognized: 'lst classes'.")])
            }));

        Assert.IsNotEmpty(rendered.FindAll(".turn-chip"));
    }

    [TestMethod]
    public void A_turn_that_worked_offers_nothing()
    {
        using var context = NewContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("list classes", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new TextBlock("Customer")])
            }));

        Assert.IsEmpty(rendered.FindAll(".turn-chip"));
    }

    /// <summary>
    /// A chip fills the box. It never runs.
    /// </summary>
    /// <remarks>
    /// One keystroke away and never a surprise execution: these commands write files and run
    /// `dotnet`, and a guess of the tool's about what you meant is not something to act on because
    /// you clicked near it.
    /// </remarks>
    [TestMethod]
    public void A_chip_fills_the_composer_rather_than_running()
    {
        using var context = NewContext();
        string? offered = null;

        var rendered = context.Render<ChatTranscript>(p => p
            .Add(c => c.Turns, new List<ChatTurn>
            {
                new("lst classes", CommandKind.Pipeline, ChatVerdict.Unrecognized,
                    [new TextBlock("Command not recognized: 'lst classes'.")])
            })
            .Add(c => c.OnSuggestion, (string s) => offered = s));

        var chip = rendered.FindAll(".turn-chip")[0];
        var text = chip.TextContent.Trim();

        chip.Click();

        Assert.AreEqual(text, offered);
    }

    private static BunitContext NewContext()
    {
        var context = new BunitContext();

        // The copy button is an interop call the strict default refuses, the same reason
        // WorkflowPageTests loosens it.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();

        return context;
    }

    private static BunitContext NewContext(out ChatSession session)
    {
        var root = Path.Combine(Path.GetTempPath(), "cp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        session = new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);

        return NewContext(session);
    }

    private static BunitContext NewContext(ChatSession session)
    {
        var context = NewContext();
        context.Services.AddSingleton(session);
        return context;
    }
}
