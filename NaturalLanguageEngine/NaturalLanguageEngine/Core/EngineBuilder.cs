using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Core.Stages;
using NaturalLanguageEngine.Knowledge.Builders;
using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Lexical;
using NaturalLanguageEngine.Lexical.Stages;
using NaturalLanguageEngine.Lexical.Tokenizers;
using NaturalLanguageEngine.Morphology;
using NaturalLanguageEngine.Semantics;
using NaturalLanguageEngine.Syntax;

/// <summary>
/// Assembles a fully-wired <see cref="NaturalLanguageEngine.Core.NaturalLanguageEngine"/>
/// using the built-in English knowledge base and the default analysis pipeline.
/// </summary>
public sealed class EngineBuilder
{
    public static NaturalLanguageEngine.Core.NaturalLanguageEngine BuildDefault()
    {
        // --- Knowledge base ---
        ILanguageRepository language = new LanguageRepositoryBuilder()
            .UseEnglish()
            .Build();

        // --- Lexical layer ---
        var registry = new TokenRecognizerRegistry()
            .Register(new UrlTokenizer())
            .Register(new EmailTokenizer())
            .Register(new DateTokenizer())
            .Register(new TimeTokenizer())
            .Register(new QuotedStringTokenizer())
            .Register(new NumberTokenizer())
            .Register(new WordTokenizer())
            .Register(new PunctuationTokenizer())
            .Register(new SymbolTokenizer());

        var tokenizer = new Tokenizer(
            new CharacterReader(),
            registry.Build());

        var sentenceSplitter = new SentenceSplitter(tokenizer, language);

        // --- Morphology ---
        var normalizer = new Normalizer();
        var stemmer = new Stemmer();
        var lemmatizer = new Lemmatizer(language, stemmer);

        // --- Syntax ---
        var posTagger = new PosTagger(language);
        var phraseDetector = new PhraseDetector();

        // --- Syntax (dependency parsing) ---
        var dependencyParser = new DependencyParser();

        // --- Semantics ---
        var entityRecognizer = new EntityRecognizer(language);
        var intentRecognizer = new IntentRecognizer();
        var relationshipExtractor = new RelationshipExtractor();

        // --- Pipeline (stages self-order by their Order property) ---
        var pipeline = new PipelineBuilder()
            .Add(new NormalizationStage(normalizer))
            .Add(new TokenizationStage(sentenceSplitter))
            .Add(new MorphologyStage(lemmatizer, language))
            .Add(new PartOfSpeechStage(posTagger))
            .Add(new PhraseDetectionStage(phraseDetector))
            .Add(new DependencyParsingStage(dependencyParser))
            .Add(new SemanticStage(entityRecognizer, intentRecognizer, relationshipExtractor))
            .Build();

        return new NaturalLanguageEngine.Core.NaturalLanguageEngine(pipeline);
    }
}
