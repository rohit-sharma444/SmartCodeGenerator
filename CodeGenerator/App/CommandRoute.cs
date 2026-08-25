namespace CodeGenerator.App;

/// <summary>
/// Which handler an input belongs to, decided once for every caller.
/// </summary>
/// <remarks>
/// This was an if-ladder inside <c>CodeGeneratorStarter.Run</c> and a shorter one, in a different
/// order, inside <c>RunOnce</c>. The chat page would have been a third copy, and three copies of
/// an ordered ladder is how the three come to disagree about what `help build app` means.
/// <para>
/// The order is behaviour. <c>help</c> is tested before <c>build app</c> because
/// <c>LiteralCommandTable</c> keys on the exact word "help" and would not match `help build app`
/// at all; <c>RunOnce</c> happens to get that right because <c>IsBuildApp</c> is anchored, which is
/// a coincidence worth pinning down.
/// </para>
/// </remarks>
internal static class CommandRoute
{
    public static CommandKind Classify(string? input)
    {
        var text = input ?? string.Empty;

        // Not trimmed, deliberately: the REPL compares the raw input to "exit", and this must
        // classify exactly what that ladder classified.
        if (text.Equals("exit", StringComparison.OrdinalIgnoreCase))
            return CommandKind.Exit;

        if (ClearCommand.IsClear(text))
            return CommandKind.Clear;

        if (HistoryCommand.IsHistory(text))
            return CommandKind.History;

        if (HelpCommand.IsHelp(text))
            return CommandKind.Help;

        if (StatusCommand.IsStatus(text))
            return CommandKind.Status;

        if (DemoCommand.IsDemo(text))
            return CommandKind.Demo;

        if (AiCommand.IsAiCommand(text))
            return CommandKind.Ai;

        if (BuildAppCommand.IsBuildApp(text))
            return CommandKind.BuildApp;

        if (BatchCommand.IsBatch(text))
            return CommandKind.Batch;

        return CommandKind.Pipeline;
    }
}
