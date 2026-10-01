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
        using var context = NewContext();
        string? sent = null;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find("textarea").Input("wibble");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("wibble", sent);
    }

    [TestMethod]
    public void Undo_and_redo_are_disabled_with_nothing_to_undo()
    {
        using var context = NewContext();
        var composer = context.Render<ChatComposer>();

        Assert.IsTrue(composer.Find(".composer-undo").HasAttribute("disabled"));
        Assert.IsTrue(composer.Find(".composer-redo").HasAttribute("disabled"));
        Assert.AreEqual("Nothing to undo", composer.Find(".composer-undo").GetAttribute("title"));
    }

    [TestMethod]
    public void Undo_sends_undo_and_names_what_it_would_undo()
    {
        using var context = NewContext();
        string? sent = null;
        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.CanUndo, true)
            .Add(c => c.UndoLabel, "add property Pages to Book")
            .Add(c => c.OnSend, (string s) => sent = s));

        Assert.AreEqual("Undo: add property Pages to Book", composer.Find(".composer-undo").GetAttribute("title"));
        composer.Find(".composer-undo").Click();
        Assert.AreEqual("undo", sent);
    }

    [TestMethod]
    public void Redo_sends_redo()
    {
        using var context = NewContext();
        string? sent = null;
        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.CanRedo, true)
            .Add(c => c.RedoLabel, "add property Pages to Book")
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find(".composer-redo").Click();
        Assert.AreEqual("redo", sent);
    }

    [TestMethod]
    public void Undo_is_disabled_while_a_command_runs()
    {
        using var context = NewContext();
        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.CanUndo, true)
            .Add(c => c.Disabled, true));

        Assert.IsTrue(composer.Find(".composer-undo").HasAttribute("disabled"));
    }

    [TestMethod]
    public void Shift_enter_does_not_send()
    {
        using var context = NewContext();
        var sent = 0;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => sent++));

        composer.Find("textarea").Input("wibble");
        composer.Find("textarea").KeyDown(
            new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        Assert.AreEqual(0, sent);
    }

    [TestMethod]
    public void Typing_a_command_prefix_opens_the_dropdown()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>();

        composer.Find("textarea").Input("list");

        Assert.IsNotEmpty(composer.FindAll(".suggestion"));
    }

    [TestMethod]
    public void Enter_inserts_the_selection_while_the_dropdown_is_open()
    {
        using var context = NewContext();
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
        using var context = NewContext();
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
        using var context = NewContext();

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.History, new List<string> { "status", "list classes" }));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.AreEqual("list classes", composer.Find("textarea").GetAttribute("value"));
    }

    [TestMethod]
    public void The_composer_is_disabled_while_a_command_runs()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>(p => p.Add(c => c.Disabled, true));

        Assert.IsTrue(composer.Find("textarea").HasAttribute("disabled"));
    }

    [TestMethod]
    public void Sending_clears_the_box()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => { }));

        composer.Find("textarea").Input("wibble");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("", composer.Find("textarea").GetAttribute("value"));
    }

    /// <summary>
    /// Typing must not write the box back.
    /// </summary>
    /// <remarks>
    /// Blazor patches the DOM for any attribute whose rendered value differs from the last render.
    /// With `value` bound to the text being typed, every keystroke sends a value back to the
    /// browser and anything typed while that was in flight is rolled back. Measured in a real
    /// browser on loopback: clean at ten characters a second, one character lost at twenty, most
    /// of the line lost above that — and a fast typist reaches twenty in bursts.
    /// <para>
    /// So the value this component renders changes only when the component itself sets the text.
    /// This is the assertable half of that; the rest was found by typing into the page.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void Typing_does_not_write_the_box_back()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>();

        composer.Find("textarea").Input("list classes");

        Assert.AreEqual("", composer.Find("textarea").GetAttribute("value"));
    }


    /// <summary>
    /// Enter on an empty box does nothing.
    /// </summary>
    /// <remarks>
    /// The console passes empty input to the pipeline and prints nothing for it, so a stray Enter
    /// there costs a blank line. Here it left a turn with no input and no output — visible, and
    /// there for the rest of the conversation. Doing nothing is what the console appears to do.
    /// </remarks>
    [TestMethod]
    public void Enter_on_an_empty_box_sends_nothing()
    {
        using var context = NewContext();
        var sent = 0;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => sent++));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        composer.Find("textarea").Input("   ");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual(0, sent);
    }

    /// <summary>
    /// Completing a command and pressing Enter runs what was completed.
    /// </summary>
    /// <remarks>
    /// The box is replaced when the component writes a completion into it, and the element on its
    /// way out fires a change event carrying the fragment that was typed. Acted on, that quietly
    /// rewrites the command back: complete `list classes`, press Enter, and `list` is what runs.
    /// Found by completing a command in the page and reading the transcript.
    /// </remarks>
    [TestMethod]
    public void Completing_then_sending_sends_what_was_completed()
    {
        using var context = NewContext();
        string? sent = null;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find("textarea").Input("list");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        var completed = composer.Find("textarea").GetAttribute("value");

        Assert.IsNotNull(completed);
        Assert.AreNotEqual("list", completed, "the completion must have replaced the fragment");

        // The stale change the replaced element fires on its way out must have nowhere to land.
        // oninput already reports every edit, paste included, so a change handler here adds
        // nothing and arrives holding text the box no longer contains.
        Assert.ThrowsExactly<MissingEventHandlerException>(
            () => composer.Find("textarea").Change("list"));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual(completed, sent);
    }

    /// <summary>A "did you mean" chip fills the box from outside the composer.</summary>
    [TestMethod]
    public void The_box_can_be_filled_from_outside()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>();

        // On the renderer dispatcher, which is where the page calls it from.
        composer.InvokeAsync(() => composer.Instance.Fill("list classes")).Wait();

        Assert.AreEqual("list classes", composer.Find("textarea").GetAttribute("value"));
    }

    /// <summary>The visible twin of the Enter rule. Enter stays the fast path; this is the one
    /// that can be found without being told about.</summary>
    [TestMethod]
    public void The_send_button_sends_what_is_in_the_box()
    {
        using var context = NewContext();
        string? sent = null;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find("textarea").Input("status");
        composer.Find(".composer-send").Click();

        Assert.AreEqual("status", sent);
    }

    [TestMethod]
    public void The_send_button_is_dead_until_something_is_typed()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>();

        Assert.IsTrue(composer.Find(".composer-send").HasAttribute("disabled"));

        composer.Find("textarea").Input("status");

        Assert.IsFalse(composer.Find(".composer-send").HasAttribute("disabled"));
    }

    /// <summary>Whitespace is not a command. The console prints nothing for an empty line, and a
    /// live button over a box holding two spaces promises otherwise.</summary>
    [TestMethod]
    public void The_send_button_is_dead_for_whitespace()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>();

        composer.Find("textarea").Input("   ");

        Assert.IsTrue(composer.Find(".composer-send").HasAttribute("disabled"));
    }

    [TestMethod]
    public void The_send_button_is_dead_while_a_command_is_running()
    {
        using var context = NewContext();

        var composer = context.Render<ChatComposer>(p => p.Add(c => c.Disabled, true));

        Assert.IsTrue(composer.Find(".composer-send").HasAttribute("disabled"));
    }

    /// <summary>Loose interop: the box takes the caret back after the component writes into it,
    /// and focusing is a call the strict default refuses.</summary>
    private static BunitContext NewContext()
    {
        var context = new BunitContext();

        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context;
    }
}
