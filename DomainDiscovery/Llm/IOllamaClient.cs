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
public sealed record OllamaAnswer(
    LlmDomainModel? Model,
    string? Error,
    string? DoneReason = null,
    string? Warning = null)
{
    /// <summary>Whether the call produced a model. Equivalent to <c>Model is not null</c>, kept
    /// as a named property because "did it work" is what most callers actually want to ask.</summary>
    public bool Succeeded => Model is not null;

    /// <summary>Carried on a success too, not only a failure: it is what tells you a run came
    /// close to the context limit without actually tripping it — evidence worth having before
    /// that becomes tomorrow's failure.</summary>
    public static OllamaAnswer Success(LlmDomainModel model, string? doneReason = null)
        => new(model, null, doneReason);

    /// <summary>
    /// A real answer, from a run that did not finish.
    /// </summary>
    /// <remarks>
    /// Succeeds, deliberately. A generation that is stopped early still leaves every class it had
    /// already written, and those classes are exactly as good as the ones from a run that
    /// finished — the model does not go back and revise them. Reporting this as a failure is what
    /// used to throw away two minutes of usable work and tell the user nothing had happened;
    /// <see cref="Warning"/> is how the panel says the list may be short without pretending it is
    /// empty.
    /// </remarks>
    public static OllamaAnswer Partial(LlmDomainModel model, string warning, string? doneReason = null)
        => new(model, null, doneReason, warning);

    public static OllamaAnswer Failure(string error, string? doneReason = null)
        => new(null, error, doneReason);
}

/// <summary>Talks to a local Ollama server. Never throws — see <see cref="Ask"/>.</summary>
public interface IOllamaClient
{
    /// <summary>Whether the server answers at all. A millisecond call, used to decide whether
    /// to run this source or fall back.</summary>
    bool IsAvailable();

    /// <summary>The model's proposed domain, or — on failure — why there is none. Never throws;
    /// the caller's job is to fall back or report, not to catch.</summary>
    /// <param name="onProgress">
    /// Called with the classes named so far, each time another one is complete, while the model is
    /// still generating. Optional, and called on whatever thread the read completes on — a UI
    /// caller must marshal. It exists because this call takes minutes on a CPU: without it the
    /// only honest thing to show for that time is a blank screen.
    /// </param>
    OllamaAnswer Ask(string description, Action<IReadOnlyList<string>>? onProgress = null);
}
