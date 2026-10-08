using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DomainDiscovery;
using DomainDiscovery.Llm;

namespace CodeGenerator.BuildFix;

/// <summary>
/// Asks the local Ollama model for a fix. A small non-streaming <c>/api/chat</c> call: the existing
/// <see cref="OllamaClient"/> is shaped around domain models and streams classes, which is the wrong
/// tool for "rewrite these 30 lines". Same endpoint, model and timeout settings (<c>discovery.json</c>).
/// </summary>
/// <remarks>
/// The reply is untrusted. It must hold exactly one fenced block, must not name a file of its own,
/// must differ from the excerpt and must not be wildly longer than it; anything else is "no
/// proposal". The model is told only the file's NAME, never a path, and it cannot choose where its
/// text goes: the caller writes it over the excerpt it sent.
/// <para>
/// The wait is wall-clock bound (CPU-only Ollama runs at 4-10 tokens/s), so the bound is a timeout
/// from <see cref="DiscoveryConfig.TimeoutMinutes"/>, not a token cap.
/// </para>
/// </remarks>
internal sealed class OllamaBuildFixAi : IBuildFixAi, IDisposable
{
    private const string SystemPrompt =
        "You fix C# compiler errors. You are given a compiler error and the lines of a file around it. "
        + "Reply with the corrected version of EXACTLY those lines, in one ```csharp fenced block, and nothing else. "
        + "Change as little as possible. Do not add explanations.";

    private static readonly Regex Fenced = new(
        @"```[A-Za-z#]*\r?\n(?<code>.*?)\r?\n?```", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>A header line such as <c>// File: ..\Other.cs</c>: the model naming a file of its own.</summary>
    private static readonly Regex FileHeader = new(
        @"^\s*(//|#|/\*)\s*(file|filename|path)\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DiscoveryConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<bool> _available;
    private readonly TimeSpan _timeout;

    public OllamaBuildFixAi(
        DiscoveryConfig config, HttpClient? http = null, Func<bool>? available = null, TimeSpan? timeout = null)
    {
        _config = config;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _timeout = timeout ?? TimeSpan.FromMinutes(Math.Clamp(config.TimeoutMinutes, 1, 3));

        _available = available ?? (() =>
        {
            using var client = new OllamaClient(config);
            return client.IsAvailable();
        });
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
            return _available();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? Propose(BuildDiagnostic d, string excerpt)
    {
        try
        {
            var body = new
            {
                model = _config.Model,
                stream = false,
                options = new { temperature = 0, num_ctx = 4096 },
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new
                    {
                        role = "user",
                        content = $"File {Path.GetFileName(d.File)}\nError {d.Code}: {d.Message}\n\nLines:\n```csharp\n{excerpt}\n```",
                    },
                },
            };

            // A per-call deadline: the whole wait is bounded in wall-clock time, however the
            // injected client is configured.
            using var cts = new CancellationTokenSource(_timeout);

            using var response = _http
                .PostAsJsonAsync($"{_config.Endpoint.TrimEnd('/')}/api/chat", body, cts.Token)
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return null;

            var json = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(json);

            // A reply cut off by the model's own limit is not a whole answer.
            if (doc.RootElement.TryGetProperty("done_reason", out var why)
                && why.ValueKind == JsonValueKind.String && why.GetString() == "length")
                return null;

            var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString();

            return content is null ? null : Validate(content, excerpt);
        }
        catch (Exception)
        {
            // Unreachable server, timeout or cancel, malformed JSON, or an unexpected shape: none of
            // these is a failure of the loop, only "no proposal".
            return null;
        }
    }

    private static string? Validate(string content, string excerpt)
    {
        var blocks = Fenced.Matches(content);

        // Two blocks cannot be told apart (the second may be "another file"); none is prose.
        if (blocks.Count != 1)
            return null;

        var code = blocks[0].Groups["code"].Value.Trim('\r', '\n');

        if (code.Trim().Length == 0)
            return null;

        var lines = code.Split('\n');

        if (lines.Any(l => FileHeader.IsMatch(l)))
            return null;

        if (Normalize(code) == Normalize(excerpt))
            return null;

        // A fix of a few lines does not triple them: that is a rewrite, not a fix.
        var excerptLines = excerpt.Split('\n').Length;

        if (lines.Length > excerptLines * 3 + 5)
            return null;

        // Keep the excerpt's own line endings so the edit does not mix them.
        return excerpt.Contains("\r\n") ? code.Replace("\r\n", "\n").Replace("\n", "\r\n") : code;
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim('\n', ' ', '\t');
}
