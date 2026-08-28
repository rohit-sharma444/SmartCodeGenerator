using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

public enum DateBound
{
    Past,
    Future,
    NotPast,
    NotFuture,
    YearsAgo,
    WithinDays
}

/// <summary>Data rule: where a date sits relative to now.</summary>
public sealed class RelativeDateRule : IValidationRule
{
    private readonly string _p;
    private readonly DateBound _bound;
    private readonly int _amount;

    public RelativeDateRule(string property, DateBound bound, int amount = 0)
    {
        _p = property;
        _bound = bound;
        _amount = amount;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _bound switch
    {
        DateBound.Past => $"{_p} must be in the past",
        DateBound.Future => $"{_p} must be in the future",
        DateBound.NotPast => $"{_p} must not be in the past",
        DateBound.NotFuture => $"{_p} must not be in the future",
        DateBound.YearsAgo => $"{_p} must be at least {_amount} years ago",
        DateBound.WithinDays => $"{_p} must be within the last {_amount} days",
        _ => throw new ArgumentOutOfRangeException(nameof(_bound), _bound, "Unhandled DateBound.")
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);

        // Decided at runtime rather than from the declared type: this one shape covers DateTime,
        // DateTime?, object and a date held as a string, and does not care how the type was
        // spelled in the source.
        ctx.Line($"var __o = (object)this.{_p};");
        ctx.Line("System.DateTime __d;");
        ctx.Line("var __ok = __o is System.DateTime;");
        ctx.Line("if (__ok) __d = (System.DateTime)__o;");
        ctx.Line("else __ok = System.DateTime.TryParse(__o?.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out __d);");

        // Day-granular throughout, and each "X"/"NotX" pair is an exact complement of the
        // other — no value satisfies both, every value satisfies exactly one:
        //   Past passes iff strictly before today; NotPast passes iff today or later.
        //   Future passes iff tomorrow or later; NotFuture passes iff today or earlier.
        // Using System.DateTime.Now on only one side of a pair (as an earlier version of this
        // code did for Past/NotPast) lets a value earlier today satisfy both — "must be in the
        // past" and "must not be in the past" are not supposed to overlap. Day granularity is
        // also why "StartDate must not be in the past" accepts a start date of today, which is
        // the behaviour business date fields want; the cost is that "must be in the future"
        // means tomorrow-or-later, not "later than this exact moment" — do not "helpfully"
        // restore Now to one arm without re-checking this partition.
        var violation = _bound switch
        {
            DateBound.Past => "__d >= System.DateTime.Today",
            DateBound.NotPast => "__d < System.DateTime.Today",
            DateBound.Future => "__d < System.DateTime.Today.AddDays(1)",
            DateBound.NotFuture => "__d >= System.DateTime.Today.AddDays(1)",
            DateBound.YearsAgo => $"__d > System.DateTime.Today.AddYears(-{_amount})",
            DateBound.WithinDays => $"__d < System.DateTime.Today.AddDays(-{_amount})",
            _ => throw new ArgumentOutOfRangeException(nameof(_bound), _bound, "Unhandled DateBound.")
        };

        ctx.Line($"if (__ok && {violation}) errors.Add({message});");
    }
}

public sealed class RelativeDateRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<neg>not\s+)?be\s+in\s+the\s+(?<when>past|future)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 70;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "DateOfBirth must be in the past",
        "ExpiryDate must be in the future",
        "StartDate must not be in the past"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var past = m.Groups["when"].Value.Equals("past", System.StringComparison.OrdinalIgnoreCase);
        var negated = m.Groups["neg"].Success;

        var bound = (past, negated) switch
        {
            (true, false) => DateBound.Past,
            (true, true) => DateBound.NotPast,
            (false, false) => DateBound.Future,
            (false, true) => DateBound.NotFuture
        };

        rule = new RelativeDateRule(m.Groups["prop"].Value, bound);
        return true;
    }
}

public sealed class DateDistanceRuleParser : IValidationRuleParser
{
    private static readonly Regex YearsAgo = new(
        @"^(?<prop>\w+)\s+must\s+be\s+at\s+least\s+(?<n>\d+)\s+years?\s+ago$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WithinDays = new(
        @"^(?<prop>\w+)\s+must\s+be\s+within\s+the\s+last\s+(?<n>\d+)\s+days?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 71;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["DateOfBirth must be at least 18 years ago", "Modified must be within the last 30 days"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        rule = null;

        var years = YearsAgo.Match(ctx.Fragment);
        if (years.Success)
        {
            rule = new RelativeDateRule(
                years.Groups["prop"].Value, DateBound.YearsAgo, int.Parse(years.Groups["n"].Value));
            return true;
        }

        var days = WithinDays.Match(ctx.Fragment);
        if (days.Success)
        {
            rule = new RelativeDateRule(
                days.Groups["prop"].Value, DateBound.WithinDays, int.Parse(days.Groups["n"].Value));
            return true;
        }

        return false;
    }
}
