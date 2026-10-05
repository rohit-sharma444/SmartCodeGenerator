using Bunit;
using CodeGenerator.App;
using CodeGenerator.App.Relationships;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>The relationship conversation on the page: asked out-of-band, and narrated as a turn
/// once it is over.</summary>
[TestClass]
[DoNotParallelize]
public class ChatRelationshipTests
{
    [TestMethod]
    public void The_asker_puts_the_question_on_the_page_and_returns_the_answer()
    {
        var session = NewSession();
        var asker = new ChatChoiceAsker(session);

        var wait = Task.Run(() => asker.Choose("How are they related?", ["one", "many"], 0));

        WaitUntil(() => session.Question is not null);

        Assert.AreEqual("How are they related?", session.Question!.Text);
        Assert.HasCount(2, session.Question.Options);

        session.Answer(1);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, wait.Result);
    }

    [TestMethod]
    public void What_the_flow_writes_becomes_a_turn_in_the_transcript()
    {
        var session = NewSession();
        var before = session.Turns.Count;

        session.Narrate("Order and Customer are now related.");

        Assert.HasCount(before + 1, session.Turns);
        Assert.Contains(
            "now related",
            string.Join(" ", session.Turns[^1].Blocks.Select(b => b.ToString())));
    }

    [TestMethod]
    public async Task A_command_that_leaves_a_relationship_to_offer_asks_about_it()
    {
        // End to end on the page: the command runs, its turn lands, and only then is the question
        // put up - the offer cannot be printed into the command's own output, because the page
        // captures that and draws it only once the command has finished.
        var session = NewSession(out var root);

        File.WriteAllText(Path.Combine(root, "Order.cs"),
            "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        File.WriteAllText(Path.Combine(root, "Customer.cs"),
            "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var sending = session.Send("list classes");

        WaitUntil(() => session.Question is not null);

        Assert.Contains("Customer", session.Question!.Text);

        // Declining leaves the schema exactly as it was.
        session.Answer(1);

        await sending.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.DoesNotContain("Customer Customer", File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public async Task Creating_a_class_is_not_a_moment_the_offer_appears()
    {
        // Same rule as the console: a run of `create class` must not be interrupted.
        var session = NewSession(out var root);

        File.WriteAllText(Path.Combine(root, "Order.cs"),
            "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        File.WriteAllText(Path.Combine(root, "Customer.cs"),
            "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        await session.Send("create class Invoice").WaitAsync(TimeSpan.FromSeconds(60));

        Assert.IsNull(session.Question, "creating a class must not be interrupted by the offer");
    }

    // U4: the console never offers after undo or redo; the page did, putting back the question
    // the undone command may just have answered.
    [TestMethod]
    [DataRow("undo")]
    [DataRow("redo")]
    public async Task Undo_and_redo_are_not_moments_the_offer_appears(string command)
    {
        var session = NewSession(out var root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Order.cs"),
                "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
            File.WriteAllText(Path.Combine(root, "Customer.cs"),
                "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

            var sending = session.Send(command);
            await Task.WhenAny(sending, Task.Run(() => WaitUntilOrTimeout(() => session.Question is not null, 10)));

            Assert.IsNull(session.Question, command + " must not put the relationship offer up");
            await sending.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (session.Question is not null)
                session.Answer(1);
            session.End();
        }
    }

    private static void WaitUntilOrTimeout(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
    }

    [TestMethod]
    public void All_four_cardinality_answers_are_drawn_as_buttons()
    {
        // The strip drew exactly two buttons before this branch, hard-coded as Yes and No. A
        // cardinality question has four, and they are long sentences rather than words - the row
        // is set to wrap for that reason. bUnit can see the count and the labels; whether four
        // long labels stay legible at a narrow width is the part only a browser shows.
        using var context = new BunitContext();
        var session = NewSession();

        var wait = Task.Run(() => session.Ask(new SessionQuestion(
            "Order -> Customer. How are they related?",
            RelationshipFlow.CardinalityLabels("Order", "Customer"),
            0)));

        WaitUntil(() => session.Question is not null);

        var page = context.Render<ContextStrip>(p => p.Add(c => c.Session, session));

        Assert.HasCount(4, page.FindAll(".question-option"));
        StringAssert.Contains(page.FindAll(".question-option")[1].TextContent, "many Customers");

        session.Answer(3);
        wait.Wait(TimeSpan.FromSeconds(5));
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.IsTrue(condition(), "the condition never became true");
    }

    private static ChatSession NewSession() => NewSession(out _);

    private static ChatSession NewSession(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "cr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }
}
