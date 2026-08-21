namespace AppScaffolding.Screens;

/// <summary>One line saying how the model was split into screens.</summary>
/// <remarks>
/// A misclassification is otherwise discoverable only by launching the application and noticing a
/// screen is missing or shaped wrongly. Line entities are named individually for exactly that
/// reason: they are the ones that got no screen at all, and nothing else in the build output would
/// mention them.
/// </remarks>
public static class ScreenSummary
{
    /// <summary>The summary, or empty for a model with nothing in it.</summary>
    public static string Describe(EntityRoleMap map)
    {
        var masters = map.Names(EntityRole.Master).Count;
        var transactions = map.Names(EntityRole.Transaction).Count;

        if (masters == 0 && transactions == 0)
            return string.Empty;

        var summary = $"{Count(masters, "master")}, {Count(transactions, "transaction")}";

        var withLines = map.Names(EntityRole.Transaction)
            .Where(name => map.LinesOf(name).Count > 0)
            .Select(name => $"{name} has {Count(map.LinesOf(name).Count, "line")}")
            .ToList();

        return withLines.Count == 0
            ? summary
            : $"{summary} ({string.Join(", ", withLines)})";
    }

    /// <summary>"1 master", "2 masters" — a generated line reading "1 masters" is the kind of
    /// detail that makes output look machine-made.</summary>
    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
