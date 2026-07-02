using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Semantics;

/// <summary>
/// Derives a coarse intent from recognized entities. The first Action
/// entity becomes the intent verb; otherwise a generic intent is produced.
/// </summary>
public sealed class IntentRecognizer : IIntentRecognizer
{
    public Intent? Recognize(IReadOnlyList<Entity> entities)
    {
        if (entities.Count == 0)
            return null;

        var action = entities.FirstOrDefault(e => e.Type == EntityType.Action);

        if (action is not null)
        {
            var verb = action.Text.Split(' ').LastOrDefault() ?? action.Text;

            return new Intent
            {
                Name = $"{Capitalize(verb)}Intent",
                Confidence = action.Confidence
            };
        }

        // No verb: treat as a statement/description of the leading entity.
        var lead = entities[0];
        return new Intent
        {
            Name = "DescribeIntent",
            Confidence = (double)lead.Confidence * 0.8
        };
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s)
            ? s
            : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
