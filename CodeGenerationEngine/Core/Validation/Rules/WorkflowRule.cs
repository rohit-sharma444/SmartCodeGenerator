using System.Linq;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>
/// Business rule: a status column may only move forward through a stated order, and never away
/// from a value marked final. Only ever built by <c>DomainModelMapper</c> from an accepted,
/// ordered Status proposal — nobody types this rule by hand, so there is no parser.
/// </summary>
public sealed class WorkflowRule : IValidationRule
{
    private readonly string _property;
    private readonly IReadOnlyList<string> _order;
    private readonly IReadOnlySet<string> _finalValues;

    public WorkflowRule(string property, IReadOnlyList<string> order, IReadOnlySet<string> finalValues)
    {
        _property = property;
        _order = order;
        _finalValues = finalValues;
    }

    public ValidationCategory Category => ValidationCategory.Workflow;
    public IReadOnlyList<string> Properties => [_property];

    public string Describe() =>
        $"{_property} must move forward through {string.Join(" -> ", _order)}"
        + (_finalValues.Count > 0
            ? $", and may not change once it is {string.Join(" or ", _finalValues)}"
            : string.Empty);

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_property))
        {
            ctx.Line($"// workflow rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var orderLiteral = string.Join(", ", _order.Select(v => "@\"" + v.Replace("\"", "\"\"") + "\""));
        var finalLiteral = string.Join(", ", _finalValues.Select(v => "@\"" + v.Replace("\"", "\"\"") + "\""));

        ctx.Line("if (stored is not null)");
        ctx.Line("{");
        ctx.Line($"    var __order = new[] {{ {orderLiteral} }};");
        ctx.Line($"    var __from = System.Array.IndexOf(__order, stored.{_property});");
        ctx.Line($"    var __to = System.Array.IndexOf(__order, item.{_property});");
        ctx.Line();
        ctx.Line("    if (__from >= 0 && __to >= 0 && __to < __from)");
        ctx.Line($"        errors.Add($\"{_property} cannot move backward from '{{stored.{_property}}}' to '{{item.{_property}}}'.\");");

        if (_finalValues.Count > 0)
        {
            ctx.Line($"    else if (__from >= 0 && new[] {{ {finalLiteral} }}.Contains(stored.{_property})"
                    + $" && !Equals(stored.{_property}, item.{_property}))");
            ctx.Line($"        errors.Add($\"{_property} cannot change once it is '{{stored.{_property}}}'.\");");
        }

        ctx.Line("}");
    }
}
