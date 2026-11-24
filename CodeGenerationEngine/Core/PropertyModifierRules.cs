using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>The C# rules for a property's <c>readonly</c>, <c>private</c>, <c>static</c> and
/// <c>required</c> words, and the text they write.</summary>
public static class PropertyModifierRules
{
    /// <summary>Why <paramref name="modifiers"/> cannot go on <paramref name="propertyName"/>
    /// (a required member must be settable by the caller, and an instance one), or null.</summary>
    public static string? Refusal(PropertyModifiers modifiers, string propertyName)
    {
        if (modifiers.HasFlag(PropertyModifiers.Required) && modifiers.HasFlag(PropertyModifiers.Static))
            return $"'{propertyName}' cannot be both required and static.";

        if (modifiers.HasFlag(PropertyModifiers.Required) && modifiers.HasFlag(PropertyModifiers.Private))
            return $"'{propertyName}' cannot be both required and private.";

        return null;
    }

    /// <summary>The accessors: <c>init</c> for a readonly property, none for a static readonly one
    /// (an <c>init</c> accessor cannot be static).</summary>
    public static string Accessors(PropertyModifiers m)
    {
        if (!m.HasFlag(PropertyModifiers.ReadOnly))
            return "{ get; set; }";

        return m.HasFlag(PropertyModifiers.Static) ? "{ get; }" : "{ get; init; }";
    }

    /// <summary>The words before the type, in C# order, ending in a space.</summary>
    public static string Prefix(PropertyModifiers m)
        => (m.HasFlag(PropertyModifiers.Private) ? "private " : "public ")
            + (m.HasFlag(PropertyModifiers.Static) ? "static " : string.Empty)
            + (m.HasFlag(PropertyModifiers.Required) ? "required " : string.Empty);
}
