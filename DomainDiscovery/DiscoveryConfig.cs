using System.Text.Json;

namespace DomainDiscovery;

/// <summary>
/// discovery.json, beside the binary — the same place and pattern <c>AiConfig</c> already uses.
/// </summary>
/// <remarks>
/// Never throws. A missing or unreadable file yields defaults, because a broken config must
/// leave the tool working rather than break the stage it configures.
/// </remarks>
public sealed class DiscoveryConfig
{
    public const string OllamaSource = "Ollama";
    public const string WikipediaSource = "Wikipedia";

    /// <summary>Chosen over llama3.2:3b because it emitted valid C# identifiers and type names
    /// where llama3.2 emitted <c>customer_id</c> and <c>datetime</c>, leaving the normalizer
    /// less to repair.</summary>
    public const string DefaultModel = "qwen2.5:3b";

    public const string DefaultEndpoint = "http://localhost:11434";

    /// <summary>Ten minutes, not HttpClient's 100 seconds. A small domain measured 64-72s on a
    /// CPU-only machine, so the framework default would kill ordinary requests.</summary>
    public const int DefaultTimeoutMinutes = 10;

    public string Source { get; set; } = OllamaSource;
    public string Model { get; set; } = DefaultModel;
    public string Endpoint { get; set; } = DefaultEndpoint;
    public int TimeoutMinutes { get; set; } = DefaultTimeoutMinutes;

    public static string DefaultPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discovery.json");

    public static DiscoveryConfig Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
                return new DiscoveryConfig();

            return JsonSerializer.Deserialize<DiscoveryConfig>(
                       File.ReadAllText(path),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new DiscoveryConfig();
        }
        catch (Exception)
        {
            return new DiscoveryConfig();
        }
    }
}
