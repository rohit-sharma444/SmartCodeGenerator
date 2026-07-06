using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class IntentLearningEngine
{
    public static void Learn(
        string sentence,
        ParsedCommand command)
    {
        var tokens =
            TextTokenizer.Tokenize(sentence);

        foreach (var token in tokens)
        {
            if (token.Category ==
                TokenCategory.Unknown)
            {
                VocabularyRepository.Store.Learn(
                    token.Original,
                    token.Normalized,
                    TokenCategory.Unknown,
                    0.2);
            }
        }

        VocabularyRepository.Save();
    }
}