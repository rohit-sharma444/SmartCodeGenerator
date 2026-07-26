using System.Text;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Heuristic mapping from a field name to a C# type, matched on whole camel/Pascal
/// tokens. Deliberately simple: the review loop lets the user correct anything this
/// gets wrong.
/// </summary>
/// <remarks>
/// Mirrors <c>CodeGenerationEngine.Core.FieldTypeHeuristics</c>, which serves the
/// `create class` path. DomainDiscovery and CodeGenerationEngine share no project
/// reference, so the two cannot be one class; FieldTypeConformanceTests in
/// CodeGenerator.Tests — the only test project that sees both — pins them to one
/// corpus. Change one, change both.
///
/// Substring matching typed <c>Discount</c> as int ("count"), <c>Isbn</c> as bool
/// ("is") and <c>InvoiceNumber</c> as int. Whole-token matching removes that family.
/// </remarks>
public static class TypeInference
{
    private static readonly string[] BoolPrefixes = ["is", "has", "can", "should", "was"];

    private static readonly string[] DecimalWords =
        ["price", "cost", "amount", "total", "subtotal", "fee", "fine",
         "balance", "salary", "discount", "rate", "tax", "vat"];

    private static readonly string[] DateWords =
        ["date", "time", "at", "created", "updated", "due", "timestamp"];

    private static readonly string[] IntWords = ["count", "quantity", "qty", "age", "stock", "id"];

    private static readonly string[] GuidWords = ["guid", "uuid"];

    public static string Infer(string fieldName)
    {
        var tokens = Tokenize(fieldName);

        if (tokens.Count == 0)
            return "string";

        // A leading Is/Has/Can/Should/Was makes the field a flag whatever follows:
        // HasLateFee is a bool, not a decimal.
        if (BoolPrefixes.Contains(tokens[0]))
            return "bool";

        // `Number` alone is a count; `<X>Number` is an identifier code
        // (InvoiceNumber, PhoneNumber, SerialNumber) and stays a string.
        if (tokens[^1] == "number")
            return tokens.Count == 1 ? "int" : "string";

        // The head noun decides: TotalCost is a cost, OrderDate is a date.
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var type = Lookup(tokens[i]);
            if (type is not null)
                return type;
        }

        return "string";
    }

    private static string? Lookup(string token) =>
        DecimalWords.Contains(token) ? "decimal"
        : DateWords.Contains(token) ? "DateTime"
        : IntWords.Contains(token) ? "int"
        : GuidWords.Contains(token) ? "Guid"
        : null;

    /// <summary>
    /// Lower-cased camel/Pascal tokens. A run of capitals stays whole, so "ISBN" is
    /// one token and never matches the "is" prefix rule.
    /// </summary>
    private static List<string> Tokenize(string? fieldName)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in fieldName ?? string.Empty)
        {
            if (ch is '_' or ' ' or '-')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(ch) && current.Length > 0 && !char.IsUpper(current[^1]))
                Flush();

            current.Append(ch);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length > 0)
                tokens.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }
    }
}
