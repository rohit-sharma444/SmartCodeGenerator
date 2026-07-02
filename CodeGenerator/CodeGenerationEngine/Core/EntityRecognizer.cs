using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class EntityRecognizer
{
    private static readonly Dictionary<string, SemanticEntityType> Keywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["class"] = SemanticEntityType.Class,
            ["interface"] = SemanticEntityType.Interface,
            ["service"] = SemanticEntityType.Service,
            ["repository"] = SemanticEntityType.Repository,
            ["controller"] = SemanticEntityType.Controller,
            ["property"] = SemanticEntityType.Property,
            ["method"] = SemanticEntityType.Method
        };

    public static IEnumerable<SemanticEntity> Extract(
        IReadOnlyList<Token> tokens)
    {
        List<SemanticEntity> entities = [];

        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (!Keywords.TryGetValue(tokens[i].Normalized, out var type))
                continue;

            string name = tokens[i + 1].Original;

            entities.Add(new SemanticEntity
            {
                Name = name,
                Type = type
            });
        }

        return entities;
    }
}

//using CodeGenerationEngine.Models;
//using CodeGenerationEngine.Semantics.Models;

//namespace CodeGenerationEngine.Core;

//public static class EntityRecognizer 
//{
//    public static IEnumerable<SemanticEntity> Extract(
//        IReadOnlyList<Token> tokens)
//    {
//        for (int i = 0; i < tokens.Count; i++)
//        {
//            string word = tokens[i].Normalized;

//            if (word == "class" &&
//                i + 1 < tokens.Count)
//            {
//                yield return new SemanticEntity
//                {
//                    Type = SemanticEntityType.Class,
//                    Name = tokens[i + 1].Original,
//                    Value = tokens[i + 1].Original,
//                    Position = i + 1
//                };
//            }

//            if (word == "service" &&
//                i + 1 < tokens.Count)
//            {
//                yield return new SemanticEntity
//                {
//                    Type = SemanticEntityType.Service,
//                    Name = tokens[i + 1].Original,
//                    Value = tokens[i + 1].Original,
//                    Position = i + 1
//                };
//            }
//        }
//    }
//}