using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;
using AppScaffolding.Screens;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The business shell in Windows Forms: a grouped tree rail, a header, and one content panel.
/// </summary>
/// <remarks>
/// The Windows Forms half of <see cref="WpfBusinessLayoutTests"/>. It shipped without these, which
/// is how 400 lines of emitted form came to have no assertion over them at all.
/// </remarks>
[TestClass]
public class WinFormsBusinessLayoutTests
{
    private const string Root = "Payroll.WinForms";

    private static LayoutContext Context(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return new LayoutContext(
            Root, "Payroll", settings, ThemePalette.Resolve(settings.Theme, settings.Accent));
    }

    private static string Source(AppCustomization? customization = null)
        => new WinFormsBusinessLayout().ViewFiles(Context(customization))
            .Single(f => f.Name == "MainForm.cs").Source;

    /// <summary>
    /// One emitted method, from its signature up to the next member named.
    /// </summary>
    /// <remarks>
    /// The emitted form is one class of three screens that build the same kinds of control, so an
    /// unscoped needle is routinely satisfied by the identical line in a DIFFERENT screen -- which
    /// is how the action-bar test came to pass with half the master's bar deleted. Anything that
    /// claims a screen does something is asserted inside that screen's own body.
    /// </remarks>
    private static string Body(string source, string signature, string nextSignature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"the emitted form has no '{signature}'");

        var end = source.IndexOf(nextSignature, start, StringComparison.Ordinal);
        Assert.IsGreaterThan(start, end, $"the emitted form has no '{nextSignature}' after '{signature}'");

        return source[start..end];
    }

    /// <summary>
    /// Asserts that a switch case's body is the call it is supposed to make.
    /// </summary>
    /// <remarks>
    /// Adjacency, not presence, and it is the whole point here. RepaintRecord, RepaintTotals and
    /// ShowStatus are each called TWICE in their method -- once to paint the screen, once from the
    /// switch that keeps it painted -- so a bare Contains on the call goes on passing after the
    /// case has been cut back to a bare break and the screen has gone deaf, which is exactly the
    /// defect these tests exist for. Only refreshList, called once, was ever really pinned.
    /// <para>
    /// Bounded by the case's own break rather than joining the two lines with a newline: a raw
    /// string literal carries the line endings of the file it is written in, both files here are
    /// CRLF, and a needle spelling "\n" between its lines would match nothing. No needle below
    /// contains a line ending at all, so there is no line-ending assumption left to get wrong.
    /// </para>
    /// <para>
    /// The slice stops at the next case label when one comes first, because bounding on break;
    /// alone still failed open. Two labels sharing one body is legal C#, and the shared body would
    /// have read as this label's own: green while an Items change repainted the whole record too.
    /// A label that falls through owns no body, so the slice is empty and the assertion goes red.
    /// </para>
    /// </remarks>
    private static void CaseRuns(string source, string label, string call)
    {
        var at = source.IndexOf(label, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, at, $"the emitted form has no '{label}'");

        var rest = source[(at + label.Length)..];
        var end = rest.IndexOf("break;", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"'{label}' never breaks");

        var next = rest.IndexOf("case \"", StringComparison.Ordinal);
        if (next >= 0 && next < end)
        {
            end = next;
        }

        StringAssert.Contains(rest[..end], call, $"'{label}' does not run '{call}'");
    }

    /// <summary>Asserts a call is made twice: once to paint the screen, once to keep it
    /// painted.</summary>
    /// <remarks>
    /// Counting is not placing. Two occurrences BOTH inside the case body satisfy a count while the
    /// screen opens blank and stays blank until the first event arrives, so the first call must
    /// also come before the case label -- ahead of the switch, on the path that builds the screen.
    /// </remarks>
    private static void RunsTwice(string source, string label, string call)
    {
        var first = source.IndexOf(call, StringComparison.Ordinal);
        Assert.AreNotEqual(
            first,
            source.LastIndexOf(call, StringComparison.Ordinal),
            $"'{call}' is called once, so either the first paint or the live update is missing");

        var at = source.IndexOf(label, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, at, $"the emitted form has no '{label}'");
        Assert.IsLessThan(
            at,
            first,
            $"'{call}' is not called before '{label}', so the screen paints nothing until an event arrives");
    }

    private static string EntryBody()
        => Body(
            Source(),
            "private Control BuildEntry(object screen)",
            "private (Control Control, Action Rebind) BuildRecordList(");

    private static string EditorBody()
        => Body(Source(), "private Control BuildEditor(IRecordEditor editor)", "private Control BuildTotals(");

    [TestMethod]
    public void The_registry_resolves_the_business_key()
    {
        Assert.AreEqual("business", WinFormsLayouts.Resolve(AppLayout.Business).Key);
        Assert.AreEqual(
            AppLayoutCatalog.Descriptions[AppLayout.Business],
            WinFormsLayouts.Resolve(AppLayout.Business).Description);
    }

    /// <summary>The registries fall back to tabs for an unimplemented layout, which would make a
    /// missing layout look like a working app rather than a failure.</summary>
    [TestMethod]
    public void Every_layout_value_has_a_windows_forms_layout()
    {
        foreach (var layout in Enum.GetValues<AppLayout>())
        {
            Assert.AreEqual(
                AppLayoutCatalog.KeyFor(layout), WinFormsLayouts.Resolve(layout).Key, $"{layout} falls back");
        }
    }

    [TestMethod]
    public void The_rail_is_a_tree_with_a_node_per_group_and_a_leaf_per_entity()
    {
        var source = Source();

        StringAssert.Contains(source, "TreeView _rail = new()");
        StringAssert.Contains(source, "foreach (var group in _shell.Groups)");
        StringAssert.Contains(source, "new TreeNode(group.Title)");
        StringAssert.Contains(source, "node.Nodes.Add(new TreeNode(entity.Title)");

        // Home is a node too, or the dashboard is reachable only at startup.
        StringAssert.Contains(source, "new TreeNode(\"Home\")");
    }

    /// <summary>A group heading is not a screen. Tagging one would navigate the content area to a
    /// NavGroup and render its type name.</summary>
    [TestMethod]
    public void A_group_heading_navigates_nowhere()
    {
        StringAssert.Contains(Source(), "if (e.Node?.Tag is { } screen)");
        StringAssert.Contains(Source(), "_shell.TryShow(screen);");
    }

    /// <summary>
    /// The form follows the shell rather than telling it what to show.
    /// </summary>
    /// <remarks>
    /// An editor request comes from a screen and is routed by the shell, so the content area has
    /// to notice a change it did not cause. Pushing from each click instead left New doing nothing
    /// once the routing moved into the shell — one signal, and both targets use the same one.
    /// </remarks>
    [TestMethod]
    public void The_content_area_follows_the_shell()
    {
        var source = Source();

        StringAssert.Contains(source, "_shell.PropertyChanged +=");
        StringAssert.Contains(source, "e.PropertyName == nameof(ShellViewModel.Current)");
    }

    [TestMethod]
    public void The_header_names_the_signed_in_user_and_offers_sign_out_and_back()
    {
        var source = Source();

        StringAssert.Contains(source, "_shell.SignedInUser");
        StringAssert.Contains(source, "Text = \"Sign out\"");
        StringAssert.Contains(source, "_shell.SignOutCommand.Execute(null)");
        StringAssert.Contains(source, "Text = \"Back\"");
        StringAssert.Contains(source, "_shell.Back()");
    }

    /// <summary>One content panel, cleared and refilled — not a panel per screen left hidden
    /// behind the others.</summary>
    [TestMethod]
    public void One_content_panel_holds_whichever_screen_is_showing()
    {
        var source = Source();

        StringAssert.Contains(source, "_content.Controls.Clear();");
        StringAssert.Contains(source, "_content.Controls.Add(BuildScreen(screen));");
        StringAssert.Contains(source, "Show(_shell.Current)");
    }

    /// <summary>The shell only knows it has to ask; what asking looks like is the view's, and on
    /// this target it is a message box.</summary>
    [TestMethod]
    public void The_discard_prompt_is_a_message_box_wired_into_the_shell()
    {
        var source = Source();

        StringAssert.Contains(source, "_shell.ConfirmDiscard = () => MessageBox.Show(");
        StringAssert.Contains(source, "\"Discard your changes?\"");
        StringAssert.Contains(source, "== DialogResult.Yes");
    }

    /// <summary>Three screens, chosen by what the value IS rather than by its type name: every
    /// screen here is a closed generic this file cannot name.</summary>
    [TestMethod]
    public void The_screens_are_chosen_by_the_faces_they_implement()
    {
        var source = Source();

        StringAssert.Contains(source, "if (screen is DashboardViewModel dashboard)");
        StringAssert.Contains(source, "if (screen is IRecordEditor editor)");
        StringAssert.Contains(source, "return BuildList(screen);");
    }

    /// <summary>
    /// A transaction's LIST stays on BuildList. This is the whole reason the three-way dispatch
    /// exists: rewriting BuildList into the entry screen would convert transaction lists too and
    /// leave no route to a purchase order's lines.
    /// </summary>
    [TestMethod]
    public void A_transaction_list_is_routed_to_BuildList()
    {
        var source = Source();

        var transaction = source.IndexOf(
            "if (screen is IEntityViewModel { Role: EntityRole.Transaction })", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, transaction, "no Role.Transaction test in the dispatch");

        var recordEditor = source.IndexOf("if (screen is IRecordEditor", transaction, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, recordEditor, "no IRecordEditor branch after the transaction test");

        // The transaction branch's own return, before the next branch begins.
        StringAssert.Contains(
            source.Substring(transaction, recordEditor - transaction), "return BuildList(screen);");
    }

    /// <summary>A transaction's stand-alone entry screen -- a RecordEditorViewModel -- is routed
    /// to BuildEditor.</summary>
    [TestMethod]
    public void A_transaction_editor_is_routed_to_BuildEditor()
    {
        var source = Source();

        var branch = source.IndexOf("if (screen is IRecordEditor editor)", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, branch, "no IRecordEditor test in the dispatch");

        StringAssert.Contains(
            source.Substring(branch, Math.Min(160, source.Length - branch)), "return BuildEditor(editor);");
    }

    /// <summary>
    /// A master's own screen implements neither Role.Transaction nor IRecordEditor, so it falls
    /// through to the new BuildEntry -- the opposite of routing the entry screen off
    /// Role.Transaction, which would send a TRANSACTION's list to the entry screen instead of a
    /// master's.
    /// </summary>
    [TestMethod]
    public void A_master_screen_falls_through_to_BuildEntry()
    {
        var source = Source();

        var branch = source.IndexOf("if (screen is IRecordEditor editor)", StringComparison.Ordinal);
        var editorReturn = source.IndexOf("return BuildEditor(editor);", branch, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, editorReturn, "no BuildEditor return after the IRecordEditor test");

        var tail = source.Substring(editorReturn);
        StringAssert.Contains(tail, "return BuildEntry(screen);");
        Assert.DoesNotContain("return BuildList(screen);", tail);
    }

    /// <summary>The list finds; the editor changes. A typeable grid would put the application back
    /// to editing two different ways.</summary>
    [TestMethod]
    public void The_list_is_read_only_and_opens_the_editor()
    {
        var source = Source();

        StringAssert.Contains(source, "ReadOnly = true,");
        StringAssert.Contains(source, "AllowUserToAddRows = false,");
        StringAssert.Contains(source, "CommandButton(screen, \"OpenCommand\", \"Open\")");
        StringAssert.Contains(source, "grid.CellDoubleClick");
    }

    /// <summary>
    /// The lines of a transaction, in a grid inside the editor.
    /// </summary>
    /// <remarks>
    /// Bound to the set's own collection rather than a copy: this grid IS the editor for a line, so
    /// a cell edit has to reach the object the editor will save.
    /// </remarks>
    [TestMethod]
    public void The_editor_shows_the_lines_of_a_transaction()
    {
        var source = Source();

        StringAssert.Contains(source, "foreach (var lines in editor.Lines)");
        StringAssert.Contains(source, "DataSource = lines.Items,");
        StringAssert.Contains(source, "lines.Add()");
        StringAssert.Contains(source, "lines.Remove(lines.Selected)");

        // The parent's id is stamped on save, so it is not a column anyone types into.
        StringAssert.Contains(source, "grid.Columns[lines.ForeignKey]");
    }

    /// <summary>A master's own editor has no lines, and a tab with an empty grid under it would be
    /// worse than no tab at all -- so the lines tab is added only when there are some.</summary>
    [TestMethod]
    public void An_editor_with_no_lines_adds_no_lines_tab()
        => StringAssert.Contains(Source(), "if (editor.Lines.Count > 0)");

    /// <summary>
    /// The header, the grouped form, the tab strip -- and the three columns the groups are packed
    /// into.
    /// </summary>
    /// <remarks>
    /// The column count and the cell each stack is added at are asserted together, inside
    /// BuildEntryForm's own body: a ColumnCount of 1 with every stack added at (0, 0) still calls
    /// FieldGroups.Columns, so naming the call alone leaves the three-column packing -- which is
    /// what Tasks 1-3 exist for -- free to collapse into one column with the suite green.
    /// </remarks>
    [TestMethod]
    public void The_form_builds_a_header_a_grouped_form_and_a_tab_strip()
    {
        var source = Source();

        StringAssert.Contains(source, "private Control BuildEntryHeader(");
        StringAssert.Contains(source, "private Control BuildEntryForm(");
        StringAssert.Contains(source, "new TabControl");

        var form = Body(source, "private Control BuildEntryForm(object record)", "private Control BuildPanel(");

        StringAssert.Contains(form, "ColumnCount = 3,");
        StringAssert.Contains(form, "var columns = FieldGroups.Columns(record.GetType());");
        StringAssert.Contains(form, "host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns.Count));");
        StringAssert.Contains(form, "host.Controls.Add(stack, i, 0);");
    }

    /// <summary>A GroupBox caption cannot take the accent colour, so the panel is a bordered Panel
    /// with a Label of its own.</summary>
    [TestMethod]
    public void A_panel_is_not_a_group_box()
        => Assert.DoesNotContain("new GroupBox", Source());

    /// <summary>
    /// A required field is marked with an asterisk, and a foreign key is a picker over the entity
    /// it points at.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted, not just the two calls: <c>Text = string.Empty</c> keeps the
    /// IsRequired call and paints no asterisk at all, and a lookup that never reads LookupRows is
    /// an empty drop-down -- neither of which naming <c>RequiredFields.IsRequired(</c> and
    /// <c>FieldGroups.NavigationFor(</c> can tell apart from the working screen.
    /// </remarks>
    [TestMethod]
    public void The_form_marks_required_fields_and_renders_lookups()
    {
        var source = Source();

        StringAssert.Contains(source, "var required = RequiredFields.IsRequired(record.GetType(), field.Name);");
        StringAssert.Contains(source, "Text = required ? \"*\" : string.Empty,");
        StringAssert.Contains(source, "ForeColor = Color.Red,");

        StringAssert.Contains(source, "if (FieldGroups.NavigationFor(field) is { } nav)");
        StringAssert.Contains(source, "DataSource = LookupRows(nav.PropertyType),");
    }

    /// <summary>
    /// Each entry screen's bar, as the tuple list it is actually built from.
    /// </summary>
    /// <remarks>
    /// Naming the six commands one at a time proved almost nothing: NewCommand and DeleteCommand
    /// also appear in BuildList and SaveCommand/CancelCommand in BuildEditor, so only EditCommand
    /// and ClearCommand were pinned to the master's bar at all -- half of it could be deleted with
    /// the suite green. The tuple lines are asserted whole instead, and the transaction editor's
    /// two-button bar with them.
    /// </remarks>
    [TestMethod]
    public void The_action_bar_offers_only_commands_that_exist()
    {
        var source = Source();

        StringAssert.Contains(
            source, "(\"NewCommand\", \"New\"), (\"EditCommand\", \"Edit\"), (\"DeleteCommand\", \"Delete\"),");
        StringAssert.Contains(
            source, "(\"SaveCommand\", \"Save\"), (\"ClearCommand\", \"Clear\"), (\"CancelCommand\", \"Cancel\"));");

        StringAssert.Contains(
            source,
            "var bar = BuildEntryActionBar(editor, (\"SaveCommand\", \"Save\"), (\"CancelCommand\", \"Cancel\"));");

        foreach (var absent in new[] { "\"Print\"", "\"Export\"", "\"Import Items\"" })
            Assert.DoesNotContain(absent, source);
    }

    /// <summary>
    /// The band beside the lines is built from the editor's totals, and is put on the screen.
    /// </summary>
    /// <remarks>
    /// The word "Totals" alone was satisfied by the METHOD NAME BuildTotals and by the prose
    /// <c>IRecordEditor.Totals</c> in that method's own remarks, so reducing BuildTotals to
    /// <c>return new Panel();</c> -- or dropping the call to it altogether -- left the suite green.
    /// The loop and the two lines that put its result on the screen are asserted instead.
    /// </remarks>
    [TestMethod]
    public void The_editor_shows_the_line_totals_and_can_clear_a_set()
    {
        var source = Source();

        StringAssert.Contains(source, "foreach (var total in editor.Totals)");
        StringAssert.Contains(source, "Text = total.Value.ToString(\"N2\"),");
        StringAssert.Contains(source, "totals = BuildTotals(editor);");
        StringAssert.Contains(source, "linesHost.Controls.Add(totals, 1, 0);");

        StringAssert.Contains(source, "var clear = new Button { Text = \"Clear All\", Width = 88 };");
        StringAssert.Contains(source, "clear.Click += (_, _) => lines.Clear();");
    }

    /// <summary>
    /// The totals band recomputes: a line added, removed, cleared or typed into repaints it.
    /// </summary>
    /// <remarks>
    /// Totals is COMPUTED from the lines, so there is nothing on it a Label can bind to -- it was
    /// painted once from a snapshot and never listened for anything, which left the most visible
    /// numbers on a document silently wrong while R42's whole CellEndEdit chain
    /// (LineEdited -> ResetBindings -> OnPropertyChanged(nameof(Totals))) terminated in no
    /// listener at all. That chain is asserted end to end here: the editor RAISES the name this
    /// form SWITCHES ON, or the subscription is deaf and nothing says so.
    /// </remarks>
    [TestMethod]
    public void The_totals_band_recomputes_when_the_lines_change()
    {
        var source = Source();
        var body = EditorBody();

        StringAssert.Contains(body, "void RepaintTotals()");
        StringAssert.Contains(body, "totals = BuildTotals(editor);");
        StringAssert.Contains(body, "linesHost.Controls.Add(totals, 1, 0);");
        StringAssert.Contains(body, "if (editor is INotifyPropertyChanged notify)");

        // The case RUNS the rebuild, and the rebuild happens twice: once beside the grid when the
        // tab is built, once from here. See CaseRuns -- naming the call alone left the band free to
        // go back to painting once and never listening, which is the finding itself.
        CaseRuns(body, "case \"Totals\":", "RepaintTotals();");
        RunsTwice(body, "case \"Totals\":", "RepaintTotals();");

        // The old band goes with it, or the panel grows a second copy on top of the first.
        StringAssert.Contains(body, "linesHost.Controls.Remove(totals);");
        StringAssert.Contains(body, "totals.Dispose();");

        // The far end of the same wire: the grid tells the set, the set tells the editor, and the
        // editor raises the very property name the switch above is written against.
        StringAssert.Contains(source, "grid.CellEndEdit += (_, _) => lines.LineEdited();");
        StringAssert.Contains(
            RecordEditorTemplate.Source(Root, validation: false), "OnPropertyChanged(nameof(Totals));");
        StringAssert.Contains(LineSetTemplate.Source(Root), "public void LineEdited()");
    }

    /// <summary>
    /// R42: every front end owes ILineSet.LineEdited() on a committed cell edit, or the totals
    /// band beside the grid goes stale the moment someone types a new quantity into a line.
    /// </summary>
    /// <remarks>
    /// CellEndEdit, not CellValueChanged: WPF needed a Dispatcher hop because its CellEditEnding
    /// fires BEFORE the grid pushes the typed value into the bound object, but WinForms'
    /// CellEndEdit fires AFTER the push, so the call can go straight in the handler.
    /// </remarks>
    [TestMethod]
    public void A_committed_line_edit_is_reported_to_the_set()
        => StringAssert.Contains(Source(), "grid.CellEndEdit += (_, _) => lines.LineEdited();");

    /// <summary>
    /// The form follows the editor the screen replaces, rather than the one it happened to hold
    /// when the screen was drawn.
    /// </summary>
    /// <remarks>
    /// A master REPLACES its whole Editor -- and with it Draft -- on select, New, Edit, Clear and
    /// after a successful Save, so a Draft read once and handed to every control's DataBindings is
    /// a record nothing else references any more. Worse, it is wrong before anyone touches the
    /// screen: setting the grid's DataSource selects row 0, which replaces the Editor while the
    /// form is still bound to the blank draft, and Save then writes the untouched row 0 and
    /// discards everything typed. The read has to happen INSIDE the repaint, which is what the
    /// ordering assertion below is for -- hoisting it back to the top of BuildEntry leaves every
    /// needle here present and the screen broken again.
    /// </remarks>
    [TestMethod]
    public void The_entry_form_is_repainted_from_the_editor_the_screen_currently_holds()
    {
        var body = EntryBody();

        StringAssert.Contains(body, "void RepaintRecord()");
        StringAssert.Contains(body, "var editor = screen.GetType().GetProperty(\"Editor\")?.GetValue(screen);");
        StringAssert.Contains(body, "var record = editor?.GetType().GetProperty(\"Draft\")?.GetValue(editor);");

        StringAssert.Contains(body, "header = BuildEntryHeader(screen, record);");
        StringAssert.Contains(body, "form = record is not null ? BuildEntryForm(record) : null;");

        // The old form and header go with it, or the screen stacks a second copy over the first.
        StringAssert.Contains(body, "layout.Controls.Remove(header);");
        StringAssert.Contains(body, "layout.Controls.Remove(form);");
        StringAssert.Contains(body, "tabs.TabPages.Remove(additionalPage);");

        StringAssert.Contains(body, "if (screen is INotifyPropertyChanged notify)");
        StringAssert.Contains(body, "notify.PropertyChanged += handler;");

        // The case RUNS the repaint, and the repaint happens twice: once for the first paint and
        // once from here. Naming the two separately let the case be cut back to a bare break --
        // paint once, then go deaf -- with the initial call keeping every needle green.
        CaseRuns(body, "case \"Editor\":", "RepaintRecord();");
        RunsTwice(body, "case \"Editor\":", "RepaintRecord();");

        var repaint = body.IndexOf("void RepaintRecord()", StringComparison.Ordinal);
        var read = body.IndexOf("GetProperty(\"Draft\")", StringComparison.Ordinal);
        Assert.IsGreaterThan(repaint, read, "the draft is read outside the repaint, so it is captured once");

        // The name the switch is written against is the one the screen raises: Editor's setter
        // goes through SetField, whose CallerMemberName IS the property name.
        StringAssert.Contains(MasterScreenTemplate.Source(Root, validation: true), "public RecordEditorViewModel<T> Editor");
        StringAssert.Contains(MasterScreenTemplate.Source(Root, validation: true), "private set => SetField(ref _editor, value);");
    }

    /// <summary>
    /// Saving or deleting on the entry screen re-reads the list under it.
    /// </summary>
    /// <remarks>
    /// On the old flat screens this was invisible: saving happened on a DIFFERENT screen, and
    /// coming back re-ran Show and rebuilt everything. Here the form and the list are one screen,
    /// so a customer entered and saved simply never appears in the list below.
    /// </remarks>
    [TestMethod]
    public void A_save_on_the_entry_screen_refreshes_the_list_under_it()
    {
        var body = EntryBody();

        StringAssert.Contains(body, "var (listControl, refreshList) = BuildRecordList(screen);");

        // Through CaseRuns like the rest, though refreshList happens to be called only here: that
        // is what made this the one test of the four the bare-presence form did pin, and it should
        // not go on depending on staying that way.
        CaseRuns(body, "case \"Items\":", "refreshList();");

        // The far end: Save re-reads the store through Reload, which announces exactly that name.
        StringAssert.Contains(MasterScreenTemplate.Source(Root, validation: true), "OnPropertyChanged(nameof(Items));");
    }

    /// <summary>
    /// A rebind of the record list does not throw away what is open in the form above it.
    /// </summary>
    /// <remarks>
    /// DataGridView.DataSource selects a row the moment it is set, and this screen's
    /// SelectionChanged writes Selected -- whose setter replaces the whole Editor. So without a
    /// guard, one keystroke in the search box, or the refresh after a Save, silently discards an
    /// in-progress entry. BuildList had the same chain and no form to lose.
    /// </remarks>
    [TestMethod]
    public void A_rebind_of_the_record_list_does_not_discard_the_entry_in_progress()
    {
        var body = Body(
            Source(),
            "private (Control Control, Action Rebind) BuildRecordList(object screen)",
            "private static bool HasAdditional(");

        StringAssert.Contains(body, "var suppressSelection = false;");
        StringAssert.Contains(body, "if (suppressSelection)");
        StringAssert.Contains(body, "suppressSelection = true;");
        StringAssert.Contains(body, "suppressSelection = false;");

        // In a finally, or a throw inside Bind leaves the grid permanently deaf to real clicks.
        StringAssert.Contains(body, "finally");

        // The guard is read BEFORE Selected is written, and every rebind goes through it: no bare
        // Bind and no Refresh left behind to set DataSource around the outside of the flag.
        var guard = body.IndexOf("if (suppressSelection)", StringComparison.Ordinal);
        var write = body.IndexOf("SetValue(screen, grid.CurrentRow?.DataBoundItem)", StringComparison.Ordinal);
        Assert.IsGreaterThan(guard, write, "Selected is written before the guard is consulted");

        Assert.DoesNotContain("Refresh(screen, layout);", body);

        var bind = body.IndexOf("Bind(screen, grid);", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, bind, "BuildRecordList never binds the grid");
        Assert.AreEqual(
            bind,
            body.LastIndexOf("Bind(screen, grid);", StringComparison.Ordinal),
            "a second, unguarded Bind would set DataSource around the outside of the flag");
    }

    /// <summary>
    /// The status line on both entry screens shows what the screen reports.
    /// </summary>
    /// <remarks>
    /// BuildList always carried an unbound status Label; the old flat editor carried none at all,
    /// so the entry screens introduced a permanently blank status line onto screens whose own
    /// commands write to Status -- including every message that says a Save did NOT happen, which
    /// the user has no other way of finding out.
    /// </remarks>
    [TestMethod]
    public void The_status_line_shows_what_the_screen_reports()
    {
        var source = Source();

        StringAssert.Contains(source, "private static void ShowStatus(Control bar, object owner)");
        StringAssert.Contains(source, "if (bar.Controls.Find(\"Status\", true).FirstOrDefault() is Label status)");
        StringAssert.Contains(
            source,
            "status.Text = owner.GetType().GetProperty(\"Status\")?.GetValue(owner) as string ?? string.Empty;");

        var entry = EntryBody();
        var editor = EditorBody();

        // Painted when the screen is built, and again on every change: a screen the rail returns to
        // is rebuilt from scratch, and whatever it last reported is still its Status. Both, on both
        // screens -- naming the call alone was satisfied by the first paint while the case beside
        // it did nothing, which is the permanently blank status line all over again.
        CaseRuns(entry, "case \"Status\":", "ShowStatus(bar, screen);");
        RunsTwice(entry, "case \"Status\":", "ShowStatus(bar, screen);");

        CaseRuns(editor, "case \"Status\":", "ShowStatus(bar, editor);");
        RunsTwice(editor, "case \"Status\":", "ShowStatus(bar, editor);");

        // Both screens raise it through SetField, whose CallerMemberName is the property name.
        StringAssert.Contains(MasterScreenTemplate.Source(Root, validation: true), "protected set => SetField(ref _status, value);");
        StringAssert.Contains(
            RecordEditorTemplate.Source(Root, validation: false), "private set => SetField(ref _status, value);");
    }

    /// <summary>
    /// A model with many field groups scrolls rather than squeezing the rest of the screen out.
    /// </summary>
    /// <remarks>
    /// Both entry screens' root rows are Absolute 74 / AutoSize / Percent 100 / Absolute 40. When
    /// the AutoSize form row grows past the remaining height the Percent row collapses to zero --
    /// the tab strip holding the record list, or the lines and their totals, simply disappears --
    /// and the action bar under it clips. The_form_does_not_grow_with_the_model varies entity
    /// COUNT, not field count, so it says nothing about this. BuildDashboard already scrolls for
    /// exactly this reason.
    /// </remarks>
    [TestMethod]
    public void A_tall_form_scrolls_rather_than_squeezing_out_the_rest_of_the_screen()
    {
        const string root =
            "var layout = new TableLayoutPanel "
            + "{ Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, AutoScroll = true };";

        StringAssert.Contains(EntryBody(), root);
        StringAssert.Contains(EditorBody(), root);
    }

    /// <summary>
    /// A screen the rail navigates away from is disposed, and every subscription onto it is undone.
    /// </summary>
    /// <remarks>
    /// The discarded tree is dozens of controls, each holding DataBindings registered in the form's
    /// BindingContext and Fonts of their own, so clearing without disposing leaks a little GDI on
    /// every rail click. The subscriptions matter more: the entry screens now attach a
    /// PropertyChanged handler to the screen itself, and a screen outlives the controls built over
    /// it, so the fix for the repaint would otherwise be a handler leak on every rail click.
    /// </remarks>
    [TestMethod]
    public void A_screen_navigated_away_from_is_disposed_with_its_subscriptions()
    {
        var source = Source();

        // Snapshotted first: Control.Dispose takes the control out of _content.Controls, so
        // disposing straight out of a foreach over it modifies the collection being enumerated.
        StringAssert.Contains(source, "foreach (var discarded in _content.Controls.Cast<Control>().ToArray())");
        StringAssert.Contains(source, "discarded.Dispose();");

        var show = source.IndexOf("private void Show(object? screen)", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, show, "the emitted form has no Show");

        var dispose = source.IndexOf("discarded.Dispose();", show, StringComparison.Ordinal);
        var clear = source.IndexOf("_content.Controls.Clear();", show, StringComparison.Ordinal);
        Assert.IsGreaterThan(dispose, clear, "Clear runs first, so there is nothing left to dispose");

        const string unsubscribe = "layout.Disposed += (_, _) => notify.PropertyChanged -= handler;";

        StringAssert.Contains(EntryBody(), unsubscribe);
        StringAssert.Contains(EditorBody(), unsubscribe);
    }

    /// <summary>
    /// Behind HasAdditional on both screens, so a model with no long-text fields shows no empty tab
    /// -- the same rule that drops an empty panel.
    /// </summary>
    /// <remarks>
    /// The heading comes off the emitted <c>FieldGroups.AdditionalTitle</c> rather than being
    /// spelled a second time. Unlike WPF -- which writes it into XAML, where the constant is
    /// reachable only through an x:Static reference and an extra xmlns -- this tab is built in
    /// code, so it can just call it, and the literal must not reappear: two spellings of one
    /// heading in one shipped app is the drift the constant exists to prevent.
    /// </remarks>
    [TestMethod]
    public void The_additional_fields_are_shown_only_when_there_are_some()
    {
        var source = Source();

        StringAssert.Contains(source, "if (record is not null && HasAdditional(screen))");
        StringAssert.Contains(source, "if (draft is not null && HasAdditional(editor))");
        StringAssert.Contains(source, "var fields = FieldGroups.Additional(record.GetType());");
        StringAssert.Contains(source, "if (fields.Count == 0)");

        StringAssert.Contains(source, "var page = new TabPage(FieldGroups.AdditionalTitle);");
        Assert.DoesNotContain($"\"{FieldGroupsTemplate.AdditionalTitle}\"", source);

        // The constant the call resolves to. The Screens/FieldGroups.cs the compile tests below
        // hand the form is this same source, so a rename there fails them rather than this.
        StringAssert.Contains(
            FieldGroupsTemplate.Source(Root),
            $"public const string AdditionalTitle = \"{FieldGroupsTemplate.AdditionalTitle}\";");
    }

    [TestMethod]
    public void An_unthemed_shell_uses_the_system_colours()
    {
        StringAssert.Contains(Source(), "BackColor = SystemColors.Control");
    }

    [TestMethod]
    public void A_themed_shell_paints_the_rail_from_the_palette()
    {
        var palette = ThemePalette.Resolve(AppTheme.Dark, accent: null)!;
        var themed = Source(AppCustomization.Default with { Theme = AppTheme.Dark });

        StringAssert.Contains(themed, $"ColorTranslator.FromHtml(\"{palette.Surface}\")");
        Assert.DoesNotContain("SystemColors.Control", themed);
    }

    [TestMethod]
    public void The_title_is_escaped_for_a_source_literal_not_for_markup()
    {
        var source = Source(AppCustomization.Default with
        {
            Company = "Smith & Sons",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        });

        StringAssert.Contains(source, "Smith & Sons");
        Assert.DoesNotContain("&amp;", source);
    }

    /// <summary>
    /// The whole point of three templates rather than a view per entity: the form is the same file
    /// for a model of two entities and a model of twenty.
    /// </summary>
    /// <remarks>
    /// Generated through the target rather than the layout, because the layout is never handed
    /// entity names — so asserting a name's absence against it could not fail.
    /// </remarks>
    [TestMethod]
    public void The_form_does_not_grow_with_the_model()
    {
        Assert.AreEqual(MainForm(["Order", "Customer"]), MainForm(Twenty()));

        static string MainForm(IReadOnlyList<string> names)
            => new WinFormsTarget().FrontEndFiles(
                    new FrontEndContext(
                        "Payroll", names,
                        AppCustomization.Default with { Layout = AppLayout.Business },
                        EntityRoleMap.Empty))
                .Single(f => f.Name == "MainForm.cs").Source;

        static IReadOnlyList<string> Twenty()
            => [.. Enumerable.Range(1, 20).Select(i => $"Entity{i}")];
    }

    /// <summary>
    /// The emitted form compiles against the emitted screens it binds to.
    /// </summary>
    /// <remarks>
    /// FieldGroups and RequiredFields travel with it now: the entry screens reflect over both, so
    /// a compile test that omitted them would no longer say anything true about the file it claims
    /// to prove compiles.
    /// </remarks>
    [TestMethod]
    public void The_form_compiles()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source()),
            ("Screens/EntityRole.cs", RoleStub),
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source(Root)),
            ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source(Root, null)),
            ("ViewModels/Shell.cs", ShellStub),
            ("Auth/Session.cs", SessionStub));
    }

    [TestMethod]
    public void The_themed_form_compiles_too()
    {
        EmittedDesktopSource.AssertCompilesTogether(
            ("Views/MainForm.cs", Source(AppCustomization.Default with { Theme = AppTheme.Dark })),
            ("Screens/EntityRole.cs", RoleStub),
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source(Root)),
            ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source(Root, null)),
            ("ViewModels/Shell.cs", ShellStub),
            ("Auth/Session.cs", SessionStub));
    }

    private const string RoleStub = """
        namespace Payroll.WinForms.Screens;

        public enum EntityRole { Master, Transaction, DocumentLine }

        /// <summary>The lines of a transaction, as much of it as a view touches.</summary>
        public interface ILineSet
        {
            string Title { get; }
            string ForeignKey { get; }
            System.Type LineType { get; }
            System.Collections.IEnumerable Items { get; }
            object? Selected { get; set; }
            bool IsDirty { get; }
            void Load(int parentId);
            void Add();
            void Remove(object? line);
            void Clear();
            void LineEdited();
            bool Save(int parentId);
        }
        """;

    /// <summary>
    /// Just enough of the screens for the form to bind to.
    /// </summary>
    /// <remarks>
    /// Stubs rather than the real emitted view models, which pull in the store, the models and the
    /// validators: a compile test of a VIEW should fail for reasons in the view.
    /// </remarks>
    private const string ShellStub = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Windows.Input;
        using Payroll.WinForms.Screens;

        namespace Payroll.WinForms.ViewModels;

        public interface IEntityViewModel
        {
            string Title { get; }
            IEnumerable Items { get; }
            object? Selected { get; set; }
            ICommand AddCommand { get; }
            ICommand DeleteCommand { get; }
            ICommand SaveCommand { get; }
            string Status { get; }
            EntityRole Role { get; }
            int Count { get; }
        }

        public sealed record LineTotal(string Column, decimal Value, bool IsGrand);

        public interface IDirtyScreen
        {
            bool IsDirty { get; }
        }

        public interface IRecordEditor : IDirtyScreen
        {
            string Title { get; }
            IReadOnlyList<ILineSet> Lines { get; }
            IReadOnlyList<LineTotal> Totals { get; }
        }

        public sealed record NavGroup(string Title, IReadOnlyList<IEntityViewModel> Entities);

        public sealed record DashboardGroup(string Title, IReadOnlyList<IEntityViewModel> Entities);

        public sealed class DashboardViewModel
        {
            public IReadOnlyList<DashboardGroup> Groups { get; } = new List<DashboardGroup>();
        }

        public sealed class MainViewModel
        {
            public ObservableCollection<IEntityViewModel> Entities { get; } = new();
        }

        public sealed class ShellViewModel : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

            public ShellViewModel(
                IReadOnlyList<IEntityViewModel> entities, IReadOnlyList<IEntityViewModel> adminEntities)
            {
            }

            public Func<bool> ConfirmDiscard { get; set; } = () => true;
            public DashboardViewModel Dashboard { get; } = new();
            public IReadOnlyList<NavGroup> Groups { get; } = new List<NavGroup>();
            public string SignedInUser => string.Empty;
            public object? Current => Dashboard;
            public ICommand SignOutCommand { get; } = null!;
            public bool TryShow(object screen) => true;
            public void Back() { }
        }
        """;

    private const string SessionStub = """
        namespace Payroll.WinForms.Auth;

        public static class Session
        {
            public static bool SignOutRequested { get; private set; }
            public static void RequestSignOut() => SignOutRequested = true;
        }
        """;

    /// <summary>
    /// The foreign-key picker shows what a person calls the row, not its key.
    /// </summary>
    /// <remarks>
    /// This target had no test here at all, which is why it sat on a hardcoded "Id" while WPF's
    /// equivalent was fixed and tested: a supplier picker listing 1, 2, 3 on the screen whose
    /// mockup picks a supplier by name. The choice lives in FieldGroups.LookupLabel now, called by
    /// both targets, and FieldGroupsTests compiles and runs the rule itself.
    /// </remarks>
    [TestMethod]
    public void The_lookup_shows_what_a_person_calls_the_record_not_its_id()
    {
        var body = Body(
            Source(),
            "private Control BuildFieldEditor(object record, PropertyInfo field)",
            "private Control BuildEntryHeader(object owner, object? record)");

        StringAssert.Contains(body, "DisplayMember = FieldGroups.LookupLabel(nav.PropertyType)");

        // ValueMember stays the key: it is what the bound property holds.
        StringAssert.Contains(body, "ValueMember = \"Id\"");
    }

    /// <summary>
    /// The rows a picker binds to are typed as the entity, not as object.
    /// </summary>
    /// <remarks>
    /// WinForms resolves DisplayMember and ValueMember against the list's ITEM type. A
    /// List&lt;object&gt; has no properties to resolve either against, so naming a display property
    /// would have bound a picker that shows nothing and returns nothing -- and an EMPTY
    /// List&lt;object&gt; gives it nothing to infer from either. Asserted on the construction
    /// rather than on the return type, because IList is the return type either way.
    /// </remarks>
    [TestMethod]
    public void The_lookup_rows_are_typed_as_the_entity_they_list()
    {
        var body = Body(Source(), "private IList LookupRows(Type entityType)", "private Control BuildEntryActionBar(");

        StringAssert.Contains(body, "typeof(List<>).MakeGenericType(entityType)");
        Assert.IsFalse(
            body.Contains("new List<object>()", StringComparison.Ordinal),
            "LookupRows still builds an untyped list, which resolves no display member");
    }

    /// <summary>
    /// The record count over the master's list -- which this target emitted none of at all.
    /// </summary>
    /// <remarks>
    /// The spec puts a search box and a record count on the master's list and says this target is
    /// the same shape through the same seam. WPF emitted a count and this one did not, and no test
    /// on either side named it, so the two front ends disagreed about what the same screen shows
    /// with the whole suite green. Scoped to BuildRecordList: "Total Records" anywhere in a
    /// 1,100-line form says nothing about the list it belongs over.
    /// </remarks>
    [TestMethod]
    public void The_master_list_says_how_many_records_it_is_showing()
    {
        var body = Body(
            Source(),
            "private (Control Control, Action Rebind) BuildRecordList(object screen)",
            "private static bool HasAdditional(");

        StringAssert.Contains(body, "Total Records: {shown}");

        // Shown, not Count: the label sits over a grid the search box narrows, and Count is the
        // whole store. MasterScreenTests proves the two differ on compiled code.
        StringAssert.Contains(body, "GetProperty(\"Shown\")");
        Assert.IsFalse(
            body.Contains("GetProperty(\"Count\")", StringComparison.Ordinal),
            "the count over the list reads the store total rather than what the list shows");

        // Set inside Rebind, or it is painted once and never follows the search box.
        var rebind = body.IndexOf("void Rebind()", StringComparison.Ordinal);
        var assigned = body.IndexOf("count.Text =", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, rebind, "BuildRecordList has no Rebind");
        Assert.IsGreaterThan(rebind, assigned, "the count is not set inside Rebind, so it never updates");
    }

    /// <summary>
    /// Issue #129: a refused save marks the fields it names, on the master's form and on a
    /// transaction's editor alike.
    /// </summary>
    /// <remarks>
    /// Each needle is scoped to the screen that must do it: the master has to follow the editor
    /// the screen swaps in, the transaction editor has to listen for Errors on its own.
    /// </remarks>
    [TestMethod]
    public void A_refused_save_marks_the_fields_it_names()
    {
        var source = Source();

        StringAssert.Contains(source, "private readonly ErrorProvider _fieldErrors");
        StringAssert.Contains(source, "_fieldErrors.SetError(");

        // Every field control carries the property it edits, which is how a refusal finds it.
        StringAssert.Contains(
            Body(source, "private Control BuildFieldEditor(", "private Control CreateFieldEditor("),
            "editor.Tag = field;");

        var master = Body(source, "private Control BuildEntry(object screen)", "private (Control");
        StringAssert.Contains(master, "watched.PropertyChanged += OnEditorChanged;");
        StringAssert.Contains(master, "watched.PropertyChanged -= OnEditorChanged;");
        StringAssert.Contains(master, "PaintErrors(editor, header, form, additionalPage);");

        var transaction = Body(source, "private Control BuildEditor(IRecordEditor editor)", "private Control BuildTotals(");
        CaseRuns(transaction, "case \"Errors\":", "PaintErrors(editor, layout);");
    }
}
