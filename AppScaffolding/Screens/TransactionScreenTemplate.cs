namespace AppScaffolding.Screens;

/// <summary>
/// Emits <c>ViewModels/TransactionScreenViewModel.cs</c>: a master screen plus the three things a
/// transaction list always needs — a date range, column totals, and a role that says so.
/// </summary>
/// <remarks>
/// Derived from the master screen rather than written beside it: a transaction differs from a
/// master in exactly these ways, and stating that in the type is cheaper than stating it twice in
/// two screens that would then drift.
/// </remarks>
public static class TransactionScreenTemplate
{
    /// <summary>Source of <c>ViewModels/TransactionScreenViewModel.cs</c>.</summary>
    public static string Source(string rootNamespace, bool editsInline = false) => $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{rootNamespace}}.Screens;

namespace {{rootNamespace}}.ViewModels;

/// <summary>One money or quantity column, summed over whatever the list is showing.</summary>
public sealed record ColumnTotal(string Column, decimal Value);

/// <summary>A transaction list: filtered by date, totalled by column.</summary>
public sealed class TransactionScreenViewModel<T> : MasterScreenViewModel<T> where T : class, new()
{
    /// <summary>
    /// The dates every discovered class carries, which are never what a user means by "the date".
    /// </summary>
    /// <remarks>
    /// The generator excludes these when classifying, and the running app has to exclude them too:
    /// filtering a screen on CreatedOn would filter it on a field nobody chose and nobody sees.
    /// </remarks>
    private static readonly string[] AuditFields = ["CreatedOn", "ModifiedOn"];

    private readonly Func<IReadOnlyList<ILineSet>>? _lines;

    private DateTime? _from;
    private DateTime? _to;

    /// <param name="lines">
    /// Builds this transaction's line sets, one editor at a time. A factory rather than a list
    /// because every editor needs its OWN lines: they are a copy of what the store holds, and two
    /// editors sharing one set would share the copy.
    /// </param>
    public TransactionScreenViewModel(string title, Func<IReadOnlyList<ILineSet>>? lines = null)
        : base(title)
    {
        _lines = lines;

        // A date every record has comes before one some leave empty (DateTime?).
        DateField = typeof(T).GetProperties()
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(DateTime))
            .OrderBy(p => p.PropertyType != typeof(DateTime))
            .Select(p => p.Name)
            .FirstOrDefault(name => !AuditFields.Contains(name, StringComparer.OrdinalIgnoreCase))
            ?? string.Empty;
    }

    public override EntityRole Role => EntityRole.Transaction;

    /// <inheritdoc />
    protected override bool EditsInline => {{(editsInline ? "true" : "false")}};

    /// <inheritdoc />
    protected override IReadOnlyList<ILineSet> LineSets() => _lines?.Invoke() ?? [];

    /// <summary>The date the range filters on, or empty when the model has none — in which case
    /// the view renders no range rather than filtering on something arbitrary.</summary>
    public string DateField { get; }

    public bool HasDateField => DateField.Length > 0;

    public DateTime? From
    {
        get => _from;
        set
        {
            if (SetField(ref _from, value))
                ShownChanged();
        }
    }

    public DateTime? To
    {
        get => _to;
        set
        {
            if (SetField(ref _to, value))
                ShownChanged();
        }
    }

    /// <summary>The rows matching the search AND the date range.</summary>
    public override IEnumerable Items => InRange(Matching(Records)).ToList();

    /// <summary>
    /// One total per money-shaped column, over whatever the list is currently showing.
    /// </summary>
    /// <remarks>
    /// Every decimal and double that is neither an Id nor a foreign key, rather than guessing
    /// which single column is "the" total. A generated application showing one confidently wrong
    /// number is worse than one showing several honest ones.
    /// </remarks>
    public IReadOnlyList<ColumnTotal> Totals
    {
        get
        {
            var rows = InRange(Matching(Records)).ToList();

            return MoneyColumns()
                .Select(p => new ColumnTotal(p.Name, rows.Sum(r => ToDecimal(p.GetValue(r)))))
                .ToList();
        }
    }

    private static IEnumerable<PropertyInfo> MoneyColumns()
        => typeof(T).GetProperties()
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var t
                        && (t == typeof(decimal) || t == typeof(double)))
            .Where(p => !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)
                        && !p.Name.EndsWith("Id", StringComparison.Ordinal));

    private static decimal ToDecimal(object? value)
        => value switch
        {
            decimal d => d,
            double d => (decimal)d,
            _ => 0m,
        };

    private IEnumerable<T> InRange(IEnumerable<T> source)
    {
        if (!HasDateField || (_from is null && _to is null))
            return source;

        var property = typeof(T).GetProperty(DateField);

        if (property is null)
            return source;

        return source.Where(record =>
        {
            if (property.GetValue(record) is not DateTime when)
                return true;

            if (_from is { } from && when < from)
                return false;

            return _to is not { } to || when <= to;
        });
    }

    /// <summary>The footer is summed over the rows on show, so it changes whenever they do —
    /// after a write as well as after a filter.</summary>
    protected override void ShownChanged()
    {
        base.ShownChanged();
        OnPropertyChanged(nameof(Totals));
    }
}
""";
}
