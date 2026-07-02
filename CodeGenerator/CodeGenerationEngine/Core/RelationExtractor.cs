using CodeGenerationEngine.Models;
using CodeGenerationEngine.Semantics.Models;

namespace CodeGenerationEngine.Core;

public static class RelationExtractor
{
    public static IEnumerable<SemanticRelation> Extract(
        IReadOnlyList<Token> tokens)
    {
        List<SemanticRelation> relations = [];

        for (int i = 0; i < tokens.Count; i++)
        {
            string word = tokens[i].Normalized;

            if (word == "inherit" ||
                word == "inherits" ||
                word == "extends")
            {
                AddInheritance(tokens, i, relations);
            }

            if (word == "implement" ||
                word == "implements")
            {
                AddImplementation(tokens, i, relations);
            }

            if (word == "derived" &&
                i + 1 < tokens.Count &&
                tokens[i + 1].Normalized == "from")
            {
                AddInheritance(tokens, i, relations);
            }
        }

        return relations;
    }

    private static void AddInheritance(
        IReadOnlyList<Token> tokens,
        int index,
        List<SemanticRelation> relations)
    {
        if (index < 1 || index + 1 >= tokens.Count)
            return;

        relations.Add(new SemanticRelation
        {
            Source = tokens[index - 1].Original,
            Target = tokens[index + 1].Original,
            RelationType = SemanticRelationType.Inherits
        });
    }

    private static void AddImplementation(
        IReadOnlyList<Token> tokens,
        int index,
        List<SemanticRelation> relations)
    {
        if (index < 1 || index + 1 >= tokens.Count)
            return;

        relations.Add(new SemanticRelation
        {
            Source = tokens[index - 1].Original,
            Target = tokens[index + 1].Original,
            RelationType = SemanticRelationType.Implements
        });
    }
}