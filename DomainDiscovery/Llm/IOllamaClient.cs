namespace DomainDiscovery.Llm;

/// <summary>Talks to a local Ollama server. Never throws — see <see cref="Ask"/>.</summary>
public interface IOllamaClient
{
    /// <summary>Whether the server answers at all. A millisecond call, used to decide whether
    /// to run this source or fall back.</summary>
    bool IsAvailable();

    /// <summary>The model's proposed domain, or null when the call failed or its output could
    /// not be parsed. Null rather than an exception because the caller's job is to fall back.</summary>
    LlmDomainModel? Ask(string description);
}
