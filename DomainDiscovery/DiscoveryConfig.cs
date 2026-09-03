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

    /// <summary>
    /// The whole-call ceiling, not the framework's 100 seconds: a thorough answer measured 2.5
    /// minutes on a CPU-only machine and a large domain considerably more, so the default would
    /// kill ordinary requests.
    /// </summary>
    /// <remarks>
    /// Five rather than the ten it was. Ten was chosen when a run that went wrong produced nothing
    /// at all, so waiting longer could only help; now a run that is stopped keeps every class it
    /// had already written, which makes a shorter ceiling strictly better. In practice
    /// <see cref="StallSeconds"/> ends a bad run long before this does.
    /// </remarks>
    public const int DefaultTimeoutMinutes = 5;

    /// <summary>
    /// How long a generation may go without completing another class before it is stopped and
    /// whatever it produced is kept.
    /// </summary>
    /// <remarks>
    /// This is the bound that actually fixes the "it hangs and then says the model did not answer"
    /// failure. A healthy run completes a class every 12-15 seconds, so two minutes cannot
    /// interrupt one that is merely slow; a generation that has stopped terminating (llama.cpp's
    /// --context-shift lets one run indefinitely) is caught in two minutes instead of ten, with
    /// its finished classes intact.
    /// <para>
    /// Raise it on a markedly slower machine. A token cap cannot do this job: the number of
    /// seconds a token budget takes depends on the hardware, which is exactly what made the
    /// previous 4096-token cap unreachable before the timeout it was supposed to beat.
    /// </para>
    /// </remarks>
    public const int DefaultStallSeconds = 120;

    public string Source { get; set; } = OllamaSource;
    public string Model { get; set; } = DefaultModel;
    public string Endpoint { get; set; } = DefaultEndpoint;
    public int TimeoutMinutes { get; set; } = DefaultTimeoutMinutes;
    public int StallSeconds { get; set; } = DefaultStallSeconds;

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
