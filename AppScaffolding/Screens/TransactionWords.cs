namespace AppScaffolding.Screens;

/// <summary>
/// The words that make an entity name sound like a transaction.
/// </summary>
/// <remarks>
/// Its own file so it can be read, argued with and extended without reading the heuristic. The
/// list is deliberately short: every word here is one that could misclassify a master, and the
/// gazetteer is only ever half of the test — an entity also needs an outgoing foreign key before
/// any of these counts for anything.
/// </remarks>
public static class TransactionWords
{
    /// <summary>Lower-case, matched as a substring of the entity name.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "order", "invoice", "payment", "receipt", "booking", "reservation",
        "sale", "purchase", "shipment", "delivery", "transfer", "transaction",
        "refund", "claim", "dispatch", "movement", "adjustment",
    ];

    /// <summary>Whether <paramref name="entityName"/> contains one of the words.</summary>
    /// <remarks>
    /// Substring rather than whole-word, so SalesOrder and PurchaseInvoice match. That is also why
    /// the list holds no word that is a common fragment of an unrelated one.
    /// </remarks>
    public static bool Contains(string entityName)
        => All.Any(word => entityName.Contains(word, StringComparison.OrdinalIgnoreCase));
}
