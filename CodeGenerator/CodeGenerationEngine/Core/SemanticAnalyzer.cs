using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class SemanticAnalyzer
{
    public static SemanticModel Analyze(string input)
    {
        string normalized =
            CommandParser.Normalize(input);

        var tokens =
            TextTokenizer.Tokenize(normalized);

        SemanticModel model = new()
        {
            OriginalText = input,
            NormalizedText = normalized,
            Tokens = tokens.ToList(),
            Context = ContextManager.Current
        };

        model.Entities.AddRange(
            EntityRecognizer.Extract(tokens));

        model.Relations.AddRange(
            RelationExtractor.Extract(tokens));

        ContextResolver.Resolve(model);

        SemanticValidator.Validate(model);

        // Detect intent
        var intent = IntentAnalyzer.Detect(input);

        model.Intent = intent.Intent;
        model.Confidence = intent.Confidence;

        return model;
    }
}