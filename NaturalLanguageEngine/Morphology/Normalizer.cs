using NaturalLanguageEngine.Abstractions.Morphology;
using System.Globalization;
using System.Text;

namespace NaturalLanguageEngine.Morphology;

/// <summary>
/// Normalizes raw text: lower-cases, collapses whitespace,
/// strips diacritics and standardizes a few unicode punctuation marks.
/// </summary>
public sealed class Normalizer : INormalizer
{
    public string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Standardize curly quotes / dashes before further processing.
        text = text
            .Replace('\u2018', '\'').Replace('\u2019', '\'')
            .Replace('\u201C', '"').Replace('\u201D', '"')
            .Replace('\u2013', '-').Replace('\u2014', '-');

        text = StripDiacritics(text);

        var sb = new StringBuilder(text.Length);
        bool previousWhitespace = false;

        foreach (char c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWhitespace)
                    sb.Append(' ');
                previousWhitespace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(c));
                previousWhitespace = false;
            }
        }

        return sb.ToString();
    }

    private static string StripDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
