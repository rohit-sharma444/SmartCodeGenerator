using CodeGenerationEngine.Models;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// Translates NLE's domain-neutral analysis into a CodeGenerationEngine
/// <see cref="IntentType"/>. This is where knowledge of code-generation
/// vocabulary lives — NLE itself never learns what a "class" is.
/// Stateless: safe for the method-parallel test run.
/// </summary>
public static class NlpCommandMapper
{
    public static IntentType ResolveIntent(SemanticResult? analysis)
        => IntentType.Unknown; // implemented in Task 2
}
