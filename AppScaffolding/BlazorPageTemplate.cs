namespace AppScaffolding;

/// <summary>
/// One Blazor page per class: a table of its records, editable in place, with add, delete and save.
/// </summary>
/// <remarks>
/// Columns and editors are built by reflecting over the model's public properties, which is the same
/// bargain the desktop targets strike — WPF and WinForms both let their grid generate its own
/// columns. It also keeps <see cref="ITargetScaffolder.FrontEndFiles"/> as it is: that interface is
/// handed entity <i>names</i> and nothing else, and widening it for this one target would touch all
/// six.
/// <para>
/// The page talks to the generated <c>&lt;Entity&gt;Repository</c> directly rather than through
/// dependency injection. <c>Repository&lt;T&gt;</c> builds its own store in its constructor, so there
/// is nothing to inject, and a generated app is easier to read without a service-registration layer
/// that earns nothing.
/// </para>
/// </remarks>
public static class BlazorPageTemplate
{
    /// <summary>The authorization attribute and the namespace it needs, or nothing at all for an
    /// app with no login.</summary>
    private static string Authorize(bool login)
        => login
            ? "\r\n@using Microsoft.AspNetCore.Authorization\r\n@attribute [Authorize]"
            : string.Empty;

    /// <summary>Source of <c>Components/Pages/&lt;className&gt;Page.razor</c>.</summary>
    /// <param name="rootNamespace">Namespace the generated code declares, prefix included.</param>
    /// <param name="className">The model class this page lists.</param>
    /// <param name="seed">
    /// Whether the app has sample data to seed. When it does the page asks
    /// <see cref="BlazorSeedTemplate"/>'s gate to run the seeder before its first read — this target
    /// emits no <c>Program.cs</c>, so there is no startup hook to do it from.
    /// </param>
    public static string Page(string rootNamespace, string className, bool seed)
        => Page(rootNamespace, className, seed, login: false);

    /// <summary>
    /// As above; with <paramref name="login"/> the page refuses an unauthenticated visitor.
    /// </summary>
    /// <remarks>
    /// The attribute alone decides nothing: it is honoured only because the emitted
    /// <c>Routes.razor</c> uses an AuthorizeRouteView. The template's plain RouteView ignores it.
    /// </remarks>
    public static string Page(string rootNamespace, string className, bool seed, bool login) => $$"""
@page "/{{className.ToLowerInvariant()}}"
@rendermode InteractiveServer{{Authorize(login)}}
@using System.Reflection
@using {{rootNamespace}}.Data
@using {{rootNamespace}}.Models

<PageTitle>{{className}}</PageTitle>

<h1>{{className}}</h1>

@if (_items.Count == 0)
{
    <p><em>No {{className}} records yet. Add one below.</em></p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                @foreach (var property in Columns)
                {
                    <th>@property.Name</th>
                }
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var item in _items)
            {
                <tr class="@(ReferenceEquals(item, _selected) ? "table-active" : null)"
                    @onclick="() => _selected = item">
                    @foreach (var property in Columns)
                    {
                        <td>
                            @if (property.Name == "Id" || !property.CanWrite)
                            {
                                @property.GetValue(item)
                            }
                            else
                            {
                                <input value="@property.GetValue(item)"
                                       @onchange="e => Set(item, property, e.Value?.ToString())" />
                            }
                        </td>
                    }
                    <td><button @onclick="() => Remove(item)">Remove</button></td>
                </tr>
            }
        </tbody>
    </table>
}

<button @onclick="Add">Add</button>
<button @onclick="Save">Save</button>

@if (_status.Length > 0)
{
    <p role="status">@_status</p>
}

@code {
    // Id first, then declaration order -- the same reading order the desktop grids use.
    private static readonly PropertyInfo[] Columns =
        typeof({{className}}).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.PropertyType.IsPrimitive
                        || p.PropertyType == typeof(string)
                        || p.PropertyType == typeof(decimal)
                        || p.PropertyType == typeof(DateTime))
            .OrderByDescending(p => p.Name == "Id")
            .ToArray();

    private static readonly PropertyInfo? IdProperty =
        Columns.FirstOrDefault(p => p.Name == "Id");

    private readonly {{className}}Repository _repository = new();

    private List<{{className}}> _items = new();

    /// <summary>
    /// Ids of records removed from the table but not yet deleted from the store.
    /// </summary>
    /// <remarks>
    /// Without this, removing a saved record only dropped it from the list: Save would then write
    /// back everything still present and leave the removed row in the store, so it reappeared on the
    /// next load. Delete has to be part of what Save persists.
    /// </remarks>
    private readonly List<int> _removed = new();

    private {{className}}? _selected;
    private string _status = string.Empty;

{{OnInitialized(seed)}}

    private void Add()
    {
        var created = new {{className}}();

        _items.Add(created);
        _selected = created;
        _status = "Added a {{className}}. Not saved yet.";
    }

    private void Remove({{className}} item)
    {
        var id = IdOf(item);

        // Only a record that reached the store needs deleting from it; one added and removed in the
        // same session was never there.
        if (id != 0)
        {
            _removed.Add(id);
        }

        _items.Remove(item);

        if (ReferenceEquals(item, _selected))
        {
            _selected = null;
        }

        _status = "Removed a {{className}}. Not saved yet.";
    }

    private static int IdOf({{className}} item)
        => IdProperty?.GetValue(item) as int? ?? 0;

    /// <summary>
    /// Writes an edited cell back onto the record.
    /// </summary>
    /// <remarks>
    /// Reflection rather than @bind-Value, which needs the property known at compile time and so
    /// cannot be driven from a loop over PropertyInfo. A value that will not convert is reported
    /// rather than swallowed: silently keeping the old number is how a user comes to distrust a form.
    /// </remarks>
    private void Set({{className}} item, PropertyInfo property, string? text)
    {
        try
        {
            var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            property.SetValue(
                item,
                string.IsNullOrWhiteSpace(text) && target != typeof(string)
                    ? null
                    : Convert.ChangeType(text, target));

            _status = string.Empty;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            _status = $"'{text}' is not a valid {property.Name}.";
        }
    }

    private void Save()
    {
        var failures = new List<string>();

        foreach (var id in _removed)
        {
            _repository.Delete(id);
        }

        _removed.Clear();

        foreach (var item in _items)
        {
            // Create for a record with no Id yet, Update for one that has been saved before --
            // the same rule the repository's own callers follow.
            var result = IdOf(item) == 0 ? _repository.Create(item) : _repository.Update(item);

            if (!result.Success)
            {
                failures.AddRange(result.Errors);
            }
        }

        _items = _repository.GetAll();
        _selected = null;

        _status = failures.Count == 0
            ? $"Saved {_items.Count} {{className}} record(s)."
            : string.Join(" ", failures);
    }
}
""";

    /// <summary>
    /// The page's <c>OnInitialized</c>, with a seeding call in front of the first read when the app
    /// has sample data.
    /// </summary>
    /// <remarks>
    /// Every page seeds, not just the home page: a visitor can arrive at any URL, and a bookmarked
    /// <c>/order</c> should not show an empty table because nobody visited <c>/</c> first. The gate
    /// makes the redundancy free after the first call.
    /// </remarks>
    private static string OnInitialized(bool seed)
    {
        var once = BlazorSeedTemplate.OnceCall(seed);

        return once.Length == 0
            ? "    protected override void OnInitialized() => _items = _repository.GetAll();"
            : $$"""
                  protected override void OnInitialized()
                  {
                      {{once}}

                      _items = _repository.GetAll();
                  }
              """;
    }
}
