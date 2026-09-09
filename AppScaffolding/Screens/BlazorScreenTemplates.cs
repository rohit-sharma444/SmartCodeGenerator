namespace AppScaffolding.Screens;

/// <summary>
/// The business screens on the web: one component per kind of screen, and the reflection they
/// share.
/// </summary>
/// <remarks>
/// The desktop targets bind these screens as view models, because a window has nothing else to
/// bind to. A Razor component already is the thing a page renders, so the web renders components
/// and keeps the shared reasoning — which columns, which date, which totals, what a search
/// matches — in one plain C# class that Roslyn can compile-test. Razor cannot be compile-tested
/// here, so the less logic lives in markup the better.
/// <para>
/// The components reach the store through <c>StoreFactory.Create&lt;T&gt;()</c> and the validators
/// through <c>ValidatorFactory.Create&lt;T&gt;()</c>, exactly as the desktop screens do: a generic
/// component cannot name <c>OrderRepository</c> any more than a generic view model can.
/// </para>
/// </remarks>
public static class BlazorScreenTemplates
{
    /// <summary>Where the screen components live, so a page's <c>@using</c> and the emitted folder
    /// cannot disagree.</summary>
    public const string Folder = "Components/Screens";

    /// <summary>Source of <c>Screens/ScreenFields.cs</c>: what every screen needs to know about a
    /// model it cannot name.</summary>
    public static string Fields(string rootNamespace) => $$"""
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace {{rootNamespace}}.Screens;

/// <summary>
/// The reflection the screens share: which columns a list shows, which date it filters on, which
/// columns it totals, and how a typed cell gets back onto a record.
/// </summary>
/// <remarks>
/// Plain C# rather than logic inside the components, so it can be compiled and tested on its own.
/// Every method takes the model type rather than a generic parameter where it can, because a page
/// holds a <c>Type</c> more often than it holds a <c>T</c>.
/// </remarks>
public static class ScreenFields
{
    /// <summary>
    /// The dates every generated class carries, which are never what a user means by "the date".
    /// </summary>
    /// <remarks>
    /// AddAuditFields gives every model CreatedOn and ModifiedOn, so a date range that filtered on
    /// the first DateTime it found would filter on a field nobody chose and nobody sees.
    /// </remarks>
    private static readonly string[] AuditFields = ["CreatedOn", "ModifiedOn"];

    /// <summary>The scalar properties a list shows, Id first and then declaration order.</summary>
    public static PropertyInfo[] Columns(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => IsScalar(p.PropertyType))
            .OrderByDescending(p => p.Name == "Id")
            .ToArray();

    /// <summary>The fields an editor offers. Id is shown by the list and assigned by the store, so
    /// it is never typed into.</summary>
    public static PropertyInfo[] Editable(Type type)
        => Columns(type)
            .Where(p => p.CanWrite && !string.Equals(p.Name, "Id", StringComparison.Ordinal))
            .ToArray();

    public static int IdOf(object record)
        => (int)(record.GetType().GetProperty("Id")?.GetValue(record) ?? 0);

    public static void SetId(object record, int id)
        => record.GetType().GetProperty("Id")?.SetValue(record, id);

    /// <summary>
    /// Whether a record matches what was typed in the search box.
    /// </summary>
    /// <remarks>
    /// Across every string property, so a user does not have to know which column holds what —
    /// the same rule the desktop screens use.
    /// </remarks>
    public static bool Matches(object record, string search)
    {
        if (search.Length == 0)
            return true;

        return record.GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .Any(p => (p.GetValue(record) as string)?
                .Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>The date a transaction list filters on, or empty when the model has none — in
    /// which case the screen renders no range rather than filtering on something arbitrary.</summary>
    public static string DateField(Type type)
        => type.GetProperties()
            .Where(p => p.PropertyType == typeof(DateTime))
            .Select(p => p.Name)
            .FirstOrDefault(name => !AuditFields.Contains(name, StringComparer.OrdinalIgnoreCase))
            ?? string.Empty;

    /// <summary>Whether a record falls inside the range. A record with no date is kept: it was
    /// never excluded by a filter it has no value for.</summary>
    public static bool InRange(object record, string dateField, DateTime? from, DateTime? to)
    {
        if (dateField.Length == 0 || (from is null && to is null))
            return true;

        if (record.GetType().GetProperty(dateField)?.GetValue(record) is not DateTime date)
            return true;

        if (from is { } start && date < start)
            return false;

        return to is not { } end || date <= end;
    }

    /// <summary>
    /// Every column a list totals.
    /// </summary>
    /// <remarks>
    /// Every decimal and double that is neither an Id nor a foreign key, rather than guessing which
    /// single column is "the" total: one confidently wrong number is worse than several honest ones.
    /// </remarks>
    public static PropertyInfo[] MoneyColumns(Type type)
        => type.GetProperties()
            .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(double))
            .Where(p => !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)
                        && !p.Name.EndsWith("Id", StringComparison.Ordinal))
            .ToArray();

    public static decimal Total(IEnumerable<object> rows, PropertyInfo column)
        => rows.Sum(row => ToDecimal(column.GetValue(row)));

    /// <summary>
    /// Writes an edited cell back onto a record, returning the reason it would not go.
    /// </summary>
    /// <remarks>
    /// A value that will not convert is reported rather than swallowed: silently keeping the old
    /// number is how a user comes to distrust a form.
    /// </remarks>
    public static string? Assign(object record, PropertyInfo property, string? text)
    {
        try
        {
            var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            property.SetValue(
                record,
                string.IsNullOrWhiteSpace(text) && target != typeof(string)
                    ? null
                    : Convert.ChangeType(text, target, CultureInfo.CurrentCulture));

            return null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return $"'{text}' is not a valid {property.Name}.";
        }
    }

    /// <summary>
    /// The copy an editor works on.
    /// </summary>
    /// <remarks>
    /// A System.Text.Json round-trip, already a dependency through JsonStore. Navigation
    /// properties are [JsonIgnore], so they drop out of the copy.
    /// </remarks>
    public static T Clone<T>(T record) where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(record))!;

    /// <summary>What the record looked like when the editor opened. Compared as text because a
    /// generated model has no change notification to subscribe to.</summary>
    public static string Snapshot(object record) => JsonSerializer.Serialize(record);

    /// <summary>What an input shows for a value: a date without a time, and everything else as
    /// itself.</summary>
    public static string Display(object? value)
        => value switch
        {
            null => string.Empty,
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>The input type a browser should offer for a property.</summary>
    public static string InputType(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(DateTime))
            return "date";

        if (type == typeof(bool))
            return "checkbox";

        return type == typeof(string) ? "text" : "number";
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(DateTime);
    }

    private static decimal ToDecimal(object? value)
        => value switch
        {
            decimal d => d,
            double d => (decimal)d,
            _ => 0m,
        };
}
""";

    /// <summary>
    /// Source of <c>Components/Screens/MasterScreen.razor</c>: reference data as a read-only list
    /// that opens an editor.
    /// </summary>
    /// <remarks>
    /// Generic over the record, so one component serves every master in the model — the web
    /// equivalent of the desktop's one template rather than a view per entity.
    /// </remarks>
    public static string MasterScreen(string rootNamespace, bool login) => $$"""
@typeparam TRecord where TRecord : class, new()
@using System.Reflection{{ClaimsUsing(login)}}
@using {{rootNamespace}}.Data
@using {{rootNamespace}}.Screens
@using {{rootNamespace}}.Validation{{AuthorizationUsing(login)}}
@inject NavigationManager Navigation

<h1>@Title</h1>

<div class="d-flex gap-2 mb-3 align-items-center">
    <input class="form-control" style="max-width: 20rem" placeholder="Search @Title"
           value="@_search" @oninput="OnSearch" />
    <button class="btn btn-primary" @onclick="New">New</button>
    <button class="btn btn-outline-secondary" @onclick="Open" disabled="@(_selected is null)">Open</button>
{{DeleteButton(login)}}</div>

@if (_rows.Count == 0)
{
    <p><em>No @Title records@(_search.Length > 0 ? " match that search" : " yet").</em></p>
}
else
{
    <table class="table table-hover">
        <thead>
            <tr>
                @foreach (var column in Columns)
                {
                    <th>@column.Name</th>
                }
            </tr>
        </thead>
        <tbody>
            @foreach (var row in _rows)
            {
                <tr class="@(ReferenceEquals(row, _selected) ? "table-active" : null)"
                    @onclick="() => _selected = row" @ondblclick="Open">
                    @foreach (var column in Columns)
                    {
                        <td>@ScreenFields.Display(column.GetValue(row))</td>
                    }
                </tr>
            }
        </tbody>
    </table>
}

@if (_status.Length > 0)
{
    <p role="status">@_status</p>
}

@code {
    // A static field on a generic component is per closed type, so this reflects once per entity
    // rather than once per render.
    private static readonly PropertyInfo[] Columns = ScreenFields.Columns(typeof(TRecord));

    private readonly IStore<TRecord> _store = StoreFactory.Create<TRecord>();

    private List<TRecord> _rows = new();
    private TRecord? _selected;
    private string _search = string.Empty;
    private string _status = string.Empty;

    /// <summary>The heading, and the noun the empty state uses.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    /// <summary>The URL segment this entity's pages live under, so the editor can be reached.</summary>
    [Parameter, EditorRequired] public string Route { get; set; } = string.Empty;
{{AuthStateParameter(login)}}
    protected override void OnInitialized() => Reload();

    private void OnSearch(ChangeEventArgs e)
    {
        _search = e.Value?.ToString() ?? string.Empty;
        Reload();
    }

    /// <summary>Re-reads the store. The list never edits, so this is the only thing that changes
    /// its rows.</summary>
    private void Reload()
    {
        _rows = _store.GetAll().Where(record => ScreenFields.Matches(record, _search)).ToList();

        // The row the search just hid cannot stay selected, or Open would edit a record that is
        // not on screen.
        if (_selected is not null && !_rows.Contains(_selected))
            _selected = null;
    }

    private void New() => Navigation.NavigateTo($"/{Route}/new");

    /// <summary>Opening a record is a navigation, not a dialog: the editor is a screen like any
    /// other, and a URL can be shared.</summary>
    private void Open()
    {
        if (_selected is { } record)
            Navigation.NavigateTo($"/{Route}/{ScreenFields.IdOf(record)}");
    }
{{DeleteHandler(login)}}}
""";

    /// <summary>
    /// Source of <c>Components/Screens/TransactionScreen.razor</c>: the master list plus the three
    /// things a transaction list always needs.
    /// </summary>
    /// <remarks>
    /// A date range, column totals, and a heading that says which. Written beside the master screen
    /// rather than deriving from it: a Razor component's markup is not inherited — a derived
    /// component replaces its base's render tree entirely — so inheritance would buy nothing here
    /// and hide where the markup comes from. The reasoning both screens share lives in
    /// <c>ScreenFields</c>, which is what keeps them from drifting.
    /// </remarks>
    public static string TransactionScreen(string rootNamespace, bool login) => $$"""
@typeparam TRecord where TRecord : class, new()
@using System.Reflection{{ClaimsUsing(login)}}
@using {{rootNamespace}}.Data
@using {{rootNamespace}}.Screens
@using {{rootNamespace}}.Validation{{AuthorizationUsing(login)}}
@inject NavigationManager Navigation

<h1>@Title</h1>

<div class="d-flex gap-2 mb-3 align-items-center flex-wrap">
    <input class="form-control" style="max-width: 16rem" placeholder="Search @Title"
           value="@_search" @oninput="OnSearch" />

    @if (DateField.Length > 0)
    {
        <label class="form-label mb-0" for="from">@DateField from</label>
        <input id="from" type="date" class="form-control" style="max-width: 11rem"
               value="@ScreenFields.Display(_from)" @onchange="OnFrom" />
        <label class="form-label mb-0" for="to">to</label>
        <input id="to" type="date" class="form-control" style="max-width: 11rem"
               value="@ScreenFields.Display(_to)" @onchange="OnTo" />
    }

    <button class="btn btn-primary" @onclick="New">New</button>
    <button class="btn btn-outline-secondary" @onclick="Open" disabled="@(_selected is null)">Open</button>
{{DeleteButton(login)}}</div>

@if (_rows.Count == 0)
{
    <p><em>No @Title records in this range.</em></p>
}
else
{
    <table class="table table-hover">
        <thead>
            <tr>
                @foreach (var column in Columns)
                {
                    <th>@column.Name</th>
                }
            </tr>
        </thead>
        <tbody>
            @foreach (var row in _rows)
            {
                <tr class="@(ReferenceEquals(row, _selected) ? "table-active" : null)"
                    @onclick="() => _selected = row" @ondblclick="Open">
                    @foreach (var column in Columns)
                    {
                        <td>@ScreenFields.Display(column.GetValue(row))</td>
                    }
                </tr>
            }
        </tbody>
        @if (Money.Length > 0)
        {
            <tfoot>
                <tr>
                    @foreach (var column in Columns)
                    {
                        <th>@Totals(column)</th>
                    }
                </tr>
            </tfoot>
        }
    </table>
}

@if (_status.Length > 0)
{
    <p role="status">@_status</p>
}

@code {
    private static readonly PropertyInfo[] Columns = ScreenFields.Columns(typeof(TRecord));

    /// <summary>The columns worth a total: every decimal and double that is neither an Id nor a
    /// foreign key.</summary>
    private static readonly PropertyInfo[] Money = ScreenFields.MoneyColumns(typeof(TRecord));

    /// <summary>The date the range filters on. Empty for a model with no date of its own, and the
    /// range is then not rendered at all rather than filtering on something arbitrary.</summary>
    private static readonly string DateField = ScreenFields.DateField(typeof(TRecord));

    private readonly IStore<TRecord> _store = StoreFactory.Create<TRecord>();

    private List<TRecord> _rows = new();
    private TRecord? _selected;
    private string _search = string.Empty;
    private string _status = string.Empty;
    private DateTime? _from;
    private DateTime? _to;

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string Route { get; set; } = string.Empty;
{{AuthStateParameter(login)}}
    protected override void OnInitialized() => Reload();

    private void OnSearch(ChangeEventArgs e)
    {
        _search = e.Value?.ToString() ?? string.Empty;
        Reload();
    }

    private void OnFrom(ChangeEventArgs e)
    {
        _from = Parse(e.Value?.ToString());
        Reload();
    }

    private void OnTo(ChangeEventArgs e)
    {
        _to = Parse(e.Value?.ToString());
        Reload();
    }

    /// <summary>An emptied date box clears that end of the range rather than rejecting it.</summary>
    private static DateTime? Parse(string? text)
        => DateTime.TryParse(text, out var parsed) ? parsed : null;

    private void Reload()
    {
        _rows = _store.GetAll()
            .Where(record => ScreenFields.Matches(record, _search))
            .Where(record => ScreenFields.InRange(record, DateField, _from, _to))
            .ToList();

        if (_selected is not null && !_rows.Contains(_selected))
            _selected = null;
    }

    /// <summary>One total per money column, over whatever the list is currently showing — not over
    /// the whole store, which would make the range look ignored.</summary>
    private string? Totals(PropertyInfo column)
        => Money.Contains(column)
            ? ScreenFields.Total(_rows.Cast<object>(), column).ToString("N2")
            : null;

    private void New() => Navigation.NavigateTo($"/{Route}/new");

    private void Open()
    {
        if (_selected is { } record)
            Navigation.NavigateTo($"/{Route}/{ScreenFields.IdOf(record)}");
    }
{{DeleteHandler(login)}}}
""";

    /// <summary>
    /// Source of <c>Components/Screens/RecordEditor.razor</c>: one record, edited as a copy.
    /// </summary>
    /// <remarks>
    /// The copy is the whole point, exactly as it is on the desktop: editing the stored record
    /// would make Cancel a lie. <c>NavigationLock</c> is the web's answer to the desktop message
    /// box — an editor that fills the content area can be navigated away from, so something has to
    /// ask before the edits are lost.
    /// </remarks>
    /// <param name="validation">
    /// Whether the app has generated validators. The editor writes through the store rather than
    /// through <c>Repository&lt;T&gt;</c>, so it has to run them itself or the web would accept
    /// records the API rejects.
    /// </param>
    public static string RecordEditor(string rootNamespace, bool validation) => $$"""
@typeparam TRecord where TRecord : class, new()
@using System.Reflection
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.JSInterop
@using {{rootNamespace}}.Data
@using {{rootNamespace}}.Screens{{ValidationUsing(rootNamespace, validation)}}
@inject NavigationManager Navigation
@inject IJSRuntime JS

<NavigationLock ConfirmExternalNavigation="@IsDirty" OnBeforeInternalNavigation="OnBeforeNavigation" />

<p><a href="/@Route">&larr; @Title</a></p>

@if (_missing)
{
    <h1>@Title</h1>
    <p role="status">That record no longer exists.</p>
}
else
{
    <h1>@Heading</h1>

    @foreach (var field in Fields)
    {
        <div class="mb-3">
            <label class="form-label" for="@field.Name">@field.Name</label>

            @if (ScreenFields.InputType(field) == "checkbox")
            {
                <input type="checkbox" class="form-check-input d-block" id="@field.Name"
                       checked="@(field.GetValue(_draft) as bool? == true)"
                       @onchange="e => Set(field, e.Value?.ToString())" />
            }
            else
            {
                <input type="@ScreenFields.InputType(field)" class="form-control" id="@field.Name"
                       value="@ScreenFields.Display(field.GetValue(_draft))"
                       @onchange="e => Set(field, e.Value?.ToString())" />
            }
        </div>
    }

    @foreach (var lines in _lines)
    {
        <h2 class="h6 mt-4">@lines.Title</h2>

        @if (Rows(lines).Count == 0)
        {
            <p><em>No @lines.Title rows yet.</em></p>
        }
        else
        {
            <table class="table table-sm">
                <thead>
                    <tr>
                        @foreach (var field in LineFields(lines))
                        {
                            <th>@field.Name</th>
                        }
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var line in Rows(lines))
                    {
                        <tr>
                            @foreach (var field in LineFields(lines))
                            {
                                <td>
                                    <input type="@ScreenFields.InputType(field)" class="form-control form-control-sm"
                                           value="@ScreenFields.Display(field.GetValue(line))"
                                           @onchange="e => SetLine(line, field, e.Value?.ToString())" />
                                </td>
                            }
                            <td>
                                <button class="btn btn-sm btn-outline-danger"
                                        @onclick="() => lines.Remove(line)">Remove</button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        <button class="btn btn-sm btn-outline-secondary" @onclick="() => lines.Add()">Add @lines.Title</button>
    }

    <div class="d-flex gap-2 mt-4">
        <button class="btn btn-primary" @onclick="Save">Save</button>
        <button class="btn btn-outline-secondary" @onclick="Cancel">Cancel</button>
    </div>

    @if (_status.Length > 0)
    {
        <p role="status" class="mt-3">@_status</p>
    }
}

@code {
    /// <summary>The fields an editor offers. Id is assigned by the store and shown by the list, so
    /// it is never typed into.</summary>
    private static readonly PropertyInfo[] Fields = ScreenFields.Editable(typeof(TRecord));

    private readonly IStore<TRecord> _store = StoreFactory.Create<TRecord>();

    private TRecord _draft = new();
    private IReadOnlyList<ILineSet> _lines = [];
    private string _openedAs = string.Empty;
    private string _status = string.Empty;
    private bool _missing;
    private bool _loaded;
    private int? _loadedId;

    /// <summary>Whichever record the URL named, or null for a new one.</summary>
    [Parameter] public int? Id { get; set; }

    /// <summary>
    /// Builds this record's line sets, or null for a master.
    /// </summary>
    /// <remarks>
    /// Supplied by the page, which is the only place that knows both the record type and the line
    /// type: this component is generic over the record alone and cannot name a line. A factory
    /// rather than a list, because every editor needs its own copy of the lines.
    /// </remarks>
    [Parameter] public Func<IReadOnlyList<ILineSet>>? Lines { get; set; }

    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string Route { get; set; } = string.Empty;

    private bool IsNew => Id is null;

    private string Heading => IsNew ? $"New {Title}" : $"{Title} {Id}";

    /// <summary>
    /// Whether anything has been typed since the editor opened.
    /// </summary>
    /// <remarks>
    /// Compared as a serialized snapshot rather than tracked per property: the draft is a plain
    /// generated model with no change notification, so there is nothing to subscribe to.
    /// </remarks>
    private bool IsDirty
        => !string.Equals(ScreenFields.Snapshot(_draft), _openedAs, StringComparison.Ordinal)
           || _lines.Any(lines => lines.IsDirty);

    /// <summary>The lines on screen. ILineSet holds them untyped, because the editor cannot name
    /// the line type any more than it can name the record's.</summary>
    private static List<object> Rows(ILineSet lines) => lines.Items.Cast<object>().ToList();

    /// <summary>The columns a line is edited through: everything but its own id and the parent's,
    /// which the editor stamps on save.</summary>
    private static IEnumerable<PropertyInfo> LineFields(ILineSet lines)
        => ScreenFields.Editable(lines.LineType)
            .Where(field => !string.Equals(field.Name, lines.ForeignKey, StringComparison.Ordinal));

    private void SetLine(object line, PropertyInfo field, string? text)
        => _status = ScreenFields.Assign(line, field, text) ?? string.Empty;

    /// <summary>Set while leaving deliberately, so Save and Cancel are not asked to confirm a
    /// discard the user just chose.</summary>
    private bool _leaving;

    /// <summary>
    /// Loads once per record.
    /// </summary>
    /// <remarks>
    /// Guarded on the id rather than done in OnInitialized: this component is reused when the URL
    /// moves from one record to another, and reloading on every parameter set would throw away
    /// what the user had typed.
    /// </remarks>
    protected override void OnParametersSet()
    {
        if (_loaded && _loadedId == Id)
            return;

        _loaded = true;
        _loadedId = Id;

        Load();
    }

    private void Load()
    {
        _missing = false;
        _status = string.Empty;

        var stored = Id is { } id
            ? _store.GetAll().FirstOrDefault(record => ScreenFields.IdOf(record) == id)
            : null;

        // A URL naming a record that has since been deleted says so, rather than silently opening
        // a blank new one and writing a second record when it is saved.
        if (Id is not null && stored is null)
        {
            _missing = true;
            return;
        }

        _draft = stored is null ? new TRecord() : ScreenFields.Clone(stored);
        _openedAs = ScreenFields.Snapshot(_draft);

        _lines = Lines?.Invoke() ?? [];

        // A new record has no id yet, so it has no lines to read either: they are written after the
        // parent is saved and has one.
        if (Id is { } parentId)
        {
            foreach (var lines in _lines)
                lines.Load(parentId);
        }
    }

    /// <summary>Writes a typed value onto the draft, reporting one that will not convert rather
    /// than swallowing it.</summary>
    private void Set(PropertyInfo field, string? text)
        => _status = ScreenFields.Assign(_draft, field, text) ?? string.Empty;

    private void Save()
    {
{{ValidateCall(validation)}}        var all = _store.GetAll();

        if (IsNew)
        {
            ScreenFields.SetId(_draft, _store.NextId(all));
            all.Add(_draft);
        }
        else
        {
            var index = all.FindIndex(record => ScreenFields.IdOf(record) == ScreenFields.IdOf(_draft));

            // Deleted from another screen while this editor was open. Saying so beats writing the
            // record back and silently resurrecting it.
            if (index < 0)
            {
                _status = "That record no longer exists.";
                return;
            }

            all[index] = _draft;
        }

        var write = _store.SaveAll(all);
        if (!write.Success)
        {
            _status = write.Refusal ?? "Could not save.";
            return;
        }

        // The lines go after the parent, because a new parent has no id until it is written -- and
        // that id is what stamps them.
        foreach (var lines in _lines)
        {
            if (lines.Save(ScreenFields.IdOf(_draft)))
                continue;

            _status = $"Saved, but could not save the {lines.Title} lines.";
            return;
        }

        // The snapshot moves to what was just written, so nothing asks to discard a saved edit.
        _openedAs = ScreenFields.Snapshot(_draft);

        Leave();
    }

    /// <summary>Cancel asks nothing: the draft is a copy, so there is nothing to undo.</summary>
    private void Cancel() => Leave();

    private void Leave()
    {
        _leaving = true;

        Navigation.NavigateTo($"/{Route}");
    }

    /// <summary>
    /// The discard prompt, which is what a modal was buying.
    /// </summary>
    /// <remarks>
    /// The desktop asks with a message box; the browser's own confirm is the equivalent, and needs
    /// no script file of its own. Navigating away is what has to be blocked — the rail, the
    /// breadcrumb and sign-out are all internal navigations.
    /// </remarks>
    private async Task OnBeforeNavigation(LocationChangingContext context)
    {
        if (_leaving || !IsDirty)
            return;

        if (!await JS.InvokeAsync<bool>("confirm", "Discard your changes?"))
            context.PreventNavigation();
    }
}
""";

    /// <summary>What <c>AuthorizeView</c> and <c>AuthenticationState</c> need imported.</summary>
    /// <remarks>
    /// A different namespace from the <c>Microsoft.AspNetCore.Authorization</c> that
    /// <c>[Authorize]</c> needs, and the template's _Imports.razor has neither — so each file that
    /// authorizes brings its own using.
    /// </remarks>
    private static string AuthorizationUsing(bool login)
        => login ? "\r\n@using Microsoft.AspNetCore.Components.Authorization" : string.Empty;

    /// <summary>
    /// Delete, for an administrator only.
    /// </summary>
    /// <remarks>
    /// Hidden rather than disabled, and hiding it is a real guard here rather than decoration: a
    /// button that is not rendered has no handler to invoke, unlike a URL that can be typed. The
    /// handler checks the role again anyway, because that is the check that would still hold if the
    /// button moved.
    /// </remarks>
    private static string DeleteButton(bool login)
        => login
            ? """
                  <AuthorizeView Roles="Admin">
                      <Authorized>
                          <button class="btn btn-outline-danger" @onclick="Delete"
                                  disabled="@(_selected is null)">Delete</button>
                      </Authorized>
                  </AuthorizeView>

              """
            : """
                  <button class="btn btn-outline-danger" @onclick="Delete"
                          disabled="@(_selected is null)">Delete</button>

              """;

    private static string AuthStateParameter(bool login)
        => login
            ? """

                  /// <summary>Who is signed in, so Delete can refuse a plain user.</summary>
                  [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

              """
            : string.Empty;

    /// <summary>The claims namespace the login variant reads the acting role from.</summary>
    /// <remarks>
    /// Only that variant: a component with no login has no authentication state to read, and an
    /// import nothing names is noise in a generated file.
    /// </remarks>
    private static string ClaimsUsing(bool login)
        => login ? "\r\n@using System.Security.Claims" : string.Empty;

    /// <summary>Delete acts on the list selection, if the permission matrix allows this role
    /// to.</summary>
    /// <remarks>
    /// Blazor has no <c>Session</c> — that is emitted for the two desktop targets alone — so the
    /// acting role comes off the cascading authentication state's role claim rather than from a
    /// <c>CurrentRole()</c> helper. Asked here rather than left to the store: <c>SaveAll</c> is
    /// handed the whole list and cannot tell a delete from any other edit.
    /// </remarks>
    private static string DeleteHandler(bool login)
        => login
            ? """

                  private async Task Delete()
                  {
                      if (_selected is not { } record)
                          return;

                      // The button is hidden from a plain user; this is the check that would hold
                      // even if it were not. One matrix decides it, so this screen and the store
                      // behind it cannot disagree about who may delete what.
                      var state = AuthState is null ? null : await AuthState;
                      var role = state?.User.FindFirst(ClaimTypes.Role)?.Value;

                      if (!Permissions.Allows(ValidationOperation.Delete, typeof(TRecord).Name, role))
                      {
                          _status = Permissions.Refusal(ValidationOperation.Delete, typeof(TRecord).Name);
                          return;
                      }

                      Remove(record);
                  }

                  private void Remove(TRecord record)
                  {
                      var all = _store.GetAll();
                      all.RemoveAll(stored => ScreenFields.IdOf(stored) == ScreenFields.IdOf(record));

                      var write = _store.SaveAll(all);
                      _status = write.Success ? "Deleted." : write.Refusal ?? "Could not delete.";
                      _selected = null;

                      Reload();
                  }

              """
            : """

                  private void Delete()
                  {
                      if (_selected is not { } record)
                          return;

                      // No login, so there is nobody to attribute the delete to and the matrix
                      // reads a null role as "no notion of who is acting" -- it can refuse
                      // nothing here today. Asked anyway, so the two variants of this handler
                      // cannot drift apart, and so this is the line that starts refusing the day
                      // the matrix tells an anonymous caller from a signed-in one.
                      if (!Permissions.Allows(ValidationOperation.Delete, typeof(TRecord).Name, null))
                      {
                          _status = Permissions.Refusal(ValidationOperation.Delete, typeof(TRecord).Name);
                          return;
                      }

                      var all = _store.GetAll();
                      all.RemoveAll(stored => ScreenFields.IdOf(stored) == ScreenFields.IdOf(record));

                      var write = _store.SaveAll(all);
                      _status = write.Success ? "Deleted." : write.Refusal ?? "Could not delete.";
                      _selected = null;

                      Reload();
                  }

              """;

    private static string ValidationUsing(string rootNamespace, bool validation)
        => validation ? $"\r\n@using {rootNamespace}.Validation" : string.Empty;

    /// <summary>The validators, run by the editor because nothing else on this path would.</summary>
    private static string ValidateCall(bool validation)
        => validation
            ? """
                      var errors = ValidatorFactory.Create<TRecord>().Validate(_draft);

                      if (errors.Count > 0)
                      {
                          _status = $"Cannot save: {errors[0]}";
                          return;
                      }


              """
            : string.Empty;
}
