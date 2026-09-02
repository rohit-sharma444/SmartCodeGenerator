using System.Text;
using System.Text.Json;

namespace DomainDiscovery.Llm;

/// <summary>
/// The local Ollama server over its HTTP API.
/// </summary>
/// <remarks>
/// Uses schema-constrained output (<c>format</c>), which Ollama applies during decoding — that is
/// what makes a 3B model's JSON reliably parseable. <c>temperature:0</c> for as much run-to-run
/// stability as a sampled model can offer.
/// </remarks>
public sealed class OllamaClient : IOllamaClient, IDisposable
{
    /// <summary>
    /// The availability probe's own timeout — deliberately much shorter than <see cref="_http"/>'s
    /// request timeout. That long timeout is right for <see cref="Ask"/>, which waits on a real
    /// generation; a customer whose <c>Endpoint</c> points at a LAN box that drops packets (rather
    /// than refusing the connection) must not have the "millisecond probe" <see cref="IsAvailable"/>
    /// hang for the same ten minutes.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private const string SystemPrompt =
        "You are a domain modelling assistant. Given a description of a business system, return "
        + "the data model: the classes, their properties with C# types, and the relationships "
        + "between classes. "
        + "Each class has two SEPARATE fields, and they must never be confused: "
        + "\"name\": a concrete domain noun taken from the description itself, e.g. Customer, "
        + "Product, Sale, Order, Employee. NEVER use a role word (see below) as a class name. "
        + "\"role\": a classification tag for the class, chosen from exactly this list: Party (a "
        + "person or organisation), Device, Place, Item (a thing bought or held), Document (a "
        + "record of something that happened), Transaction (value changing hands), Event "
        + "(something scheduled). "
        + "Example: a class named \"Customer\" gets role \"Party\". A class named \"Sale\" gets "
        + "role \"Transaction\". The role word is metadata about the class; it must never appear "
        + "as the class's name. "
        + "Return only JSON matching the schema.";

    private readonly DiscoveryConfig _config;
    private readonly HttpClient _http;

    /// <summary>Whether this instance created <see cref="_http"/> itself, and so owns disposing it.
    /// An injected client (tests, or a future caller with its own lifetime rules) is never ours to
    /// dispose.</summary>
    private readonly bool _ownsHttp;

    public OllamaClient(DiscoveryConfig config, HttpClient? http = null)
    {
        _config = config;
        _ownsHttp = http is null;

        // The framework's 100-second default would kill an ordinary request: a small domain
        // measured 64-72 seconds on a CPU-only machine.
        _http = http ?? new HttpClient();

        if (http is null)
            _http.Timeout = TimeSpan.FromMinutes(Math.Max(1, config.TimeoutMinutes));
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }

    public bool IsAvailable()
    {
        try
        {
            // A short-lived token of its own: this probe must stay a millisecond call even though
            // _http's own Timeout is set for Ask()'s ten-minute generation.
            using var cts = new CancellationTokenSource(ProbeTimeout);

            using var response = _http
                .GetAsync($"{_config.Endpoint}/api/tags", cts.Token)
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return false;

            var body = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
                return false;

            return IsConfiguredModelPulled(models);
        }
        catch (Exception)
        {
            // Unreachable server, timeout, or a body /api/tags did not in fact return.
            return false;
        }
    }

    /// <summary>
    /// Whether <c>models[].name</c> lists <see cref="DiscoveryConfig.Model"/>. Ollama reports pulled
    /// models by their full "name:tag" (a plain pull with no tag is stored under "latest"), so a
    /// configured name that names no tag of its own — "qwen2.5" rather than "qwen2.5:3b" — is
    /// compared against "&lt;name&gt;:latest" rather than requiring an exact match; a configured
    /// name that already carries a tag is compared exactly. Both sides are compared
    /// case-insensitively, since nothing enforces that a hand-edited config matches Ollama's own
    /// (lowercase) casing.
    /// </summary>
    private bool IsConfiguredModelPulled(JsonElement models)
    {
        var configured = _config.Model ?? string.Empty;
        var wanted = configured.Contains(':') ? configured : $"{configured}:latest";

        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind != JsonValueKind.Object)
                continue;

            if (!model.TryGetProperty("name", out var nameProperty))
                continue;

            var pulled = nameProperty.GetString();
            if (pulled is not null && string.Equals(pulled, wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public LlmDomainModel? Ask(string description)
    {
        try
        {
            var request = new
            {
                model = _config.Model,
                stream = false,
                options = new { temperature = 0 },
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = description }
                },
                format = Schema()
            };

            var body = new StringContent(
                JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            using var response = _http
                .PostAsync($"{_config.Endpoint}/api/chat", body)
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return null;

            var envelope = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            using var document = JsonDocument.Parse(envelope);

            var content = document.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return null;

            return JsonSerializer.Deserialize<LlmDomainModel>(content);
        }
        catch (Exception)
        {
            // Unreachable server, timeout, or output the schema did not in fact constrain.
            // Null is the contract; the caller falls back.
            return null;
        }
    }

    /// <summary>The JSON schema Ollama constrains decoding to.</summary>
    private static object Schema() => new
    {
        type = "object",
        properties = new
        {
            classes = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        role = new { type = "string" },
                        purpose = new { type = "string" },
                        properties = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    type = new { type = "string" }
                                },
                                required = new[] { "name", "type" }
                            }
                        },
                        relationships = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    target = new { type = "string" },
                                    role = new { type = "string" }
                                },
                                required = new[] { "target", "role" }
                            }
                        }
                    },
                    required = new[] { "name", "role", "properties" }
                }
            }
        },
        required = new[] { "classes" }
    };
}
