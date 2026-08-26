using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// A question with more than two answers, asked in the browser.
/// </summary>
/// <remarks>
/// The relationship flow asks which cardinality, which is four answers, not yes and no. The
/// mechanism is the same one the AI confirmation already uses — the answer has to travel back to a
/// worker thread blocked inside the flow — so every test here is about that thread being released.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChoiceQuestionTests
{
    [TestMethod]
    public void A_chosen_index_comes_back_to_the_waiting_thread()
    {
        var session = NewSession();
        var question = new SessionQuestion("How are they related?", ["one", "many", "both"], 0);

        var wait = Task.Run(() => session.Ask(question));

        WaitUntil(() => session.Question is not null);

        session.Answer(2);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)), "the answer must release the thread");
        Assert.AreEqual(2, wait.Result);
        Assert.IsNull(session.Question);
    }

    [TestMethod]
    public void Ending_the_session_releases_it_with_a_cancellation()
    {
        var session = NewSession();

        var wait = Task.Run(() => session.Ask(
            new SessionQuestion("How are they related?", ["one", "many"], 0)));

        WaitUntil(() => session.Question is not null);

        session.End();

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsNull(wait.Result, "an abandoned question is a cancellation, never an answer");
    }

    [TestMethod]
    public void A_question_asked_after_the_session_ended_is_cancelled_rather_than_waited_on()
    {
        var session = NewSession();
        session.End();

        Assert.IsNull(session.Ask(SessionQuestion.YesNo("Set relationships now?")));
    }

    [TestMethod]
    public void A_yes_no_question_still_reads_as_two_options()
    {
        var question = SessionQuestion.YesNo("Set relationships now?");

        Assert.HasCount(2, question.Options);
        Assert.AreEqual(1, question.DefaultIndex);
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
        var root = Path.Combine(Path.GetTempPath(), "cq-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }
}
