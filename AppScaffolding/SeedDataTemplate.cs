using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// Emits <c>Data/SeedData.cs</c>: a reflection-based seeder that populates every empty store
/// with sample rows on first run. Consistent with JsonStore/Repository, which already reflect
/// on Id. Seed-if-empty and idempotent, so a running app never accrues duplicate sample data.
/// </summary>
public static class SeedDataTemplate
{
    /// <summary>
    /// Entity class names in seed order: an entity referenced by a foreign key is placed before
    /// the entity that references it, so a child's FK can point at a parent row that already
    /// exists. A dependency cycle degrades to declaration order.
    /// </summary>
    public static IReadOnlyList<string> OrderByDependency(IReadOnlyList<ClassDefinition> classes)
    {
        var names = ForeignKeys.ClassNameSet(classes);
        var parents = classes.ToDictionary(
            c => c.Name,
            c => (IReadOnlyList<string>)[.. ForeignKeys.LinksOf(c, names).Select(l => l.Target)],
            StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool progress = true;
        while (progress && placed.Count < classes.Count)
        {
            progress = false;
            foreach (var c in classes)
            {
                if (placed.Contains(c.Name))
                    continue;

                // Placeable once every parent is already placed (a self-reference never blocks).
                if (parents[c.Name].All(p =>
                        placed.Contains(p) || string.Equals(p, c.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    ordered.Add(c.Name);
                    placed.Add(c.Name);
                    progress = true;
                }
            }
        }

        // Any remaining (a cycle) fall back to declaration order.
        foreach (var c in classes)
            if (placed.Add(c.Name))
                ordered.Add(c.Name);

        return ordered;
    }

    /// <summary>The field names that are genuine foreign keys across <paramref name="classes"/>.
    /// Delegates to <see cref="ForeignKeys"/>, which the generated validators share.</summary>
    public static IReadOnlyList<string> ForeignKeyFieldNames(IReadOnlyList<ClassDefinition> classes)
        => ForeignKeys.FieldNames(classes);

    /// <summary>Source of <c>Data/SeedData.cs</c> in <paramref name="rootNamespace"/>.Data, seeding
    /// the entities in <paramref name="orderedEntityNames"/> order. <paramref name="foreignKeyFieldNames"/>
    /// gates the generated seeder's runtime FK assignment to genuine foreign keys.</summary>
    public static string Source(
        string rootNamespace,
        IReadOnlyList<string> orderedEntityNames,
        IReadOnlyList<string> foreignKeyFieldNames)
        => Source(rootNamespace, orderedEntityNames, foreignKeyFieldNames, []);

    /// <summary>
    /// The entry-point call into this file — <c>SeedData.EnsureSeeded();</c> and the blank line
    /// after it — or nothing when the app was generated without seeding, in which case there is no
    /// file for the call to reach.
    /// </summary>
    /// <remarks>
    /// A raw string literal rather than "\n" concatenation, because a raw literal carries the line
    /// endings of the source file it is written in and the templates splicing this in carry those
    /// same endings. Built any other way, a seeding app's entry point would come out one CR per
    /// line different from the one this generator has always produced.
    /// </remarks>
    public static string EnsureSeededCall(bool seed) => seed
        ? """
          SeedData.EnsureSeeded();


          """
        : string.Empty;

    /// <summary>Rows inserted into each empty store: the emitted <c>SeedRowCount</c>.</summary>
    private const int SeedRowCount = 3;

    /// <summary>
    /// For each string property with a value list, keyed "Class.Property", the values it allows.
    /// The emitted validator checks exactly these (<see cref="ValidatorTemplate"/>), so it is the
    /// same test: a property of type <c>string</c> with an <c>allowedvalues</c> rule.
    /// </summary>
    internal static IReadOnlyList<(string Key, IReadOnlyList<string> Values, bool Unique)> ValueLists(
        IReadOnlyList<ClassDefinition> classes)
        => [.. classes.SelectMany(definition => definition.Properties
            .Where(p => p.Type == "string")
            .Select(p => (Property: p, Allowed: p.Rules.FirstOrDefault(r =>
                string.Equals(r.Name, "allowedvalues", StringComparison.OrdinalIgnoreCase))?.AllowedValues))
            .Where(pair => pair.Allowed is { Count: > 0 })
            .Select(pair => (
                $"{definition.Name}.{pair.Property.Name}",
                (IReadOnlyList<string>)[.. pair.Allowed!],
                pair.Property.Rules.Any(r => string.Equals(r.Name, "unique", StringComparison.OrdinalIgnoreCase))
                    && ValidatorTemplate.CanEmitEntityRule(pair.Property.Type, "unique"))))];

    /// <summary>
    /// The classes that are seeded with fewer rows than <see cref="SeedRowCount"/>, how many, and
    /// why: a value list no two rows may share allows only as many rows as it has values.
    /// </summary>
    /// <remarks>
    /// One value is left over, not every value used: a record the user adds, and the generated
    /// round-trip test's own record, need a value no seeded row holds - with every value taken,
    /// neither could ever be saved. A list of one value still seeds its one row.
    /// </remarks>
    internal static IReadOnlyList<(string ClassName, int Rows, string Why)> RowLimits(
        IReadOnlyList<ClassDefinition> classes)
    {
        var limits = new List<(string, int, string)>();

        foreach (var group in ValueLists(classes).Where(l => l.Unique).GroupBy(l => l.Key[..l.Key.IndexOf('.')]))
        {
            var tightest = group.MinBy(l => l.Values.Count);
            var rows = Math.Max(1, tightest.Values.Count - 1);

            if (rows >= SeedRowCount)
                continue;

            var property = tightest.Key[(tightest.Key.IndexOf('.') + 1)..];

            limits.Add((group.Key, rows,
                $"{property} allows {tightest.Values.Count} value(s) and no two rows may share one, "
                + $"so {rows} row(s) are seeded and a value is left for a new record."));
        }

        return limits;
    }

    /// <inheritdoc cref="Source(string, IReadOnlyList{string}, IReadOnlyList{string})"/>
    /// <param name="seededFromFile">As in the overload without <paramref name="classes"/>.</param>
    /// <param name="classes">
    /// The classes being seeded. A property with a value list takes its listed values in turn,
    /// so a seeded row passes its own validator: "Status 1" broke the list on every row, and
    /// saving a seeded row unchanged was refused. With no value list among them the emitted file
    /// is exactly what it was before.
    /// </param>
    public static string Source(
        string rootNamespace,
        IReadOnlyList<string> orderedEntityNames,
        IReadOnlyList<string> foreignKeyFieldNames,
        IReadOnlyList<string> seededFromFile,
        IReadOnlyList<ClassDefinition> classes)
    {
        var lists = ValueLists(classes);
        var limits = RowLimits(classes);

        var source = Source(rootNamespace, orderedEntityNames, foreignKeyFieldNames, seededFromFile);

        if (lists.Count == 0)
            return source;

        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var tables = new System.Text.StringBuilder();
        tables.Append(newline).Append(newline);
        tables.Append("    /// <summary>The values each property's value list allows, keyed \"Model.Property\". A seeded").Append(newline);
        tables.Append("    /// row takes them in turn, so it passes the validator's own check.</summary>").Append(newline);
        tables.Append("    private static readonly Dictionary<string, string[]> ValueLists = new(StringComparer.Ordinal)").Append(newline);
        tables.Append("    {").Append(newline);

        foreach (var (key, values, _) in lists)
            tables.Append("        [").Append(ValidatorTemplate.Quoted(key)).Append("] = new[] { ")
                .Append(string.Join(", ", values.Select(ValidatorTemplate.Quoted))).Append(" },").Append(newline);

        tables.Append("    };");

        if (limits.Count > 0)
        {
            tables.Append(newline).Append(newline);
            tables.Append("    /// <summary>The classes seeded with fewer than SeedRowCount rows, and how many.</summary>").Append(newline);
            tables.Append("    private static readonly Dictionary<string, int> RowLimits = new(StringComparer.Ordinal)").Append(newline);
            tables.Append("    {").Append(newline);

            foreach (var (className, rows, why) in limits)
            {
                tables.Append("        // ").Append(className).Append(": ").Append(why).Append(newline);
                tables.Append("        [").Append(ValidatorTemplate.Quoted(className)).Append("] = ")
                    .Append(rows.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(newline);
            }

            tables.Append("    };");
        }

        source = InsertAfterLine(source, "private static readonly HashSet<string> ForeignKeyFields = ", tables.ToString());

        if (limits.Count > 0)
            source = ReplaceOnce(source, "for (var n = 1; n <= SeedRowCount; n++)",
                "for (var n = 1; n <= (RowLimits.TryGetValue(typeof(T).Name, out var limit) ? limit : SeedRowCount); n++)");

        var listed = string.Join(newline,
            "        if (type == typeof(string)",
            "            && ValueLists.TryGetValue(prop.DeclaringType?.Name + \".\" + name, out var listed))",
            "            return listed[(n - 1) % listed.Length];",
            "",
            "        if (type == typeof(string))");

        return ReplaceOnce(source, "        if (type == typeof(string))", listed);
    }

    /// <summary><paramref name="source"/> with its one <paramref name="find"/> replaced by
    /// <paramref name="replacement"/>.</summary>
    private static string ReplaceOnce(string source, string find, string replacement)
    {
        var at = Once(source, find);
        return source[..at] + replacement + source[(at + find.Length)..];
    }

    /// <summary><paramref name="source"/> with <paramref name="insert"/> at the end of the line
    /// that holds its one <paramref name="find"/>.</summary>
    private static string InsertAfterLine(string source, string find, string insert)
    {
        var end = source.IndexOf('\n', Once(source, find));
        var lineEnd = end > 0 && source[end - 1] == '\r' ? end - 1 : end;
        return source[..lineEnd] + insert + source[lineEnd..];
    }

    /// <summary>Where <paramref name="find"/> is in <paramref name="source"/>. Throws when it is not
    /// there exactly once: a template that changed under this must fail its tests, not emit a
    /// seeder that ignores the value lists.</summary>
    private static int Once(string source, string find)
    {
        var at = source.IndexOf(find, StringComparison.Ordinal);

        if (at < 0 || source.IndexOf(find, at + find.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"SeedData template: expected one \"{find}\".");

        return at;
    }

    /// <inheritdoc cref="Source(string, IReadOnlyList{string}, IReadOnlyList{string})"/>
    /// <param name="seededFromFile">
    /// Class names whose starting rows were imported from the user's own data and written to
    /// <c>Data/Seed/&lt;Class&gt;.json</c>. Empty for a description-built app, in which case the
    /// emitted file is byte-for-byte what it has always been — a project with no imported data
    /// must not carry a branch it can never take.
    /// </param>
    public static string Source(
        string rootNamespace,
        IReadOnlyList<string> orderedEntityNames,
        IReadOnlyList<string> foreignKeyFieldNames,
        IReadOnlyList<string> seededFromFile)
    {
        var seedCalls = string.Join("\n",
            orderedEntityNames.Select(n => $"        Seed<{n}>(seededIds);"));

        var fkSet = foreignKeyFieldNames.Count == 0
            ? "new(StringComparer.Ordinal)"
            : "new(StringComparer.Ordinal) { "
              + string.Join(", ", foreignKeyFieldNames.Select(f => $"\"{f}\"")) + " }";

        // Every fragment below is empty when nothing was imported, so the emitted file is exactly
        // what it was before this feature existed. An unused using is also a warning, and CI
        // builds with -warnaserror, so even the usings are conditional.
        var extraUsings = seededFromFile.Count == 0
            ? string.Empty
            : "using System.IO;\nusing System.Text.Json;\n";

        var seedFileSet = seededFromFile.Count == 0
            ? string.Empty
            : "\n\n"
              + "    /// <summary>Entities whose starting rows were imported from the user's own data\n"
              + "    /// and written to Data/Seed/&lt;Class&gt;.json. Everything else falls back to\n"
              + "    /// generated sample rows.</summary>\n"
              + "    private static readonly HashSet<string> SeedFiles = new(StringComparer.Ordinal)\n"
              + "    { " + string.Join(", ", seededFromFile.Select(n => $"\"{n}\"")) + " };";

        var seedFileBranch = seededFromFile.Count == 0
            ? string.Empty
            : """


        // Imported rows win over sample rows. Written at generation time from the user's own data,
        // with Ids already assigned 1..N so the foreign keys in these files resolve.
        //
        // Unchecked: this runs before anyone could possibly be signed in, same as
        // UserSeed.EnsureAdminSeeded. SaveAll's permission gate -- correctly, for every other
        // caller -- refuses a signed-out caller of a login app, which would otherwise leave a
        // freshly generated login app starting with no data at all.
        if (SeedFiles.Contains(typeof(T).Name) && TryLoadSeedFile<T>(out var imported))
        {
            store.SaveAllUnchecked(imported);
            seededIds[typeof(T).Name] = imported.Select(GetId).ToList();
            return;
        }
""";

        var seedFileLoader = seededFromFile.Count == 0
            ? string.Empty
            : """


    /// <summary>Reads Data/Seed/&lt;T&gt;.json from beside the binary. A missing or unreadable file
    /// is not fatal: the caller falls back to sample rows, which is better than refusing to start.</summary>
    private static bool TryLoadSeedFile<T>(out List<T> rows) where T : class, new()
    {
        rows = new List<T>();

        var path = Path.Combine(
            AppContext.BaseDirectory, "Data", "SEED_FOLDER", typeof(T).Name + ".json");

        if (!File.Exists(path))
            return false;

        try
        {
            var loaded = JsonSerializer.Deserialize<List<T>>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (loaded is null || loaded.Count == 0)
                return false;

            rows = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }
""".Replace("SEED_FOLDER", SeedFileWriter.SeedFolderName);

        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
{{extraUsings}}using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Data;

/// <summary>
/// Populates empty stores with sample rows on first run. Seed-if-empty and idempotent: a store
/// that already holds data is left untouched, so re-running never accrues duplicates. This is a
/// starting point — edit the sample values, or delete this file, as the app grows.
/// </summary>
public static class SeedData
{
    /// <summary>Rows inserted into each empty store.</summary>
    public const int SeedRowCount = 3;

    /// <summary>The field names that are genuine foreign keys — a <c>&lt;Target&gt;Id</c> whose
    /// owner also has a nav property typed as that target. Gates the FK branch in
    /// <see cref="SampleValue"/> so an incidental int named <c>...Id</c> with no relationship is
    /// never wired to an unrelated entity's seeded rows.</summary>
    private static readonly HashSet<string> ForeignKeyFields = {{fkSet}};{{seedFileSet}}

    /// <summary>Seeds every empty store, referenced entities before their referencers so a
    /// foreign key points at a row that exists.</summary>
    public static void EnsureSeeded()
    {
        var seededIds = new Dictionary<string, List<int>>();
{{seedCalls}}
    }

    private static void Seed<T>(Dictionary<string, List<int>> seededIds) where T : class, new()
    {
        IStore<T> store = StoreFactory.Create<T>();
        var existing = store.GetAll();
        if (existing.Count > 0)
        {
            seededIds[typeof(T).Name] = existing.Select(GetId).ToList();
            return;
        }
{{seedFileBranch}}
        var rows = new List<T>();
        var ids = new List<int>();
        for (var n = 1; n <= SeedRowCount; n++)
        {
            var item = new T();
            foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite)
                    continue;

                if (string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    prop.SetValue(item, n);
                    continue;
                }

                var value = SampleValue(prop, n, seededIds);
                if (value is not null)
                    prop.SetValue(item, value);
            }

            rows.Add(item);
            ids.Add(n);
        }

        // Unchecked, same reason as the imported-rows branch above: this runs before anyone
        // could possibly be signed in.
        store.SaveAllUnchecked(rows);
        seededIds[typeof(T).Name] = ids;
    }

    private static object? SampleValue(
        PropertyInfo prop, int n, Dictionary<string, List<int>> seededIds)
    {
        var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        var name = prop.Name;

        // Foreign key: an int named <Nav>Id -> an existing seeded row of the type <Nav> is.
        if (type == typeof(int) && ForeignKeyFields.Contains(name))
        {
            var navName = name.Substring(0, name.Length - 2);
            var target = prop.DeclaringType?.GetProperty(navName)?.PropertyType.Name ?? navName;
            if (seededIds.TryGetValue(target, out var parentIds) && parentIds.Count > 0)
                return parentIds[(n - 1) % parentIds.Count];
            return 0;
        }

        if (type == typeof(string))
        {
            if (name.Contains("Email", StringComparison.OrdinalIgnoreCase))
                return $"user{n}@example.com";
            if (name.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                return $"555-010{n}";
            return $"{name} {n}";
        }

        if (type == typeof(int)) return n;
        if (type == typeof(decimal)) return n * 10 + 0.99m;
        if (type == typeof(double)) return n * 10 + 0.99;
        if (type == typeof(DateTime)) return DateTime.Today.AddDays(-n);
        if (type == typeof(bool)) return n % 2 == 0;

        // Navigation properties (entity types, List<>) and anything else are left at default.
        return null;
    }
{{seedFileLoader}}
    private static int GetId<T>(T item)
    {
        var id = typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(id?.GetValue(item) ?? 0);
    }
}
""";
    }
}
