using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.App.Relationships;

/// <summary>
/// The relationship conversation: offer, propose, write, report.
/// </summary>
/// <remarks>
/// Never run from inside a command. <c>CommandColour.Run</c> redirects <c>Console.Out</c> into a
/// buffer for a command's whole life, and the chat page captures output the same way — a question
/// asked in there is a question nobody can see, which is exactly why <c>build app</c>, <c>batch</c>
/// and <c>demo</c> are kept out of both. This runs after the command has finished and its output
/// has been written.
/// <para>
/// What is remembered is the candidates already put to the user, not a single "asked already"
/// flag. A flag would mean that declining once silences the tool for every class created
/// afterwards, which is the session where it is most useful.
/// </para>
/// <para>
/// The workspace root arrives as a function, not a string. <c>load</c> and <c>use project</c>
/// re-root the workspace mid-session, while the refused-candidate memory has to survive across
/// commands — so the flow cannot be rebuilt per command, and a root captured once in the
/// constructor would go on reading the project the session started in.
/// </para>
/// </remarks>
public sealed class RelationshipFlow(
    Func<string> workspaceRoot,
    IChoiceAsker asker,
    Action<string> report)
{
    private static readonly IReadOnlyList<string> YesNo = ["Yes", "No"];

    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The four cardinality answers, worded from the owner's side so the direction is in the
    /// sentence rather than in a separate question.
    /// </summary>
    /// <remarks>
    /// The second one says "many" in both directions on purpose: it maps to a many-to-many, which
    /// builds a third class. Worded as "each Order has many Products" alone it describes a
    /// reversed one-to-many, and a user picking it would get a join class they never asked for.
    /// </remarks>
    public static IReadOnlyList<string> CardinalityLabels(string owner, string target) =>
    [
        $"each {owner} has one {target}  (many {RelationshipConvention.Pluralize(owner)} per {target})",
        $"each {owner} has many {RelationshipConvention.Pluralize(target)}, and each {target} many {RelationshipConvention.Pluralize(owner)}",
        "exactly one each way",
        "skip this one"
    ];

    /// <summary>
    /// Runs the conversation. Returns false when there was nothing outstanding to offer, so the
    /// caller knows nothing was put on screen.
    /// </summary>
    public bool Run()
    {
        var outstanding = Outstanding();

        if (outstanding.Count == 0)
            return false;

        var first = outstanding[0];

        // The reason comes from the detector rather than being re-worded here. Only one of its
        // three rules is about a `<Target>Id`, so a question that describes a foreign key would be
        // untrue for a candidate found by either of the other two.
        var offer = asker.Choose(
            $"{first.Owner} and {first.Target} look related {first.Reason}. "
            + $"Set relationships now? ({outstanding.Count} to review)",
            YesNo,
            1);

        if (offer != 0)
        {
            // Declining dismisses every outstanding candidate, not just the one named — which is
            // why the question says how many there are.
            foreach (var proposal in outstanding)
                _refused.Add(proposal.Key);

            return true;
        }

        foreach (var proposal in outstanding)
            Consider(proposal);

        return true;
    }

    private void Consider(RelationshipProposal proposal)
    {
        var choice = asker.Choose(
            $"{proposal.Owner} -> {proposal.Target}, {proposal.Reason}. How are they related?",
            CardinalityLabels(proposal.Owner, proposal.Target),
            0);

        var kind = choice switch
        {
            0 => RelationshipKind.OneToMany,
            1 => RelationshipKind.ManyToMany,
            2 => RelationshipKind.OneToOne,
            _ => (RelationshipKind?)null
        };

        // Marked before the write, and whether or not the write works. A candidate put to the user
        // has been put to them: re-offering the same pair because writing it failed turns one
        // problem into a question asked after every command for the rest of the session.
        _refused.Add(proposal.Key);

        if (kind is null)
            return;

        var written = RelationshipWriter.Apply(
            workspaceRoot(), proposal with { Kind = kind.Value });

        report(written.Ok
            ? written.Message + " Added: " + string.Join(", ", written.Added)
            : written.Message);
    }

    private List<RelationshipProposal> Outstanding() =>
    [
        .. RelationshipDetector
            .Detect(ClassSnapshots.Read(workspaceRoot()))
            .Where(p => !_refused.Contains(p.Key))
    ];
}
