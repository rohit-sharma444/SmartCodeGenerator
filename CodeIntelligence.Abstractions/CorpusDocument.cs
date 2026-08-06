using CodeGenerationEngine.Models;

namespace CodeIntelligence.Abstractions;

public enum CorpusSourceKind { IntentRule, LiteralCommand, CommandCatalog, Documentation }

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
