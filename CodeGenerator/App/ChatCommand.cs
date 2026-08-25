namespace CodeGenerator.App;

/// <summary>
/// Recognizes the bare <c>chat</c> command.
/// </summary>
/// <remarks>
/// Written the same way as <see cref="DemoCommand"/> and <see cref="ClearCommand"/>: a bare word,
/// trimmed and case-insensitive, so <c>help chat</c> stays a help topic.
/// <para>
/// The predicate arrives ahead of the command it names, because the chat page has to be able to
/// decline <c>chat</c> — you are already in it — and it can only decline a kind
/// <see cref="CommandRoute"/> actually produces.
/// </para>
/// </remarks>
internal static class ChatCommand
{
    public static bool IsChat(string? input)
        => (input ?? string.Empty).Trim().Equals("chat", StringComparison.OrdinalIgnoreCase);
}
