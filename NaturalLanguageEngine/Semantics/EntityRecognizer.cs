using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Knowledge.Repositories;
using NaturalLanguageEngine.Models.Enums;
using NaturalLanguageEngine.Models.Semantics;
using NaturalLanguageEngine.Models.Syntax;

namespace NaturalLanguageEngine.Semantics;

/// <summary>
/// Maps detected phrases to semantic entities using part-of-speech,
/// token type, a small gazetteer of known locations/organizations,
/// and simple capitalization heuristics.
/// </summary>
public sealed class EntityRecognizer : IEntityRecognizer
{
    private readonly ILanguageRepository? _language;

    /// <summary>
    /// Creates a recognizer with no gazetteer. Proper nouns that aren't
    /// covered by token type will fall back to <see cref="EntityType.Person"/>.
    /// </summary>
    public EntityRecognizer()
        : this(null)
    {
    }

    public EntityRecognizer(ILanguageRepository? language)
    {
        _language = language;
    }

    public IReadOnlyList<Entity> Recognize(IReadOnlyList<Phrase> phrases)
    {
        var entities = new List<Entity>();

        foreach (var phrase in phrases)
        {
            var head = phrase.Head ?? phrase.Tokens.LastOrDefault();
            if (head is null)
                continue;

            var text = string.Join(" ", phrase.Tokens.Select(t => t.Token.Text));
            var headText = head.Token.Text;

            var (type, confidence) = Classify(phrase, headText, head.Token.Type);

            if (type == EntityType.Unknown)
                continue;

            var entity = new Entity
            {
                Text = text,
                Type = type,
                Confidence = confidence
            };

            entities.Add(entity);

            // Link the phrase head back to the entity it produced, so a
            // later stage can map dependency edges (which reference
            // TokenAnalysis, not Entity) back to entities without
            // re-deriving the phrase -> entity association.
            head.Entity = entity;
        }

        return entities;
    }

    private (EntityType type, double confidence) Classify(
        Phrase phrase,
        string headText,
        TokenType headTokenType)
    {
        // Token-type driven entities (set during tokenization).
        switch (headTokenType)
        {
            case TokenType.Date:
                return (EntityType.Date, 0.9);
            case TokenType.Time:
                return (EntityType.Time, 0.9);
            case TokenType.Number:
                return (EntityType.Quantity, 0.85);
            case TokenType.Url:
            case TokenType.Email:
                return (EntityType.Value, 0.95);
        }

        if (phrase.Type == PhraseType.VerbPhrase)
            return (EntityType.Action, 0.8);

        if (phrase.Type == PhraseType.NounPhrase)
        {
            // Capitalized head with no determiner => proper noun (Person/Org/Location).
            bool capitalized = headText.Length > 0 && char.IsUpper(headText[0]);
            if (!capitalized)
                return (EntityType.Object, 0.7);

            if (_language is not null)
            {
                // Any token in the phrase marking it as an organization
                // (either a known org name, e.g. "NASA", or a name-final
                // marker, e.g. "... Corp" / "... University").
                if (phrase.Tokens.Any(t => _language.IsKnownOrganization(t.Token.Text)))
                    return (EntityType.Organization, 0.85);

                if (_language.IsKnownLocation(headText))
                    return (EntityType.Location, 0.85);
            }

            return (EntityType.Person, 0.6);
        }

        return (EntityType.Unknown, 0d);
    }
}
