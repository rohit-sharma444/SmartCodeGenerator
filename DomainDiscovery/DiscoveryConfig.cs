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

    /// <summary>
    /// How many calls a discovery makes: 2 to ask which classes exist and then what is in them,
    /// 1 to ask for everything at once.
    /// </summary>
    /// <remarks>
    /// Two by default, measured on a point-of-sale system against the real model:
    /// <list type="bullet">
    /// <item>Two phases put the COMPLETE list of twelve classes on screen at 42 seconds, and
    /// finished at 229.</item>
    /// <item>One phase produced its first class at about 17 seconds and its tenth — the last — at
    /// 158, so there is no moment before the end where the whole list can be seen.</item>
    /// </list>
    /// The split costs about 45% more in total and buys two things: a readable class list in a
    /// quarter of the time, and much better behaviour when a run is cut short, since the roster
    /// decides which classes exist and it is the pass that almost always finishes. Set this to 1
    /// for the shortest total wait.
    /// </remarks>
    public const int DefaultPhases = 2;

    public string Source { get; set; } = OllamaSource;
    public string Model { get; set; } = DefaultModel;
    public string Endpoint { get; set; } = DefaultEndpoint;
    public int TimeoutMinutes { get; set; } = DefaultTimeoutMinutes;
    public int StallSeconds { get; set; } = DefaultStallSeconds;
    public int Phases { get; set; } = DefaultPhases;

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
