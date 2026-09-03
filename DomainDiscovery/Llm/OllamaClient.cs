using System.Text;
using System.Text.Json;

namespace DomainDiscovery.Llm;

/// <summary>
/// The local Ollama server over its HTTP API.
/// </summary>
/// <remarks>
/// Uses schema-constrained output (<c>format</c>), which Ollama applies during decoding — that is
/// what makes a 3B model's JSON reliably parseable, and what lets <see cref="PartialLlmJson"/>
/// trust a half-received answer. <c>temperature:0</c> for as much run-to-run stability as a
/// sampled model can offer.
/// <para>
/// The response is <em>streamed</em>. That is not a refinement: on a CPU-only machine this model
/// generates at 4-10 tokens/s (median 6.9, measured over 3935 samples from the server's own log),
/// so a thorough answer of ~1100 tokens takes two and a half minutes and a large one much longer.
/// Without a stream there is nothing to show for that whole wait, and nothing to keep if it ends
/// badly. With one, <see cref="Ask"/> reports each class as it arrives and salvages every complete
/// class if the generation is stopped early.
/// </para>
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
    /// The cost is real and unavoidable: more classes means more tokens, and more tokens means
    /// minutes on a CPU. That cost is paid for by streaming rather than by asking for less.
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

        _http = http ?? new HttpClient();

        if (http is null)
            // The transport's own last resort. The stall detector below almost always fires long
            // before this does, but a connection that goes silent at the socket level rather than
            // at the token level is not something the stall detector can see.
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
            // _http's own Timeout is set for Ask()'s long generation.
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

    /// <summary>
    /// Passed as <c>options.num_ctx</c> — the shared window that counts the system prompt, the
    /// user's description AND the model's own reply together.
    /// </summary>
    /// <remarks>
    /// Sized for headroom over a long description rather than for the reply, which
    /// <see cref="ResponseTokenCap"/> bounds independently and much lower. Ollama may or may not
    /// honour this on a runner it has already loaded at another size; both outcomes are safe
    /// precisely because the cap fits inside the smaller of them.
    /// </remarks>
    private const int ContextWindow = 8192;

    /// <summary>
    /// Passed as <c>options.num_predict</c> — the hard cap on how many tokens the model may
    /// generate before Ollama stops it and reports <c>done_reason: "length"</c>.
    /// </summary>
    /// <remarks>
    /// 3072, and the exact number matters. Ollama starts a CPU runner with <c>-c 4096</c> by
    /// default and reuses a runner it has already loaded, so a request cannot assume it got the
    /// <see cref="ContextWindow"/> it asked for. It also starts every runner with
    /// <c>--context-shift</c>: when a generation reaches the end of the window llama.cpp does not
    /// stop it, it discards the oldest half of the context and carries on — which is how a
    /// generation becomes non-terminating, since the model loses the beginning of the JSON it is
    /// still supposed to be closing. The server's log shows exactly this, 32 times
    /// (<c>slot context shift, n_keep = 4, n_left = 4091, n_discard = 2045</c>).
    /// <para>
    /// So the reply cap plus the prompt must fit inside 4096, not merely inside
    /// <see cref="ContextWindow"/>: 3072 + the 354-token system prompt leaves ~670 tokens of
    /// description before a shift becomes possible at all.
    /// </para>
    /// <para>
    /// This replaces a 4096 cap that could not do its job. At the speeds actually observed on this
    /// hardware, 4096 tokens takes 7.6 minutes at best and 16.6 at worst — so for over half of all
    /// runs the cap could only fire AFTER the client had already given up, and the self-explaining
    /// <c>done_reason: "length"</c> it exists to produce was unreachable. The real bound on how
    /// long a run may take is now wall-clock (<see cref="DiscoveryConfig.StallSeconds"/> and
    /// <see cref="DiscoveryConfig.TimeoutMinutes"/>), which is the only bound that does not depend
    /// on how fast the machine happens to be.
    /// </para>
    /// </remarks>
    private const int ResponseTokenCap = 3072;

    public OllamaAnswer Ask(string description, Action<IReadOnlyList<string>>? onProgress = null)
        => AskAsync(description, onProgress).GetAwaiter().GetResult();

    private async Task<OllamaAnswer> AskAsync(
        string description, Action<IReadOnlyList<string>>? onProgress)
    {
        // The two wall-clock bounds. `budget` is the whole call; `stall` is reset every time a
        // class is completed, so a generation that is still making progress is never interrupted
        // however long it takes, and one that has stopped making progress is not waited out.
        using var budget = new CancellationTokenSource(Budget);
        using var stall = new CancellationTokenSource(StallWindow);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, stall.Token);

        var content = new StringBuilder();
        string? doneReason = null;
        var reported = 0;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{_config.Endpoint}/api/chat")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(RequestBody(description)), Encoding.UTF8,
                    "application/json")
            };

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(linked.Token);

                return OllamaAnswer.Failure(
                    $"the server returned {(int)response.StatusCode} {response.StatusCode}"
                    + (string.IsNullOrWhiteSpace(errorBody) ? "." : $": {errorBody.Trim()}"));
            }

            await using var body = await response.Content.ReadAsStreamAsync(linked.Token);
            using var reader = new StreamReader(body);

            while (await reader.ReadLineAsync(linked.Token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (!TryReadChunk(line, out var piece, out var chunkDoneReason, out var done))
                    continue;

                doneReason ??= chunkDoneReason;

                if (piece.Length > 0)
                {
                    content.Append(piece);

                    // Only when a class may have closed. Rescanning the whole buffer on every
                    // token would be wasted work; a class cannot complete without a '}'.
                    if (piece.Contains('}'))
                    {
                        var found = PartialLlmJson.ClassesSoFar(content.ToString());

                        if (found.Count > reported)
                        {
                            reported = found.Count;

                            // Progress is progress: the run is alive, so the stall clock restarts.
                            stall.CancelAfter(StallWindow);

                            onProgress?.Invoke(found);
                        }
                    }
                }

                if (done)
                    break;
            }
        }
        catch (OperationCanceledException) when (stall.IsCancellationRequested)
        {
            return Stopped(
                content, $"stopped after {Describe(StallWindow)} with no further classes — "
                + $"{_config.Model} was still connected but had stopped producing complete "
                + "classes");
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            return Stopped(
                content, $"stopped at the {Describe(Budget)} limit — {_config.Model} was still "
                + "generating");
        }
        catch (TaskCanceledException)
        {
            // Not one of ours: HttpClient.Timeout, which it reports as this type with a
            // TimeoutException inside. Ask hands SendAsync no token but `linked`, so anything else
            // would have matched a filter above.
            return Stopped(content, $"the request timed out after {TimeoutDescription()}");
        }
        catch (HttpRequestException ex)
        {
            return Stopped(content, $"could not reach {_config.Endpoint}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Stopped(content, $"the request failed: {ex.Message}");
        }

        return Finished(content.ToString(), doneReason);
    }

    /// <summary>
    /// The end of a stream that ran to completion — or stopped without ever saying <c>done</c>.
    /// </summary>
    private static OllamaAnswer Finished(string content, string? doneReason)
    {
        if (string.IsNullOrWhiteSpace(content))
            return OllamaAnswer.Failure(
                $"the server returned an empty response{DoneReasonNote(doneReason)}", doneReason);

        // The whole document first: that is the ordinary case and it keeps every class.
        try
        {
            if (JsonSerializer.Deserialize<LlmDomainModel>(content) is { } model)
                return OllamaAnswer.Success(model, doneReason);
        }
        catch (JsonException)
        {
            // Falls through to salvage - see below.
        }

        // It did not parse whole, so it was cut off. Everything already generated is still good.
        if (PartialLlmJson.Salvage(content) is { } salvaged)
            return OllamaAnswer.Partial(
                salvaged,
                $"{salvaged.Classes.Count} class(es) were read from an answer that was cut short"
                + DoneReasonNote(doneReason),
                doneReason);

        return OllamaAnswer.Failure(
            "the response could not be parsed as JSON "
            + $"({content.Length} characters, ending \"...{Tail(content)}\")"
            + DoneReasonNote(doneReason),
            doneReason);
    }

    /// <summary>
    /// A run that ended early, for any reason. Whatever the model had already finished saying is
    /// kept and reported as a partial answer; only a run with nothing complete in it is a failure.
    /// </summary>
    /// <remarks>
    /// This is the difference the user actually sees. Before it, every one of these paths returned
    /// null and the panel said "qwen2.5:3b did not answer" — throwing away, in the measured case,
    /// five complete classes that had already cost two minutes to generate.
    /// </remarks>
    private static OllamaAnswer Stopped(StringBuilder content, string reason)
    {
        if (PartialLlmJson.Salvage(content.ToString()) is { } salvaged)
            return OllamaAnswer.Partial(
                salvaged,
                $"{salvaged.Classes.Count} class(es) were kept from a run that {reason}.");

        return OllamaAnswer.Failure(reason + ".");
    }

    /// <summary>
    /// One line of the NDJSON stream. False for a line that is not a chunk at all, which is
    /// skipped rather than failed: one unreadable line must not lose an answer that is otherwise
    /// arriving fine.
    /// </summary>
    private static bool TryReadChunk(
        string line, out string content, out string? doneReason, out bool done)
    {
        content = string.Empty;
        doneReason = null;
        done = false;

        try
        {
            using var chunk = JsonDocument.Parse(line);
            var root = chunk.RootElement;

            if (root.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var piece)
                && piece.ValueKind == JsonValueKind.String)
                content = piece.GetString() ?? string.Empty;

            if (root.TryGetProperty("done_reason", out var reason))
                doneReason = reason.GetString();

            done = root.TryGetProperty("done", out var doneProperty)
                   && doneProperty.ValueKind == JsonValueKind.True;

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private object RequestBody(string description) => new
    {
        model = _config.Model,
        stream = true,
        options = new { temperature = 0, num_ctx = ContextWindow, num_predict = ResponseTokenCap },
        messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user", content = description }
        },
        format = Schema()
    };

    /// <summary>How long one call may run in total, however well it is going.</summary>
    private TimeSpan Budget => TimeSpan.FromMinutes(Math.Max(1, _config.TimeoutMinutes));

    /// <summary>
    /// How long a call may go without completing another class before it is stopped and whatever
    /// it has is kept.
    /// </summary>
    /// <remarks>
    /// A healthy run completes a class every 12-15 seconds (measured: 14 classes over 127
    /// seconds), so the default of two minutes is many times the real gap and cannot interrupt a
    /// run that is merely slow. It is what turns a non-terminating generation from ten minutes of
    /// nothing into two minutes and a usable answer.
    /// </remarks>
    private TimeSpan StallWindow => TimeSpan.FromSeconds(Math.Max(1, _config.StallSeconds));

    private string TimeoutDescription() => Describe(_http.Timeout);

    private static string Describe(TimeSpan span) => span.TotalSeconds < 60
        ? $"{span.TotalSeconds:0.#} second(s)"
        : $"{span.TotalMinutes:0.#} minute(s)";

    private static string Tail(string content) => content.Length <= 200 ? content : content[^200..];

    /// <summary>
    /// Nothing for a clean stop (<c>"stop"</c>) or no reason at all — the ordinary cases, worth no
    /// extra words. Called out explicitly for <c>"length"</c>, the truncation signal. Any other
    /// value is still shown, since it is still evidence.
    /// </summary>
    private static string DoneReasonNote(string? doneReason) => doneReason switch
    {
        null or "stop" => string.Empty,
        "length" => " — Ollama cut the response short (done_reason: \"length\"): it hit the "
            + "token or context limit before the model reached its own stop point.",
        _ => $" (done_reason: \"{doneReason}\")"
    };

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
