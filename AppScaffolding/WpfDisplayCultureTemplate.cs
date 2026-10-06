namespace AppScaffolding;

/// <summary>
/// Source of a WPF app's <c>Views/DisplayCulture.cs</c>: one culture for every date on screen.
/// </summary>
/// <remarks>
/// WPF bindings format in the element's <c>Language</c>, which defaults to en-US whatever the
/// machine's culture, while a DatePicker formats in the machine's culture. So one screen showed
/// <c>8/5/2026 12:00:00 AM</c> in the grid beside <c>05-Aug-26</c> in the picker. The language is
/// overridden from <c>CultureInfo.CurrentCulture</c> in a module initializer, because the
/// metadata is sealed by the first element created -- the login window, before any app code a
/// template could otherwise hook into -- and a grid's date column shows the date alone when it
/// has no time of day, as the picker does.
/// </remarks>
public static class WpfDisplayCultureTemplate
{
    public const string FileName = "DisplayCulture.cs";

    public static string Source(string rootNamespace) => $$"""
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace {{rootNamespace}}.Views;

/// <summary>Formats every binding in the machine's culture, the one a DatePicker already uses.</summary>
public static class DisplayCulture
{
#pragma warning disable CA2255 // An application's own startup hook, which is what this attribute is for.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Apply()
        => FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

    /// <summary>A grid's date cell: the short date alone at midnight, date and time otherwise.</summary>
    /// <remarks>Formatted with CultureInfo.CurrentCulture rather than the binding's culture: that
    /// one comes from the XmlLanguage, which names a culture but drops the user's own overrides
    /// (a custom short date such as dd-MMM-yy), and the DatePicker honours them.</remarks>
    public static IValueConverter DateCell { get; } = new DateCellConverter();

    private sealed class DateCellConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is DateTime date
                ? date.ToString(date.TimeOfDay == TimeSpan.Zero ? "d" : "g", CultureInfo.CurrentCulture)
                : string.Empty;

        /// <summary>A typed cell back to a date; an emptied one is null, which a DateTime? takes
        /// and a DateTime refuses, so the grid marks it rather than storing 0001-01-01.</summary>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = value as string;

            if (string.IsNullOrWhiteSpace(text))
                return Nullable.GetUnderlyingType(targetType) is not null ? null : DependencyProperty.UnsetValue;

            return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date)
                ? date
                : DependencyProperty.UnsetValue;
        }
    }
}
""";
}
