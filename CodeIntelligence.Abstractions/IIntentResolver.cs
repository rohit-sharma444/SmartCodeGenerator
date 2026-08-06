namespace CodeIntelligence.Abstractions;

/// <summary>Proposes an intent for input the rule-based parser returned Unknown for.
/// Implementations MUST NOT throw: a failure is a null return, which callers treat as
/// "unrecognized" and print exactly what they print today.</summary>
public interface IIntentResolver
{
    AiResolution? Resolve(string input, CancellationToken ct);
}
