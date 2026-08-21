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
    public static string Resource(
        ThemePalette? palette, bool withEditor = false, bool login = false)
    {
        // Indented to align under the DataGrid's first attribute below, which sits ten columns
        // past its element's own indent.
        var gridBrushes = palette is null
            ? """

                                      AlternatingRowBackground="#FAFAFA"
            """
            : """

                                      Background="{DynamicResource AppSurfaceBrush}"
                                      Foreground="{DynamicResource AppForegroundBrush}"
                                      HorizontalGridLinesBrush="{DynamicResource AppGridLineBrush}"
                                      VerticalGridLinesBrush="{DynamicResource AppGridLineBrush}"
                                      AlternatingRowBackground="{DynamicResource AppBackgroundBrush}"
            """;

        var statusBrush = palette is null
            ? "#6B7280"
            : "{DynamicResource AppForegroundBrush}";

        var rule = palette is null ? "#E5E7EB" : "{DynamicResource AppGridLineBrush}";

        var editor = withEditor
            ? $$"""

                            <Border DockPanel.Dock="Bottom"
                                    Margin="0,12,0,0"
                                    Padding="0,12,0,0"
                                    BorderThickness="0,1,0,0"
                                    BorderBrush="{{rule}}">
                                <local:PropertyEditor Record="{Binding Selected}" />
                            </Border>
            """
            : string.Empty;

        // Docked above the records rather than beside Add/Delete/Save: signing out is not an
        // action on a record. Only one pane is on screen at a time in every layout, so this
        // renders once rather than per tab.
        //
        // The name comes through MainViewModel, reached with a RelativeSource walk to the
        // window, rather than an x:Static -- an x:Static would need a new xmlns on all four
        // window roots. That binding is also why this literal is $$$: it ends in }}.
        var signOut = login
            ? $$$"""

                <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                    <TextBlock VerticalAlignment="Center"
                               Foreground="{{{statusBrush}}}"
                               Text="{Binding DataContext.SignedInUser, RelativeSource={RelativeSource AncestorType=Window}}" />
                    <Button Content="Sign out"
                            Click="OnSignOut"
                            HorizontalAlignment="Right"
                            Width="88" Height="26" />
                </Grid>
            """
            : string.Empty;

        return $$$"""
    <Window.Resources>
        <DataTemplate x:Key="{{{Key}}}">
            <DockPanel Margin="12">{{{signOut}}}
                <!-- Status shares the button row rather than taking a strip of its own: it is
                     assigned only by Add, Delete and Save, so a bar would be an empty band for the
                     whole first session. -->
                <Grid DockPanel.Dock="Bottom" Margin="0,12,0,0">
                    <TextBlock Text="{Binding Status}"
                               VerticalAlignment="Center"
                               TextTrimming="CharacterEllipsis"
                               Foreground="{{{statusBrush}}}" />
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                        <Button Content="Add" Command="{Binding AddCommand}"
                                Width="88" Height="28" Margin="8,0,0,0" />
                        <Button Content="Delete" Command="{Binding DeleteCommand}"
                                Width="88" Height="28" Margin="8,0,0,0" />
                        <Button Content="Save" Command="{Binding SaveCommand}"
                                Width="88" Height="28" Margin="8,0,0,0" />
                    </StackPanel>
                </Grid>{{{editor}}}
                <DataGrid ItemsSource="{Binding Items}"{{{gridBrushes}}}
                          SelectedItem="{Binding Selected, Mode=TwoWay}"
                          AutoGenerateColumns="True"
                          CanUserAddRows="False"
                          RowHeight="26"
                          GridLinesVisibility="Horizontal"
                          AlternationCount="2"
                          AutoGeneratingColumn="OnAutoGeneratingColumn" />
            </DockPanel>
        </DataTemplate>
    </Window.Resources>
""";
    }
}
