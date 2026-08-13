namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// A home screen of per-entity cards showing row counts, clicking through to that entity's pane.
/// </summary>
/// <remarks>
/// The row count needs nothing added to the view models: <c>IEntityViewModel.Items</c> returns the
/// <c>ObservableCollection&lt;T&gt;</c> behind it, which raises <c>PropertyChanged("Count")</c> on
/// every mutation, and a WPF binding reflects over the runtime type. Navigation is view-local too
/// — swapping two Visibility values in the code-behind — so <c>MainViewModel</c> still exposes only
/// <c>Entities</c>.
/// </remarks>
public sealed class WpfDashboardLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Dashboard);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Dashboard];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml", Xaml(context)),
        new("Views", "MainWindow.xaml.cs", CodeBehind(context.RootNamespace)),
    ];

    private static string Xaml(LayoutContext context)
    {
        var windowIcon = context.IconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{context.IconFileName}\"";

        var windowBrushes = context.Themed
            ? "\n        Background=\"{DynamicResource AppBackgroundBrush}\""
              + "\n        Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        // A Border with only a CornerRadius draws nothing at all, so the fill and the edge are what
        // make the card a card rather than a title and a number floating in the window.
        var cardFill = context.Themed ? "{DynamicResource AppSurfaceBrush}" : "#FFFFFF";
        var cardEdge = context.Themed ? "{DynamicResource AppGridLineBrush}" : "#E5E7EB";
        var headingBrush = context.Themed ? "{DynamicResource AppForegroundBrush}" : "#111827";
        var captionBrush = context.Themed ? "{DynamicResource AppForegroundBrush}" : "#6B7280";

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{context.TitleAttribute}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
{{EntityPaneXaml.Resource(context.Palette)}}
    <DockPanel>
        <Button x:Name="HomeButton"
                DockPanel.Dock="Top"
                Content="Home"
                Width="80"
                Margin="8"
                HorizontalAlignment="Left"
                Visibility="Collapsed"
                Click="OnHome" />
        <Grid>
            <DockPanel x:Name="CardsHost">
                <TextBlock DockPanel.Dock="Top"
                           Text="{{context.TitleAttribute}}"
                           Margin="16,16,16,4"
                           FontSize="20"
                           TextTrimming="CharacterEllipsis"
                           Foreground="{{headingBrush}}" />
                <!-- SelectedIndex is -1 on purpose: the app opens on the cards, and the pane
                     appears only once one is clicked. -->
                <ListBox x:Name="EntityCards"
                         ItemsSource="{Binding Entities}"
                         SelectedIndex="-1"
                         BorderThickness="0"
                         Background="Transparent"
                         Padding="8"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                         SelectionChanged="OnCardSelected">
                    <ListBox.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ListBox.ItemsPanel>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Border Width="168"
                                    Height="104"
                                    Margin="8"
                                    CornerRadius="6"
                                    Padding="12"
                                    Background="{{cardFill}}"
                                    BorderBrush="{{cardEdge}}"
                                    BorderThickness="1">
                                <StackPanel VerticalAlignment="Center">
                                    <TextBlock Text="{Binding Title}"
                                               FontSize="15"
                                               HorizontalAlignment="Center"
                                               TextTrimming="CharacterEllipsis" />
                                    <TextBlock Text="{Binding Items.Count}"
                                               FontSize="30"
                                               HorizontalAlignment="Center" />
                                    <TextBlock Text="rows"
                                               FontSize="11"
                                               HorizontalAlignment="Center"
                                               Foreground="{{captionBrush}}" />
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </DockPanel>
            <ContentControl x:Name="PaneHost"
                            Visibility="Collapsed"
                            Content="{Binding SelectedItem, ElementName=EntityCards}"
                            ContentTemplate="{StaticResource EntityPane}" />
        </Grid>
    </DockPanel>
</Window>
""";
    }

    /// <summary>
    /// Today's code-behind plus the two navigation handlers.
    /// </summary>
    /// <remarks>
    /// Built by inserting into <see cref="MainWindowTemplate.CodeBehind"/> rather than restating
    /// it: the AutoGeneratingColumn handler, the foreign-key combo logic and the display
    /// convention are long, and a copy here would be a second thing to keep in sync.
    /// </remarks>
    private static string CodeBehind(string rootNamespace)
    {
        var source = MainWindowTemplate.CodeBehind(rootNamespace);

        // Raw literal, so the inserted lines carry this file's own line endings — the same ones
        // the template's literal carries. Rebuilt with "\n" they would silently lose a CR. The
        // closing delimiter sits one level left of the members, so they land at class-member depth.
        var handlers = """
                // Navigation is a view concern: the view models know nothing about which screen is
                // showing, so the dashboard swaps two Visibility values here.
                private void OnCardSelected(object sender, SelectionChangedEventArgs e)
                {
                    // Clearing the selection also raises this event; the Home handler has already
                    // put the cards back, so there is nothing to do.
                    if (EntityCards.SelectedItem is null)
                        return;

                    // CardsHost, not the list: it carries the heading above the cards, which would
                    // otherwise stay on screen over the records.
                    CardsHost.Visibility = Visibility.Collapsed;
                    PaneHost.Visibility = Visibility.Visible;
                    HomeButton.Visibility = Visibility.Visible;
                }

                private void OnHome(object sender, RoutedEventArgs e)
                {
                    EntityCards.SelectedIndex = -1;
                    PaneHost.Visibility = Visibility.Collapsed;
                    HomeButton.Visibility = Visibility.Collapsed;
                    CardsHost.Visibility = Visibility.Visible;
                }


            """;

        // Inserted before the AutoGeneratingColumn handler's comment, which is the first member
        // after the constructor.
        const string anchor = "    // Column presentation is a view concern";

        return source.Replace(anchor, handlers + anchor);
    }
}
