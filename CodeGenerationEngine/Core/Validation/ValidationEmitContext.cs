using System.Linq;
using System.Text;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>Sink a rule writes its generated C# into. <see cref="Line"/> appends
/// one statement inside the generated <c>Validate()</c> method body.</summary>
public sealed class ValidationEmitContext
{
    private readonly StringBuilder _sb;

    public ValidationEmitContext(StringBuilder sb, IReadOnlyList<PropertyDefinition> properties)
    {
        _sb = sb;
        Properties = properties;
    }

    public IReadOnlyList<PropertyDefinition> Properties { get; }
    public string Source => _sb.ToString();

    public void Line(string code) => _sb.AppendLine("            " + code);
    public void Line() => _sb.AppendLine();

    public bool HasProperty(string name) =>
        Properties.Any(p => string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase));
}
