namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// The per-entity pane — grid, button row, status bar — as a keyed <c>DataTemplate</c> in
/// <c>Window.Resources</c>.
/// </summary>
/// <remarks>
/// Shared by the sidebar, master-detail and dashboard layouts, which reference it as
/// <c>{StaticResource EntityPane}</c>. A keyed resource rather than markup inlined into each
/// window means one fixed nesting depth, so no layout has to re-indent the fragment.
/// <para>
/// The tabbed layout does NOT use this and must not be refactored onto it: its markup is the
/// byte-identical output every pre-layout app has, and recomposing it from fragments is precisely
/// how a CRLF regression enters invisibly. The duplication is deliberate — change both sites.
/// </para>
/// </remarks>
public static class EntityPaneXaml
{
    /// <summary>The resource key layouts bind to.</summary>
    public const string Key = "EntityPane";

    /// <summary>
    /// The <c>Window.Resources</c> block declaring the pane. With <paramref name="withEditor"/> the
    /// pane carries a <c>PropertyEditor</c> for the selected row between the grid and the buttons.
    /// </summary>
    public static string Resource(ThemePalette? palette, bool withEditor = false)
    {
        // Indented to align under the DataGrid's first attribute below, which sits ten columns
        // past its element's own indent.
        var gridBrushes = palette is null
            ? string.Empty
            : """

                                      Background="{DynamicResource AppSurfaceBrush}"
                                      Foreground="{DynamicResource AppForegroundBrush}"
                                      HorizontalGridLinesBrush="{DynamicResource AppGridLineBrush}"
                                      VerticalGridLinesBrush="{DynamicResource AppGridLineBrush}"
            """;

        var editor = withEditor
            ? """

                            <local:PropertyEditor DockPanel.Dock="Bottom"
                                                  Margin="8"
                                                  Record="{Binding Selected}" />
            """
            : string.Empty;

        return $$"""
    <Window.Resources>
        <DataTemplate x:Key="{{Key}}">
            <DockPanel>
                <StatusBar DockPanel.Dock="Bottom">
                    <TextBlock Text="{Binding Status}" />
                </StatusBar>
                <StackPanel DockPanel.Dock="Bottom"
                            Orientation="Horizontal"
                            HorizontalAlignment="Right"
                            Margin="8">
                    <Button Content="Add" Command="{Binding AddCommand}"
                            Width="80" Margin="4,0" />
                    <Button Content="Delete" Command="{Binding DeleteCommand}"
                            Width="80" Margin="4,0" />
                    <Button Content="Save" Command="{Binding SaveCommand}"
                            Width="80" Margin="4,0" />
                </StackPanel>{{editor}}
                <DataGrid ItemsSource="{Binding Items}"{{gridBrushes}}
                          SelectedItem="{Binding Selected, Mode=TwoWay}"
                          AutoGenerateColumns="True"
                          CanUserAddRows="False"
                          AutoGeneratingColumn="OnAutoGeneratingColumn" />
            </DockPanel>
        </DataTemplate>
    </Window.Resources>
""";
    }
}
