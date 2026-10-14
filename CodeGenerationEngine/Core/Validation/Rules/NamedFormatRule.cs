using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Data rule: the value has to parse as a date.</summary>
/// <remarks>
/// Not a regex, unlike the rest of this file: dates are not a shape, and a pattern that accepted
/// 31/02 would be worse than no rule. Invariant culture and <c>DateTimeStyles.None</c>, so
/// 03/04/2026 does not mean March on one machine and April on the next.
/// </remarks>
public sealed class ValidDateRule : IValidationRule
{
    private readonly string _p;

    public ValidDateRule(string property) => _p = property;

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => $"{_p} must be a valid date";

    public void Emit(ValidationEmitContext ctx)
    {
        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __t = ((object)this.{_p})?.ToString();");
        ctx.Line("var __ok = string.IsNullOrWhiteSpace(__t) || System.DateTime.TryParse(__t, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);");
        ctx.Line($"if (!__ok) errors.Add({message});");
    }
}

/// <summary>The formats people ask for by name. Each is sugar over <see cref="RegexRule"/>,
/// the way <c>EmailRuleParser</c> already is — which is why they describe themselves as the
/// pattern they became.</summary>
public sealed class NamedFormatRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:a\s+|an\s+)?(?:valid\s+)?(?<format>phone\s+number|phone|url|web\s+address|postcode|post\s+code|guid|uuid|ip\s+address|ip)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Deliberately loose where the real-world format is: a phone number pattern that
    /// rejects a legitimate number is a pattern that gets deleted.</summary>
    private static readonly Dictionary<string, string> Patterns = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["phone number"] = @"^\+?[0-9 ().\-]{7,20}$",
        ["phone"] = @"^\+?[0-9 ().\-]{7,20}$",
        ["url"] = @"^(?:https?://)?[^\s.]+(?:\.[^\s.]+)+$",
        ["web address"] = @"^(?:https?://)?[^\s.]+(?:\.[^\s.]+)+$",
        ["postcode"] = @"^[A-Za-z]{1,2}[0-9][0-9A-Za-z]?\s*[0-9][A-Za-z]{2}$",
        ["post code"] = @"^[A-Za-z]{1,2}[0-9][0-9A-Za-z]?\s*[0-9][A-Za-z]{2}$",
        ["guid"] = @"^\{[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\}$|^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$|^[0-9a-fA-F]{32}$",
        ["uuid"] = @"^\{[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\}$|^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$|^[0-9a-fA-F]{32}$",
        ["ip address"] = @"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$|^::$|^[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){7}$|^(?=.*::)(?!.*::.*::)(?!.*:::)[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}$",
        ["ip"] = @"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$|^::$|^[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){7}$|^(?=.*::)(?!.*::.*::)(?!.*:::)[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}$"
    };

    public int Priority => 65;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "Phone must be a valid phone number",
        "Website must be a valid URL",
        "Postcode must be a valid postcode",
        "Reference must be a valid GUID",
        "Host must be a valid IP address"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var format = Regex.Replace(m.Groups["format"].Value.Trim(), @"\s+", " ");
        if (!Patterns.TryGetValue(format, out var pattern)) return false;

        rule = new RegexRule(m.Groups["prop"].Value, pattern);
        return true;
    }
}

/// <remarks>
/// Priority 66, right after <see cref="NamedFormatRuleParser"/> at 65: both patterns accept
/// "X must be a valid &lt;word&gt;", but 65's format alternation lists phone/url/postcode/guid/
/// uuid/ip and never the word "date" — so "Born must be a valid date" fails to match at 65 (no
/// alternative in the group consumes "date") and falls through to this parser rather than being
/// claimed and then rejected.
/// </remarks>
public sealed class ValidDateRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+a\s+(?:valid\s+)?date$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 66;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Born must be a valid date"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new ValidDateRule(m.Groups["prop"].Value);
        return true;
    }
}
