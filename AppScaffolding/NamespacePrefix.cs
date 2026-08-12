using Microsoft.CodeAnalysis.CSharp;

namespace AppScaffolding;

/// <summary>
/// The namespace root a generated app's code is placed under, above the project name.
/// </summary>
/// <remarks>
/// Validated rather than ignored, unlike every other customization value: a malformed accent
/// costs a colour, but a malformed namespace does not compile. Roslyn answers both halves of the
/// question — <c>SyntaxFacts.IsValidIdentifier</c> for the shape and
/// <c>SyntaxFacts.GetKeywordKind</c> for the reserved words — rather than a hand-kept keyword
/// list that would drift from the language.
/// </remarks>
public static class NamespacePrefix
{
    /// <summary>Whether <paramref name="value"/> can be written after <c>namespace</c>.</summary>
    /// <remarks>
    /// Contextual keywords pass: `namespace var;` compiles, so rejecting it would be stricter than
    /// C# is. <c>GetKeywordKind</c> returns <c>SyntaxKind.None</c> for those, which is the
    /// distinction being relied on here.
    /// </remarks>
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var segments = value.Split('.');

        return segments.All(segment =>
            SyntaxFacts.IsValidIdentifier(segment)
            && SyntaxFacts.GetKeywordKind(segment) == SyntaxKind.None);
    }

    /// <summary>
    /// <paramref name="company"/> reduced to a single identifier — every character that cannot
    /// appear in one removed — or null when the result would not be a valid prefix.
    /// </summary>
    /// <remarks>
    /// Used only to pre-fill the wizard's prompt. A company like "3M" reduces to "3M", which no
    /// namespace may be called, and offering it would put a value in front of the user that the
    /// same run would then refuse.
    /// </remarks>
    public static string? FromCompany(string company)
    {
        var identifier = new string(
            company.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        return IsValid(identifier) ? identifier : null;
    }
}
