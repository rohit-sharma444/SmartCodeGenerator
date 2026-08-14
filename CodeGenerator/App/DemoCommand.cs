namespace CodeGenerator.App;

/// <summary>
/// Recognizes the bare <c>demo</c> command.
/// </summary>
/// <remarks>
/// Handled in the CLI, like <see cref="ClearCommand"/> and <see cref="StatusCommand"/>, because the
/// demo now runs real commands through the pipeline and needs the session that owns it. The old
/// interception in <c>CommandFront</c> — which printed a canned class and ran nothing — is gone.
/// </remarks>
internal static class DemoCommand
{
    public static bool IsDemo(string? input)
        => (input ?? string.Empty).Trim().Equals("demo", StringComparison.OrdinalIgnoreCase);
}
