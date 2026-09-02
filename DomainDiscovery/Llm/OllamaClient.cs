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
public sealed class OllamaClient : IOllamaClient
{
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

    public OllamaClient(DiscoveryConfig config, HttpClient? http = null)
    {
        _config = config;

        // The framework's 100-second default would kill an ordinary request: a small domain
        // measured 64-72 seconds on a CPU-only machine.
        _http = http ?? new HttpClient();

        if (http is null)
            _http.Timeout = TimeSpan.FromMinutes(Math.Max(1, config.TimeoutMinutes));
    }

    public bool IsAvailable()
    {
        try
        {
            using var response = _http.GetAsync($"{_config.Endpoint}/api/tags").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
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
