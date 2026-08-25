namespace CodeGenerator.App;

/// <summary>
/// Which handler an input belongs to.
/// </summary>
/// <remarks>
/// Public because a <c>ChatTurn</c> carries one and a Blazor component takes that as a parameter —
/// a public component cannot expose an internal type.
/// </remarks>
public enum CommandKind
{
    /// <summary>Leave. In the console that ends the process; in the chat it ends the session.</summary>
    Exit,

    Clear,

    History,

    Help,

    Status,

    Demo,

    Ai,

    /// <summary>Open the chat page. Declined inside the chat, which is already it.</summary>
    Chat,

    BuildApp,

    Batch,

    /// <summary>Everything else: the natural-language pipeline decides.</summary>
    Pipeline
}
