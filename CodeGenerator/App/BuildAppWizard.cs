using AppScaffolding;
using DataIngestion;

namespace CodeGenerator.App;

/// <summary>What the wizard concluded.</summary>
internal enum WizardDecision
{
    /// <summary>Every question was answered. Build from <see cref="WizardOutcome"/>.</summary>
    Proceed,

    /// <summary>
    /// The user backed out, or stdin closed mid-wizard. Nothing is generated — deliberately not a
    /// "no data" answer, which would hand them a Wikipedia build they never asked for.
    /// </summary>
    Cancelled
}

/// <summary>
/// The answers a <c>build app</c> run needs. <c>Source</c> is null when the user has no data,
/// which is what selects the description-driven discovery path.
/// </summary>
internal sealed record WizardOutcome(
    WizardDecision Decision,
    string Description,
    ITargetScaffolder? Target,
    DataSourceSpec? Source,
    bool Normalize);

/// <summary>
/// Asks what to build, what kind of app it is, and whether there is data — in that order.
/// </summary>
/// <remarks>
/// Reads and writes through the <see cref="TextReader"/>/<see cref="TextWriter"/> it is given
/// rather than the console, so the whole flow is drivable from a test — the pattern
/// <c>DataSourceInterview</c> and <c>BuildAppCommand.ResolveTarget</c> established. The menu
/// arrives as a delegate for the same reason: it is the one step that needs raw key input.
/// <para>
/// The caller decides whether to run this at all: redirected stdin never prompts, and uses
/// <see cref="InlineDataSource"/> alone.
/// </para>
/// </remarks>
internal static class BuildAppWizard
{
    private static readonly WizardOutcome Cancelled =
        new(WizardDecision.Cancelled, string.Empty, null, null, false);

    public static WizardOutcome Run(
        InlineBuildRequest request,
        TextReader reader,
        TextWriter writer,
        Func<int?> chooseApplicationType)
    {
        var description = request.Description;

        // Asked first, and only when it was not already typed: `build app: a shop` has answered
        // this, and re-asking would make the shortcut pointless.
        if (description.Length == 0)
        {
            var typed = AskForDescription(reader, writer);

            if (typed is null)
                return Cancelled;

            description = typed;
        }

        // Before discovery, not at `approve`: a user who wants a Web API should not find out
        // after a Wikipedia fetch and a model review.
        var index = chooseApplicationType();

        if (index is null)
            return Cancelled;

        var target = ApplicationTypeMenu.Resolve(index.Value);

        var source = request.Source;

        if (source is null)
        {
            var outcome = DataSourceInterview.Resolve(reader, writer);

            if (outcome.Decision == InterviewDecision.Cancelled)
                return Cancelled;

            // NoData leaves this null, which is what selects the discovery path downstream.
            source = outcome.Source;
        }

        return new WizardOutcome(
            WizardDecision.Proceed, description, target, source, request.Normalize);
    }

    /// <summary>
    /// The typed description, <see cref="string.Empty"/> if the user skipped it, or null to
    /// cancel.
    /// </summary>
    /// <remarks>
    /// Blank is a real answer: `build app from csv orders.csv` with no description is a supported
    /// flow, so the prompt says so rather than rejecting an empty line.
    /// </remarks>
    private static string? AskForDescription(TextReader reader, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("Describe the application you want to build.");
        writer.WriteLine("(Enter to skip if you'll point at a data file instead, 'c' to cancel)");
        writer.Write("> ");

        var line = reader.ReadLine();

        // EOF is the absence of an answer, not an empty one. Conflating the two would run a whole
        // discovery pipeline off a closed pipe.
        if (line is null)
            return null;

        var text = line.Trim();

        return string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }
}
