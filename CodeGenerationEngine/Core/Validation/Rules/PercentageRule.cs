using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: one amount always equals another plus a percentage of it —
/// `Total == Net * (1 + Rate)`. Only ever built by the Calculation scan; nobody types this
/// rule by hand, so there is no parser.</summary>
public sealed class PercentageRule : IValidationRule
{
    private readonly string _total;
    private readonly string _net;
    private readonly string _rate;

    public PercentageRule(string total, string net, string rate)
    {
        _total = total;
        _net = net;
        _rate = rate;
    }

    public ValidationCategory Category => ValidationCategory.Business;
    public IReadOnlyList<string> Properties => [_total, _net, _rate];
    public string Describe() => $"{_total} must equal {_net} times (1 plus {_rate})";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_total) || !ctx.HasProperty(_net) || !ctx.HasProperty(_rate))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);
        const string Parse = "System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture";
        static string Text(string property) =>
            $"(((object)this.{property}) as System.IFormattable)?.ToString(null, System.Globalization.CultureInfo.InvariantCulture)";

        ctx.Line($"if (decimal.TryParse({Text(_total)}, {Parse}, out var __t)");
        ctx.Line($"    && decimal.TryParse({Text(_net)}, {Parse}, out var __n)");
        ctx.Line($"    && decimal.TryParse({Text(_rate)}, {Parse}, out var __r))");
        ctx.Line("{");
        ctx.Line($"    if (System.Math.Abs(__t - (__n * (1 + __r))) > 0.0001m) errors.Add({message});");
        ctx.Line("}");
    }
}
