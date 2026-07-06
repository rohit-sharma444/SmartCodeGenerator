using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

public static partial class TextTokenizer
{
    private static readonly HashSet<string> StopWords =
    [
        "a","an","the","please","can","could","would","should",
        "may","might","you","me","my","your","our","their",
        "to","for","of","on","in","at","with","and","or",
        "is","are","was","were","be","been","being",
        "i","want","need","like","some","all"
    ];

    private static readonly HashSet<string> ActionWords =
    [
        "create","add","delete","remove","rename","update",
        "modify","generate","build","compile","show","list",
        "display","analyze","validate","restore","use",
        "open","current","save"
    ];

    private static readonly HashSet<string> ArtifactWords =
    [
        "class",
        "entity",
        "model",
        "dto",
        "record",
        "struct",
        "interface",
        "enum",
        "service",
        "repository",
        "controller",
        "validator",
        "factory",
        "manager",
        "handler",
        "command",
        "query",
        "project",
        "solution"
    ];

    private static readonly HashSet<string> DataTypes =
    [
        "string",
        "char",
        "bool",
        "byte",
        "sbyte",
        "short",
        "ushort",
        "int",
        "uint",
        "long",
        "ulong",
        "float",
        "double",
        "decimal",
        "datetime",
        "datetimeoffset",
        "timespan",
        "guid",
        "object",
        "dynamic"
    ];

    public static IReadOnlyList<Token> Tokenize(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return [];

        sentence = Normalize(sentence);

        var words = sentence.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        List<Token> tokens = [];

        bool vocabularyChanged = false;

        foreach (var original in words)
        {
            var normalized = Stem(original.ToLowerInvariant());

            if (StopWords.Contains(normalized))
                continue;

            var token = CreateToken(original, normalized);

            tokens.Add(token);

            if (token.Category != TokenCategory.Unknown &&
                !VocabularyRepository.Store.TryGet(token.Original, out _))
            {
                VocabularyRepository.Store.Learn(
                    token.Original,
                    token.Normalized,
                    token.Category,
                    token.Confidence);

                vocabularyChanged = true;
            }
        }

        if (vocabularyChanged)
            VocabularyRepository.Save();

        return tokens;
    }

    private static string Normalize(string text)
    {
        // Preserve generic type syntax and nullable '?'
        text = Regex.Replace(
            text,
            @"[^A-Za-z0-9_<>,?\s]",
            " ");

        text = Regex.Replace(
            text,
            @"\s+",
            " ");

        return text.Trim();
    }

    private static Token CreateToken(
        string original,
        string normalized)
    {
        if (VocabularyRepository.Store.TryGet(normalized, out var learned))
        {
            return new Token
            {
                Original = original,
                Normalized = learned.NormalizedWord,
                Category = learned.Category,
                Confidence = learned.Confidence
            };
        }

        return InferToken(original, normalized);
    }

    private static Token InferToken(
        string original,
        string normalized)
    {
        var token = new Token
        {
            Original = original,
            Normalized = normalized,
            Category = TokenCategory.Unknown,
            Confidence = 0.10
        };

        if (ActionWords.Contains(normalized))
        {
            token.Category = TokenCategory.Action;
            token.Confidence = 0.95;
        }
        else if (ArtifactWords.Contains(normalized))
        {
            token.Category = TokenCategory.Artifact;
            token.Confidence = 0.95;
        }
        else if (LooksLikeDataType(normalized))
        {
            token.Category = TokenCategory.DataType;
            token.Confidence = 0.95;
        }
        else if (LooksLikeIdentifier(original))
        {
            token.Category = TokenCategory.Identifier;
            token.Confidence = 0.60;
        }

        return token;
    }

    private static bool LooksLikeDataType(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        word = word.Trim().TrimEnd('?').ToLowerInvariant();

        if (DataTypes.Contains(word))
            return true;

        return word.StartsWith("list<", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("ilist<", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("icollection<", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("ienumerable<", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("dictionary<", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeIdentifier(string word)
    {
        return Regex.IsMatch(
            word,
            @"^[A-Za-z_][A-Za-z0-9_]*$");
    }

    private static string Stem(string word)
    {
        if (word.Length < 4)
            return word;

        if (word.EndsWith("ies"))
            return word[..^3] + "y";

        if (word.EndsWith("sses"))
            return word[..^2];

        if (word.EndsWith("s") &&
            !word.EndsWith("ss"))
            return word[..^1];

        return word;
    }
}

//using CodeGenerationEngine.Models;
//using System.Text.RegularExpressions;

//namespace CodeGenerationEngine.Core;

//public static class TextTokenizer
//{
//    private static readonly HashSet<string> StopWords =
//    [
//        "a",
//        "an",
//        "the",
//        "please",
//        "can",
//        "could",
//        "would",
//        "should",
//        "may",
//        "might",
//        "you",
//        "me",
//        "my",
//        "your",
//        "our",
//        "their",
//        "to",
//        "for",
//        "of",
//        "on",
//        "in",
//        "at",
//        "with",
//        "and",
//        "or",
//        "is",
//        "are",
//        "was",
//        "were",
//        "be",
//        "been",
//        "being",
//        "i",
//        "want",
//        "need",
//        "like",
//        "need",
//        "some",
//        "all"
//    ];

//    public static IReadOnlyList<Token> Tokenize(string sentence)
//    {
//        if (string.IsNullOrWhiteSpace(sentence))
//            return [];

//        sentence = Normalize(sentence);

//        var words = sentence.Split(
//            ' ',
//            StringSplitOptions.RemoveEmptyEntries);

//        List<Token> tokens = [];

//        foreach (var word in words)
//        {
//            var cleaned = Stem(word);

//            if (StopWords.Contains(cleaned))
//                continue;

//            var token = CreateToken(cleaned);

//            tokens.Add(token);
//        }

//        LearnUnknownTokens(tokens);

//        return tokens;
//    }

//    private static string Normalize(string text)
//    {
//        text = text.ToLowerInvariant();

//        text = Regex.Replace(
//            text,
//            @"[^\w\s]",
//            " ");

//        text = Regex.Replace(
//            text,
//            @"\s+",
//            " ");

//        return text.Trim();
//    }

//    private static Token CreateToken(string word)
//    {
//        if (VocabularyRepository.Store.TryGet(word, out var learned))
//        {
//            return new Token
//            {
//                Original = word,
//                Normalized = learned.NormalizedWord,
//                Category = learned.Category,
//                Confidence = learned.Confidence
//            };
//        }

//        return InferToken(word);
//    }

//    private static Token InferToken(string word)
//    {
//        TokenCategory category = TokenCategory.Unknown;

//        string normalized = word;

//        if (LooksLikeAction(word))
//        {
//            category = TokenCategory.Action;
//        }
//        else if (LooksLikeArtifact(word))
//        {
//            category = TokenCategory.Artifact;
//        }
//        else if (LooksLikeDataType(word))
//        {
//            category = TokenCategory.DataType;
//        }

//        return new Token
//        {
//            Original = word,
//            Normalized = normalized,
//            Category = category,
//            Confidence = 0.30
//        };
//    }

//    private static readonly HashSet<string> ActionWords =
//    [
//        "create",
//        "add",
//        "delete",
//        "remove",
//        "rename",
//        "update",
//        "modify",
//        "generate",
//        "build",
//        "compile",
//        "show",
//        "list",
//        "display",
//        "analyze",
//        "validate",
//        "restore",
//        "use",
//        "open",
//        "current",
//        "save"
//    ];

//    private static bool LooksLikeAction(string word)
//    {
//        if (string.IsNullOrWhiteSpace(word))
//            return false;
//        word = word.Trim().ToLowerInvariant();
//        return ActionWords.Contains(word);
//    }

//    private static readonly HashSet<string> ArtifactWords =
//    [
//        "class",
//        "entity",
//        "model",
//        "dto",
//        "record",
//        "struct",
//        "interface",
//        "enum",
//        "service",
//        "repository",
//        "controller",
//        "validator",
//        "factory",
//        "manager",
//        "handler",
//        "command",
//        "query",
//        "project",
//        "solution"
//    ];

//    private static bool LooksLikeArtifact(string word)
//    {
//        if (string.IsNullOrWhiteSpace(word))
//            return false;

//        return ArtifactWords.Contains(word.ToLowerInvariant());
//    }

//    private static readonly HashSet<string> DataTypes =
//    [
//        "string",
//        "char",
//        "bool",
//        "byte",
//        "sbyte",
//        "short",
//        "ushort",
//        "int",
//        "uint",
//        "long",
//        "ulong",
//        "float",
//        "double",
//        "decimal",
//        "datetime",
//        "datetimeoffset",
//        "timespan",
//        "guid",
//        "object",
//        "dynamic"
//    ];

//    private static bool LooksLikeDataType(string word)
//    {
//        if (string.IsNullOrWhiteSpace(word))
//            return false;

//        return DataTypes.Contains(word.Trim().ToLowerInvariant());
//    }

//    private static string Stem(string word)
//    {
//        if (word.Length < 4)
//            return word;

//        if (word.EndsWith("ies"))
//            return word[..^3] + "y";

//        if (word.EndsWith("sses"))
//            return word[..^2];

//        if (word.EndsWith("s"))
//            return word[..^1];

//        return word;
//    }

//    private static void LearnUnknownTokens(IEnumerable<Token> tokens)
//    {
//        foreach (var token in tokens)
//        {
//            VocabularyRepository.Store.Learn(
//                token.Original,
//                token.Normalized,
//                token.Category,
//                token.Confidence);
//        }

//        VocabularyRepository.Save();
//    }
//}