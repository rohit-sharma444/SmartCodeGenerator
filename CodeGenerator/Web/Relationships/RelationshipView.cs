using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Web.Relationships;

/// <summary>
/// What the relationships page draws: a workspace, read once.
/// </summary>
/// <remarks>
/// A snapshot rather than a live view. The page answers "what is related to what right now", and
/// a picture that redrew itself while someone was reading it would be answering a question they
/// had already stopped asking. Running the command again is the refresh.
/// </remarks>
public sealed record RelationshipView(string Root, IReadOnlyList<RelationshipSource> Sources);
