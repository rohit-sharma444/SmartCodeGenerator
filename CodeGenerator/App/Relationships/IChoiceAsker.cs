namespace CodeGenerator.App.Relationships;

/// <summary>
/// Asks a question with a fixed set of answers, wherever the user happens to be.
/// </summary>
/// <remarks>
/// A port rather than a call to <c>Menu.Choose</c>, because the same conversation has to happen on
/// a console and on a Blazor page — and because a piped run supplies none of it. Null means
/// cancelled: Esc, a closed tab, or input that ran out. Every caller must treat null as the
/// safest answer rather than as a default.
/// </remarks>
public interface IChoiceAsker
{
    int? Choose(string title, IReadOnlyList<string> options, int defaultIndex);
}
