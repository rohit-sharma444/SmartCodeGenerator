using CodeGenerator.App.Relationships;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// Asks the relationship questions on the page.
/// </summary>
/// <remarks>
/// Through <see cref="ChatSession.Ask"/> and never through <c>Console.WriteLine</c>: this page
/// captures a command's output and renders it only once the command finishes, which is the stated
/// reason <c>build app</c>, <c>batch</c> and <c>demo</c> are declined here. The question therefore
/// has to carry its own text — the proposal and the evidence included — because anything printed
/// alongside it would arrive after the answer was needed.
/// </remarks>
public sealed class ChatChoiceAsker(ChatSession session) : IChoiceAsker
{
    public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex)
        => session.Ask(new SessionQuestion(title, options, defaultIndex));
}
