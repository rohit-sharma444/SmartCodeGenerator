using System.Linq;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Web.Chat;

/// <summary>What one submission of the box did.</summary>
/// <param name="Unwritten">The lines of the box that produced no rule on disk, as typed, so the
/// page can leave exactly those in the box to be corrected.</param>
public sealed record ValidationOutcome(
    IReadOnlyList<string> Added, IReadOnlyList<string> Refused, IReadOnlyList<string> Unwritten);

/// <summary>
/// One box of plain English, applied to one class.
/// </summary>
/// <remarks>
/// Here rather than in the page's code block so it can be tested without a renderer - the page is
/// then markup and one call.
/// <para>
/// Data first, business second, and never a choice the user has to make: the parser that
/// recognizes the sentence is the thing that knows which category it belongs to.
/// </para>
/// </remarks>
public static class ValidationAdd
{
    public static ValidationOutcome Apply(ValidationTarget target, string text)
    {
        var added = new List<string>();
        var refused = new List<string>();
        var unwritten = new List<string>();

        var properties = EntityExtractor.ExtractProperties(target.File);
        var known = properties.Select(p => p.Name).ToList();

        foreach (var fragment in ValidationRuleCatalog.Fragments(text))
        {
            var ctx = new ValidationParseContext
            {
                Fragment = fragment,
                ClassName = target.ClassName,
                KnownProperties = known
            };

            var rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Data, ctx);

            if (rules.Count == 0)
                rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Business, ctx);

            if (rules.Count == 0)
            {
                refused.Add($"I could not turn this into a rule: \"{fragment}\"");
                unwritten.Add(fragment);
                continue;
            }

            var wroteAll = true;

            foreach (var rule in rules)
            {
                var result = ClassValidationWriter.Add(target.File, target.ClassName, rule);

                if (result.Ok)
                {
                    // Registered as well as written, so `show validation <Class>` in the same
                    // session agrees with what is on disk.
                    ValidationRegistry.Add(target.ClassName, rule, fragment);
                    added.Add(result.Message);
                }
                else
                {
                    refused.Add(result.Message);
                    wroteAll = false;
                }
            }

            if (!wroteAll)
                unwritten.Add(fragment);
        }

        return new ValidationOutcome(added, refused, unwritten);
    }

    /// <summary>Every phrasing the catalog accepts, asked of the catalog.</summary>
    /// <remarks>
    /// Not a list kept here. A hand-written list beside a growing catalog is a page telling people
    /// to type things the tool no longer understands.
    /// </remarks>
    public static IReadOnlyList<string> Phrasings() =>
        ValidationRuleCatalog.Default.Parsers
            .SelectMany(p => p.Examples)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
}
