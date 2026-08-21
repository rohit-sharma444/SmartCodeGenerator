namespace AppScaffolding.Layouts.WinForms;

/// <summary>
/// Tab chrome, with each pane split into a grid above and a PropertyGrid for the selected row below.
/// </summary>
/// <remarks>
/// Cheaper than the WPF side of the same layout: WinForms ships a PropertyGrid, so
/// <see cref="WinFormsPaneCode.BuildPaneMethod"/> only has to place one, where WPF needs a whole
/// emitted <c>PropertyEditor</c> control to reflect over the record itself.
/// </remarks>
public sealed class WinFormsMasterDetailLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.MasterDetail);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.MasterDetail];

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

/// <summary>One tab per entity, each showing its records above an edit form for the current row.</summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _model = new();

    public MainForm()
    {
        Text = "{{context.TitleLiteral}}";
        Width = 900;
        Height = 600;
{{formColours}}{{formIcon}}
        var tabs = new TabControl { Dock = DockStyle.Fill };

        foreach (var entity in _model.Entities)
        {
            // The pane is built separately and hosted, rather than being a TabPage itself: the
            // sidebar and dashboard layouts host the same Panel in containers of their own.
            var page = new TabPage(entity.Title);
            page.Controls.Add(BuildPane(entity, _model));
            tabs.TabPages.Add(page);
        }

        Controls.Add(tabs);
    }

{{WinFormsPaneCode.BuildPaneMethod(context.Palette, withPropertyGrid: true, login: context.Customization.Login)}}
}
""";
    }
}
