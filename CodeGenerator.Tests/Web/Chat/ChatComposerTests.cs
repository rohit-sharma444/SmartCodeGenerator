using Bunit;
using CodeGenerator.Web.Components;
using Microsoft.AspNetCore.Components.Web;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Typing into the chat.
/// </summary>
/// <remarks>
/// The Enter rule is the whole point of these tests. A composer where Enter sometimes sends and
/// sometimes completes, and the user cannot tell which, is worse than one with no completion at
/// all.
/// </remarks>
[TestClass]
public class ChatComposerTests
{
    [TestMethod]
    public void Enter_sends_when_no_dropdown_is_open()
    {
        using var context = new BunitContext();
        string? sent = null;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find("textarea").Change("status");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("status", sent);
    }

    [TestMethod]
    public void Shift_enter_does_not_send()
    {
        using var context = new BunitContext();
        var sent = 0;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => sent++));

        composer.Find("textarea").Change("status");
        composer.Find("textarea").KeyDown(
            new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        Assert.AreEqual(0, sent);
    }

    [TestMethod]
    public void Typing_a_command_prefix_opens_the_dropdown()
    {
        using var context = new BunitContext();

        var composer = context.Render<ChatComposer>();

        composer.Find("textarea").Input("list");

        Assert.IsNotEmpty(composer.FindAll(".suggestion"));
    }

    [TestMethod]
    public void Enter_inserts_the_selection_while_the_dropdown_is_open()
    {
        using var context = new BunitContext();
        var sent = 0;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => sent++));

        composer.Find("textarea").Input("list");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual(0, sent, "Enter must complete, not send, while the dropdown is open");
        Assert.IsEmpty(composer.FindAll(".suggestion"), "and it must close the dropdown");
        StringAssert.StartsWith(composer.Find("textarea").GetAttribute("value") ?? "", "list");
    }

    [TestMethod]
    public void Escape_closes_the_dropdown_and_then_enter_sends()
    {
        using var context = new BunitContext();
        string? sent = null;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find("textarea").Input("list");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.IsEmpty(composer.FindAll(".suggestion"));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("list", sent);
    }

    [TestMethod]
    public void Up_recalls_history_when_the_dropdown_is_closed()
    {
        using var context = new BunitContext();

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.History, new List<string> { "status", "list classes" }));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.AreEqual("list classes", composer.Find("textarea").GetAttribute("value"));
    }

    [TestMethod]
    public void The_composer_is_disabled_while_a_command_runs()
    {
        using var context = new BunitContext();

        var composer = context.Render<ChatComposer>(p => p.Add(c => c.Disabled, true));

        Assert.IsTrue(composer.Find("textarea").HasAttribute("disabled"));
    }

    [TestMethod]
    public void Sending_clears_the_box()
    {
        using var context = new BunitContext();

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => { }));

        composer.Find("textarea").Change("status");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("", composer.Find("textarea").GetAttribute("value"));
    }
}
