namespace AppScaffolding;

/// <summary>
/// The rows a user's data source contributed, keyed by entity name and then by field name. An
/// input to <see cref="AppScaffolder"/> alongside <see cref="PersistenceKind"/>.
/// </summary>
/// <remarks>
/// Values stay as raw strings. They are converted to the field's declared type when the seed file
/// is written, which is the one place that knows the FINAL field list — after the review loop has
/// had its say. Converting earlier would fix a type the user is still free to change.
/// </remarks>
public sealed class ImportedData
{
    /// <summary>
    /// Most rows imported per entity. A large export would otherwise become a large JSON file
    /// inside the generated project, committed and diffed forever, when its purpose is to give the
    /// app something to show on first run.
    /// </summary>
    public const int MaxSeedRows = 1000;

    public static ImportedData None { get; } =
        new(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(), []);

    public ImportedData(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> rows,
        IReadOnlyList<string> notes)
    {
        Rows = rows;
        Notes = notes;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> Rows
    { get; }

    /// <summary>
    /// What the caller must tell the user — above all, that rows were dropped. A cap the user is
    /// not told about reads as "all your data is in there", which is a false claim about their data.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The rows for <paramref name="entityName"/>, or none. Never null: an entity the user
    /// added by hand in the review loop has no imported rows and that is not an error.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string?>> For(string entityName)
        => Rows.TryGetValue(entityName, out var rows) ? rows : [];
}
