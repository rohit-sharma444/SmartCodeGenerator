using AppScaffolding;

namespace CodeGenerator.App;

/// <summary>
/// Lifts the customization options off a `build app` line, leaving the rest for
/// <c>InlineDataSource.Parse</c>.
/// </summary>
/// <remarks>
/// Runs FIRST, and that ordering is not incidental: <c>InlineDataSource.Parse</c> treats
/// everything after `from &lt;format&gt;` as the location, right to the end of the line, because
/// Windows paths contain spaces. Options left on the line would be swallowed by the path.
/// <para>
/// Scanning is right to left and stops at the first token that is not part of a recognized
/// option — the rule <c>InlineDataSource</c> already uses to strip a trailing `normalize`. The
/// known cost is that a description whose final words spell a valid option is misread
/// ("track theme dark"); quoting or reordering avoids it, and
/// <c>InlineCustomizationTests.A_description_ending_in_an_option_is_misread</c> pins the
/// behaviour so it stays a decision rather than a surprise.
/// </para>
/// <para>
/// The same right-to-left scan applies to the data source's location: a QUOTED location is one
/// atomic token (the trailing quote hides any flag-shaped word inside it from the scan), but an
/// UNQUOTED location is not, so its final word can be misread as an option the same way a
/// description can. This is a ruled decision, not an oversight — a `--` separator or a
/// path-detection heuristic was considered and rejected. <b>A location containing spaces must be
/// quoted.</b> <c>InlineCustomizationTests.A_quoted_location_survives_intact</c> and
/// <c>InlineCustomizationTests.An_unquoted_location_ending_in_a_flag_word_is_truncated</c> pin
/// both halves of that rule.
/// </para>
/// </remarks>
internal static class InlineCustomization
{
    /// <summary>Options taking one value.</summary>
    private static readonly string[] ValueKeys =
        ["company", "theme", "accent", "icon", "title", "namespace", "layout"];

    /// <summary>Options that are a bare word.</summary>
    private static readonly string[] Flags =
        ["novalidation", "logging", "noseed"];

    /// <summary>
    /// Splits <paramref name="tail"/> into what remains for the data-source parser and the
    /// customization it asked for, applied over <paramref name="start"/>.
    /// </summary>
    public static (string Remaining, AppCustomization Customization) Split(
        string tail, AppCustomization start)
    {
        var text = (tail ?? string.Empty).Trim();
        var customization = start;

        while (true)
        {
            var stripped = TryStripLast(text, ref customization);

            if (stripped is null)
                return (text, customization);

            text = stripped;
        }
    }

    /// <summary>
    /// Removes one trailing option from <paramref name="text"/>, applying it to
    /// <paramref name="customization"/>. Null when the line no longer ends in one.
    /// </summary>
    private static string? TryStripLast(string text, ref AppCustomization customization)
    {
        if (text.Length == 0)
            return null;

        var (lastWord, beforeLast) = SplitLastToken(text);

        if (Flags.Contains(lastWord, StringComparer.OrdinalIgnoreCase))
        {
            customization = ApplyFlag(customization, lastWord);
            return beforeLast;
        }

        // A value option is two tokens, and the value may be a quoted phrase, so the value is
        // taken first and the key looked for behind it.
        var (value, beforeValue) = SplitLastToken(text);
        var (key, beforeKey) = SplitLastToken(beforeValue);

        if (!ValueKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            return null;

        var applied = ApplyValue(customization, key, Unquote(value));

        // An unusable value (theme mauve) leaves the line alone rather than silently eating two
        // tokens: the user's words are more likely prose than a typo we should guess at.
        if (applied is null)
            return null;

        customization = applied;
        return beforeKey;
    }

    private static AppCustomization ApplyFlag(AppCustomization customization, string flag)
        => flag.ToLowerInvariant() switch
        {
            "novalidation" => customization with { Validation = false },
            "logging" => customization with { Logging = true },
            "noseed" => customization with { SampleSeedData = false },
            _ => customization,
        };

    /// <summary>The customization with <paramref name="key"/> set, or null when
    /// <paramref name="value"/> is not usable for it.</summary>
    private static AppCustomization? ApplyValue(
        AppCustomization customization, string key, string value)
    {
        if (value.Length == 0)
            return null;

        switch (key.ToLowerInvariant())
        {
            case "company":
                // Naming a company adopts the company title pattern unless a title was already
                // given. Options are read right to left, so a `title` later on the line has
                // already been applied by the time this runs — hence the check.
                return customization with
                {
                    Company = value,
                    TitlePattern = customization.TitlePattern == AppCustomization.AppPlaceholder
                        ? AppCustomization.CompanyTitlePattern
                        : customization.TitlePattern,
                };

            case "theme":
                if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
                    return customization with { Theme = AppTheme.Light };
                if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
                    return customization with { Theme = AppTheme.Dark };
                return null;

            case "accent":
                return ThemePalette.IsValidHexColour(value)
                    ? customization with { Accent = value }
                    : null;

            case "icon":
                return customization with { IconPath = value };

            case "title":
                return customization with { TitlePattern = value };

            case "namespace":
                // "none" clears a remembered prefix, ahead of the validity check below: it is the
                // vocabulary the wizard already teaches for clearing a value, and inline would
                // otherwise have no way at all to turn one off from a piped run. `IsValid("none")`
                // is true, so without this ordering it would be accepted as a literal namespace
                // segment instead.
                if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                    return customization with { NamespacePrefix = string.Empty };

                // Refused rather than ignored, unlike accent: an invalid namespace does not
                // compile, and returning null leaves the words in the description where a user
                // who was describing rather than configuring meant them to be.
                return NamespacePrefix.IsValid(value)
                    ? customization with { NamespacePrefix = value }
                    : null;

            case "layout":
                // Refused rather than ignored when unusable, like `theme mauve`: the words are
                // more likely part of the description than a layout name we should guess at.
                return AppLayoutCatalog.TryResolve(value, out var layout)
                    ? customization with { Layout = layout }
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// The last token of <paramref name="text"/> and what precedes it. A token is a quoted
    /// phrase when the text ends in a quote, so `company "Codis Ltd"` is two tokens, not three.
    /// A lone quote character is not a closing partner for anything, so it is treated as an
    /// ordinary token rather than searched for an opening match — the search index would go
    /// negative.
    /// </summary>
    private static (string Token, string Before) SplitLastToken(string text)
    {
        var trimmed = text.TrimEnd();

        if (trimmed.Length == 0)
            return (string.Empty, string.Empty);

        if (trimmed.Length >= 2 && trimmed[^1] == '"')
        {
            var open = trimmed.LastIndexOf('"', trimmed.Length - 2);

            if (open >= 0)
                return (trimmed[open..], trimmed[..open].TrimEnd());
        }

        var space = trimmed.LastIndexOf(' ');

        return space < 0
            ? (trimmed, string.Empty)
            : (trimmed[(space + 1)..], trimmed[..space].TrimEnd());
    }

    /// <summary>
    /// Strips the quotes from a fully-quoted token. An unmatched OPENING quote (a value like
    /// `"Codis` with no closing partner) is also dropped rather than kept: the alternative of
    /// rejecting the whole value was considered and rejected, because a stray quote character
    /// flowing into a generated title or project file is worse than tolerating an unbalanced
    /// quote the user probably mistyped.
    /// </summary>
    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return text[1..^1];

        return text.Length >= 1 && text[0] == '"' ? text[1..] : text;
    }
}
