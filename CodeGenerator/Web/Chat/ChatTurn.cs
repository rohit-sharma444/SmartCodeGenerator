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

/// <summary>One exchange: what was typed, what it was, and what came back.</summary>
public sealed record ChatTurn(
    string Input,
    CommandKind Kind,
    ChatVerdict Verdict,
    IReadOnlyList<OutputBlock> Blocks);
