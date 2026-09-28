namespace AppScaffolding.Screens;

/// <summary>
/// Records a screen-split choice in the two comma-joined strings
/// <see cref="ScreenOverrides"/> parses.
/// </summary>
/// <remarks>
/// The write side of <see cref="ScreenOverrides"/>, kept beside it so the spelling of those
/// strings has one owner. Only DISAGREEMENTS with the heuristic are recorded, which is what the
/// <c>transactions</c>/<c>masters</c> options have always meant — "these name the ones it got
/// wrong". Recording agreement as well would put every class in the replay line, and a line
/// naming all of them says nothing about which ones the user actually moved.
/// <para>
/// The consequence, which is deliberate: agreeing with the heuristic does not PIN a class. Edit
/// the model afterwards so the heuristic decides differently and the class moves. That is visible
/// on the panel as it happens, which a stored answer silently overruling a re-classification
/// would not be.
/// </para>
/// </remarks>
public static class ScreenChoice
{
    /// <summary>
    /// <paramref name="current"/> with <paramref name="className"/> recorded as
    /// <paramref name="chosen"/>, given that the heuristic alone would make it
    /// <paramref name="baseline"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chosen"/> is <see cref="EntityRole.DocumentLine"/>. Nothing can ask for
    /// that: <see cref="EntityRoles.Classify"/> reaches it in a second pass that only a master
    /// override skips, so an answer saying "make this a line" is one the generator would drop
    /// without saying so.
    /// </exception>
    public static AppCustomization Set(
        AppCustomization current, string className, EntityRole chosen, EntityRole baseline)
    {
        if (chosen is not (EntityRole.Master or EntityRole.Transaction))
            throw new ArgumentOutOfRangeException(
                nameof(chosen),
                chosen,
                "A class can be recorded as a master or a transaction. Lines are decided by the "
                + "classifier; pull one out with a master choice instead.");

        // Out of BOTH first, whatever is being chosen. A name left in the other string is the
        // contradiction ScreenOverrides resolves by quietly preferring master — which would make
        // choosing "transaction" after "master" do nothing at all.
        var cleared = Clear(current, className);

        if (chosen == baseline)
            return cleared;

        return chosen == EntityRole.Transaction
            ? cleared with { Transactions = Add(cleared.Transactions, className) }
            : cleared with { Masters = Add(cleared.Masters, className) };
    }

    /// <summary>
    /// <paramref name="current"/> with no answer of any kind about <paramref name="className"/>.
    /// </summary>
    /// <remarks>
    /// How a caller works out the baseline to pass <see cref="Set"/>: classify with this, and the
    /// answer is what the heuristic says when the user's own choice is not in the way.
    /// </remarks>
    public static AppCustomization Clear(AppCustomization current, string className)
        => current with
        {
            Transactions = Remove(current.Transactions, className),
            Masters = Remove(current.Masters, className)
        };

    /// <summary>
    /// <paramref name="current"/> with any answer about <paramref name="className"/> moved to
    /// <paramref name="renamed"/>.
    /// </summary>
    /// <remarks>
    /// The answers are matched by name, so a class renamed after its screen was chosen would
    /// otherwise lose the choice - and leave it waiting for any class later given the old name.
    /// </remarks>
    public static AppCustomization Rename(AppCustomization current, string className, string renamed)
        => current with
        {
            Transactions = Rename(current.Transactions, className, renamed),
            Masters = Rename(current.Masters, className, renamed)
        };

    private static string Rename(string value, string className, string renamed)
        => string.Join(
            ",",
            Names(value).Select(n => string.Equals(n, className, StringComparison.OrdinalIgnoreCase) ? renamed : n));

    private static string Add(string value, string className)
        => string.Join(",", Names(value).Append(className));

    private static string Remove(string value, string className)
        => string.Join(
            ",",
            Names(value).Where(n => !string.Equals(n, className, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Split the way <see cref="ScreenOverrides"/> splits, so a value written here and a
    /// value typed on the command line are read identically.</summary>
    private static IEnumerable<string> Names(string value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
