namespace DomainDiscovery.Llm;

/// <summary>
/// What an ask produced: the parsed model on success, or — on failure — a one-line,
/// human-readable reason.
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

/// <summary>Talks to a local Ollama server. Never throws — every ask reports failure in its
/// <see cref="OllamaAnswer"/> instead.</summary>
/// <remarks>
/// <paramref name="onClasses"/> on every ask is called with the classes completed so far, each
/// time another one is finished, while the model is still generating. Optional, and called on
/// whatever thread the read completes on — a UI caller must marshal. It exists because these calls
/// take minutes on a CPU: without it the only honest thing to show for that time is a blank
/// screen.
/// </remarks>
public interface IOllamaClient
{
    /// <summary>Whether the server answers at all. A millisecond call, used to decide whether
    /// to run this source or fall back.</summary>
    bool IsAvailable();

    /// <summary>The whole model in one call — names, roles, properties and relationships.</summary>
    OllamaAnswer Ask(string description, Action<IReadOnlyList<LlmClass>>? onClasses = null);

    /// <summary>
    /// Phase one of the two-phase route: which classes exist, with roles and purposes, and nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// Measured at 42-68 seconds against 158 for <see cref="Ask"/>, because the schema it is given
    /// has no room for properties — so this is what puts a complete class list in front of the
    /// user while the long pass is still to come.
    /// </remarks>
    OllamaAnswer AskForClasses(string description, Action<IReadOnlyList<LlmClass>>? onClasses = null);

    /// <summary>
    /// Phase two: the properties and relationships of classes that have already been chosen.
    /// </summary>
    /// <remarks>
    /// The model is told not to add, remove or rename anything, and
    /// <see cref="TwoPhaseModel.Merge"/> enforces that regardless — a 3B model obeys such an
    /// instruction most of the time rather than always.
    /// </remarks>
    OllamaAnswer AskForDetail(
        string description, LlmDomainModel roster, Action<IReadOnlyList<LlmClass>>? onClasses = null);
}
