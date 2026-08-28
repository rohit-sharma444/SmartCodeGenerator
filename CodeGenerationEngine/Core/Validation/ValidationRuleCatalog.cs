using System.Linq;
using System.Reflection;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>Holds the registered rule parsers and turns rule text into
/// <see cref="IValidationRule"/>s. <see cref="Default"/> reflection-discovers every
/// parser in the engine assembly, so a new rule type is picked up automatically.</summary>
public sealed class ValidationRuleCatalog
{
    private static readonly Lazy<ValidationRuleCatalog> _default = new(DiscoverFromAssembly);

    public ValidationRuleCatalog(IEnumerable<IValidationRuleParser> parsers)
    {
        Parsers = parsers.OrderBy(p => p.Priority)
                         .ThenBy(p => p.GetType().Name, System.StringComparer.Ordinal)
                         .ToList();
    }

    public static ValidationRuleCatalog Default => _default.Value;

    public IReadOnlyList<IValidationRuleParser> Parsers { get; }

    public IReadOnlyList<IValidationRule> Parse(
        string text, ValidationCategory category, ValidationParseContext ctx)
    {
        var rules = new List<IValidationRule>();
        if (string.IsNullOrWhiteSpace(text))
            return rules;

        foreach (var fragment in Fragments(text))
        {
            var fragCtx = new ValidationParseContext
            {
                Fragment = fragment,
                ClassName = ctx.ClassName,
                TargetProperty = ctx.TargetProperty,
                KnownProperties = ctx.KnownProperties
            };

            foreach (var parser in Parsers.Where(p => p.Category == category))
            {
                if (parser.TryParse(fragCtx, out var rule) && rule != null)
                {
                    rules.Add(rule);
                    break; // first match wins for this fragment
                }
            }
        }

        return rules;
    }

    /// <summary>
    /// One rule per line, or per semicolon.
    /// </summary>
    /// <remarks>
    /// Public because the validation page parses fragment by fragment: it has to be able to say
    /// which of the three lines someone typed was the one it could not understand.
    /// </remarks>
    // Split on newlines and ';', trim, and drop a trailing sentence period without
    // harming decimals mid-string (e.g. "between 3.5 and 10").
    public static IReadOnlyList<string> Fragments(string text) =>
        (text ?? string.Empty).Replace("\r", "")
            .Split(new[] { '\n', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimEnd('.').Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static ValidationRuleCatalog DiscoverFromAssembly()
    {
        var parsers = typeof(IValidationRuleParser).Assembly.GetTypes()
            .Where(t => typeof(IValidationRuleParser).IsAssignableFrom(t)
                        && !t.IsInterface && !t.IsAbstract
                        && t.GetConstructor(System.Type.EmptyTypes) != null)
            .Select(t => (IValidationRuleParser)System.Activator.CreateInstance(t)!);

        return new ValidationRuleCatalog(parsers);
    }
}
