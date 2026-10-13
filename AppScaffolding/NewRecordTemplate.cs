namespace AppScaffolding;

/// <summary>
/// The one rule for a record the user has just asked for: every non-nullable date starts at today.
/// </summary>
/// <remarks>
/// A <c>DateTime</c> left at <c>default</c> showed as 01-Jan-0001 on a new record's form in WPF and
/// on the web. WinForms hid it, because its date picker cannot show a date before 1753 and showed
/// today instead -- while the record under it still held 0001-01-01. Midnight, not now: a SQL
/// <c>date</c> column's rule refuses a time of day. A nullable date stays empty, which is what it
/// means. CreatedOn and ModifiedOn are left alone, because the store stamps them with the time.
/// <para>
/// Two shapes of the same loop: a public class in <c>Screens/LineSet.cs</c> for the editors and the
/// lines, which every business shell emits, and a private member of each store for the Add button
/// of the older layouts, which have no Screens folder.
/// </para>
/// </remarks>
public static class NewRecordTemplate
{
    /// <summary>The loop over <c>item</c>'s properties, indented for a method body.</summary>
    private const string Loop = """
        foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(DateTime) || !property.CanRead || !property.CanWrite
                || property.GetIndexParameters().Length > 0
                || property.Name is "CreatedOn" or "ModifiedOn")
                continue;

            // Unspecified, as every other date the stores write: a Local one went to JSON with an
            // offset beside rows that have none.
            if ((DateTime)property.GetValue(item)! == default)
                property.SetValue(item, DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified));
        }
""";

    /// <summary>
    /// The <c>NewRecord</c> class, for a file that imports System and System.Reflection.
    /// </summary>
    public static string Class() => $$"""
/// <summary>
/// The record a form starts from when the user asks for a new one.
/// </summary>
/// <remarks>
/// Every non-nullable date starts at today, at midnight, because a date left at its default shows
/// as 01-Jan-0001 and a SQL date column refuses a time of day. A nullable date stays empty, and
/// CreatedOn and ModifiedOn are left for the store to stamp.
/// </remarks>
public static class NewRecord
{
    /// <summary>A new <typeparamref name="T"/> with its dates at today.</summary>
    public static T Create<T>() where T : new()
    {
        var item = new T();
        StartDatesAtToday(item);
        return item;
    }

    /// <summary>Sets every non-nullable date of <paramref name="item"/> still at its default to
    /// today.</summary>
    public static void StartDatesAtToday(object item)
    {
{{Loop}}
    }
}
""";

    /// <summary>
    /// The store's private copy, for a store class that imports System and System.Reflection.
    /// Indented for a class body.
    /// </summary>
    public static string StoreMember() => $$"""
    /// <summary>Sets every non-nullable date of a new record to today, at midnight, so its form
    /// does not show 01-Jan-0001. CreatedOn and ModifiedOn are left for the stamp on save.</summary>
    private static void StartDatesAtToday(T item)
    {
{{Loop}}
    }
""";
}
