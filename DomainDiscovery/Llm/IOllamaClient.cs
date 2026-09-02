namespace DomainDiscovery.Llm;

/// <summary>
/// What <see cref="IOllamaClient.Ask"/> produced: the parsed model on success, or — on
/// failure — a one-line, human-readable reason.
/// </summary>
/// <remarks>
/// Introduced because a bare <c>null</c> collapsed five genuinely different failures (a
/// non-2xx status, a timeout, an unreachable host, an envelope that did not carry the expected
/// shape, and content the schema did not in fact constrain to valid JSON) into one unhelpful
/// sentence — "qwen2.5:3b did not answer" — that told nobody, including the person debugging
/// it, which of those five had actually happened. <see cref="Error"/> is that missing sentence.
/// </remarks>
public sealed record OllamaAnswer(LlmDomainModel? Model, string? Error, string? DoneReason = null)
{
    /// <summary>Whether the call produced a model. Equivalent to <c>Model is not null</c>, kept
    /// as a named property because "did it work" is what most callers actually want to ask.</summary>
    public bool Succeeded => Model is not null;

    /// <summary>Carried on a success too, not only a failure: it is what tells you a run came
    /// close to the context limit without actually tripping it — evidence worth having before
    /// that becomes tomorrow's failure.</summary>
    public static OllamaAnswer Success(LlmDomainModel model, string? doneReason = null) => new(model, null, doneReason);

    public static OllamaAnswer Failure(string error, string? doneReason = null) => new(null, error, doneReason);
}

/// <summary>Talks to a local Ollama server. Never throws — see <see cref="Ask"/>.</summary>
public interface IOllamaClient
{
    /// <summary>Whether the server answers at all. A millisecond call, used to decide whether
    /// to run this source or fall back.</summary>
    bool IsAvailable();

    /// <summary>The model's proposed domain, or — on failure — why there is none. Never throws;
    /// the caller's job is to fall back or report, not to catch.</summary>
    OllamaAnswer Ask(string description);
}
