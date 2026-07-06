using NaturalLanguageEngine.Knowledge.Providers;
using NaturalLanguageEngine.Knowledge.Repositories;

namespace NaturalLanguageEngine.Knowledge.Builders;

public sealed class LanguageRepositoryBuilder
{
    private ILanguageProvider? _provider;

    public LanguageRepositoryBuilder UseProvider(
        ILanguageProvider provider)
    {
        _provider = provider;
        return this;
    }

    /// <summary>Uses the built-in English provider.</summary>
    public LanguageRepositoryBuilder UseEnglish()
        => UseProvider(new EnglishLanguageProvider());

    public ILanguageRepository Build()
    {
        if (_provider is null)
            throw new InvalidOperationException("Language provider is not configured.");

        return new LanguageRepository(_provider);
    }
}
