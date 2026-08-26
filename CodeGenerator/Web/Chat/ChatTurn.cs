using CodeGenerator.App;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// How a chat turn turned out.
/// </summary>
/// <remarks>
/// The first three are <c>CommandVerdict</c> under another name, so green, red and amber mean in
/// the browser exactly what they mean in the console: it worked, it was understood and could not be
/// done, the words were not understood.
/// <para>
/// <see cref="Declined"/> is the fourth because a decline is none of those — nothing was attempted.
/// It draws the same amber as <see cref="Unrecognized"/> and is a separate value anyway, because a
/// model that calls a decline "unrecognized" is a model that will eventually print that word.
/// </para>
/// </remarks>
public enum ChatVerdict
{
    Succeeded,

    Failed,

    Unrecognized,

    Declined
}

/// <summary>One exchange: what was typed, what it was, what came back, and when.</summary>
/// <remarks>
/// <paramref name="At"/> is last and optional so that the turns built in tests, which care about
/// none of it, keep compiling. Unset it stays <c>default</c>, and the transcript draws no time at
/// all rather than drawing the first of January in year one.
/// </remarks>
public sealed record ChatTurn(
    string Input,
    CommandKind Kind,
    ChatVerdict Verdict,
    IReadOnlyList<OutputBlock> Blocks,
    DateTimeOffset At = default);

/// <summary>A question the session is waiting on an answer to.</summary>
/// <remarks>
/// Options rather than yes/no, because the relationship flow asks which cardinality and that is
/// four answers. The AI confirmation is the two-option case, built by <see cref="YesNo"/>, so
/// there is one question mechanism on this page rather than two that drift.
/// </remarks>
public sealed record SessionQuestion(
    string Text,
    IReadOnlyList<string> Options,
    int DefaultIndex)
{
    /// <summary>A yes/no question. The default points at No: a question that can change code must
    /// not act because someone dismissed it.</summary>
    public static SessionQuestion YesNo(string text) => new(text, ["Yes", "No"], 1);
}
