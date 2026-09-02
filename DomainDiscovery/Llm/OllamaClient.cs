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

    /// <summary>
    /// What the model is told before the user's description.
    /// </summary>
    /// <remarks>
    /// Every clause here was put in by measurement, not taste, because a 3B model's output moves
    /// sharply with wording and no unit test can see any of it. Measured on qwen2.5:3b against
    /// "Inventory Management system":
    /// <list type="bullet">
    /// <item>Asking only for "the data model" returned THREE classes. Asking for a complete model
    /// and saying not to stop at the obvious ones returned NINE. That instruction is the whole
    /// difference between a usable starting point and a toy.</item>
    /// <item>Naming the roles without forbidding them as names returned classes literally called
    /// Item and Transaction. Saying which ones are always wrong, with the right name beside each,
    /// returned zero role-word names.</item>
    /// <item>With a terse description the model prefixed every class with the system's own name —
    /// InventoryItem, InventoryLog, InventoryPolicy — so it is told to name the thing, not the
    /// system, and not to emit near-duplicates.</item>
    /// </list>
    /// The cost is real and unavoidable: more classes means more tokens, and discovery went from
    /// ~42s to ~3 minutes on a CPU-only machine. Thoroughness is what was asked for.
    /// <para>
    /// If you edit this, re-measure against a running model. A schema-validity test passes either
    /// way, so the suite cannot tell you that you have made the output worse.
    /// </para>
    /// </remarks>
    private const string SystemPrompt =
        "You are a domain modelling assistant. Given a description of a business system, return "
        + "the complete data model a developer would need to build it. "
        + "Be thorough: cover the things the system tracks, the people and organisations "
        + "involved, the documents and transactions it records, and the reference data it needs. "
        + "Do not stop at the two or three most obvious classes. "
        + "Each class needs a name, a role, a one-line purpose, its properties with C# types, and "
        + "its relationships. "
        + "The name must be a concrete business noun (Product, Supplier, PurchaseOrder, "
        + "StockMovement, Warehouse). It must NEVER be one of the role words below, which are "
        + "classification tags and not names: a class named exactly Item, Transaction, Document, "
        + "Party, Event, Device or Place is always wrong. Name the real thing instead: Product "
        + "not Item, StockMovement not Transaction, Supplier not Party. "
        + "The role is a separate field, chosen from exactly this list: Party (a person or "
        + "organisation), Device, Place, Item (a thing bought or held), Document (a record of "
        + "something that happened), Transaction (value changing hands), Event (something "
        + "scheduled). "
        + "Name each class for the thing itself, never for the system it belongs to: in an "
        + "inventory system use Product, Supplier, Warehouse, StockMovement, PurchaseOrder - not "
        + "InventoryProduct or InventoryItem. Do not prefix every class with the system name. Do "
        + "not emit several near-duplicate classes that mean the same thing. "
        + "Example for a shop: Product [Item], Supplier [Party], Customer [Party], Sale "
        + "[Transaction], SaleLine [Document], Store [Place], Employee [Party]. "
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
