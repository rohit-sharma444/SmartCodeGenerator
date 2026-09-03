using System.Text.Json.Serialization;

namespace DomainDiscovery.Llm;

/// <summary>One property as the model described it — before any repair. See
/// <see cref="LlmModelNormalizer"/> for why nothing here is trusted as-is.</summary>
public sealed class LlmProperty
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
}

public sealed class LlmRelationship
{
    [JsonPropertyName("target")] public string Target { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
}

public sealed class LlmClass
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>The model's guess at an <c>EntityRole</c> name. Parsed leniently; an
    /// unrecognized value becomes <c>EntityRole.None</c>.</summary>
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;

    /// <summary>One line saying what the class is for. Becomes the evidence on the class's
    /// <c>DecisionRecord</c>, so <c>why Customer</c> has a real answer on this path.</summary>
    [JsonPropertyName("purpose")] public string Purpose { get; set; } = string.Empty;

    [JsonPropertyName("properties")] public List<LlmProperty> Properties { get; set; } = new();
    [JsonPropertyName("relationships")] public List<LlmRelationship> Relationships { get; set; } = new();
}

public sealed class LlmDomainModel
{
    [JsonPropertyName("classes")] public List<LlmClass> Classes { get; set; } = new();
}
