using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

public static class IntentAnalyzer
{
    private static readonly Dictionary<IntentType, string[]> IntentHints =
        new()
        {
            [IntentType.CreateClass] =
            [
                "i need a class",
                "create a class",
                "generate a class",
                "make a class",
                "new class",
                "create entity",
                "generate model",
                "build dto"
            ],

            [IntentType.CreateProperty] =
            [
                "add property",
                "create property",
                "add field",
                "create field",
                "new property"
            ],

            [IntentType.CreateMethod] =
            [
                "add method",
                "create method",
                "generate function",
                "new method"
            ],

            [IntentType.CreateService] =
            [
                "create service",
                "generate service",
                "service for",
                "manager for",
                "handler for"
            ],

            [IntentType.CreateRepository] =
            [
                "create repository",
                "repository for",
                "storage for",
                "data access for"
            ],

            [IntentType.CreateController] =
            [
                "create controller",
                "api controller",
                "endpoint controller"
            ],

            [IntentType.ListProjects] =
            [
                "list projects",
                "show projects",
                "display projects",
                "what projects",
                "all projects"
            ]
        };

    public static IntentType Detect(SemanticModel semantic)
        => DetectWithConfidence(semantic).Intent;

    public static IntentMatch DetectWithConfidence(SemanticModel semantic)
    {
        if (semantic == null)
        {
            return new IntentMatch(
                IntentType.Unknown,
                0,
                0,
                "Semantic model is null");
        }

        return new IntentMatch(
            semantic.Intent,
            (int)(semantic.Confidence * 100),
            semantic.Confidence,
            "Semantic analysis");
    }

    public static IntentMatch Detect(string sentence, string? nlpAction = null)
        => DetectWithConfidence(sentence, nlpAction);

    private static IntentMatch DetectWithConfidence(string sentence, string? nlpAction)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return new(IntentType.Unknown, 0, 0, "Empty input");

        sentence = NormalizeSentence(sentence);

        var tokens = TextTokenizer.Tokenize(sentence)
            .Select(t => t.Normalized)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (Regex.IsMatch(sentence,
        @"^\s*\w+\s+(inherit|inherits|extends)\s+\w+\s*$",
        RegexOptions.IgnoreCase))
        {
            return new IntentMatch(IntentType.ClassInherit,
                999, 1.0, "Direct inheritance command");
        }

        // "list classes" / "class list" / "show classes" style commands. Handled
        // up front because the scored rules (loaded from an accumulated
        // intents.json) misfire on these - "class list" was being read as
        // CreateClass (generating a class named "List") and "list classes" lost
        // to an unrelated intent.
        if (TryDetectClassListing(sentence, out var listingIntent))
        {
            return new IntentMatch(listingIntent, 999, 1.0,
                "Class listing command");
        }

        IntentType bestIntent = IntentType.Unknown;
        int bestScore = 0;
        int bestPriority = int.MinValue;

        foreach (var rule in IntentRules.Rules)
        {
            int score = CalculateScore(sentence, tokens, rule, nlpAction);

            if (score > bestScore ||
                (score == bestScore &&
                 rule.Priority > bestPriority))
            {
                bestScore = score;
                bestPriority = rule.Priority;
                bestIntent = rule.Intent;
            }
        }

        // Prevent weak accidental matches
        if (bestScore < 40)
        {
            bestIntent = IntentType.Unknown;
        }

        return new(
            bestIntent,
            bestScore,
            CalculateConfidence(bestScore),
            $"Matched {bestIntent} with score {bestScore}");
    }

    private static string NormalizeSentence(string sentence)
    {
        sentence = CommandParser.Normalize(sentence);
        sentence = EnglishLanguageModel.Normalize(sentence);

        return sentence.Trim().ToLowerInvariant();
    }

    private static int CalculateScore(string sentence, HashSet<string> tokens, IntentRule rule, string? nlpAction)
    {
        int score = 0;

        score += ScoreActions(sentence, tokens, rule);
        score += ScoreTargets(sentence, tokens, rule);
        score += GetPhraseBonus(sentence, rule);
        score += GetEnglishHintScore(sentence, rule.Intent);
        score += GetContextScore(sentence, rule.Intent);
        score += GetNlpActionScore(nlpAction, rule);

        return score;
    }

    // Additive only: when NLE identified a primary action verb that matches one
    // of this rule's actions, add a fixed bonus. If NLE returned nothing (null),
    // every rule's score is unchanged, so behavior is identical to before.
    private static int GetNlpActionScore(string? nlpAction, IntentRule rule)
    {
        if (string.IsNullOrWhiteSpace(nlpAction))
            return 0;

        return rule.Actions.Any(a =>
            string.Equals(a, nlpAction, StringComparison.OrdinalIgnoreCase))
            ? 25
            : 0;
    }

    private static int ScoreActions(
        string sentence,
        HashSet<string> tokens,
        IntentRule rule)
    {
        int score = 0;

        var terms = rule.Actions
            .Concat(rule.Keywords)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            if (ContainsPhrase(sentence, term))
                score += 30;
            else if (ContainsWord(tokens, term))
                score += 20;

            score += GetSynonymScore(sentence, term);
        }

        return score;
    }

    private static int ScoreTargets(
        string sentence,
        HashSet<string> tokens,
        IntentRule rule)
    {
        int score = 0;

        foreach (var target in rule.Targets)
        {
            if (ContainsPhrase(sentence, target))
                score += 40;
            else if (ContainsWord(tokens, target))
                score += 15;
        }

        return score;
    }

    private static int GetSynonymScore(
        string sentence,
        string word)
    {
        if (!SynonymDictionary.Synonyms.TryGetValue(word, out var synonyms))
            return 0;

        int score = 0;

        foreach (var synonym in synonyms)
        {
            if (ContainsPhrase(sentence, synonym))
                score += 20;
        }

        return score;
    }

    private static int GetPhraseBonus(
        string sentence,
        IntentRule rule)
    {
        int score = 0;

        var terms = rule.Actions
            .Concat(rule.Keywords)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var action in terms)
        {
            foreach (var target in rule.Targets)
            {
                if (sentence.Contains(
                        $"{action} {target}",
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 25;
                }
            }
        }

        return score;
    }

    private static int GetEnglishHintScore(
        string sentence,
        IntentType intent)
    {
        if (!IntentHints.TryGetValue(intent, out var hints))
            return 0;

        foreach (var hint in hints)
        {
            if (sentence.Contains(
                    hint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 60;
            }
        }

        return 0;
    }

    private static int GetContextScore(string sentence, IntentType intent)
    {
        int score = 0;

        switch (intent)
        {
            case IntentType.CreateClass:
                if (sentence.Contains("class"))
                    score += 50;
                if (sentence.Contains("with"))
                    score += 20;
                if (sentence.Contains("property"))
                    score += 20;
                if (sentence.Contains("properties"))
                    score += 20;
                if (sentence.Contains("method"))
                    score += 20;
                if (sentence.Contains("methods"))
                    score += 20;

                // An explicit "create/generate/make ... class" command IS a
                // CreateClass, even when it also lists methods or properties in
                // the same sentence. Without this, the "add method X and Y"
                // clause can score higher for CreateMethod and hijack the intent
                // so the class is never generated.
                if (IsExplicitCreateClass(sentence))
                    score += 120;
                break;

            case IntentType.CreateService:
                if (sentence.Contains("manage"))
                    score += 10;
                if (sentence.Contains("handle"))
                    score += 10;
                if (sentence.Contains("process"))
                    score += 10;
                break;
        }

        return score;
    }

    // Matches "create class", "create a Customer class", "generate model class",
    // "make new class", etc. Deliberately requires a creation verb bound to the
    // "class" target, so "add method Save to class Order" (class as a target of
    // a different verb) does NOT match and stays a member-creation intent.
    // Recognizes commands that ask for a listing of classes, in either word
    // order, without stealing "show class Foo" (a single-class view) or
    // "list deleted classes" (a different intent).
    private static bool TryDetectClassListing(string sentence, out IntentType intent)
    {
        intent = IntentType.Unknown;

        if (Regex.IsMatch(sentence, @"\bdeleted\b", RegexOptions.IgnoreCase))
            return false;

        var listsClasses =
            // "list class" / "list classes" / "list all classes"
            Regex.IsMatch(sentence, @"\blist\s+(?:all\s+)?class(?:es)?\b", RegexOptions.IgnoreCase)
            // "show/display/view classes" (plural only; singular is ShowClass)
            || Regex.IsMatch(sentence, @"\b(?:show|display|view)\s+(?:all\s+)?classes\b", RegexOptions.IgnoreCase)
            // "class list" / "classes list"
            || Regex.IsMatch(sentence, @"\bclass(?:es)?\s+list\b", RegexOptions.IgnoreCase);

        if (!listsClasses)
            return false;

        // "list classes in <project>" / "... in project" targets a named project.
        intent = Regex.IsMatch(sentence, @"\bclasses?\s+in\b", RegexOptions.IgnoreCase)
                 || Regex.IsMatch(sentence, @"\bin\s+project\b", RegexOptions.IgnoreCase)
            ? IntentType.ListClassesInProject
            : IntentType.ListClasses;

        return true;
    }

    private static bool IsExplicitCreateClass(string sentence)
        => Regex.IsMatch(
            sentence,
            @"\b(create|generate|make|build|new|need)\b\s+(?:a\s+|an\s+|the\s+)?(?:[a-z][a-z0-9_]*\s+)?class\b",
            RegexOptions.IgnoreCase);

    private static bool ContainsWord(
        HashSet<string> tokens,
        string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        if (word.Contains(' '))
            return false;

        return tokens.Contains(word);
    }

    private static bool ContainsPhrase(
        string sentence,
        string phrase)
    {
        return sentence.Contains(
            phrase,
            StringComparison.OrdinalIgnoreCase);
    }

    private static double CalculateConfidence(int score)
    {
        return score switch
        {
            >= 180 => 1.00,
            >= 150 => 0.98,
            >= 120 => 0.95,
            >= 100 => 0.90,
            >= 80 => 0.80,
            >= 60 => 0.70,
            >= 40 => 0.60,
            >= 20 => 0.50,
            _ => 0.0
        };
    }
}