using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Core.Stages;
using NaturalLanguageEngine.Knowledge.Builders;
using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Lexical;
using NaturalLanguageEngine.Lexical.Stages;
using NaturalLanguageEngine.Lexical.Tokenizers;
using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Morphology;
using NaturalLanguageEngine.Semantics;
using NaturalLanguageEngine.Syntax;

namespace NaturalLanguageEngine.Core;

/// <summary>
/// High-level semantic analyzer that provides a unified interface for
/// analyzing natural language text and extracting semantic information
/// (entities, relationships, and intent).
/// </summary>
public sealed class SemanticAnalyzer : ISemanticAnalyzer
{
    private readonly NaturalLanguageEngine _engine;

    /// <summary>
    /// Creates a semantic analyzer with the default fully-configured
    /// NaturalLanguageEngine pipeline.
    /// </summary>
    public SemanticAnalyzer()
        : this(BuildDefaultEngine())
    {
    }

    /// <summary>
    /// Creates a semantic analyzer with a custom engine instance.
    /// </summary>
    /// <param name="engine">The NaturalLanguageEngine to use for analysis.</param>
    /// <exception cref="ArgumentNullException">Thrown when engine is null.</exception>
    public SemanticAnalyzer(NaturalLanguageEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Analyzes the given text and returns a comprehensive semantic result
    /// containing entities, relationships, intent, confidence score, and diagnostics.
    /// </summary>
    /// <param name="text">The natural language text to analyze.</param>
    /// <returns>A SemanticResult containing extracted semantic information.</returns>
    /// <exception cref="ArgumentException">Thrown when text is null, empty, or whitespace.</exception>
    public SemanticResult Analyze(string text)
    {
        return _engine.Analyze(text);
    }

    /// <summary>
    /// Builds a default NaturalLanguageEngine with all layers configured.
    /// This is called by the parameterless constructor.
    /// </summary>
    private static NaturalLanguageEngine BuildDefaultEngine()
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

        // --- Build pipeline ---
        var pipeline = new Pipeline()
            .Add(new NormalizationStage(normalizer))
            .Add(new TokenizationStage(sentenceSplitter))
            .Add(new MorphologyStage(lemmatizer, language))
            .Add(new PartOfSpeechStage(posTagger))
            .Add(new PhraseDetectionStage(phraseDetector))
            .Add(new DependencyParsingStage(dependencyParser))
            .Add(new SemanticStage(entityRecognizer, intentRecognizer, relationshipExtractor));

        return new NaturalLanguageEngine(pipeline);
    }
}