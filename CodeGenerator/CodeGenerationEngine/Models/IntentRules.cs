using CodeGenerationEngine.Core;

namespace CodeGenerationEngine.Models;

public static class IntentRules
{
    public static IReadOnlyList<IntentRule> Rules
        => IntentRepository.GetAll();
}