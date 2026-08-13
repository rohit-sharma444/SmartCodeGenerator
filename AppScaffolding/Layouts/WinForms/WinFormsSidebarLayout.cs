namespace AppScaffolding.Layouts.WinForms;

/// <summary>A navigation list of entities docked left, the selected entity's pane filling the rest.</summary>
public sealed class WinFormsSidebarLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Sidebar);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Sidebar];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainForm.cs", Source(context)),
    ];

    private static string Source(LayoutContext context)
    {
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
using {{context.RootNamespace}}.ViewModels;

namespace {{context.RootNamespace}}.Views;

/// <summary>A list of entities beside the selected one's records.</summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _model = new();
    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "{{context.TitleLiteral}}";
        Width = 900;
        Height = 600;
{{formColours}}{{formIcon}}
        var list = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 180,
            DisplayMember = "Title",
            IntegralHeight = false,
        };

        foreach (var entity in _model.Entities)
            list.Items.Add(entity);

        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is not IEntityViewModel entity)
                return;

            // One pane at a time: the old one is disposed rather than hidden, so its event
            // handlers stop firing when the selection moves on. Snapshotted first, because
            // disposing a control removes it from this collection.
            var open = _host.Controls.Cast<Control>().ToArray();
            _host.Controls.Clear();

            foreach (var pane in open)
                pane.Dispose();

            _host.Controls.Add(BuildPane(entity, _model));
        };

        // Added before the list so docking resolves the fill against the remaining space.
        Controls.Add(_host);
        Controls.Add(list);

        // Opens on the first entity rather than an empty panel, as every other layout does.
        if (list.Items.Count > 0)
            list.SelectedIndex = 0;
    }

{{WinFormsPaneCode.BuildPaneMethod(context.Palette)}}
}
""";
    }
}
