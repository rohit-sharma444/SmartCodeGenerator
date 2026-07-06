using CodeGenerationEngine.Core;

namespace CodeGenerationEngine.Models;

public static class IntentAnalyzer
{
    public static IntentType Detect(string sentence)
    {
        return DetectWithConfidence(sentence).Intent;
    }

    public static IntentMatch DetectWithConfidence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return new(IntentType.Unknown, 0, 0, "Empty input");
        }

        var tokens = TextTokenizer.Tokenize(sentence);

        IntentType bestIntent = IntentType.Unknown;

        int bestScore = 0;

        string reason = string.Empty;

        foreach (var rule in IntentRules.Rules)
        {
            var score =
                CalculateScore(
                    tokens,
                    rule);

            if (score <= bestScore)
                continue;

            bestIntent = rule.Intent;
            bestScore = score;

            reason =
                $"Matched {rule.Intent}";
        }

        return new(
            bestIntent,
            bestScore,
            CalculateConfidence(bestScore),
            reason);
    }

    private static int CalculateScore(
        IReadOnlyList<Token> tokens,
        IntentRule rule)
    {
        int score = 0;

        foreach (var token in tokens)
        {
            if (rule.Actions.Contains(
                    token.Normalized,
                    StringComparer.OrdinalIgnoreCase))
            {
                score += 25;
            }

            if (rule.Targets.Contains(
                    token.Normalized,
                    StringComparer.OrdinalIgnoreCase))
            {
                score += 40;
            }

            if (rule.Keywords.Contains(
                    token.Normalized,
                    StringComparer.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }

        return score;
    }

    private static double CalculateConfidence(
        int score)
    {
        return score switch
        {
            >= 120 => 1.0,
            >= 100 => 0.95,
            >= 80 => 0.90,
            >= 60 => 0.80,
            >= 40 => 0.70,
            >= 20 => 0.50,
            _ => 0
        };
    }
}