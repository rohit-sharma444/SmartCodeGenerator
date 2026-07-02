using NaturalLanguageEngine.Knowledge.Builders;
using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Lexical;
using NaturalLanguageEngine.Lexical.Tokenizers;
using NaturalLanguageEngine.Syntax;

namespace NaturalLanguageEngine.Tests.TestSupport;

/// <summary>
/// Builds the individual, real components the production
/// <c>EngineBuilder</c> wires together, so unit tests exercise the actual
/// English lexicon and heuristics rather than hand-rolled fakes. Keeping
/// this construction logic in one place also means that if EngineBuilder's
/// wiring changes, only this file needs to follow.
/// </summary>
internal static class EngineTestFactory
{
    public static ILanguageRepository CreateEnglishRepository()
        => new LanguageRepositoryBuilder().UseEnglish().Build();

    public static Tokenizer CreateTokenizer()
        => new(
            new CharacterReader(),
            new TokenRecognizerRegistry()
                .Register(new UrlTokenizer())
                .Register(new EmailTokenizer())
                .Register(new DateTokenizer())
                .Register(new TimeTokenizer())
                .Register(new QuotedStringTokenizer())
                .Register(new NumberTokenizer())
                .Register(new WordTokenizer())
                .Register(new PunctuationTokenizer())
                .Register(new SymbolTokenizer())
                .Build());

    public static SentenceSplitter CreateSentenceSplitter(ILanguageRepository? language = null)
        => new(CreateTokenizer(), language ?? CreateEnglishRepository());

    public static PosTagger CreatePosTagger(ILanguageRepository? language = null)
        => new(language ?? CreateEnglishRepository());
}
