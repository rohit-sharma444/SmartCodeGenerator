using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Asking the AI question in the browser instead of on a console nobody is watching.
/// </summary>
/// <remarks>
/// The pipeline calls its confirm delegate synchronously from inside itself, so the answer has to
/// travel back to a blocked worker thread. Every test here is about that thread being released:
/// by an answer, or by the session ending. A path that does not release it hangs the command and
/// the console behind it.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class AiConfirmationTests
{
    [TestMethod]
    public void An_answer_releases_the_waiting_command()
    {
        var session = NewSession();
        var asked = SessionQuestion.YesNo("Did you mean `list classes`?");

        var wait = Task.Run(() => session.Ask(asked));

        WaitUntil(() => session.Question is not null);

        session.Answer(0);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)), "the answer must release the thread");
        Assert.AreEqual(0, wait.Result, "index 0 is Yes, which is the only thing that means yes");
        Assert.IsNull(session.Question, "the question must be cleared once answered");
    }

    [TestMethod]
    public void Declining_releases_it_too()
    {
        var session = NewSession();

        var wait = Task.Run(() => session.Ask(SessionQuestion.YesNo("Did you mean something?")));

        WaitUntil(() => session.Question is not null);

        session.Answer(1);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, wait.Result);
    }

    [TestMethod]
    public void Ending_the_session_releases_it_with_a_cancellation()
    {
        // The path that would otherwise hang forever: the tab is closed while the question is on
        // screen, the host abandons the session, and nobody is left to answer.
        var session = NewSession();

        var wait = Task.Run(() => session.Ask(SessionQuestion.YesNo("Did you mean something?")));

        WaitUntil(() => session.Question is not null);

        session.End();

        Assert.IsTrue(
            wait.Wait(TimeSpan.FromSeconds(5)),
            "abandoning the session must release the thread, or the console waits forever");

        // Null, not index 1: nobody answered, and a caller that read that as "they said No"
        // could not tell it apart from a deliberate refusal.
        Assert.IsNull(wait.Result, "an unanswered question is a cancellation");
    }

    [TestMethod]
    public void Answering_when_nothing_was_asked_does_nothing()
    {
        var session = NewSession();

        session.Answer(0);

        Assert.IsNull(session.Question);
    }

    [TestMethod]
    public void The_question_raises_changed_so_every_tab_shows_it()
    {
        var session = NewSession();
        var raised = 0;

        session.Changed += () => raised++;

        var wait = Task.Run(() => session.Ask(SessionQuestion.YesNo("Did you mean something?")));

        WaitUntil(() => session.Question is not null);

        Assert.IsGreaterThan(0, raised);

        session.Answer(1);
        wait.Wait(TimeSpan.FromSeconds(5));
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.IsTrue(condition(), "the condition never became true");
    }

    private static ChatSession NewSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }
}
