namespace AppScaffolding.Layouts.WinForms;

/// <summary>
/// A home screen of per-entity cards showing row counts, clicking through to that entity's pane.
/// </summary>
/// <remarks>
/// The count is refreshed from the collection's own <c>CollectionChanged</c> rather than bound:
/// WinForms has no binding path to a count, which is the one place this layout costs more than its
/// WPF twin, where <c>{Binding Items.Count}</c> does the same work.
/// </remarks>
public sealed class WinFormsDashboardLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Dashboard);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Dashboard];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainForm.cs", Source(context)),
    ];

    private static string Source(LayoutContext context)
    {
        var authUsing = context.Customization.Login
            ? $"using {context.RootNamespace}.Auth;\r\n"
            : string.Empty;

        var formColours = context.Palette is null
            ? string.Empty
            : $$"""

        BackColor = ColorTranslator.FromHtml("{{context.Palette.Background}}");
        ForeColor = ColorTranslator.FromHtml("{{context.Palette.Foreground}}");
""";

        var formIcon = context.IconFileName is not null
                       && context.IconFileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? $$"""

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "{{AppCustomization.AssetsFolder}}", "{{context.IconFileName}}");
        if (System.IO.File.Exists(iconPath))
            Icon = new Icon(iconPath);
"""
            : string.Empty;

        return $$"""
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
{{authUsing}}using {{context.RootNamespace}}.ViewModels;

namespace {{context.RootNamespace}}.Views;

/// <summary>A card per entity showing its row count, opening that entity's records when clicked.</summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _model = new();

    // Both fills occupy the same area and take turns being visible; the toolbar appears only once
    // a pane is open, so the home screen is nothing but cards.
    private readonly Panel _host = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly FlowLayoutPanel _cards = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
    private readonly Panel _toolbar = new() { Dock = DockStyle.Top, Height = 40, Visible = false };

    public MainForm()
    {
        Text = "{{context.TitleLiteral}}";
        Width = 900;
        Height = 600;
{{formColours}}{{formIcon}}
        // Held in a panel rather than docked itself: a Button docked to an edge stretches across
        // the whole width.
        var back = new Button { Text = "Back", Width = 80, Left = 8, Top = 8 };
        back.Click += (_, _) => ShowCards();
        _toolbar.Controls.Add(back);

        foreach (var entity in _model.Entities)
            _cards.Controls.Add(BuildCard(entity, _model));

        // The filling controls are added before the docked toolbar: WinForms resolves docking in
        // reverse z-order, so the last control added claims its edge first and the first one added
        // keeps whatever space is left.
        Controls.Add(_host);
        Controls.Add(_cards);
        Controls.Add(_toolbar);
    }

    // A button rather than a styled panel: it is already focusable, keyboard-activatable and
    // themed by the OS, which a Panel with a Click handler is not.
    private Button BuildCard(IEntityViewModel entity, MainViewModel model)
    {
        var card = new Button
        {
            Width = 160,
            Height = 90,
            Margin = new Padding(8),
            TextAlign = ContentAlignment.MiddleCenter,
        };

        void Refresh() =>
            card.Text = entity.Title + Environment.NewLine + entity.Items.Cast<object>().Count();

        Refresh();

        // No binding path to a count in WinForms, so the card listens to the collection itself.
        if (entity.Items is INotifyCollectionChanged observable)
            observable.CollectionChanged += (_, _) => Refresh();

        card.Click += (_, _) => ShowPane(entity, model);

        return card;
    }

    private void ShowPane(IEntityViewModel entity, MainViewModel model)
    {
        _host.Controls.Add(BuildPane(entity, model));

        _cards.Visible = false;
        _host.Visible = true;
        _toolbar.Visible = true;
    }

    private void ShowCards()
    {
        // One pane at a time, and the outgoing one is disposed rather than hidden so its event
        // handlers stop firing. Snapshotted first: disposing a control removes it from this
        // collection, so disposing inside a foreach over it would skip whatever follows.
        var open = _host.Controls.Cast<Control>().ToArray();
        _host.Controls.Clear();

        foreach (var pane in open)
            pane.Dispose();

        _host.Visible = false;
        _toolbar.Visible = false;
        _cards.Visible = true;
    }

{{WinFormsPaneCode.BuildPaneMethod(context.Palette, login: context.Customization.Login)}}
}
""";
    }
}
