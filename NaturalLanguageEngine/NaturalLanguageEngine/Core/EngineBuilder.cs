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
/// Supports optional features like logging and caching.
/// </summary>
public sealed class EngineBuilder
{
    /// <summary>
    /// Builds an engine with default options (no logging or caching).
    /// </summary>
    public static NaturalLanguageEngine.Core.NaturalLanguageEngine BuildDefault()
        => (NaturalLanguageEngine.Core.NaturalLanguageEngine)Build(new EngineBuilderOptions());

    /// <summary>
    /// Builds an engine with the specified configuration options.
    /// </summary>
    public static object Build(EngineBuilderOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

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

        // --- Pipeline with optional logging ---
        var logger = options.EnableLogging ? new PipelineLogger() : null;

        var pipeline = new Pipeline(logger)
            .Add(new NormalizationStage(normalizer))
            .Add(new TokenizationStage(sentenceSplitter))
            .Add(new MorphologyStage(lemmatizer, language))
            .Add(new PartOfSpeechStage(posTagger))
            .Add(new PhraseDetectionStage(phraseDetector))
            .Add(new DependencyParsingStage(dependencyParser))
            .Add(new SemanticStage(entityRecognizer, intentRecognizer, relationshipExtractor));

        var engine = new NaturalLanguageEngine.Core.NaturalLanguageEngine(pipeline);

        // --- Wrap with caching if enabled ---
        if (options.EnableCaching)
        {
            var analyzer = new SemanticAnalyzer(engine);
            return new CachedSemanticAnalyzer(analyzer, options.MaxCacheSize);
        }

        return engine;
    }
}
