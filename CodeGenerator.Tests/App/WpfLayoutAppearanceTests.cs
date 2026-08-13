using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;

namespace CodeGenerator.Tests.App;

/// <summary>
/// What the three non-tabbed WPF layouts must look like, as far as markup can say it.
/// </summary>
/// <remarks>
/// Appearance is judged by eye, but the handful of properties WITHOUT which a control is invisible
/// or unusable can be pinned here — a card with no Background is not a card, and an editor field
/// with no width cap stretches the window. The tabbed layout is deliberately excluded: its markup
/// is the byte-identical output every pre-layout app has.
/// </remarks>
[TestClass]
public class WpfLayoutAppearanceTests
{
    private static string Xaml(AppLayout layout, AppTheme theme = AppTheme.Default)
    {
        var settings = AppCustomization.Default with { Theme = theme };

        return WpfLayouts.Resolve(layout).ViewFiles(
                new LayoutContext("Payroll", "Payroll", settings,
                    ThemePalette.Resolve(settings.Theme, settings.Accent)))
            .Single(f => f.Name == "MainWindow.xaml").Source;
    }

    [TestMethod]
    public void A_dashboard_card_has_a_surface_and_an_edge_so_it_reads_as_a_card()
    {
        // CornerRadius alone draws nothing. Without both of these the card is invisible and the
        // window shows a title and a number floating in empty space.
        foreach (var theme in new[] { AppTheme.Default, AppTheme.Dark })
        {
            var xaml = Xaml(AppLayout.Dashboard, theme);
            var card = xaml.Split("<Border").Last();

            StringAssert.Contains(card, "Background=", "the card needs a fill");
            StringAssert.Contains(card, "BorderBrush=", "the card needs a visible edge");
            StringAssert.Contains(card, "Padding=");
        }
    }

    [TestMethod]
    public void A_dashboard_names_the_app_above_its_cards()
    {
        StringAssert.Contains(Xaml(AppLayout.Dashboard), "Text=\"Payroll\"");
    }

    [TestMethod]
    public void A_dashboard_card_says_what_its_number_counts()
    {
        // A bare number under a class name is ambiguous.
        StringAssert.Contains(Xaml(AppLayout.Dashboard), "Text=\"rows\"");
    }

    [TestMethod]
    public void A_sidebar_list_sits_in_a_surface_divided_from_the_pane()
    {
        var xaml = Xaml(AppLayout.Sidebar);

        // A white column against a white pane reads as an accident, not as navigation.
        StringAssert.Contains(xaml, "BorderThickness=\"0,0,1,0\"");
        StringAssert.Contains(xaml, "Text=\"Payroll\"");
    }

    [TestMethod]
    public void The_shared_pane_puts_the_status_beside_the_buttons_rather_than_in_a_strip_of_its_own()
    {
        // Status is only assigned by Add, Delete and Save, so a StatusBar of its own is a blank
        // strip for the whole first session.
        foreach (var layout in new[] { AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
        {
            Assert.DoesNotContain("<StatusBar", Xaml(layout));
            StringAssert.Contains(Xaml(layout), "Text=\"{Binding Status}\"");
        }
    }

    [TestMethod]
    public void The_tabbed_layout_keeps_its_status_bar_because_it_owes_byte_identity()
    {
        // The pairing with the test above is the point: the shared pane changed and the tabbed
        // window did not.
        StringAssert.Contains(Xaml(AppLayout.Tabs), "<StatusBar DockPanel.Dock=\"Bottom\">");
    }

    [TestMethod]
    public void The_shared_pane_gives_its_rows_room_to_read()
    {
        foreach (var layout in new[] { AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
            StringAssert.Contains(Xaml(layout), "RowHeight=");
    }

    [TestMethod]
    public void The_master_detail_editor_is_separated_from_the_grid_above_it()
    {
        StringAssert.Contains(Xaml(AppLayout.MasterDetail), "BorderThickness=\"0,1,0,0\"");
    }

    [TestMethod]
    public void No_layout_names_a_theme_brush_when_no_theme_was_asked_for()
    {
        // The literal colours the plain look uses must not leak a DynamicResource, or an unthemed
        // app would bind to brushes its App.xaml never merges.
        foreach (var layout in Enum.GetValues<AppLayout>())
            Assert.DoesNotContain("DynamicResource", Xaml(layout));
    }
}
