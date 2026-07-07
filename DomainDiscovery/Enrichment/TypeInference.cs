namespace DomainDiscovery.Enrichment;

/// <summary>
/// Heuristic mapping from a field name to a C# type. Deliberately simple: the
/// review loop lets the user correct anything this gets wrong.
/// </summary>
public static class TypeInference
{
    public static string Infer(string fieldName)
    {
        var name = (fieldName ?? string.Empty).ToLowerInvariant();

        if (name.StartsWith("is", StringComparison.Ordinal)
            || name.StartsWith("has", StringComparison.Ordinal))
            return "bool";

        if (ContainsAny(name, "price", "cost", "amount", "total", "fee", "fine", "balance", "salary"))
            return "decimal";

        if (ContainsAny(name, "date", "time", "created", "updated", "due"))
            return "DateTime";

        if (ContainsAny(name, "quantity", "count", "number", "qty", "age", "stock"))
            return "int";

        return "string";
    }

    private static bool ContainsAny(string text, params string[] words)
        => words.Any(w => text.Contains(w, StringComparison.Ordinal));
}
