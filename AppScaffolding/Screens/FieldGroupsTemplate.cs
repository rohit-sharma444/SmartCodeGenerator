namespace AppScaffolding.Screens;

/// <summary>
/// Emits <c>Screens/FieldGroups.cs</c>: which titled panel each of a record's fields belongs in.
/// </summary>
/// <remarks>
/// The rules live once, in the generated application, and both desktop targets reflect over them
/// at run time. Putting them in the generator instead would mean emitting them twice — and
/// <see cref="PropertyEditorTemplate"/> already documents an <c>IsDisplayable</c> convention that
/// exists in three emitted files and has to be hand-kept in sync. Nothing in the generator needs
/// to know the groups at build time, so one copy is reachable here.
/// <para>
/// <c>Displayable</c> is nonetheless a FOURTH copy of that convention, and deliberately not the
/// web target's <c>ScreenFields.Editable</c>: that one's <c>IsScalar</c> admits neither an enum
/// nor a Guid, and reusing it would silently drop both from every desktop form.
/// </para>
/// </remarks>
public static class FieldGroupsTemplate
{
    /// <summary>Source of <c>Screens/FieldGroups.cs</c>.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace {{rootNamespace}}.Screens;

/// <summary>One titled panel on an entry screen, and the fields in it.</summary>
public sealed record FieldGroup(string Title, IReadOnlyList<PropertyInfo> Fields);

/// <summary>The record's key fields, held out of the panels because the screen shows them its own
/// way rather than in a titled group.</summary>
public sealed record KeyFields(PropertyInfo? Code, PropertyInfo? Date, PropertyInfo? Status);

/// <summary>Which panel each of a record's fields belongs in.</summary>
public static class FieldGroups
{
    public const string Basic = "Basic Information";
    public const string More = "More Information";
    public const string Address = "Address Information";
    public const string Contact = "Contact Information";
    public const string Other = "Other Information";

    /// <summary>The heading for the long-text tab <see cref="Additional"/> feeds. Not read inside
    /// this file: it exists for the WPF and WinForms layouts (Tasks 8 and 10) that build that
    /// tab, so both targets have one shared name for it rather than each hardcoding the string.</summary>
    public const string AdditionalTitle = "Additional Information";

    /// <summary>The point at which Basic Information stops fitting the panel height and splits.
    /// A layout constant, not a claim about meaning.</summary>
    private const int PanelLimit = 12;

    // Tested in order, first match wins. Address BEFORE contact, so ContactAddress is an address
    // and ContactPerson is not.
    private static readonly string[] AddressWords =
    [
        "address", "street", "city", "province", "district", "postal", "pincode", "pin", "zip",
        "country", "region", "state", "location",
    ];

    private static readonly string[] ContactWords =
    [
        "contact", "phone", "mobile", "telephone", "fax", "email", "website", "url", "designation",
    ];

    private static readonly string[] OtherWords = ["status", "active", "since"];

    private static readonly string[] LongTextWords =
    [
        "remark", "note", "terms", "condition", "description",
    ];

    private static readonly string[] AuditFields = ["CreatedOn", "ModifiedOn"];

    private static readonly string[] CodeSuffixes = ["Code", "No", "Number"];

    /// <summary>
    /// What the header band shows. Excluded from every panel.
    /// </summary>
    /// <remarks>
    /// Without the exclusion the same property is drawn twice on one screen: Status matches the
    /// Other Information bucket, and a transaction's date matches it by type. Two live inputs
    /// bound to one property is a defect rather than a style.
    /// </remarks>
    public static KeyFields Key(Type type)
    {
        var pool = Displayable(type);

        var code = pool.FirstOrDefault(p =>
            p.PropertyType == typeof(string)
            && CodeSuffixes.Any(s => p.Name.EndsWith(s, StringComparison.Ordinal)));

        // The same date the transaction list filters on, and excluded for the same reason: every
        // model carries CreatedOn and ModifiedOn, which are not what a user means by "the date".
        var date = pool.FirstOrDefault(p =>
            IsDate(p) && !AuditFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase));

        var status = pool.FirstOrDefault(p =>
            string.Equals(p.Name, "Status", StringComparison.Ordinal)
            || string.Equals(p.Name, "State", StringComparison.Ordinal));

        return new KeyFields(code, date, status);
    }

    /// <summary>
    /// The fields an entry form shows.
    /// </summary>
    /// <remarks>
    /// Enums and Guids are included, which is where this parts company with the web target's
    /// ScreenFields.Editable. Id is excluded because the store assigns it; navigation properties
    /// and collections because they are not something a user types.
    /// </remarks>
    public static PropertyInfo[] Displayable(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => !string.Equals(p.Name, "Id", StringComparison.Ordinal))
            .Where(p => p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .Where(p => IsDisplayable(p.PropertyType))
            .ToArray();

    /// <summary>The titled panels, in display order. Never contains an empty group.</summary>
    public static IReadOnlyList<FieldGroup> For(Type type)
    {
        var key = Key(type);

        var pool = Displayable(type)
            .Where(p => p != key.Code && p != key.Date && p != key.Status)
            .Where(p => !IsLongText(p))
            .ToList();

        var basic = new List<PropertyInfo>();
        var address = new List<PropertyInfo>();
        var contact = new List<PropertyInfo>();
        var other = new List<PropertyInfo>();

        foreach (var property in pool)
        {
            var name = LabelOf(property).ToLowerInvariant();

            if (AddressWords.Any(name.Contains))
                address.Add(property);
            else if (ContactWords.Any(name.Contains))
                contact.Add(property);
            else if (OtherWords.Any(name.Contains) || IsDate(property))
                other.Add(property);
            else
                basic.Add(property);
        }

        // A heading with nothing under it is noise -- the rule DashboardViewModel.Group follows.
        return new[]
            {
                new FieldGroup(Basic, basic.Take(PanelLimit).ToList()),
                new FieldGroup(More, basic.Skip(PanelLimit).ToList()),
                new FieldGroup(Address, address),
                new FieldGroup(Contact, contact),
                new FieldGroup(Other, other),
            }
            .Where(g => g.Fields.Count > 0)
            .ToList();
    }

    /// <summary>The long-text fields, which the view shows as a tab rather than a panel.</summary>
    public static IReadOnlyList<PropertyInfo> Additional(Type type)
        => Displayable(type).Where(IsLongText).ToList();

    /// <summary>
    /// The label a field is shown under.
    /// </summary>
    /// <remarks>
    /// A foreign key is labelled by the entity it points at: SupplierId reads "Supplier", which is
    /// also what decides its bucket -- ShipToLocationId is an address, and the raw key name is not.
    /// </remarks>
    public static string LabelOf(PropertyInfo property)
        => NavigationFor(property)?.Name ?? property.Name;

    /// <summary>
    /// The navigation property a foreign key points at, or null.
    /// </summary>
    /// <remarks>
    /// The same convention MainWindowTemplate's column rule uses: an int &lt;Nav&gt;Id whose
    /// sibling &lt;Nav&gt; is an entity. A form that dropped these would leave no way to set a
    /// document's supplier at all.
    /// </remarks>
    public static PropertyInfo? NavigationFor(PropertyInfo property)
    {
        if (property.PropertyType != typeof(int) || !property.Name.EndsWith("Id", StringComparison.Ordinal))
            return null;

        var nav = property.DeclaringType?
            .GetProperty(property.Name.Substring(0, property.Name.Length - 2));

        return nav is not null && IsEntity(nav.PropertyType) ? nav : null;
    }

    private static bool IsLongText(PropertyInfo property)
        => property.PropertyType == typeof(string)
           && LongTextWords.Any(word => property.Name.ToLowerInvariant().Contains(word));

    private static bool IsDate(PropertyInfo property)
        => (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
           == typeof(DateTime);

    private static bool IsDisplayable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
               || underlying.IsEnum
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(DateTime)
               || underlying == typeof(Guid);
    }

    /// <summary>A generated model: a class in this assembly that is neither a string nor a
    /// collection.</summary>
    private static bool IsEntity(Type type)
        => type.IsClass
           && type != typeof(string)
           && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
}
""";
}
