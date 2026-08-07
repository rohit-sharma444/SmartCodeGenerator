using CodeIntelligence;

namespace CodeGenerator.App;

/// <summary>The `ai ...` meta-commands. Handled in the CLI beside `history`, `clear`
/// and `build app`, because they are console reports rather than code-generation
/// actions — and for the same reason they get no <c>IntentType</c> members: nothing in
/// LiteralCommandTable, the CommandFront guards or NlpCommandMapper produces them, so
/// enum members would be unreachable dead code.</summary>
internal static class AiCommand
{
    internal static bool IsAiCommand(string input)
    {
        var text = (input ?? string.Empty).Trim();

        return text.Equals("ai", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("ai ", StringComparison.OrdinalIgnoreCase);
    }

    internal static string Handle(string input, AiLayer layer)
    {
        var parts = (input ?? string.Empty)
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "status";

        switch (verb)
        {
            case "status":
                return layer.StatusText();

            case "enable":
                layer.Config.Enabled = true;
                layer.Config.Save(layer.ConfigPath);
                return "AI enabled. Restart the CLI to load the model, then run 'ai reindex' if prompted.";

            case "disable":
                layer.Config.Enabled = false;
                layer.Config.Save(layer.ConfigPath);
                return "AI disabled. Command recognition is unchanged from the rule-based path.";

            case "reindex":
                return layer.Reindex();

            case "mode":
                if (parts.Length < 3)
                    return "Usage: ai mode <retrieval|generative|hybrid>";

                var mode = parts[2].ToLowerInvariant();
                if (mode is not ("retrieval" or "generative" or "hybrid"))
                    return $"Unknown mode '{parts[2]}'. Use retrieval, generative or hybrid.";

                if (mode is "generative" or "hybrid")
                    return $"Mode '{mode}' arrives in phase B. Only 'retrieval' is available today.";

                layer.Config.ResolverMode = "Retrieval";
                layer.Config.Save(layer.ConfigPath);
                return "Mode set to Retrieval.";

            case "why":
                var last = layer.DecisionLog.Last();
                if (last is null)
                    return "No AI decisions recorded yet.";

                var verdict = last.Accepted switch
                {
                    true => "accepted",
                    false => "declined",
                    null => "not answered"
                };

                return $"""
                    Input:      {last.Input}
                    Read as:    {last.Intent} ({last.Confidence:P0} confidence)
                    Evidence:   corpus documents {last.EvidenceIds}
                    You:        {verdict}
                    Recorded:   {last.CreatedUtc:u}
                    """;

            default:
                return $"Unknown ai command '{verb}'. Try: status, enable, disable, reindex, mode, why.";
        }
    }
}
