using System.Text.Json;

namespace DomainDiscovery.Llm;

/// <summary>
/// Reads the classes a model has finished writing out of a JSON document it has not finished
/// writing.
/// </summary>
/// <remarks>
/// Two jobs, one scan. While a generation is still running this is what lets the Discover panel
/// name the classes found so far instead of showing a blank wait; when a generation is stopped
/// early — the budget in <see cref="OllamaClient"/>, or the model failing to terminate — it is
/// what turns "nothing" into "most of an answer".
/// <para>
/// It exists because of a measurement, not a hunch: a real generation cut at 60% of its length
/// still had five complete, valid classes in the bytes already received, and the non-streaming
/// client threw all five away because the document <em>as a whole</em> did not parse. Everything
/// this recovers was already paid for.
/// </para>
/// <para>
/// Deliberately a brace scan rather than a streaming JSON reader: the input is a prefix, so every
/// incremental reader in the framework reports it as invalid, which is exactly the answer that is
/// no use here. Schema-constrained decoding guarantees the prefix is well-formed as far as it
/// goes, which is the only property this relies on.
/// </para>
/// </remarks>
public static class PartialLlmJson
{
    /// <summary>
    /// The classes complete in <paramref name="prefix"/>, or null when not one of them is —
    /// including when <paramref name="prefix"/> is a complete document, which this reads in full.
    /// </summary>
    public static LlmDomainModel? Salvage(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return null;

        var end = LastCompleteClassEnd(prefix);

        if (end < 0)
            return null;

        try
        {
            // Close the two containers the prefix left open — the classes array and the root
            // object. Everything before `end` is untouched, so what comes back is exactly what the
            // model said, never a repair of it.
            var model = JsonSerializer.Deserialize<LlmDomainModel>(
                string.Concat(prefix.AsSpan(0, end + 1), "]}"));

            return model is { Classes.Count: > 0 } ? model : null;
        }
        catch (JsonException)
        {
            // A prefix that is not the {"classes":[...]} shape at all. Nothing to salvage, and
            // saying so is this method's contract.
            return null;
        }
    }

    /// <summary>
    /// The complete classes as "<c>Name [Role]</c>" lines — what the Discover panel lists while
    /// the model is still generating. Empty rather than null, since a caller drawing a list wants
    /// a list.
    /// </summary>
    public static IReadOnlyList<string> ClassesSoFar(string? prefix)
    {
        var model = Salvage(prefix);

        if (model is null)
            return [];

        return model.Classes
            .Select(c => string.IsNullOrWhiteSpace(c.Role) ? c.Name : $"{c.Name} [{c.Role}]")
            .ToList();
    }

    /// <summary>
    /// The index of the <c>}</c> closing the last complete class, or -1 when there is none.
    /// </summary>
    /// <remarks>
    /// Depth 1 is the root object, 2 the <c>classes</c> array, 3 a class — so a <c>}</c> that
    /// returns the depth to 2 is a class that got written in full. A class's own nested containers
    /// (its properties and relationships arrays, and the objects in them) close at depth 4 and
    /// above and so cannot be mistaken for one.
    /// <para>
    /// String state is tracked because the model writes prose into <c>purpose</c>, and a brace or
    /// a bracket in that prose would otherwise move the depth and let a half-written class be
    /// salvaged as though it were whole.
    /// </para>
    /// </remarks>
    private static int LastCompleteClassEnd(string text)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var last = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                // Only meaningful inside a string, but harmless outside one: JSON has no other
                // use for a backslash, so a document containing one there is malformed anyway.
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            switch (c)
            {
                case '{':
                case '[':
                    depth++;
                    break;

                case '}':
                case ']':
                    depth--;

                    if (c == '}' && depth == 2)
                        last = i;

                    break;
            }
        }

        return last;
    }
}
