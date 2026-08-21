namespace AppScaffolding.Screens;

/// <summary>The screen types a generated application declares for itself.</summary>
/// <remarks>
/// The generator has its own <see cref="EntityRole"/>, used to decide what to emit. This emits the
/// application's copy, in the application's own namespace: a generated project references nothing
/// of this generator, so a shared enum is not available to it.
/// </remarks>
public static class ScreenTemplates
{
    /// <summary>
    /// Source of <c>Views/ScreenTemplateSelector.cs</c> — the WPF shell's content-area selector.
    /// </summary>
    /// <remarks>
    /// A selector rather than four implicit <c>DataTemplate</c>s keyed by <c>DataType</c>, which
    /// would have been tidier. WPF resolves an implicit template by the value's EXACT type and
    /// cannot name an open generic, and every screen is <c>MasterScreenViewModel&lt;T&gt;</c> or
    /// similar — so <c>DataType</c> can address none of them. Testing the non-generic
    /// <c>IEntityViewModel</c> and <c>IRecordEditor</c> faces is what makes the decision possible
    /// at all.
    /// </remarks>
    public static string TemplateSelector(string rootNamespace) => $$"""
using System.Windows;
using System.Windows.Controls;
using {{rootNamespace}}.Screens;
using {{rootNamespace}}.ViewModels;

namespace {{rootNamespace}}.Views;

/// <summary>Chooses the content-area template for whatever screen the shell is showing.</summary>
public sealed class ScreenTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Dashboard { get; set; }
    public DataTemplate? Master { get; set; }
    public DataTemplate? Transaction { get; set; }
    public DataTemplate? Editor { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item switch
        {
            DashboardViewModel => Dashboard,

            // The editor first: it is not an IEntityViewModel, but testing it first keeps the
            // order of these arms from mattering if that ever changes.
            IRecordEditor => Editor,

            IEntityViewModel { Role: EntityRole.Transaction } => Transaction,
            IEntityViewModel => Master,

            _ => base.SelectTemplate(item, container),
        };
}
""";

    /// <summary>Source of <c>Screens/EntityRole.cs</c> in the generated app.</summary>
    public static string RoleEnum(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Screens;

/// <summary>What kind of screen an entity gets.</summary>
public enum EntityRole
{
    /// <summary>Reference data — a list and an editor.</summary>
    Master,

    /// <summary>A record that references reference data — a filtered list, totals, and an editor
    /// that may hold lines.</summary>
    Transaction,

    /// <summary>A line belonging to a transaction. Edited inside its parent, never on a screen of
    /// its own.</summary>
    DocumentLine
}
""";
}
