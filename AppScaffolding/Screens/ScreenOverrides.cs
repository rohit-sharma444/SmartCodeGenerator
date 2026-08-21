namespace AppScaffolding.Screens;

/// <summary>
/// The entities the user named, overruling the heuristic.
/// </summary>
/// <remarks>
/// Parsed from two comma-joined strings rather than carried as collections, because the strings
/// live on <see cref="AppCustomization"/> — a record compared by value across the existing suite,
/// where a list member would break equality silently, since records compare those by reference.
/// </remarks>
public sealed class ScreenOverrides
{
    private readonly HashSet<string> _transactions;
    private readonly HashSet<string> _masters;

    private ScreenOverrides(HashSet<string> transactions, HashSet<string> masters)
    {
        _transactions = transactions;
        _masters = masters;
    }

    /// <summary>Nothing overridden — the heuristic decides everything.</summary>
    public static ScreenOverrides None { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether the user named anything at all.</summary>
    public bool Any => _transactions.Count > 0 || _masters.Count > 0;

    public static ScreenOverrides Parse(string transactions, string masters)
        => new(Split(transactions), Split(masters));

    /// <summary>
    /// Whether the user asked for <paramref name="name"/> to be a transaction.
    /// </summary>
    /// <remarks>
    /// False when the same name was also given as a master. Naming an entity both ways is a
    /// contradiction, and master is the safe half to honour: an entity wrongly made a master
    /// renders as the ordinary grid this generator has always produced.
    /// </remarks>
    public bool IsTransaction(string name)
        => _transactions.Contains(name) && !_masters.Contains(name);

    public bool IsMaster(string name) => _masters.Contains(name);

    /// <summary>Trimmed as well as split: a person typing a quoted value writes "Order, Invoice"
    /// as readily as "Order,Invoice".</summary>
    private static HashSet<string> Split(string value)
        => new(
            (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
}
