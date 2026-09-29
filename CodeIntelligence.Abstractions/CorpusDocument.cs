using CodeGenerationEngine.Models;

namespace CodeIntelligence.Abstractions;

/// <summary>Where a phrasing came from. <see cref="Learned"/>: a user's own phrasing the CLI did
/// not understand, paired with the command they typed next, reviewed into learned-phrases.tsv.</summary>
public enum CorpusSourceKind { IntentRule, LiteralCommand, CommandCatalog, Documentation, Learned }

/// <summary>A phrasing to embed. <paramref name="ContentHash"/> is over Text+Intent so a
/// reindex can skip unchanged rows. Id is 0 until the store assigns one.</summary>
public sealed record CorpusDocument(
    string Text,
    IntentType Intent,
    CorpusSourceKind SourceKind,
    string SourceRef,
    string ContentHash)
{
    public long Id { get; init; }
}
