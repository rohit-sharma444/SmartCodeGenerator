using CodeGenerationEngine.Models;

namespace AppScaffolding.Screens;

/// <summary>
/// Emits <c>Screens/RequiredFields.cs</c>: which properties an entry form marks with an asterisk.
/// </summary>
/// <remarks>
/// Requiredness lives in <see cref="PropertyDefinition.Rules"/> and is compiled into the generated
/// validator's body, not onto the model — so a running application cannot see it by reflection and
/// the form needs its own map. Written from the same rule scan <see cref="ValidatorTemplate"/>
/// performs, so the asterisk and the validator cannot disagree.
/// <para>
/// Always emitted, empty when the build has no validation. Emitting it conditionally would push
/// every consumer into the conditional-fragment bargain <see cref="RecordEditorTemplate"/> already
/// strikes, in two more places, for no gain.
/// </para>
/// </remarks>
public static class RequiredFieldsTemplate
{
    private const string RequiredRule = "required";

    /// <summary>Source of <c>Screens/RequiredFields.cs</c>.</summary>
    public static string Source(string rootNamespace, IReadOnlyList<ClassDefinition>? classes)
    {
        var entries = (classes ?? [])
            .Select(c => (c.Name, Required: c.Properties
                .Where(p => p.Rules.Any(r =>
                    string.Equals(r.Name, RequiredRule, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name)
                .ToList()))
            .Where(x => x.Required.Count > 0)
            .Select(x =>
                $"        [\"{x.Name}\"] = [{string.Join(", ", x.Required.Select(n => $"\"{n}\""))}],")
            .ToList();

        // The map carries its own outer braces so the $$ literal below only ever needs a plain
        // {{map}} hole -- no {{{...}}} escaping to get right, and a valid initializer either way:
        // "{ }" with no entries, or "{ <entries> }" with them.
        var map = entries.Count == 0
            ? "{ }"
            : "{\r\n" + string.Join("\r\n", entries) + "\r\n    }";

        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;

namespace {{rootNamespace}}.Screens;

/// <summary>Which properties an entry form marks required.</summary>
public static class RequiredFields
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.Ordinal)
    {{map}};

    /// <summary>Whether <paramref name="property"/> on <paramref name="type"/> is required.
    /// False for a type the map does not hold, which is every type in a build with no
    /// validation.</summary>
    public static bool IsRequired(Type type, string property)
        => Map.TryGetValue(type.Name, out var names)
           && names.Contains(property, StringComparer.Ordinal);
}
""";
    }
}
